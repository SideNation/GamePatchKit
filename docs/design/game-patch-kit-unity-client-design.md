# GamePatchKit Unity 클라이언트 설계 문서

> 기준 문서: [`docs/prd/gamepatch-kit-distribution-prd.md`](../prd/gamepatch-kit-distribution-prd.md) "클라이언트"·"데이터 복원" 절
>
> 참조 구현: [`game-patch-kit-cli-sync-design.md`](game-patch-kit-cli-sync-design.md) (`gpk sync`)
>
> 상태: grilling으로 확정한 결정을 기록한 구현 기준. 사람이 실시간으로 답하지 않는 환경이라 Q1~Q3은 제시한 권장안을 채택하고 Q4 이후는 조사한 근거로 판단했다.

## 1. 기능 요약

게임 서버가 알려준 `releaseVersion`의 세대를 공개 Storage URL에서 받아 `<rootPath>/data` 아래에 원본 트리로 복원하는 UPM 패키지 `com.sidenation.gamepatchkit`이다. `gpk sync`와 같은 로컬 배치를 만들고 같은 델타 규칙(새 매니페스트에만 있는 `name`만 받는다)을 따른다. 네트워크는 Unity 메인 스레드의 `UnityWebRequest`가, SHA-256 검증과 zstd 해제는 백그라운드 스레드가 맡는다.

## 2. 사전 검토 결과

### UnityWebRequest 사용 가능 여부: 가능

- `DownloadHandlerFile`이 응답 본문을 managed 메모리에 올리지 않고 디스크에 직접 쓴다. 큰 아카이브를 받아도 메모리 부담이 없다.
- 이 저장소의 2026-07 세대(커밋 `62a3f28`)가 같은 조합을 Unity 6000.4.4f1 EditMode 테스트와 macOS IL2CPP Player에서 이미 검증했다.
- 제약 두 가지를 설계로 흡수한다. UnityWebRequest는 메인 스레드에서만 만들고 보내며 취소 시 `Abort`도 메인 스레드에서 호출한다. 다운로드 중 해시를 계산할 수 없으므로 받은 임시 파일을 백그라운드에서 다시 읽어 검증한다.
- `System.Net.Http`는 WebGL에서 동작하지 않고 플랫폼별 TLS 이슈가 있어 대안으로 두지 않는다.

### NuGet 배포 가능 여부: Unity 소비 불가, UPM으로 배포

- Unity Package Manager는 UPM 레지스트리·Git URL·로컬 tarball만 다룬다. NuGet 패키지는 NuGetForUnity나 OpenUPM의 UnityNuGet 미러를 거쳐야 하고, `UnityEngine`에 의존하는 코드는 애초에 NuGet으로 만들 수 없다.
- 배포는 Git URL 설치다: `https://github.com/SideNation/GamePatchKit.git?path=/src/GamePatchKit.Unity#v<버전>`. 기존 CI의 `v<버전>` 태그를 그대로 쓴다.
- UPM은 package.json 안에 Git 의존성을 선언할 수 없으므로 서드파티 DLL은 패키지에 동봉한다. Newtonsoft는 Unity 공식 레지스트리 패키지 `com.unity.nuget.newtonsoft-json` 3.2.2(= Newtonsoft 13.0.2, CLI와 동일)로 선언한다.
- 서버용 순수 C# 라이브러리의 NuGet 배포는 서버 구현이 시작될 때 별도 작업으로 검토한다.

### zstd 디코더: ZstdSharp.Port 0.8.8 (managed)

| 후보 | 판정 근거 |
| --- | --- |
| ZstdSharp.Port 0.8.8 (MIT) | 순수 managed라 IL2CPP·Android·iOS·WebGL에서 같은 코드로 동작한다. IL2CPP `nuint` 연산 버그는 Unity 2022.3.9f1 / 2023.1.11f1부터 수정됐다([issue #24](https://github.com/oleg-st/ZstdSharp/issues/24)). CLI가 `NativeCompressions`로 만든 `1.gpka`와 파일 객체를 바이트 단위로 같게 풀고 변조 프레임을 `ZstdException`으로 거부하는 것을 확인했다 |
| NativeCompressions.Zstandard 0.6.1 (CLI가 쓰는 네이티브) | README가 "현재 preview는 iOS IL2CPP 미지원"이고 NuGetForUnity 설치를 전제한다. 2026-07 세대도 같은 이유로 Unity에서 zstd를 제외했었다 |

- ZstdSharp의 `netstandard2.1` 빌드는 `System.Runtime.CompilerServices.Unsafe` 6.1.0을 참조한다. Unity 6000.4.4f1의 Player용 .NET Standard 프로필에는 이 어셈블리가 없어(에디터 전용 `EditorExtensions`에만 존재) 같은 버전의 `netstandard2.0` DLL(MIT)을 함께 동봉한다.
- 소비 프로젝트에 같은 이름의 DLL이 이미 있으면 Unity가 중복 어셈블리 오류를 내므로 한쪽을 제거해야 한다. 사용법 문서에 명시한다.

## 3. grilling 결정 기록

| 번호 | 결정 | 근거 |
| --- | --- | --- |
| Q1 | Unity 6(6000.x) 전용, 지원 목표 Standalone·Android·iOS. WebGL 제외 | 설치된 에디터가 6000.1.17f1·6000.4.4f1이며 IL2CPP nuint 수정이 포함된 버전이다. WebGL은 파일시스템 가상화라 별도 검증이 필요하다 |
| Q2 | ZstdSharp.Port 0.8.8 managed DLL 동봉 | 위 표 |
| Q3 | UPM Git URL 설치, 패키지 이름 `com.sidenation.gamepatchkit` | 위 "NuGet 배포 가능 여부" |
| Q4 | 패키지 루트 `src/GamePatchKit.Unity/`, 검증용 호스트 프로젝트 `unity/GamePatchKit.Unity.Host/`가 `file:../../../src/GamePatchKit.Unity`로 참조 | Git URL의 `?path=`가 짧고 패키지가 호스트 프로젝트와 분리된다. 호스트 프로젝트는 테스트와 IL2CPP 빌드를 재현하기 위해 필요하다 |
| Q5 | CLI는 건드리지 않고 패키지 안에 매니페스트 모델·검증·해제를 순수 C#으로 둔다. `Runtime/Core/`에 격리하고 `UnityEngine`을 참조하지 않는다 | 요청 범위가 Unity 다운로드 기능이다. 서버 구현이 생기면 이 폴더를 `<Compile Include>`로 링크하는 방식을 검토한다. CLI와의 중복은 의도된 것이며 필드 이름·검증 규칙을 동일하게 유지한다 |
| Q6 | `gpk sync`와 같은 배치(`manifest.json`, `archives/`, `files/`, `data/`)이며 압축 미러를 남긴다. 저장 루트는 생성자 인자이고 기본값이 없다 | PRD 델타 규칙("새 매니페스트에만 있는 name")과 검증 로직을 그대로 옮길 수 있고 부분 실패·롤백에서 재다운로드가 없다. 디스크가 약 2배 필요하다는 비용은 모바일에서 실제 문제로 확인되면 삭제 정책을 별도 작업으로 붙인다 |
| Q7 | 일반 C# 클래스 `PatchClient(baseUrl, rootPath)` + `Task<PatchSyncResult> SyncAsync(long releaseVersion, CancellationToken)`. 포인터 조회·진행률 보고 없음. 반환은 `Task` | PRD대로 클라이언트는 서버가 알려준 버전을 쓴다. `Task`는 NUnit async 테스트와 다른 async 라이브러리에 호환되고 `Awaitable`의 재-await 불가 제약이 없다. 취소는 앱 종료 시 중단에 필요하다 |
| Q8 | 호스트 프로젝트의 PlayMode 테스트(루프백 HTTP + 실제 `gpk build` 픽스처)를 batchmode로 실행하고, 같은 테스트를 macOS IL2CPP Player로 실행한다. Android·iOS 기기 검증은 미검증으로 명시 | 6000.4.4f1에 Mac IL2CPP 모듈이 설치돼 있다. ZstdSharp·Newtonsoft·파일 API의 IL2CPP 동작이 핵심 위험이라 Player 실행까지 넣는다 |
| Q9 | 이 문서를 먼저 남기고 구현한다 | 저장소 관례 |
| 후속 | 오류는 `PatchClientException` 하나로 보고하고 취소는 `OperationCanceledException`. 메시지는 CLI와 같은 한국어 | CLI의 `BuildException` 단일 예외 관례 |
| 후속 | 임시 파일은 대상 폴더의 `.<이름>.<guid>.tmp`이며 실패 시 삭제. 시작 시 잔여 임시 파일을 훑지 않는다 | `gpk sync`와 동일 |
| 후속 | 임시 파일의 최종 이름 교체는 대상이 있으면 `File.Replace`, 없으면 `File.Move`(`FileMover.MoveReplacing`) | Unity .NET Standard 2.1 프로필에 `File.Move(…, overwrite)`가 없다. `File.Replace`는 같은 볼륨에서 rename이라 대상이 사라지는 순간이 없다 |
| 후속 | `data/` 트리를 바꾸기 시작할 때 이전 `manifest.json`을 먼저 지우고, 해제가 모두 끝난 뒤 새 매니페스트를 쓴다. 해제 중 실패·취소 후 다음 호출은 이전 세대를 가정하지 않고 전량 다시 푼다 | Codex 검토 F1. 이전 매니페스트를 남겨 두면 해제 중 실패한 트리를 다음 호출의 같은-세대 fast path가 완전한 것으로 오인한다. 스테이징 폴더에 새 세대를 완성한 뒤 교체하는 방식은 디스크가 세 배 필요해 별도 결정으로 미룬다 |
| 후속 | 취소 토큰을 해제 루프(파일·버퍼 단위)와 매니페스트 쓰기 직전까지 전달한다 | Codex 검토 F5. `Task.Run`의 토큰은 시작 전에만 반영된다 |
| 후속 | 로컬 I/O 오류(`IOException`, `UnauthorizedAccessException`)와 zstd 프레임 오류(`ZstdException`)는 `PatchClientException`으로 감싸고 `InnerException`을 보존한다 | Codex 검토 F11. 문서의 예외 계약과 실제 동작을 맞춘다 |
| 후속 | `link.xml`로 패키지 어셈블리를 보존한다 | Newtonsoft가 리플렉션으로 모델을 채우므로 IL2CPP managed stripping에서 setter가 제거되면 안 된다 |
| 후속 | 사용법 문서는 `docs/unity/patch-client.md`, 샘플 씬은 만들지 않는다 | feature-docs 관례(`docs/<카테고리>/<기능>.md`). 사용 예시는 문서의 코드로 충분하다 |
| 후속 | 파일 락·동시 실행 제어를 넣지 않는다 | 단일 프로세스이며 같은 폴더의 동시 `SyncAsync`는 호출자가 하지 않는다고 문서에 적는다 |

## 4. 요구사항 정리

- **입력**: 공개 URL prefix(`https://<project-ref>.supabase.co/storage/v1/object/public/<bucket>/`), 저장 루트 경로, 게임 서버가 알려준 `releaseVersion`, 선택적 `CancellationToken`
- **출력**: `<rootPath>/manifest.json`(받은 바이트 그대로), `<rootPath>/archives/**`, `<rootPath>/files/**`, `<rootPath>/data/<그룹 id>/<엔트리 path>`; 결과 `PatchSyncResult`(이전·현재 세대, 다운로드 수·바이트, 재사용 수, 해제 수, 삭제 수)
- **제약**:
  - 로컬 `manifest.json`의 `releaseVersion`이 요청 값과 같으면 원격을 호출하지 않는다. 다르면(크든 작든) 동기화하므로 롤백이 같은 경로다.
  - 산출물은 임시 파일로 받아 크기와 SHA-256이 매니페스트와 같을 때만 최종 이름을 얻는다.
  - 세대 매니페스트는 CLI와 같은 관계 검증을 통과해야 하고 `releaseVersion`이 요청 값과 같아야 한다.
  - 모든 산출물을 받고 해제까지 끝낸 뒤에만 `manifest.json`을 원자적으로 교체한다. 어느 시점에 중단돼도 로컬 세대는 완전하다.
  - 재시도·백오프·`Range` 재개·진행률·포인터 조회는 넣지 않는다(PRD 제외 범위).
- **성능 목표**: 다운로드 크기에 비례하는 managed 배열 할당 없음(`DownloadHandlerFile`), 메인 스레드에서 해시·해제를 수행하지 않음, `Update` 없음

## 5. 단순 설계 기준

- MonoBehaviour·ScriptableObject·Scene·Prefab을 만들지 않는다. 호출자가 자기 생명주기에서 `PatchClient`를 만들고 `SyncAsync`를 기다린다.
- 인터페이스를 만들지 않는다. 테스트는 실제 `UnityWebRequest`를 루프백 `HttpListener` 서버에 보내므로 전송 추상화가 필요 없다.
- `gpk sync`의 `SyncCommand`·`DataExtractor`·`ManifestStore` 로직을 그대로 옮기고 Unity 전용 부분(`UnityWebRequest`, 스레드 전환)만 `PatchClient`에 둔다.

## 6. Unity 객체 분해

| 분류 | 이름 | 책임 |
| --- | --- | --- |
| MonoBehaviour | 없음 | 생명주기는 호출자 소유 |
| ScriptableObject | 없음 | URL·경로는 생성자 인자 |
| 일반 C# 클래스 (public) | `PatchClient` | 세대 비교, 세대 매니페스트·산출물 다운로드, 검증, 해제 호출, `manifest.json` 원자 교체 |
| 일반 C# 클래스 (public) | `PatchSyncResult`, `PatchClientException` | 결과 DTO, 실패 보고 |
| 일반 C# 클래스 (internal, `Runtime/Core/`) | `PatchManifest`·`ManifestGroup`·`ManifestArchive`·`ManifestEntry`·`ManifestArtifact`, `ManifestKinds`, `ManifestStore`, `DataExtractor`, `RelativePathValidator` | CLI와 같은 매니페스트 모델, 파싱·관계 검증, `data` 트리 복원, 경로 규칙 |
| Editor 확장 | 없음 | 테스트의 `IPrebuildSetup`이 Player 빌드 전에 픽스처를 StreamingAssets로 복사하는 것뿐 |

## 7. 적용 패턴

없음.

## 8. 제외한 구조 · 패턴과 제외 이유

- 전송 인터페이스(`ITransport`) 미적용 — 구현이 UnityWebRequest 하나뿐이고 테스트는 실제 HTTP로 한다.
- `Awaitable` 반환 미적용 — 재-await 불가 제약과 NUnit 호환성 때문에 `Task`를 쓴다. 내부에서도 `Task.Run`과 Unity `SynchronizationContext`만 쓰고 `Awaitable`에 의존하지 않는다.
- 진행률(`IProgress<T>`) 미적용 — 요청에 없고 별도로 붙일 수 있다.
- 배타 파일 락 미적용 — 단일 프로세스.
- 해제 후 압축 산출물 삭제 미적용 — Q6.
- 재시도·백오프·`Range` 재개 미적용 — PRD 제외 범위.

## 9. 컴포넌트 · 인터페이스 시그니처

```csharp
// Signature sketch, not implementation.

public sealed class PatchClient
{
    public PatchClient(string baseUrl, string rootPath);

    public string DataPath { get; }

    public Task<PatchSyncResult> SyncAsync(long releaseVersion, CancellationToken cancellationToken = default);
}

public sealed class PatchSyncResult
{
    public long ReleaseVersion { get; }
    public long? PreviousReleaseVersion { get; }
    public bool IsAlreadyUpToDate { get; }
    public int DownloadedCount { get; }
    public long DownloadedBytes { get; }
    public int ReusedCount { get; }
    public int ExtractedCount { get; }
    public int RemovedCount { get; }
}

public sealed class PatchClientException : Exception
{
    public PatchClientException(string message);
}
```

### 핵심 처리 흐름

1. `SynchronizationContext.Current`를 캡처한다(취소 시 `Abort`를 메인 스레드로 보내기 위해). 없으면 메인 스레드가 아니므로 `InvalidOperationException`.
2. 로컬 `manifest.json`을 읽어 검증한다. `releaseVersion`이 같으면 `IsAlreadyUpToDate`로 끝낸다.
3. `manifests/<releaseVersion>.json`을 `DownloadHandlerBuffer`로 받아 백그라운드에서 파싱·검증하고 `releaseVersion` 일치를 확인한다.
4. 매니페스트가 참조하는 산출물을 이름 순으로 처리한다. 로컬에 같은 이름·크기가 있으면 재사용, 없으면 `DownloadHandlerFile`로 임시 파일에 받은 뒤 백그라운드에서 크기·SHA-256을 대조하고 최종 이름으로 옮긴다.
5. 이전 `manifest.json`을 지운 뒤 백그라운드에서 `DataExtractor`가 `data` 트리를 새 매니페스트에 맞춘다(없어진 파일 삭제 → 바뀐 엔트리만 해제).
6. `manifest.json`을 받은 바이트 그대로 쓴다.

4단계까지 실패하면 이전 세대의 `manifest.json`과 `data/`가 그대로 남는다. 5단계부터는 `manifest.json`이 없는 상태이므로 실패·취소 뒤의 다음 호출은 산출물을 다시 받지 않고 `data`를 전량 다시 푼다. 취소는 각 단계 사이, 요청 대기 중, 해제 루프의 파일·버퍼 단위 경계에서 반영된다. 요청 대기 중 취소는 캡처한 컨텍스트로 `Abort`를 보내고 `OperationCanceledException`으로 끝난다. 임시 파일은 `finally`에서 삭제한다.

## 10. 파일 · 폴더 · Asmdef 배치

```text
src/GamePatchKit.Unity/                       # UPM 패키지 루트 (com.sidenation.gamepatchkit)
├── package.json
├── LICENSE.md, Third Party Notices.md
├── Runtime/
│   ├── GamePatchKit.Unity.asmdef             # precompiledReferences: Newtonsoft.Json.dll, ZstdSharp.dll
│   ├── link.xml
│   ├── PatchClient.cs, PatchSyncResult.cs
│   ├── Core/                                 # UnityEngine 비의존
│   │   ├── PatchManifest.cs, ManifestKinds.cs, ManifestStore.cs
│   │   ├── DataExtractor.cs, RelativePathValidator.cs, PatchClientException.cs
│   └── Plugins/
│       ├── ZstdSharp.dll
│       └── System.Runtime.CompilerServices.Unsafe.dll
└── Tests/Runtime/
    ├── GamePatchKit.Unity.Tests.asmdef       # 에디터 PlayMode와 Player 모두 대상
    ├── TestPatchClient.cs, FixtureServer.cs
    └── Fixtures/                             # generate-fixtures.sh가 실제 gpk build로 재생성
        ├── bucket/{manifests,archives,files}/
        └── source/{0,1}/

unity/GamePatchKit.Unity.Host/                # 검증용·수동 시험용 Unity 6000.4.4f1 프로젝트
├── Packages/manifest.json                    # file: 참조 + testables
├── Assets/
│   ├── PatchClientSample/                    # 손으로 시험하는 샘플 씬과 IMGUI 패널
│   │   ├── PatchClientSample.cs              # Base URL은 [SerializeField]로 씬에서 설정
│   │   └── PatchClientSample.unity
│   └── Editor/PatchClientSampleSceneBuilder.cs   # 씬을 코드로 생성 (메뉴 / -executeMethod)
└── scripts/
    ├── generate-fixtures.sh
    ├── serve-fixtures.sh                     # 픽스처 버킷을 정적 HTTP로 띄운다
    ├── run-tests.sh                          # -testPlatform PlayMode
    ├── run-player-tests.sh                   # -testPlatform StandaloneOSX, IL2CPP
    └── StandaloneOsxIl2CppTestSettings.json
```

- 수동 시험 경로는 자동 테스트와 같은 픽스처(`Tests/Runtime/Fixtures/bucket`)를 쓴다. 별도 샘플 데이터를 만들지 않는다.
- 호스트 프로젝트의 `Allow downloads over HTTP`는 `Development Only`다. 평문 http 픽스처 서버를 LAN IP로 붙이는 기기 시험이 이 설정 없이는 전송 전에 거부된다(loopback은 제외).

- asmdef 2개: Runtime 어셈블리(precompiled DLL 참조를 명시하기 위해)와 테스트 어셈블리(Player 빌드에 테스트가 들어가지 않게 하기 위해). 최소 구성이다.
- 테스트 어셈블리를 Editor 전용으로 두지 않는다. 같은 테스트를 에디터 PlayMode와 IL2CPP Player에서 돌리기 위해서다.

## 11. Prefab · Scene · Addressable 배치

없음.

## 12. 성능 · GC 검토 메모

- 다운로드 본문은 `DownloadHandlerFile`이 파일에 직접 쓴다. 세대 매니페스트만 `DownloadHandlerBuffer`로 메모리에 받는다.
- 해시·파싱·해제는 `Task.Run`으로 스레드 풀에서 실행하고, `await` 뒤에는 Unity `SynchronizationContext`가 메인 스레드로 되돌린다. 프레임을 막는 작업은 파일 존재·크기 확인과 `manifest.json` 교체뿐이다.
- 코루틴·`Awaitable`·UniTask를 쓰지 않는다. `Task` 하나로 통일한다.

## 13. 단순화 자가 검토 결과

- 패턴 0개, 인터페이스 0개.
- asmdef 2개는 precompiled 참조 명시와 테스트 분리를 위한 최소값이다.
- `Manager`·`Service`·`Controller` 객체 없음. MonoBehaviour·ScriptableObject 없음.
- 요청 없이 추가한 보안·견고성·성능·설정·관측·동시성·확장·방어 장치 없음. 취소 토큰과 백그라운드 스레드는 Q7에서 결정했다.

## 14. 에이전트 검토 결과

`codex-collab-workflow` adversarial 검토(모델 `gpt-5.6-sol`) 1차 결과와 처리.

| 번호 | 지적 | 처리 |
| --- | --- | --- |
| F1 | 해제 중 실패 시 이전 `manifest.json`이 남아 훼손된 `data/`를 이전 세대로 오인 | 반영 — 해제 시작 전에 이전 매니페스트를 지운다. 스테이징 교체는 디스크 3배 비용이라 별도 결정으로 미룸 |
| F2 | `delete + move` 교체는 대상이 사라지는 구간이 있음 | 반영 — 대상이 있으면 `File.Replace` |
| F3 | 설치 예시 태그 `v0.1.0`에는 패키지가 없음 | 반영 — 패키지 버전과 예시를 `v0.1.9`로 맞추고 하위 태그에 패키지가 없음을 문서화 |
| F4 | 같은 크기의 기존 산출물은 SHA-256을 재검증하지 않음 | 거부 — `gpk sync`와 같은 설계. 이름이 같으면 바이트가 같다는 게시 계약이며, PRD의 checksum은 전송 손상 감지용이다. 문서에 명시 |
| F5 | 해제 시작 후 취소가 무시됨 | 반영 — 토큰을 해제 루프와 매니페스트 쓰기 직전까지 전달 |
| F6 | `data/`·`archives/`·`files/`가 symlink이면 루트 밖 삭제·쓰기 (Speculative) | 거부 — 앱 전용 폴더에 symlink를 심을 수 있는 로컬 공격자는 범위 밖. CLI와 동일 |
| F7 | 취소 테스트가 `DownloadHandlerFile` 경로를 지나지 않고, 손상 테스트가 이전 세대 보존을 검증하지 않음 | 반영 — 아카이브 요청 중 취소, 이전 세대 보존, 해제 실패 후 수렴 테스트 추가 |
| F8 | 같은 폴더 동시 호출 보호 없음 | 거부 — 단일 프로세스이며 호출자 계약으로 문서화. 락은 요청 범위 밖 |
| F9 | `Skip`이 EOF를 성공으로 처리 | 거부 — CLI와 동일하며 결과는 0바이트 엔트리의 빈 파일로 무해. 잘린 아카이브는 다운로드 검증이 잡는다 |
| F10 | 대소문자·조상 경로 충돌 미검증 (Speculative) | 거부 — CLI와 같은 검증 규칙. 게시 측 문제 |
| F11 | I/O·zstd 예외가 문서의 예외 계약과 다름 | 반영 — `PatchClientException`으로 감싸고 `InnerException` 보존 |

2차 검토(수정된 diff)는 Blocker/High 없음. 테스트 결정성 Medium 2건을 반영했다: 취소 테스트는 서버 응답을 게이트로 막아 `Abort`가 요청을 실제로 끊어야만 끝나게 하고, 해제 실패 테스트는 앞 엔트리가 세대 1로 풀린 뒤 실패해 두 세대가 섞인 트리에서 수렴을 검증한다.

3차 검토(테스트 변경)는 Medium 이상 없음. Low 2건(테스트 서버 `Dispose`가 등록 직전의 응답 스레드를 놓칠 수 있음, 취소 테스트의 10초 타임아웃 실패 경로에서 `sync` 정리 없음)은 실패·극단 스케줄링에서만 나타나는 테스트 격리 문제라 보류했다.

수동 시험용 샘플(호스트 프로젝트 파일 3개와 문서 한 절)에 대한 standard 검토 1회에서 받은 7건은 모두 적용했다: 씬 빌더의 미저장 씬 확인과 `SaveScene` 실패 전파, 호스트의 `Allow downloads over HTTP`를 `Development Only`로 변경, 패널 전체 스크롤, 로그 메시지 한 줄 정규화, `GUI.matrix` 복원, 문서 스크립트 경로 통일. 이 검토에서 드러난 평문 http 거부(`InvalidOperationException: Insecure connection not allowed`)는 패키지 계약을 빠져나가므로 `PatchClient.SendAsync`에서 `PatchClientException`으로 감쌌다.

## 15. 위임 다음 단계

- 구현: 위 배치대로 패키지·호스트 프로젝트 작성
- 테스트: `run-tests.sh`(PlayMode), `run-player-tests.sh`(macOS IL2CPP)
- 사용법 문서: `docs/unity/patch-client.md`
- 검토: `codex-collab-workflow` adversarial(네트워크·새 공개 API)
- Android·iOS 기기 검증, 서버용 순수 C# 라이브러리 분리, 압축 미러 삭제 정책: 별도 작업

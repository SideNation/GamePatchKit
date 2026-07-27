# Unity Runtime 통합 설계 문서

## 1. 기능 요약

GamePatchKit의 `GamePatchKit.Core`와 `GamePatchKit.Runtime`을 Unity에서 실제로 사용할 수 있도록
Unity 전용 transport와 storage adapter를 제공한다. 저장소 안의 Unity 검증 프로젝트가
adapter 테스트와 IL2CPP 빌드를 재현하며, 첫 공식 범위는 Unity 6.4와
`compression:none`이다.

## 2. 요구사항 정리

- **입력**: host가 제공하는 publish base URL, `TargetManifestReference`, 선택적인
  `CancellationToken`, Runtime 저장 root
- **출력**: UnityWebRequest로 내려받은 manifest·signature·artifact stream,
  `Application.persistentDataPath` 아래의 cache·installation·canonical package state
- **제약**:
  - 기준 Editor는 Unity `6000.4.4f1`, API compatibility는 .NET Standard 2.1이다.
  - `GamePatchKit.DotNet`은 `net10.0`이므로 Unity에서 참조하지 않는다.
  - 첫 지원 범위에서 zstd는 제외한다. `NativeCompressions.Zstandard`는 iOS IL2CPP를
    지원하지 않으므로 `compression:none` fixture로 검증한다.
  - Unity API 호출은 생성 시 캡처한 Unity main-thread `SynchronizationContext`에서 수행한다.
  - transport 응답은 `DownloadHandlerFile`로 임시 파일에 저장한 뒤 읽기 stream으로 반환해
    큰 artifact를 managed memory에 통째로 올리지 않는다.
- **성능 목표**: 매 프레임 `Update` 없음, 다운로드 크기에 비례하는 managed byte 배열 할당
  없음, state·cache 교체 중 partial file 노출 없음

## 3. 단순 설계 기준

- Unity 프로젝트 하나를 package 개발·테스트 host로 함께 사용한다.
- Unity 전용 코드는 embedded UPM package 하나에 둬 다른 Unity 프로젝트로 옮길 수 있게 한다.
- Scene·Prefab·MonoBehaviour·ScriptableObject를 만들지 않는다. Runtime 호출은 host의 기존
  lifecycle에서 수행하며 adapter는 일반 C# 클래스만 제공한다.
- Core·Runtime·BouncyCastle DLL은 커밋하지 않고 준비 스크립트가 고정된 build output에서
  복사한다. Newtonsoft.Json은 Unity 공식 package를 사용한다.
- 기존 Runtime interface를 그대로 구현하며 Unity 전용 facade나 중복 상태 머신은 만들지 않는다.

## 4. Unity 객체 분해

| 분류 | 이름 | 책임 |
| --- | --- | --- |
| MonoBehaviour | 없음 | Scene lifecycle을 adapter 내부 책임으로 만들지 않는다. |
| ScriptableObject | 없음 | URL·경로 설정 형식을 새로 고정하지 않는다. |
| 일반 C# 클래스 | `UnityWebRequestArtifactTransport` | publish URL을 UnityWebRequest로 내려받아 임시 파일 stream으로 반환한다. |
| 일반 C# 클래스 | `UnityRuntimeStorage` | persistent root 아래 package lock·state·cache·staging·installation을 관리한다. |
| 일반 C# 클래스 | internal storage helper | atomic file, 안전한 상대 경로, cache writer, staging area의 좁은 책임을 구현한다. |
| Editor 확장 | `GamePatchKitUnityBuild` | 빈 Scene을 코드로 만들고 macOS IL2CPP player를 batchmode로 빌드한다. |

## 5. 적용 패턴

없음.

## 6. 제외한 구조 · 패턴과 제외 이유

- MonoBehaviour dispatcher 미적용 — adapter 생성 시 Unity main-thread
  `SynchronizationContext`를 캡처하면 매 프레임 queue를 처리할 GameObject가 필요 없다.
- DI container·Service Locator 미적용 — 기존 `PackageRuntime` 생성자 주입만으로 충분하다.
- Unity용 Runtime facade 미적용 — Core Runtime API를 감싸면 두 공개 API의 동작이 어긋날 수 있다.
- zstd 자동 설치 미적용 — target별 native plugin 지원 범위를 먼저 검증해야 하며 iOS IL2CPP는
  현재 upstream 제약상 지원할 수 없다.
- Addressables 미적용 — patch transport·storage 검증과 무관하다.

## 7. 컴포넌트 · 인터페이스 시그니처

```csharp
// Signature sketch, not implementation.

public sealed class UnityWebRequestArtifactTransport : IArtifactTransport
{
    public UnityWebRequestArtifactTransport(string baseUrl);
    public UnityWebRequestArtifactTransport(string baseUrl, string downloadRoot);

    public Task<Stream> OpenManifestAsync(
        TargetManifestReference target,
        CancellationToken cancellationToken);

    public Task<Stream> OpenManifestSignatureAsync(
        TargetManifestReference target,
        CancellationToken cancellationToken);

    public Task<Stream> OpenArtifactAsync(
        string packageId,
        string relativePath,
        CancellationToken cancellationToken);
}
```

```csharp
// Signature sketch, not implementation.

public sealed class UnityRuntimeStorage : IRuntimeStorage
{
    public UnityRuntimeStorage();
    public UnityRuntimeStorage(string runtimeRoot);

    public Task<IAsyncDisposable> AcquirePackageWriterLockAsync(
        string packageId,
        CancellationToken cancellationToken);

    // 나머지 IRuntimeStorage 시그니처는 기존 interface와 동일하다.
}
```

새 Unity 전용 interface는 만들지 않는다. 외부 경계는 이미 `IArtifactTransport`,
`IRuntimeStorage`, `ICompressionCodec`으로 고정되어 있다.

## 8. 파일 · 폴더 · Asmdef 배치 제안

```text
unity/GamePatchKit.Unity/
├── Assets/
│   ├── Editor/
│   │   └── GamePatchKitUnityBuild.cs
│   └── Plugins/GamePatchKit/                 # 준비 스크립트가 DLL 생성, gitignore
├── Packages/
│   ├── manifest.json
│   └── com.sidenation.gamepatchkit.unity/
│       ├── package.json
│       ├── Runtime/
│       │   ├── GamePatchKit.Unity.asmdef
│       │   ├── UnityWebRequestArtifactTransport.cs
│       │   └── UnityRuntimeStorage.cs
│       └── Tests/Editor/
│           ├── GamePatchKit.Unity.Tests.asmdef
│           ├── TestUnityWebRequestArtifactTransport.cs
│           └── TestUnityRuntimeStorage.cs
├── ProjectSettings/
│   └── ProjectVersion.txt
└── scripts/
    ├── prepare.sh
    ├── test.sh
    └── build-macos-il2cpp.sh
```

- `GamePatchKit.Unity.asmdef`: precompiled Core·Runtime assembly 참조와 Runtime/Test 경계를
  명시하기 위해 필요하다.
- `GamePatchKit.Unity.Tests.asmdef`: test assembly가 player build에 포함되지 않게 하는
  Unity Test Framework 경계이므로 필요하다.

## 9. Prefab · Scene · Addressable 배치

없음. macOS IL2CPP build 검증용 빈 Scene은 Editor build method가 임시로 생성하고 build 완료 후
삭제한다.

## 10. 성능 · GC 검토 메모

- `Update`, `GetComponent`, `Find`, `Instantiate`, `Destroy`를 사용하지 않는다.
- UnityWebRequest body는 `DownloadHandlerFile`로 기록하고 반환 stream dispose 시 임시 파일을
  삭제한다.
- Runtime의 async 호출은 `Task`를 유지한다. transport의 Unity API 시작·완료·취소 처리만
  Unity main thread로 marshal한다.
- storage는 파일 stream과 rename을 사용한다. state와 cache는 같은 directory의 임시 파일을
  최종 경로로 교체한다.

## 11. 단순화 자가 검토 결과

- 적용 패턴은 0개다.
- 새 interface는 0개다.
- asmdef는 Runtime과 Editor test의 컴파일 경계를 위해 2개만 둔다.
- `Manager`·`Service`·`Controller` 객체는 없다.
- MonoBehaviour·ScriptableObject·Scene object를 추가하지 않았다.
- DotNet adapter와 동일한 helper 책임은 Unity assembly 내부 `internal` 타입으로만 분리한다.

## 12. 에이전트 검토 결과

사용 안 함.

## 13. 위임 다음 단계

- 구현: embedded UPM package의 transport·storage 일반 C# 코드
- 테스트: Unity Test Framework의 Editor adapter tests와 Runtime smoke
- 빌드: Unity batchmode Editor tests, macOS IL2CPP player 생성
- 사용법 문서: `docs/contracts/unity-adapter.md`
- zstd·Android·WebGL 지원: target별 native codec 검증 후 별도 작업

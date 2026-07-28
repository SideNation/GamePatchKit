# Unity 통합 가이드

Unity 프로젝트에서 GamePatchKit Runtime으로 게임 데이터를 받아 설치하는 절차. adapter가
무엇을 보장하는지는 [Unity Runtime adapter 계약](../contracts/unity-adapter.md)에 있고,
이 문서는 **내 프로젝트에 붙이는 순서**를 다룬다.

일단 동작하는 것부터 보고 싶다면 [Unity 10분 QuickStart](unity-quickstart.md)가 더
빠르다.

## 먼저 알아야 할 것

**GamePatchKit 저장소는 Unity 전용 assembly나 UPM 패키지를 배포하지 않는다.** PRD가
Unity 전용 산출물을 범위에서 뺐기 때문이다. 대신 이 저장소는 `netstandard2.1` managed
assembly(`GamePatchKit.Core`, `GamePatchKit.Runtime`)와, 그것을 Unity에 연결하는
**embedded package 구현 예시**를 함께 제공한다.

```text
unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity/
```

이 package를 자기 프로젝트로 복사하거나 로컬 경로로 참조해서 쓰고, 필요하면 고쳐 쓴다.

## 지원 범위

| 항목 | 현재 판정 |
| --- | --- |
| Unity 버전 | `6000.4.4f1` 기준 (package.json은 `6000.4` 이상) |
| scripting runtime | .NET Standard 2.1 |
| compression | **`compression: none`만 검증됨.** zstd native plugin은 target별 검증 전까지 포함하지 않는다 |
| macOS Standalone (Mono) | 동작 확인 |
| macOS Standalone (IL2CPP) | Player test로 스모크 확인 |
| Windows / Android / iOS / WebGL | **아직 지원 판정 대상이 아니다** |
| iOS IL2CPP + 기본 zstd | 미지원 (`NativeCompressions.Zstandard` preview 제약) |

zstd가 필요한 target은 호환 `ICompressionCodec`을 직접 구현해 주입하거나, package 설정을
`compression: {kind: none}`으로 두고 만든 release를 쓴다. **압축 정책을 바꿔도 이전
release의 재사용 artifact는 여전히 zstd일 수 있으므로**, 그 group을 compact해 새 baseline을
만들어야 한다 ([compression 정책](package-config.md#compression-정책)).

## 1. managed plugin 준비

Unity는 이 저장소의 csproj를 직접 빌드하지 않으므로 DLL을 넣어 줘야 한다.

```bash
./unity/GamePatchKit.Unity/scripts/prepare.sh
```

`GamePatchKit.Runtime`을 Release로 빌드하고 세 DLL을
`unity/GamePatchKit.Unity/Assets/Plugins/GamePatchKit/`에 복사한다.

| DLL | 출처 |
| --- | --- |
| `GamePatchKit.Core.dll` | 이 저장소 |
| `GamePatchKit.Runtime.dll` | 이 저장소 |
| `BouncyCastle.Cryptography.dll` | NuGet (Ed25519 서명 검증) |

`Newtonsoft.Json`은 DLL로 넣지 않는다. Unity 공식 package
`com.unity.nuget.newtonsoft-json`을 쓴다 — 직접 넣으면 다른 package가 가져오는 것과
중복돼 assembly 충돌이 난다.

> **이 DLL들은 생성물이라 커밋하지 않는다.** clone 후 반드시 `prepare.sh`로 다시 만든다.

## 2. 내 Unity 프로젝트에 붙이기

1. **Newtonsoft package 추가** — Package Manager에서
   `com.unity.nuget.newtonsoft-json` (3.2.2 이상).
2. **managed plugin 복사** — 위 세 DLL을 `Assets/Plugins/GamePatchKit/`에 둔다.
3. **adapter package 복사** — `com.sidenation.gamepatchkit.unity` 디렉터리를 자기
   프로젝트의 `Packages/` 아래에 두거나, `manifest.json`에 로컬 경로로 추가한다.

   ```json
   { "dependencies": { "com.sidenation.gamepatchkit.unity": "file:../../path/to/com.sidenation.gamepatchkit.unity" } }
   ```

4. **asmdef 확인** — package의 `GamePatchKit.Unity.asmdef`는 `overrideReferences: true`와
   `precompiledReferences: ["GamePatchKit.Core.dll", "GamePatchKit.Runtime.dll"]`로 위
   plugin을 참조한다. DLL 이름을 바꾸면 여기도 함께 바꾼다.
5. **IL2CPP managed stripping 대비** — package의 `Runtime/link.xml`이 네 assembly를
   `preserve="all"`로 지킨다. 자기 프로젝트로 옮길 때 **이 파일을 빠뜨리지 않는다.**
   빠지면 Editor·Mono에서는 되고 IL2CPP player에서만 리플렉션 경로가 깨진다.

   ```xml
   <linker>
     <assembly fullname="BouncyCastle.Cryptography" preserve="all" />
     <assembly fullname="GamePatchKit.Core" preserve="all" />
     <assembly fullname="GamePatchKit.Runtime" preserve="all" />
     <assembly fullname="GamePatchKit.Unity" preserve="all" />
   </linker>
   ```

`GamePatchKit.DotNet`은 `net10.0` adapter라 **Unity에서 참조하지 않는다.** Unity용
transport·storage는 이 package가 제공한다.

## 3. 최소 코드

```csharp
using System.Threading;
using GamePatchKit.Runtime;
using GamePatchKit.Unity;
using UnityEngine;

public sealed class PatchBootstrap : MonoBehaviour
{
    private readonly CancellationTokenSource _lifetime = new();

    private async void Start()
    {
        // 반드시 main thread에서 만든다. transport 생성자가 SynchronizationContext를 캡처한다.
        var transport = new UnityWebRequestArtifactTransport("https://cdn.example.com/gpk/");
        var storage = new UnityRuntimeStorage();
        var runtime = new PackageRuntime(transport, storage);

        // 세 값은 서버가 알려준다. Runtime은 최신 release를 스스로 고르지 않는다.
        var target = new TargetManifestReference(packageId, dataVersion, manifestHash);

        PackageState state = await runtime.InstallOrUpdateAsync(
            target,
            progress: new Progress<PatchProgress>(OnProgress),
            cancellationToken: _lifetime.Token);

        Debug.Log($"installed revision {state.StateRevision}");
    }

    private void OnProgress(PatchProgress progress)
    {
        // UI 갱신. Progress<T>가 캡처한 main thread context로 돌아온다.
    }

    private void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

- **base URL은 absolute URI이고 `/`로 끝나야 한다.** artifact 경로는 manifest의
  content-addressed 상대 경로를 그대로 붙인다.
- codec 인자를 주지 않으면 압축을 지원하지 않는 상태다. `compression: none`으로 만든
  release라면 이것으로 충분하고, zstd artifact를 참조하는 manifest는 활성화 전에
  `runtime.missing-compression-codec`으로 실패한다.
- 앱 종료·화면 전환은 반드시 `CancellationToken`으로 전달한다. 취소해도 검증된 cache는
  남아 다음 실행에서 이어받는다.

## 4. 저장 위치

기본 root는 `Application.persistentDataPath/GamePatchKit`이다. 테스트나 host 정책상
다른 경로가 필요하면 `new UnityRuntimeStorage(runtimeRoot)`를 쓴다.

```text
<persistentDataPath>/GamePatchKit/packages/<packageId>/
├── state/package-state.json     # 원자적으로 교체되는 활성 상태
├── cache/                       # content-addressed cache
├── installs/<installationKey>/  # 실제 게임 데이터
└── staging/
```

게임 데이터는 `installs/` 아래에 있지만 **경로를 직접 조합해 읽지 않는다.**
`installationKey`는 storage adapter만 해석하는 불투명 값이다. 다운로드 stream을
dispose하면 `Application.temporaryCachePath` 아래 임시 파일이 지워진다.

## 5. 검증과 빌드

이 저장소의 `unity/GamePatchKit.Unity/`는 adapter를 검증하기 위한 프로젝트다.

```bash
./unity/GamePatchKit.Unity/scripts/prepare.sh            # managed plugin 생성 (선행 필수)
./unity/GamePatchKit.Unity/scripts/test.sh               # EditMode 테스트
./unity/GamePatchKit.Unity/scripts/build-macos-il2cpp.sh # macOS IL2CPP player 빌드
```

`test.sh`는 전송, HTTP 오류 분류, 취소, 저장소 원자 교체, writer lock, staging promotion과
`PackageRuntime` 단일 파일 E2E를 EditMode에서 실행한다. IL2CPP 빌드에는 Unity Hub의
같은 버전 **`Mac Build Support (IL2CPP)`** 모듈이 필요하다.

Player 스모크 테스트는 `Application.isEditor == false` 환경에서 `UnityRuntimeStorage`
상태 저장·조회와 loopback HTTP 다운로드를 확인하고, 결과를 Player log의
`GAMEPATCHKIT_UNITY_PLAYER_TEST_RESULT:<status>` marker로도 남긴다.

> **GUI가 없는 터미널에서는** 일반 그래픽 Player가 표시 신호를 기다려 Test Runner 결과를
> Editor로 보내지 못한다. `PlayerWithTests.app/Contents/MacOS/GamePatchKit.Unity
> -batchmode -nographics`로 직접 실행한다. `-runTests`와 `-quit`을 함께 주면 import 후
> 종료되어 테스트가 돌지 않는다.

자기 adapter를 따로 구현했다면 [adapter conformance
suite](distribution.md#adapter-conformance-suite)로 검증한다. 다만 이 suite는 xUnit
기반이라 Unity에서 그대로 돌지 않고, 시나리오를 Unity Test Framework로 옮겨야 한다.

## 6. 오류 처리

| 예외/상태 | 의미 | 권장 처리 |
| --- | --- | --- |
| `OperationCanceledException` | lifecycle token으로 중단 | 종료 흐름에서는 무시하고 다음 실행에서 재개 |
| `ArtifactTransportException.IsTransient == true` | 연결 실패, HTTP 408/429/5xx | Runtime이 최대 3회 재시도한 뒤에도 실패한 것. 네트워크 재시도 UI |
| `ArtifactTransportException.IsNotFound == true` | HTTP 404로 부재 확인 | target reference나 publish 상태 점검 |
| `RuntimeException` | manifest·hash·state·staging 검증 실패 | `Error.Code` 기록. **손상 artifact를 활성화하지 않는다** |
| `IOException` | 저장소 권한·용량·writer lock | `persistentDataPath` 가용 공간과 동시 실행 확인 |

전체 오류 코드 표는 [Runtime 통합
가이드](runtime-integration.md#진행률과-오류)에 있다.

## 7. 체크리스트

- [ ] `prepare.sh`로 DLL을 만들었는가 (커밋된 DLL은 없다)
- [ ] `com.unity.nuget.newtonsoft-json`을 추가했고 Newtonsoft DLL을 직접 넣지 **않았는가**
- [ ] `link.xml`을 함께 옮겼는가
- [ ] transport와 `PackageRuntime`을 main thread에서 만들었는가
- [ ] base URL이 `/`로 끝나는가
- [ ] 대상 release가 `compression: none`인가, 아니면 codec을 주입했는가
- [ ] `CancellationToken`을 앱 lifecycle에 연결했는가
- [ ] 게임 데이터를 `installationKey` 경로 조합이 아니라 storage adapter로 읽는가

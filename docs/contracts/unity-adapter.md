# Unity Runtime adapter

## 개요

`GamePatchKit.Unity`는 Unity 프로젝트에서 `GamePatchKit.Runtime`을 실행하기 위한
`UnityWebRequestArtifactTransport`와 `UnityRuntimeStorage`를 제공한다. 기준 환경은
Unity `6000.4.4f1`, .NET Standard 2.1이며 첫 지원 범위는
`compression:none`과 macOS Standalone이다.

## 사용법

embedded package는
[`unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity)에
있다. `prepare.sh`가 `GamePatchKit.Core`, `GamePatchKit.Runtime`,
`BouncyCastle.Cryptography` managed plugin을 빌드·복사하고 Newtonsoft.Json은
Unity 공식 package를 사용한다.

```bash
./unity/GamePatchKit.Unity/scripts/prepare.sh
```

Unity main thread에서 adapter와 `PackageRuntime`을 만들고 host lifecycle의
`CancellationToken`을 전달한다.

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
        var transport = new UnityWebRequestArtifactTransport(
            "https://cdn.example.com/gamepatchkit/");
        var storage = new UnityRuntimeStorage();
        var runtime = new PackageRuntime(transport, storage);
        var target = new TargetManifestReference(
            "game-data",
            "v1-0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef",
            "abcdef0123456789abcdef0123456789abcdef0123456789abcdef0123456789");

        PackageState state = await runtime.InstallOrUpdateAsync(
            target,
            cancellationToken: _lifetime.Token);
        Debug.Log($"Installed revision {state.StateRevision}");
    }

    private void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

- publish base URL은 absolute URI이고 `/`로 끝나야 한다. artifact path는 manifest의
  content-addressed 상대 경로를 그대로 사용한다.
- 기본 저장 root는 `Application.persistentDataPath/GamePatchKit`이다. 테스트나 host
  정책상 별도 root가 필요하면 `new UnityRuntimeStorage(runtimeRoot)`를 사용한다.
- `UnityWebRequestArtifactTransport` 생성자는 Unity main thread의
  `SynchronizationContext`를 캡처하므로 worker thread에서 생성하면 안 된다.
- 다운로드 stream을 dispose하면 `Application.temporaryCachePath` 아래 임시 파일이
  삭제된다.

## 흐름

1. `PackageRuntime`이 manifest와 artifact를 transport에 요청한다.
2. transport가 `DownloadHandlerFile`로 임시 파일에 내려받고 stream을 반환한다.
3. Runtime이 hash를 검증한 뒤 storage의 cache writer를 commit한다.
4. required group을 staging directory에 완성하고 directory rename으로 설치를 공개한다.
5. canonical package state를 임시 파일에 쓴 뒤 원자적으로 교체한다.

## 에러 처리

| 예외/상태 | 의미 | 권장 처리 |
| --- | --- | --- |
| `OperationCanceledException` | host가 전달한 lifecycle token으로 작업 중단 | 화면 종료·앱 종료 흐름에서는 무시하고 다음 실행에서 재개 |
| `ArtifactTransportException.IsTransient == true` | 연결 실패, HTTP 408/429/5xx | Runtime retry 이후에도 실패하면 네트워크 재시도 UI 표시 |
| `ArtifactTransportException.IsNotFound == true` | HTTP 404로 부재 확인 | target reference 또는 publish 상태 점검 |
| `RuntimeException` | manifest·hash·state·staging 검증 실패 | `Error.Code`를 기록하고 손상 artifact를 활성화하지 않음 |
| `IOException` | storage 권한·용량·writer lock 문제 | `persistentDataPath` 가용 공간과 동시 실행 여부 점검 |

## 테스트 방법

```bash
./unity/GamePatchKit.Unity/scripts/test.sh
./unity/GamePatchKit.Unity/scripts/build-macos-il2cpp.sh
```

`test.sh`는 Unity EditMode에서 전송, HTTP 오류 분류, 취소, 저장소 원자 교체,
writer lock, staging promotion과 `PackageRuntime` 단일 파일 E2E를 실행한다.

`build-macos-il2cpp.sh`는 빈 Scene을 코드로 만들고
`Build/macOS/GamePatchKitUnitySmoke.app`을 Development + IL2CPP로 빌드한 뒤 임시
Scene을 삭제한다. 같은 Unity Editor 버전에 Unity Hub의
`Mac Build Support (IL2CPP)` 모듈이 설치되어 있어야 한다.

## 제약/주의사항

- 현재 Unity 검증 fixture는 `compression:none`만 지원한다. zstd native plugin은
  target별 검증 전까지 Unity 기본 구성에 포함하지 않는다.
- Android, iOS, WebGL과 Windows Player는 아직 지원 판정 대상이 아니다.
- `GamePatchKit.DotNet`은 `net10.0` adapter이므로 Unity에서 참조하지 않는다.
- Core·Runtime·BouncyCastle DLL은 생성물이라 커밋하지 않는다. 반드시
  `prepare.sh`로 재생성한다.
- IL2CPP managed stripping을 위해 package의
  [`link.xml`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity/Runtime/link.xml)을
  유지한다.

## 관련 파일

- [`UnityWebRequestArtifactTransport.cs`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity/Runtime/UnityWebRequestArtifactTransport.cs)
- [`UnityRuntimeStorage.cs`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity/Runtime/UnityRuntimeStorage.cs)
- [`TestUnityWebRequestArtifactTransport.cs`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity/Tests/Editor/TestUnityWebRequestArtifactTransport.cs)
- [`TestUnityRuntimeStorage.cs`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity/Tests/Editor/TestUnityRuntimeStorage.cs)
- [`GamePatchKitUnityBuild.cs`](../../unity/GamePatchKit.Unity/Assets/Editor/GamePatchKitUnityBuild.cs)

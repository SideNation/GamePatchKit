# Unity 패치 클라이언트 (`PatchClient`)

## 개요

`com.sidenation.gamepatchkit` 패키지의 `PatchClient`는 `gpk upload`가 공개 Storage에 게시한 세대를 Unity 클라이언트에 내려받아 원본 파일 트리로 복원한다. 게임 서버가 알려준 `releaseVersion`을 그대로 넘기면 로컬이 그 세대와 다를 때만 세대 매니페스트와 없는 산출물을 받아 `<rootPath>/data` 아래를 맞춘다. 롤백(서버가 이전 세대를 알려주는 경우)도 같은 호출로 처리된다.

- 네트워크는 Unity 메인 스레드의 `UnityWebRequest`가, SHA-256 검증과 zstd 해제는 백그라운드 스레드가 맡는다.
- 로컬 배치는 [`gpk sync`](../cli/sync.md)와 같다. `manifest.json`, 압축 미러(`archives/`, `files/`), 해제한 원본 트리(`data/`)가 한 폴더에 놓인다.
- Supabase 포인터 테이블은 읽지 않는다. 어느 세대를 쓸지는 [배포 PRD](../prd/gamepatch-kit-distribution-prd.md)대로 게임 서버가 정해 클라이언트에 알린다.

## 설치

Unity 6(6000.x) 프로젝트의 `Packages/manifest.json`에 Git URL로 추가한다. 태그는 저장소의 릴리스 태그(`v<버전>`)를 쓴다.

```json
{
  "dependencies": {
    "com.sidenation.gamepatchkit": "https://github.com/SideNation/GamePatchKit.git?path=/src/GamePatchKit.Unity#v0.1.9"
  }
}
```

- `v0.1.8` 이하 태그에는 이 패키지가 없다. 패키지가 포함된 릴리스 태그(`v0.1.9` 이상)를 지정한다.
- 의존성 `com.unity.nuget.newtonsoft-json`(3.2.2)은 Unity 레지스트리에서 자동으로 해석된다.
- zstd 해제용 `ZstdSharp.dll`과 그 의존성 `System.Runtime.CompilerServices.Unsafe.dll`은 패키지 `Runtime/Plugins/`에 동봉돼 있다. 프로젝트에 같은 이름의 DLL이 이미 있으면 Unity가 중복 어셈블리 오류를 내므로 한쪽을 제거한다.
- NuGet 패키지로는 배포하지 않는다. Unity Package Manager가 NuGet을 소비하지 못하고 `UnityEngine`에 의존하는 코드는 NuGet 대상이 아니기 때문이다.

## 사용법

```csharp
using System.Threading;
using System.Threading.Tasks;
using GamePatchKit.Unity;
using UnityEngine;

public sealed class PatchBootstrap : MonoBehaviour
{
    private readonly CancellationTokenSource _lifetime = new CancellationTokenSource();

    private async void Start()
    {
        var client = new PatchClient(
            "https://<project-ref>.supabase.co/storage/v1/object/public/<bucket>/",
            Path.Combine(Application.persistentDataPath, "GamePatchKit"));
        long releaseVersion = /* 접속한 게임 서버가 알려준 값 */ 7;

        try
        {
            PatchSyncResult result = await client.SyncAsync(releaseVersion, _lifetime.Token);
            Debug.Log($"releaseVersion={result.ReleaseVersion} downloaded={result.DownloadedCount} extracted={result.ExtractedCount}");
            // 게임 데이터는 client.DataPath 아래의 <그룹 id>/<엔트리 path>에서 읽는다.
        }
        catch (PatchClientException exception)
        {
            Debug.LogError(exception.Message);
        }
        catch (OperationCanceledException)
        {
        }
    }

    private void OnDestroy()
    {
        _lifetime.Cancel();
        _lifetime.Dispose();
    }
}
```

- `baseUrl`은 게시된 객체 이름을 그대로 뒤에 붙일 URL prefix다. Supabase 공개 버킷이면 `https://<project-ref>.supabase.co/storage/v1/object/public/<bucket>/` 형식이다. 끝의 `/`는 없어도 붙여 준다. http 또는 https 절대 URL이 아니면 생성자가 `ArgumentException`을 던진다.
- `rootPath`는 클라이언트가 소유하는 폴더다. 없으면 만든다. `data/` 아래에 매니페스트에 없는 파일은 지우므로 다른 파일을 두지 않는다.
- `SyncAsync`는 Unity 메인 스레드에서 호출한다. 다른 스레드에서 호출하면 `InvalidOperationException`이다. 같은 폴더에 대한 `SyncAsync`를 동시에 실행하지 않는다.
- `releaseVersion`은 0 이상이어야 한다.

## 결과

`PatchSyncResult`는 이번 호출이 한 일을 담는다.

| 값 | 의미 |
| --- | --- |
| `IsAlreadyUpToDate` | 로컬이 이미 요청한 세대여서 원격을 한 번도 호출하지 않았다 |
| `PreviousReleaseVersion` | 호출 전 로컬 세대. 처음 받는 폴더면 `null` |
| `DownloadedCount`, `DownloadedBytes` | 이번에 받은 산출물 수와 저장 바이트 |
| `ReusedCount` | 로컬에 같은 이름·크기로 있어 받지 않은 산출물 수. 이름에 세대가 들어간 불변 객체라 이름이 같으면 바이트도 같다는 게시 계약을 신뢰하며, 재사용 산출물의 SHA-256은 다시 계산하지 않는다 |
| `ExtractedCount`, `RemovedCount` | `data`에 새로 푼 파일 수와 새 세대에 없어 지운 파일 수 |

세대가 같지 않으면(크든 작든) 동기화하므로 이전 세대로 돌아가는 호출도 같은 결과 형태를 돌려준다.

## 폴더 배치

```text
<rootPath>/
├── manifest.json         받은 세대 매니페스트 바이트 그대로
├── archives/             압축 미러: 아카이브 산출물
├── files/                압축 미러: 파일 객체 산출물
└── data/                 해제한 원본 트리 <- 게임이 읽는 곳
    ├── content/maps/desert.json
    └── raw/config.txt
```

- `data/<그룹 id>/<엔트리 path>`는 `gpk build`에 쓴 데이터 루트와 같은 배치다.
- 압축 미러는 해제 후에도 남긴다. 다음 세대에서 바뀌지 않은 산출물을 다시 받지 않기 위한 것이며, 디스크는 압축본과 원본을 합한 만큼 필요하다.
- 다운로드·검증 단계에서 실패하면 `manifest.json`과 `data/`는 이전 세대 그대로이며, 다시 호출하면 이미 받은 산출물은 재사용하고 나머지만 받는다.
- `data/` 트리를 바꾸기 시작하면 이전 세대 표식인 `manifest.json`을 먼저 지우고, 모든 해제가 끝난 뒤에만 새 매니페스트를 쓴다. 해제 중에 실패하거나 취소되면 `manifest.json`이 없는 상태로 남고, 다음 호출은 `PreviousReleaseVersion`을 `null`로 보고하며 산출물은 다시 받지 않고 `data`를 전량 다시 풀어 수렴한다.
- 동기화 중에는 `data/`를 읽지 않는다. 세대를 넘어가는 구간에는 두 세대의 파일이 섞여 있다.

## 실패

실패는 모두 `PatchClientException`으로 보고하고 취소는 `OperationCanceledException`이다.

| 메시지 | 원인 | 조치 |
| --- | --- | --- |
| `게시된 객체가 없습니다: <경로>` | HTTP 404. 요청한 세대가 완전히 게시되지 않았거나 URL prefix가 틀림 | 게시 순서와 `baseUrl` 확인 |
| `객체 다운로드가 실패했습니다: <경로> (statusCode=...)` | 404 이외의 HTTP 오류 | 버킷 공개 설정과 경로 확인 |
| `원격 요청이 실패했습니다: <경로> (result=..., error=...)` | 연결 실패, 파일 쓰기 실패 등 | 네트워크와 저장 공간 확인 후 재호출 |
| `원격 요청을 보낼 수 없습니다: <경로> (Insecure connection not allowed)` | 평문 `http` 주소인데 Player Settings의 `Allow downloads over HTTP`가 `Not allowed` | 이 설정을 `Development Only` 이상으로 바꾸거나 `https` 주소를 쓴다. loopback은 제한 대상이 아니다 |
| `세대 매니페스트가 올바르지 않습니다. <필드>: <이유>` | 매니페스트가 CLI 관계 검증을 통과하지 못함 | 게시 산출물 점검 |
| `세대 매니페스트의 releaseVersion이 요청한 값과 다릅니다` | 세대 경로에 다른 세대의 매니페스트가 올라감 | 게시 산출물 점검 |
| `받은 산출물의 checksum이 다릅니다` / `크기가 다릅니다` | 전송 손상 또는 원격 객체 변조 | 재호출. 반복되면 해당 객체를 다시 게시 |
| `해제한 파일의 크기가 다릅니다` | 매니페스트의 `size`와 산출물 내용이 맞지 않음 | 게시 산출물 점검 |
| `산출물을 해제하지 못했습니다: <경로>` | checksum은 맞지만 zstd 프레임이 아닌 산출물 | 게시 산출물 점검. `InnerException`에 원인이 있다 |
| `로컬 파일 작업이 실패했습니다` | 저장 공간·권한 등 로컬 I/O 오류 | `InnerException`을 확인하고 재호출 |
| `로컬 매니페스트가 올바르지 않습니다.` | `<rootPath>/manifest.json`이 손상됨 | 파일을 지우고 재호출. 산출물은 재사용되고 `data`는 전량 다시 푼다 |

재시도·백오프는 넣지 않는다. 호출자가 다시 `SyncAsync`를 부르면 된다.

## 지원 범위

- Unity 6(6000.x), API Compatibility Level .NET Standard 2.1.
- 검증한 환경: Unity 6000.4.4f1 에디터 PlayMode 테스트와 macOS IL2CPP Player. Android·iOS·WebGL은 기기 검증 전이다. WebGL은 파일시스템 가상화 때문에 지원 목표에서 제외한다.
- 진행률 보고, 재시도, `Range` 재개, Supabase 포인터 조회는 제공하지 않는다.

## 직접 테스트

호스트 프로젝트 `unity/GamePatchKit.Unity.Host`의 샘플 씬으로 Unity 안에서 손으로 시험한다.

1. 픽스처 버킷(실제 `gpk build` 산출물, 세대 0과 1)을 로컬 HTTP로 띄운다.

   ```shell
   bash unity/GamePatchKit.Unity.Host/scripts/serve-fixtures.sh
   ```

2. Unity Hub에서 `unity/GamePatchKit.Unity.Host`를 Unity 6000.4.4f1로 열고 `Assets/PatchClientSample/PatchClientSample.unity`를 연 뒤 Play를 누른다.
3. `Base URL`은 씬의 `PatchClientSample` 오브젝트 인스펙터에서 설정한다(기본값 `http://127.0.0.1:8765/`, 실행 중 패널에서도 고칠 수 있다). 화면 패널에서 `releaseVersion`에 `0`을 넣고 **Sync**를 누른다. 로그에 `downloaded=2`가 찍히고 로컬 상태에 `data/content/maps/desert.json`, `data/content/units.json`, `data/raw/config.txt`가 나타난다.
4. `releaseVersion`을 `1`로 바꿔 **Sync**하면 바뀐 산출물 3개만 받고(`reused=1`) `data/content/maps/forest.json`이 추가된다. 다시 `0`으로 **Sync**하면 롤백돼 `forest.json`이 사라진다(`removed=1`). 같은 값으로 다시 누르면 `이미 최신입니다`가 나온다.
5. **Delete local root**는 `rootPath` 폴더를 지워 처음부터 다시 시험할 수 있게 한다. **Cancel**은 진행 중인 동기화를 취소한다.

- `rootPath`는 `Application.persistentDataPath/GamePatchKit`이며 패널에 표시된다.
- 실제 Supabase 버킷을 시험하려면 인스펙터의 `Base URL`에 `https://<project-ref>.supabase.co/storage/v1/object/public/<bucket>/`를 넣고, `releaseVersion`에 `gpk upload`가 출력한 값을 넣는다.
- 기기에서 시험하려면 씬이 Build Settings에 등록돼 있으므로 그대로 빌드한다. 6000.4.4f1에 Android·iOS Build Support 모듈이 필요하다. 로컬 픽스처를 쓰려면 bind 주소를 열어 띄우고 `Base URL`에 PC의 LAN IP를 넣는다.

  ```shell
  bash unity/GamePatchKit.Unity.Host/scripts/serve-fixtures.sh 0.0.0.0 8765
  ```

- 평문 `http` 주소는 Player Settings의 `Allow downloads over HTTP`가 `Not allowed`면 전송 전에 거부된다. loopback(`127.0.0.1`)은 이 제한에서 빠지지만 LAN IP는 걸린다. 호스트 프로젝트는 `Development Only`로 설정해 두었으므로 기기 빌드는 **Development Build**로 만든다. 소비 프로젝트에서 같은 오류가 나면 이 설정을 확인한다.
- 씬을 다시 만들려면 메뉴 `GamePatchKit > Rebuild Sample Scene`을 실행한다.

## 검증 방법

호스트 프로젝트 `unity/GamePatchKit.Unity.Host`에서 실행한다. `GPK_UNITY_EDITOR_PATH`로 에디터 경로를 바꿀 수 있다.

```shell
bash unity/GamePatchKit.Unity.Host/scripts/run-tests.sh          # 에디터 PlayMode 테스트
bash unity/GamePatchKit.Unity.Host/scripts/run-player-tests.sh   # macOS IL2CPP Player에서 같은 테스트
bash unity/GamePatchKit.Unity.Host/scripts/generate-fixtures.sh  # 실제 gpk build로 테스트 픽스처 재생성
```

## 관련 파일

- [`PatchClient.cs`](../../src/GamePatchKit.Unity/Runtime/PatchClient.cs)
- [`PatchSyncResult.cs`](../../src/GamePatchKit.Unity/Runtime/PatchSyncResult.cs)
- [`DataExtractor.cs`](../../src/GamePatchKit.Unity/Runtime/Core/DataExtractor.cs)
- [`ManifestStore.cs`](../../src/GamePatchKit.Unity/Runtime/Core/ManifestStore.cs)
- [`TestPatchClient.cs`](../../src/GamePatchKit.Unity/Tests/Runtime/TestPatchClient.cs)
- [`PatchClientSample.cs`](../../unity/GamePatchKit.Unity.Host/Assets/PatchClientSample/PatchClientSample.cs)
- [`game-patch-kit-unity-client-design.md`](../design/game-patch-kit-unity-client-design.md)

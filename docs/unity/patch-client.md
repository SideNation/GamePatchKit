# Unity 패치 클라이언트 (`PatchClient`)

## 개요

`com.sidenation.gamepatchkit` 패키지의 `PatchClient`는 `gpk upload`가 공개 Storage에 게시한 세대를 Unity 클라이언트에 내려받아 원본 파일 트리로 복원한다. 게임 서버가 알려준 `releaseVersion`을 그대로 넘기면 로컬이 그 세대와 다를 때만 세대 매니페스트와 다시 풀어야 하는 엔트리의 산출물을 받아 `<rootPath>/data` 아래를 맞춘다. 롤백(서버가 이전 세대를 알려주는 경우)도 같은 호출로 처리된다.

- 네트워크는 Unity 메인 스레드의 `UnityWebRequest`가, SHA-256 검증과 zstd 해제는 백그라운드 스레드가 맡는다.
- 동기화가 끝나면 저장 폴더에는 `manifest.json`과 해제한 원본 트리(`data/`)만 남는다. 받은 압축 산출물은 해제한 뒤 지운다.
- Supabase 포인터 테이블은 읽지 않는다. 어느 세대를 쓸지는 [배포 PRD](../prd/gamepatch-kit-distribution-prd.md)대로 게임 서버가 정해 클라이언트에 알린다.

## 설치

Unity 6(6000.x) 프로젝트의 `Packages/manifest.json`에 Git URL로 추가한다. 태그는 저장소의 릴리스 태그(`v<버전>`)를 쓴다.

```json
{
  "dependencies": {
    "com.sidenation.gamepatchkit": "https://github.com/SideNation/GamePatchKit.git?path=/src/GamePatchKit.Unity#v0.1.10"
  }
}
```

- 진행률 보고(`IProgress<PatchSyncProgress>`)는 `0.1.11`, 로컬 상태 조회(`ReadLocalState`)는 `0.1.12`에 추가됐다.
- `v0.1.8` 이하 태그에는 이 패키지가 없다. `v0.1.9`에는 패키지가 있지만 이 문서가 설명하는 압축 미러 삭제·진행 중 표식 동작을 포함하지 않는다. `v0.1.10` 이상을 지정한다.
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
- `rootPath`는 클라이언트가 소유하는 폴더다. 없으면 만든다. `data/` 아래에 매니페스트에 없는 파일은 지우고, 루트 바로 아래의 `<숫자>.json`은 진행 중 표식으로 보고 지울 수 있으므로 다른 파일을 두지 않는다.
- `SyncAsync`는 Unity 메인 스레드에서 호출한다. 다른 스레드에서 호출하면 `InvalidOperationException`이다. 같은 폴더에 대한 `SyncAsync`를 동시에 실행하지 않는다.
- `releaseVersion`은 0 이상이어야 한다.

## 결과

`PatchSyncResult`는 이번 호출이 한 일을 담는다.

| 값 | 의미 |
| --- | --- |
| `IsAlreadyUpToDate` | 로컬이 이미 요청한 세대여서 원격을 한 번도 호출하지 않았다 |
| `PreviousReleaseVersion` | 호출 전 로컬 세대. 처음 받는 폴더면 `null` |
| `DownloadedCount`, `DownloadedBytes` | 이번에 받은 산출물 수와 저장 바이트 |
| `ReusedCount` | 앞서 중단된 호출이 이미 받아 둔 산출물 수. 성공한 동기화는 압축 산출물을 지우므로 평상시에는 0이다. 이름에 세대가 들어간 불변 객체라 이름이 같으면 바이트도 같다는 게시 계약을 신뢰하며, 재사용 산출물의 SHA-256은 다시 계산하지 않는다 |
| `ExtractedCount`, `RemovedCount` | `data`에 새로 푼 파일 수와 새 세대에 없어 지운 파일 수 |

세대가 같지 않으면(크든 작든) 동기화하므로 이전 세대로 돌아가는 호출도 같은 결과 형태를 돌려준다.

## 로컬 상태

동기화하기 전에 저장 폴더가 무엇을 담고 있는지 묻는다. 네트워크를 쓰지 않고 루트를 만들지도 않는다.
`baseUrl`을 알기 전에 부를 수 있도록 static이다.

```csharp
string rootPath = Path.Combine(Application.persistentDataPath, "GamePatchKit");
PatchLocalState state = PatchClient.ReadLocalState(rootPath);

if (state.HasPendingGeneration)
{
    // data에 두 세대가 섞여 있을 수 있다. 읽지 말고 동기화로 마저 끝낸다.
}
else if (state.CompletedReleaseVersion is long version)
{
    // 서버에 닿지 못해도 이 세대는 그대로 쓸 수 있다. 오프라인 진입 판정에 쓴다.
}
else if (state.IsManifestCorrupted)
{
    // 복구 흐름으로 보낸다.
}
```

| 값 | 의미 |
| --- | --- |
| `CompletedReleaseVersion` | `manifest.json`이 가리키는 완료 세대. 파일이 없거나 읽을 수 없으면 `null` |
| `HasPendingGeneration` | 루트에 `<세대>.json` 표식이 남아 있다. 해석할 수 없는 표식도 센다 |
| `IsManifestCorrupted` | `manifest.json`이 있으나 스키마 검증을 통과하지 못했다 |

- **`manifest.json`과 `<세대>.json`을 직접 읽지 않는다.** 두 파일의 형식은 이 패키지의 내부이며 `schemaVersion`은
  예고 없이 바뀔 수 있다. 소비자가 직접 파싱하면 그때 완전한 캐시를 손상으로 오판한다.
- **손상된 `manifest.json`은 예외가 아니다.** 복구 흐름으로 보낼 정상적인 결과이므로 `IsManifestCorrupted`로 돌려준다.
  예외는 로컬 I/O 오류(`PatchClientException`)뿐이다. `rootPath`가 `null`이거나 형식이 잘못된 경우는 표준
  `ArgumentNullException`·`ArgumentException`으로 그대로 나간다.
- "루트 폴더가 없음"과 "루트는 있으나 `manifest.json`이 없음"을 구분하지 않는다. 둘 다 `CompletedReleaseVersion`이
  `null`이다. 그 둘을 다르게 다루려면 `Directory.Exists`를 소비자가 직접 확인한다.
- **같은 루트에 대한 `SyncAsync`와 동시에 부르지 않는다.** 표식 스캔과 매니페스트 읽기는 두 번의 파일 접근이라
  그 사이에 세대가 바뀌면 서로 맞지 않는 스냅샷이 나온다. 표식이 없을 때 스캔한 직후 다른 동기화가 표식을 쓰고
  `data`를 갱신하기 시작하면, 이어진 읽기는 이전 완료 세대를 돌려주어 **섞이는 중인 트리를 완전한 것으로 보이게**
  한다. 읽는 순서를 바꿔도 반대 방향의 어긋남이 생기므로 순서가 아니라 호출 계약으로 막는다.
- 동기화 뒤 정리가 실패해 표식이 남았는지도 같은 호출로 확인한다. `SyncAsync`는 삭제 I/O 오류를 삼키고 성공을
  돌려주기 때문이다.

## 폴더 배치

동기화가 끝난 상태다.

```text
<rootPath>/
├── manifest.json         현재 세대 매니페스트 바이트 그대로
└── data/                 해제한 원본 트리 <- 게임이 읽는 곳
    ├── content/maps/desert.json
    └── raw/config.txt
```

동기화가 진행 중이거나 중단된 상태다.

```text
<rootPath>/
├── manifest.json         아직 이전 세대
├── 7.json                받아 둔 목표 세대 매니페스트
├── archives/, files/     이번 호출이 받은 압축 산출물
└── data/                 두 세대가 섞여 있을 수 있다
```

- `data/<그룹 id>/<엔트리 path>`는 `gpk build`에 쓴 데이터 루트와 같은 배치다.
- 목표 세대 매니페스트를 `<세대>.json`으로 먼저 저장하고, 필요한 산출물을 받아 해제한 뒤, 그 파일을 `manifest.json`으로 한 번에 교체한다. 교체가 끝나면 압축 산출물과 남은 `<세대>.json`을 지운다.
- 다운로드·검증 단계에서 실패하면 `manifest.json`과 `data/`는 이전 세대 그대로다.
- 해제 도중 실패하거나 취소되면 `data/`에 두 세대가 섞이지만 `manifest.json`은 이전 세대를 가리킨 채 남고 `<세대>.json`이 함께 남는다. 같은 세대를 다시 요청하면 매니페스트를 다시 받지 않고, 이미 받아 둔 산출물도 다시 받지 않으며, 남은 엔트리만 풀어 마친다. 다른 세대를 요청하면 로컬에 있는 모든 매니페스트가 보증하는 엔트리만 건너뛰고 나머지를 다시 푼다.
- **`<세대>.json`이 하나라도 있으면 `data/`를 읽지 않는다.** 그 사이에는 두 세대의 파일이 섞여 있을 수 있다. 게임은 `SyncAsync`가 성공으로 끝난 뒤에 읽는다.
- 받은 압축 산출물을 남기지 않으므로 디스크는 `data/` 크기만 필요하다. 대신 이전 세대로 되돌리는 호출은 되돌릴 엔트리가 아카이브 안에 있으면 그 아카이브를 다시 받는다.

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
| `로컬 매니페스트가 올바르지 않습니다.` | `<rootPath>/manifest.json`이 손상됨 | 파일을 지우고 재호출하면 필요한 산출물을 다시 받아 `data`를 전량 다시 푼다 |

재시도·백오프는 넣지 않는다. 호출자가 다시 `SyncAsync`를 부르면 된다.

## 진행률

`IProgress<PatchSyncProgress>`를 받는 오버로드로 진행 상황을 읽는다.

```csharp
var progress = new Progress<PatchSyncProgress>(report =>
{
    // Progress<T>는 생성한 스레드의 컨텍스트로 넘겨주므로 여기서 UI를 만져도 된다.
    bar.fillAmount = (float)report.Ratio;
    label.text = report.Phase switch
    {
        PatchPhase.FetchingManifest => "확인 중",
        PatchPhase.Downloading => $"내려받는 중 {report.CompletedCount}/{report.TotalCount}",
        _ => "압축 푸는 중",
    };
});

PatchSyncResult result = await client.SyncAsync(releaseVersion, progress, _lifetime.Token);
```

| 단계 | 구간 | 총량의 의미 |
| --- | --- | --- |
| `FetchingManifest` | 세대 매니페스트를 받는 중 | 받기 전에는 크기를 알 수 없어 모두 0이다. 퍼센트 대신 불확정 표시를 쓴다 |
| `Downloading` | 산출물을 받는 중 | 전송 바이트. 이미 받아 둔 산출물은 빠진 정확한 값이다 |
| `Extracting` | `data` 아래에 푸는 중 | 푼 원본 바이트 |

- `Ratio`는 **현재 단계 안에서의** 0~1이다. 전송 바이트와 원본 바이트는 압축 때문에 단위가 달라 단계를 가로지르는
  통합 비율은 제공하지 않는다.
- 해시 검증은 산출물마다의 내부 동작이라 단계로 나누지 않고 `Downloading`에 포함한다.
- 로컬이 이미 요청한 세대여서 원격을 호출하지 않으면(`IsAlreadyUpToDate`) **보고가 한 번도 오지 않는다.**
- **할 일이 없는 단계는 보고하지 않는다.** 받을 산출물이 0개면 `Downloading`이, 다시 풀 엔트리가 0개면
  `Extracting`이 한 번도 오지 않는다. 그 단계는 100%에 닿을 수 없어 0%만 내보내게 되기 때문이다. 따라서
  호출자는 `TotalCount == 0`을 따로 분기할 필요가 없고, 받은 스냅샷은 항상 실제로 진행 중인 단계다.
- 동기화 중에는 저장 폴더를 `PatchClient`가 독점한다. 받을 목록과 총량을 첫 바이트 전에 확정하므로, 실행 중
  다른 프로세스가 산출물을 만들어 넣어도 재사용하지 않고 다시 받는다.
- **`Extracting` 단계의 보고는 백그라운드 스레드에서 호출된다.** 위 예시처럼 `Progress<T>`를 쓰면 메인 스레드로
  넘어오지만, `IProgress<T>`를 직접 구현했다면 Unity 객체를 그 자리에서 만지면 안 된다.

## 지원 범위

- Unity 6(6000.x), API Compatibility Level .NET Standard 2.1.
- 검증한 환경: Unity 6000.4.4f1 에디터 PlayMode 테스트와 macOS IL2CPP Player. Android·iOS·WebGL은 기기 검증 전이다. WebGL은 파일시스템 가상화 때문에 지원 목표에서 제외한다.
- 재시도, `Range` 재개, Supabase 포인터 조회는 제공하지 않는다.

## 직접 테스트

호스트 프로젝트 `unity/GamePatchKit.Unity.Host`의 샘플 씬으로 Unity 안에서 손으로 시험한다.

1. 픽스처 버킷(실제 `gpk build` 산출물, 세대 0과 1)을 로컬 HTTP로 띄운다.

   ```shell
   bash unity/GamePatchKit.Unity.Host/scripts/serve-fixtures.sh
   ```

2. Unity Hub에서 `unity/GamePatchKit.Unity.Host`를 Unity 6000.4.4f1로 열고 `Assets/PatchClientSample/PatchClientSample.unity`를 연 뒤 Play를 누른다.
3. `Base URL`은 씬의 `PatchClientSample` 오브젝트 인스펙터에서 설정한다(기본값 `http://127.0.0.1:8765/`, 실행 중 패널에서도 고칠 수 있다). 화면 패널에서 `releaseVersion`에 `0`을 넣고 **Sync**를 누른다. 로그에 `downloaded=2`가 찍히고 로컬 상태에 `data/content/maps/desert.json`, `data/content/units.json`, `data/raw/config.txt`가 나타난다.
4. `releaseVersion`을 `1`로 바꿔 **Sync**하면 바뀐 산출물 3개만 받고 `data/content/maps/forest.json`이 추가된다. 아카이브는 다시 받지 않는다. 다시 `0`으로 **Sync**하면 롤백돼 `forest.json`이 사라진다(`removed=1`). 같은 값으로 다시 누르면 `이미 최신입니다`가 나온다.
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

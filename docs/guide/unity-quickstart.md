# Unity 10분 QuickStart

Unity에서 GamePatchKit이 도는 것을 가장 빨리 확인하는 경로. 로컬에 release를 만들고,
로컬 HTTP로 서빙하고, Play를 눌러 데이터가 설치되는 것까지 본다.

절차·계약의 전체 설명은 [Unity 통합 가이드](unity.md)에 있다. 이 문서는 그중 "일단
돌려보기"만 뽑았다.

## 0. 준비물

| 필요한 것 | 비고 |
| --- | --- |
| Unity `6000.4` 이상 | 검증 기준은 `6000.4.4f1` |
| .NET SDK 10 | `gpk`와 managed plugin 빌드용 |
| `python3` | 로컬 HTTP 서버 |
| macOS | Windows·Android·iOS·WebGL은 [아직 지원 판정 대상이 아니다](unity.md#지원-범위) |

## 1. managed plugin 만들기

Unity는 이 저장소의 csproj를 빌드하지 않으므로 DLL을 먼저 만든다.

```bash
./unity/GamePatchKit.Unity/scripts/prepare.sh
```

`GamePatchKit.Core.dll`·`GamePatchKit.Runtime.dll`·`BouncyCastle.Cryptography.dll`이
`unity/GamePatchKit.Unity/Assets/Plugins/GamePatchKit/`에 생긴다. **`Newtonsoft.Json`은
넣지 않는다** — UPM package `com.unity.nuget.newtonsoft-json`이 가져오고, DLL을 직접
넣으면 충돌한다.

## 2. release 만들고 서빙하기

```bash
./samples/unity-quickstart/serve.sh
```

```text
== Unity Inspector에 넣을 값 ==
  Base Url      : http://127.0.0.1:8080/
  Package Id    : unity-sample-data
  Data Version  : v1-f82ab476...
  Manifest Hash : 7e41ab06...

서빙 중: samples/unity-quickstart/.work/publish (Ctrl+C로 종료)
```

이 창은 그대로 둔다. 네 값은 다음 단계에서 Inspector에 그대로 붙여 넣는다.

이 샘플의 설정은 [.NET quickstart 샘플](../../samples/quickstart)과 **`compression: none`
하나만 다르다.** Unity 지원 범위가 아직 그것뿐이라, zstd로 만든 release를 가리키면
활성화 전에 `runtime.missing-compression-codec`으로 실패한다.

## 3. Unity 프로젝트 열기

가장 빠른 길은 이 저장소의 검증 프로젝트를 그대로 여는 것이다. adapter package와
plugin 배치가 이미 되어 있다.

```text
unity/GamePatchKit.Unity/
```

자기 프로젝트에 붙이려면 세 가지만 하면 된다. 자세한 이유는
[Unity 통합 가이드 2장](unity.md#2-내-unity-프로젝트에-붙이기)에 있다.

1. `Packages/com.sidenation.gamepatchkit.unity`를 복사하거나 `manifest.json`에 로컬
   경로로 추가한다. Newtonsoft 의존성은 이 package가 선언하므로 자동으로 따라온다.
2. 1단계에서 만든 DLL 세 개를 `Assets/Plugins/GamePatchKit/`에 둔다.
3. package의 `Runtime/link.xml`을 함께 옮긴다. **빠뜨리면 Editor에서는 되고 IL2CPP
   빌드에서만 깨진다.**

## 4. 붙여 넣고 Play

빈 Scene에 빈 GameObject를 만들고 `PatchQuickStart` 컴포넌트를 붙인 뒤, Inspector에
2단계에서 출력된 네 값을 넣고 Play를 누른다.

스크립트 전문은
[`unity/GamePatchKit.Unity/Assets/PatchQuickStart.cs`](../../unity/GamePatchKit.Unity/Assets/PatchQuickStart.cs)에
있다. 핵심은 이 부분이다.

```csharp
// transport와 storage는 반드시 main thread에서 만든다.
var storage = new UnityRuntimeStorage();
var runtime = new PackageRuntime(new UnityWebRequestArtifactTransport(_baseUrl), storage);

// 세 값은 서버가 알려준다. Runtime은 최신 release를 스스로 고르지 않는다.
var target = new TargetManifestReference(_packageId, _dataVersion, _manifestHash);

PackageState state = await runtime.InstallOrUpdateAsync(target, progress, _lifetime.Token);
state = await runtime.InstallOptionalGroupsAsync(
    _packageId, new[] { "maps" }, cancellationToken: _lifetime.Token);

// 게임 데이터는 경로를 조합하지 말고 storage로 읽는다.
PackageGroupState core = state.Groups.First(group => group.Name == "core");
using (Stream stream = await storage.OpenInstallationFileAsync(
           core.InstallationKey, "core/config.json", _lifetime.Token))
```

- `InstallOrUpdateAsync`는 **required group만** 받는다. 이 await가 끝나면 게임을 시작할
   수 있고, optional group은 필요한 시점에 따로 받는다.
- codec을 하나도 주지 않았다. `compression: none` release라 그것으로 충분하다.
- `async void Start()`는 예외를 삼키므로 `catch (Exception)`에서 `Debug.LogException`을
   해야 실패가 보인다.

## 5. 확인

Console에 이런 로그가 남으면 성공이다.

```text
[Manifest] 0/0
[Planning] 0/2
[Downloading] 2/2
...
required 완료: stateRevision=1
optional 완료: stateRevision=2
core/config.json => {
  "volume": 0.8,
  "difficulty": "normal"
}
```

`stateRevision`이 1 → 2로 올라간 것이 optional group까지 활성화됐다는 뜻이다.

실제 파일은 여기에 있다.

```text
<Application.persistentDataPath>/GamePatchKit/packages/unity-sample-data/
├── state/package-state.json     # 원자적으로 교체되는 활성 상태
├── cache/                       # content-addressed cache
└── installs/<localId>/          # 실제 게임 데이터
```

처음부터 다시 하려면 `packages/unity-sample-data/` 디렉터리를 지운다. `cache/`만
남기면 다음 실행이 그것을 재사용해 다운로드를 건너뛴다.

## 자주 막히는 곳

| 증상 | 원인 | 해결 |
| --- | --- | --- |
| `UnityWebRequestArtifactTransport must be created on the Unity main thread.` | worker thread에서 생성 | `Start()`·`Awake()` 같은 main thread에서 만든다 |
| `baseUrl must end with '/'.` | base URL 끝의 `/` 누락 | `http://127.0.0.1:8080/` 처럼 `/`로 끝낸다 |
| 모든 요청이 404 | 서버가 publish **루트**를 서빙하지 않음 | base URL 바로 아래에 `<packageId>/manifests/...`가 보여야 한다 |
| `runtime.missing-compression-codec` | 목표 manifest가 zstd artifact를 참조 | `compression: none`으로 만든 release를 쓰거나 codec을 주입한다 |
| loopback이 아닌 `http://` 주소가 차단됨 | Player Settings의 **Allow downloads over HTTP**가 `Not allowed` | HTTPS를 쓰거나 그 설정을 바꾼다. `127.0.0.1`은 이 제한을 받지 않는다 |
| IL2CPP 빌드에서만 실패 | `link.xml` 누락 | package의 `Runtime/link.xml`을 프로젝트에 포함한다 |
| Newtonsoft 관련 assembly 충돌 | Newtonsoft DLL을 직접 넣음 | DLL을 지우고 `com.unity.nuget.newtonsoft-json` package만 쓴다 |
| Play해도 아무 로그가 없다 | `async void`가 예외를 삼킴 | `catch (Exception e) { Debug.LogException(e); }`를 넣는다 |

## 다음에 읽을 것

| 하고 싶은 일 | 문서 |
| --- | --- |
| 내 프로젝트에 제대로 붙이기 | [Unity 통합 가이드](unity.md) |
| 내 데이터로 release 만들기 | [5분 QuickStart](quickstart.md) · [package 설정과 파일 선택](package-config.md) |
| client 동작·복구 규칙 이해하기 | [Runtime 통합 가이드](runtime-integration.md) |
| CDN에 올리고 서명하기 | [publish와 서명 운영](publishing.md) |
| adapter가 보장하는 것 확인하기 | [Unity Runtime adapter 계약](../contracts/unity-adapter.md) |

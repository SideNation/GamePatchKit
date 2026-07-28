# Runtime 통합 가이드

애플리케이션이 published release를 다운로드·검증·활성화하는 방법. 일반 .NET 환경은
`GamePatchKit.DotNet` adapter를 그대로 쓰고, Unity 같은 외부 host는 같은 interface를
직접 구현한다.

구현 계약은 [Runtime 상태 머신](../contracts/runtime.md),
[DotNet adapter](../contracts/dotnet-adapter.md),
[Unity Runtime adapter](../contracts/unity-adapter.md)에 있다.

## Runtime이 하지 않는 일

**Runtime은 최신 release나 배포 환경을 스스로 선택하지 않는다.** 신뢰하는 host나 서버가
`packageId`·`dataVersion`·`manifestHash` 세 값을 주면, Runtime은 정확히 그 manifest만
목표로 삼는다. 자세한 책임 경계는 [publish와 운영](publishing.md)을 참조한다.

멀티플레이 client는 최신 release를 독립적으로 고르지 않고, 접속할 서버가 지정한 정확한
client package target manifest를 사용한다.

## .NET에서 시작하기

```csharp
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

var runtime = new PackageRuntime(
    new HttpArtifactTransport(httpClient),          // httpClient.BaseAddress는 publish root, '/'로 끝나야 한다
    new FileSystemRuntimeStorage(runtimeRoot),
    DefaultCompressionCodecs.Create());

var target = new TargetManifestReference(
    "sample-game-client-data",
    "v1-0123...cdef",   // 서버가 알려준 dataVersion
    "89ab...4567");     // 서버가 알려준 manifestHash

PackageState state = await runtime.InstallOrUpdateAsync(
    target,
    progress: new Progress<PatchProgress>(p => Report(p)),
    cancellationToken: token);
```

`InstallOrUpdateAsync`는 target manifest의 **required group만** 준비한다. optional group은
필요한 시점에 따로 요청한다.

```csharp
PackageState state = await runtime.InstallOptionalGroupsAsync(
    "sample-game-client-data",
    new[] { "maps", "voice" },
    cancellationToken: token);
```

이 호출은 별도 target pointer를 받지 않는다. 현재 active `PackageState`가 가리키는
manifest에서 요청한 group만 준비하며 전역 `dataVersion`·`manifestHash`는 그대로 둔다.

서명을 검증하려면 신뢰 key를 생성자에 넘긴다.

```csharp
var runtime = new PackageRuntime(
    transport,
    storage,
    DefaultCompressionCodecs.Create(),
    trustedSigningKeys: new TrustedSigningKeys(new[] { oldPublicKey, newPublicKey }),
    requireSignature: true);
```

- `trustedSigningKeys`가 `null`(기본값)이면 signature를 아예 확인하지 않는다.
- key가 있고 `requireSignature: false`면 signature가 없는 release는 통과하지만, 있으면
  반드시 신뢰 key로 검증되어야 한다.
- `requireSignature: true`면 signature 누락도 거부한다.
- `requireSignature: true`인데 신뢰 key가 없으면 생성자가 `ArgumentException`을 던진다.
  검증 수단 없이 "필수"만 요구하면 위조와 진짜 서명을 구분할 수 없기 때문이다.

ASP.NET·worker가 시작 전 필수 package를 보장하려면 별도 wrapper 없이 시작 경로에서
`InstallOrUpdateAsync`를 호출하고 예외를 그대로 전파시키면 된다.

## 네 가지 상태 개념의 차이

이름이 비슷해 헷갈리기 쉬운 네 가지다.

| 개념 | 누가 소유하나 | 무엇인가 |
| --- | --- | --- |
| **target manifest reference** | host/서버 | Runtime에 "이 release로 가라"고 전달하는 `packageId`·`dataVersion`·`manifestHash`. 로컬에 저장하지 않는다 |
| **active pointer** | Runtime | required group까지 준비돼 **현재 활성화된** 로컬 `dataVersion`·`manifestHash` |
| **`PackageState`** | Runtime | active pointer와 group별 설치 상태를 원자적으로 기록하는 package별 로컬 상태. package당 하나 |
| **group install state** | Runtime | optional group이 **어느 manifest 기준으로** 설치·검증됐는지 (`ready`/`notInstalled`/`stale`) |

target reference는 매 실행마다 host가 새로 주는 입력이고, 나머지 셋은 Runtime이
관리하는 로컬 상태다.

### required-only 최초 설치 → optional 후속 설치

```text
1. host가 target reference 전달
2. InstallOrUpdateAsync
   └─ required group만 다운로드·검증·승격
   └─ 최초 PackageState commit (stateRevision = 1)
      · required group  → ready,        verifiedManifestHash = active.manifestHash
      · optional group  → notInstalled
3. 게임이 "maps"를 필요로 하는 시점
   └─ InstallOptionalGroupsAsync(packageId, ["maps"])
      └─ active manifest 기준으로 maps만 준비
      └─ active pointer는 그대로, maps만 ready로 바꾼 새 revision commit
```

전역 release 갱신에서 optional group은 이렇게 처리된다.

| 상황 | 결과 |
| --- | --- |
| 설치하지 않은 group | `notInstalled` 유지 |
| 이전·목표 manifest에서 경로·크기·`fileHash`가 전부 같음 | 다운로드 없이 재사용, `verifiedManifestHash`만 새 manifest로 갱신 |
| 파일 상태가 달라짐 | installation은 보존하되 group을 `stale`로 |
| target manifest에서 사라진 group | state에서 제거하고 활성 데이터로 노출하지 않음 |
| optional이 required로 바뀜 | 전역 active pointer 교체 **전에** 반드시 준비 |

## activation batch

**한 호출의 target group 집합 전체가 하나의 commit 단위다.**

- 다운로드·artifact 검증·staging은 group별로 병렬 수행할 수 있지만 이 과정은 committed
  `PackageState`를 건드리지 않는다.
- 모든 target group을 검증된 immutable installation으로 승격한 **뒤에만** state commit을
  시작한다.
- group 하나라도 실패하면 batch 전체의 state 변경을 취소하고 이전 state를 유지한다.
- 성공 시 `groups[]` 전체를 포함한 state를 **한 번** 교체하고 `stateRevision`도 한 번만
  증가시킨다. reader는 batch 전 state 또는 batch 후 state만 관찰하며, **일부 group만
  `ready`인 중간 state는 관찰하지 않는다.**
- 서로 다른 요청은 별도 batch이며 자동으로 하나의 transaction으로 합쳐지지 않는다.
- state commit 직전 writer lock 안에서 작업 시작 snapshot의 `stateRevision`과 active
  `dataVersion`·`manifestHash`를 다시 확인한다. 달라졌으면 stale state를 쓰지 않고 최신
  state 기준으로 최대 3회 재계획한다. 이미 검증한 cache·installation은 재사용한다.

## `package-state.json`

package마다 하나이며 Runtime이 canonical JSON 모델·직렬화·검증을 소유한다. **adapter는
byte를 해석하지 않는다.** 파일이 없으면 활성 package가 없는 상태다.

```json
{
  "active": {
    "dataVersion": "v1-...",
    "manifestHash": "..."
  },
  "groups": [
    {
      "installationKey": "sample-game-client-data/6f1c...",
      "name": "core",
      "status": "ready",
      "verifiedManifestHash": "..."
    },
    {
      "name": "maps",
      "status": "notInstalled"
    }
  ],
  "packageId": "sample-game-client-data",
  "schemaVersion": 1,
  "stateRevision": 1
}
```

| 필드 | 형식 | 규칙 |
| --- | --- | --- |
| `schemaVersion` | integer | v1에서는 `1` |
| `stateRevision` | integer | 최초 commit이 `1`, 이후 성공한 commit마다 1 증가. I-JSON safe integer 상한을 넘을 수 없다 |
| `packageId` | string | state가 속한 package |
| `active.dataVersion` | string | required group까지 활성화된 전역 논리 버전 |
| `active.manifestHash` | string | lowercase hex64 |
| `groups[]` | array | active manifest의 모든 group을 이름의 UTF-8 byte ordinal 순으로 정확히 한 번 |
| `groups[].name` | string | manifest에 선언된 group 이름 |
| `groups[].status` | string | `ready` / `notInstalled` / `stale` |
| `groups[].verifiedManifestHash` | string | `ready`·`stale`에서 필수 |
| `groups[].installationKey` | string | `ready`·`stale`에서 필수인 비어 있지 않은 opaque key |

| `status` | 의미 |
| --- | --- |
| `ready` | active manifest 기준으로 사용 가능 |
| `notInstalled` | 다운로드하지 않은 optional group |
| `stale` | byte는 보존하지만 active release에서 사용할 수 없음 |

### 불변 조건

- `groups[]`는 active manifest의 모든 group을 정확히 한 번 포함하고 알 수 없는 group을
  포함하지 않는다.
- 모든 required group은 `ready`이고 `verifiedManifestHash`가 `active.manifestHash`와 같다.
- `ready` optional group도 `verifiedManifestHash`가 `active.manifestHash`와 같다.
- `notInstalled`와 `stale`는 **optional group에만** 허용한다.
- `notInstalled` group에는 `installationKey`가 없다.
- `stale` group의 installation은 보존할 수 있지만 애플리케이션에 활성 데이터로 노출하지
  않는다.
- 모든 `installationKey`는 준비·검증이 끝난 immutable installation을 가리킨다.
- group별 독립 `dataVersion`이나 target manifest reference는 만들지 않는다.

`installationKey`는 **storage adapter가 해석하는 불투명 식별자**이며 절대 OS 경로를
저장하지 않는다. `installing` 상태와 다운로드 진행률, signature, secret은 state에 기록하지
않고 staging·cache의 일시 상태로 관리한다.

### filesystem 배치 (DotNet adapter)

```text
<runtimeRoot>/
└── packages/
    └── <packageId>/
        ├── state/
        │   ├── package-state.json
        │   └── package-state.lock
        ├── cache/
        ├── installs/<installationKey의 로컬 부분>/
        └── staging/<operationId>/
```

### writer lock과 원자적 교체

- package별 writer는 **하나만** 허용한다. lock은 `state/package-state.lock`을
  `FileShare.None`으로 여는 **독점 OS handle**이며, 파일이 존재하는지가 아니라 handle을
  잡을 수 있는지로 판단한다. 이미 열려 있으면 짧게 대기하며 재시도하고 전체 30초 예산을
  넘기면 마지막 `IOException`을 담아 실패한다.
- state 교체는 같은 디렉터리에 `package-state.json.tmp-<guid>`를 쓰고
  `Flush(flushToDisk: true)` 후 `File.Move(..., overwrite: true)` 한 번으로 끝낸다. 이
  rename은 Windows·Unix 모두에서 원자적이므로 reader는 항상 이전 전체 byte 또는 새 전체
  byte만 본다.
- **state 파일을 in-place로 덮어쓰지 않는다.** 교체 실패 시 임시 파일만 지우고 기존
  `package-state.json`은 열지도 않는다.
- state 교체 전에는 기존 state와 참조된 installation을 변경·삭제하지 않는다.

### 손상 복구

- **손상되거나 알 수 없는 schema의 state는 활성 근거로 사용하지 않는다.** host가 제공한
  신뢰 가능한 target manifest를 다시 검증하고 cache·installation의 file hash를 확인해 새
  state를 구성하며, 복구 중 기존 cache·installation은 보존한다.
- `InstallOrUpdateAsync`는 새 target을 먼저 검증한 뒤 로컬 state를 읽는다. **현재 active
  manifest를 어떤 이유로든 가져오거나 검증할 수 없으면** state를 신뢰 근거로 쓰지 않고
  검증된 target에서 다시 만든다. 과거 release의 manifest를 회수했을 때 client가 영구히
  갱신 불가 상태에 빠지는 것을 막는다. 이때 optional group은 `notInstalled`로 떨어지므로
  다시 설치해야 하지만 데이터 자체는 지우지 않는다.
- optional group의 기존 installation을 target 기준으로 재검증하지 못했고 그 데이터의
  마지막 검증 manifest가 **곧 target일 때**는 `stale`이 아니라 `notInstalled`로 전이한다.
  `stale`은 "이전 manifest 기준으로는 유효하다"는 뜻인데 방금 그 manifest로 검증에
  실패했으므로 거짓 주장이기 때문이다.
- 신뢰 가능한 target manifest reference가 없으면 cache나 디렉터리 이름만으로 active
  release를 추정하지 않고 활성 package가 없는 상태를 반환한다. optional 설치는 유효한
  active state가 없으면 실패한다.
- 취소·실패·프로세스 종료 시 검증된 cache는 보존하고 다음 실행에서 이어받는다.

## 외부 host 구현하기

외부 host는 호환되는 `GamePatchKit.Core`·`GamePatchKit.Runtime` assembly를 소비하고
필요한 interface만 애플리케이션 코드에서 구현한다. **download plan, manifest·hash 검증,
retry, 활성화 규칙을 복제하지 않는다.**

### `IArtifactTransport`

immutable manifest·signature·artifact를 읽기 전용 stream으로 연다.

- `OpenManifestAsync` / `OpenManifestSignatureAsync` / `OpenArtifactAsync`가 읽기 가능한
  stream을 반환한다. **반환 stream은 Runtime이 dispose한다.**
- artifact 경로는 검증된 manifest의 content-addressed 상대 경로가 그대로 전달된다. 이미
  자기 `packageId/artifacts/...` 접두사를 포함하므로 다시 붙이지 않는다.
- 큰 payload를 managed memory에 모두 올리지 말고 stream으로 전달한다.
- 재시도 가능한 전송 실패는 `ArtifactTransportException(IsTransient: true)`로 보고한다.
  Runtime이 최대 3회 시도하며 cancellation은 재시도하지 않는다. **retry 정책은 adapter가
  아니라 Runtime의 몫이다.**

> **`IsNotFound`는 조심해서 다뤄야 한다.**
> `OpenManifestSignatureAsync`가 "이 리소스가 존재하지 않는다고 **확인**됐을 때"에만
> `IsNotFound: true`를 쓴다(예: 실제 HTTP 404). 확실하지 않으면 기본값 `false`를 유지한다.
> 잘못 `true`로 설정하면 401·403처럼 다른 이유로 실패한 응답이 "signature 없음"으로
> 취급되어, **실제로 서명된 release가 검증 없이 unsigned로 넘어갈 수 있다.**
> `HttpArtifactTransport`는 HTTP 404만 `IsNotFound: true`로 표시하고 401/403/410을 포함한
> 다른 모든 4xx/5xx는 `false`로 둔다.

### `IRuntimeStorage`

package별 writer lock, `PackageState` 읽기·원자적 교체, content-addressed cache, staging
stream과 immutable installation 승격을 제공한다.

| 멤버 | 반드시 지켜야 할 것 |
| --- | --- |
| `AcquirePackageWriterLockAsync` | 같은 package의 process/thread 전체 writer를 배타적으로 막는다. **lock 파일 존재만으로 판단하면 안 된다** |
| `ReadPackageStateAsync` | 완전한 old 또는 new byte snapshot만 반환한다 |
| `ReplacePackageStateAsync` | 임시 파일 flush + atomic rename처럼 state 전체를 한 번에 교체한다. **in-place write 금지** |
| `CreateCacheWriterAsync` | `Content`는 아직 공개되지 않은 임시 object다. `CommitAsync` 성공 후에만 보여야 하고, commit 없이 dispose하면 기존 검증 cache를 바꾸지 않는다 |
| `CreateStagingAreaAsync` / `PromoteAsync` | promotion 전에는 reader에게 노출하지 않는다. promotion은 완성된 group directory를 immutable installation으로 승격하고 **비어 있지 않은 opaque key**를 반환한다 |
| `GetInstallationFilePathsAsync` | installation이 가진 모든 정규화 상대 경로를 반환한다. Runtime이 target group의 경로 집합과 정확히 같은지 확인하므로, 삭제된 파일이 남은 installation을 재사용하지 않는다 |
| `CreateScratchStreamAsync` | 읽기·쓰기·seek 가능한 임시 stream. 압축 bundle을 크기 제한 안에서 해제할 때 쓴다 |

cache commit은 같은 content-addressed 경로의 손상 object를 **덮어써 복구할 수 있어야
한다.** Runtime은 전송 byte를 크기와 SHA-256으로 검증한 뒤 commit하고 다시 연다.

### codec 주입

zstd가 필요한 host는 호환되는 `ICompressionCodec`을 주입한다. **목표 manifest가 참조하는
모든 codec을 지원해야 하며**, 주입되지 않은 codec을 요구하면 활성화 전에
`runtime.missing-compression-codec`으로 실패한다.

- desktop(Windows·Linux·macOS의 x64·arm64)은 기본
  `GamePatchKit.Compression.NativeCompressions`를 재사용할 수 있다.
- **iOS IL2CPP는 `NativeCompressions.Zstandard` preview의 기본 지원 대상이 아니다.** 해당
  target은 호환 zstd codec을 별도로 구현하거나 package 설정에서 압축을 쓰지 않아야 한다.
- `Core`와 `Runtime`의 public API는 `UnityEngine`·`NativeCompressions.Zstandard` type을
  노출하지 않는다.

### 구현이 맞는지 확인하기

`GamePatchKit.Conformance`의 `ConformanceTestBase`를 상속해 factory 메서드만 구현하면
DotNet adapter와 동일한 시나리오를 그대로 실행할 수 있다. [배포 산출물과 버전
정책](distribution.md#adapter-conformance-suite)을 참조한다.

### Unity

Unity host는 `UnityWebRequest`·`Application.persistentDataPath` 기반 구현을 쓴다. 이
저장소는 embedded package
[`unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity`](../../unity/GamePatchKit.Unity/Packages/com.sidenation.gamepatchkit.unity)에
`UnityWebRequestArtifactTransport`와 `UnityRuntimeStorage`를 제공한다.

```bash
./unity/GamePatchKit.Unity/scripts/prepare.sh   # Core·Runtime·BouncyCastle managed plugin 생성
./unity/GamePatchKit.Unity/scripts/test.sh      # EditMode 테스트
./unity/GamePatchKit.Unity/scripts/build-macos-il2cpp.sh
```

- Unity lifecycle과 애플리케이션 중단은 `CancellationToken`으로 Runtime에 전달한다.
- progress는 `IProgress<PatchProgress>`를 UI에 연결한다.
- 기준 환경은 Unity `6000.4.4f1`, .NET Standard 2.1이며 **첫 지원 범위는
  `compression: none` + macOS Standalone이다.** Android·iOS·WebGL·Windows Player는 아직
  지원 판정 대상이 아니다.
- `GamePatchKit.DotNet`은 `net10.0` adapter이므로 Unity에서 참조하지 않는다.
- 자세한 내용은 [Unity Runtime adapter](../contracts/unity-adapter.md)를 참조한다.

## 진행률과 오류

`IProgress<PatchProgress>`는 `Manifest`, `Planning`, `Downloading`, `Staging`,
`Activating`, `Completed` 단계를 보고하며 artifact 상대 경로, group, 파일·byte 진행과
현재 retry 횟수를 포함한다. 재시도 때문에 실제 전송 byte가 계획 byte보다 많아질 수 있다.

예상 가능한 실패는 `RuntimeException.Error`의 `GamePatchKitError`로 확인한다.
`OperationCanceledException`은 변환하지 않고 그대로 전달한다.

| 코드 | 의미 |
| --- | --- |
| `runtime.manifest-invalid` | manifest hash·canonical·schema·identity·참조 검증 실패 |
| `runtime.signature-invalid` | signature 누락(필수 모드)·문서 손상·알 수 없는 key ID·서명 검증 실패 |
| `runtime.state-invalid` | state schema·불변 조건·installation 참조 또는 revision 상한 위반 |
| `runtime.state-conflict` | 세 번의 activation 시도가 모두 concurrent revision과 충돌 |
| `runtime.artifact-corrupted` | 전송·cache object, 압축 해제·bundle 또는 복원 file 검증 실패 |
| `runtime.missing-compression-codec` | 선택 group이 요구하는 codec 미주입 |
| `runtime.transport-failed` | 재시도 불가 전송 오류 또는 retry 소진 |
| `runtime.staging-failed` | group staging 또는 immutable promotion 실패 |
| `runtime.activation-failed` | package writer lock 또는 atomic state 교체 실패 |

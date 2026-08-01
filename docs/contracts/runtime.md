# Runtime 상태 머신

## 개요

`GamePatchKit.Runtime`은 host가 지정한 정확한 target manifest를 검증하고 required
group 또는 요청한 optional group을 준비한 뒤 package별 `PackageState`를 한 번
교체한다. 최신 release나 배포 환경을 선택하지 않으며 `releaseId`, channel,
optional group 전용 pointer를 만들지 않는다.

Runtime은 `GamePatchKit.Core`만 참조한다. 전송·filesystem·플랫폼 압축 구현은
`IArtifactTransport`, `IRuntimeStorage`, `ICompressionCodec`으로 주입한다.

## 사용법

host는 서버나 신뢰하는 설정에서 받은 `packageId`, `dataVersion`, `manifestHash`로
target을 만든다. 최초 설치와 전역 갱신은 target manifest의 required group만
다운로드한다.

```csharp
using GamePatchKit.Core;
using GamePatchKit.Runtime;

public static class RuntimeInstall
{
    public static Task<PackageState> InstallAsync(
        IArtifactTransport transport,
        IRuntimeStorage storage,
        ICompressionCodec zstdCodec,
        CancellationToken cancellationToken)
    {
        var runtime = new PackageRuntime(
            transport,
            storage,
            new[] { zstdCodec });
        var target = new TargetManifestReference(
            "game-data",
            "v1-<64자리-lowercase-sha256>",
            "<64자리-lowercase-manifest-sha256>");

        return runtime.InstallOrUpdateAsync(
            target,
            cancellationToken: cancellationToken);
    }
}
```

optional group은 별도 target pointer를 받지 않는다. 현재 canonical
`PackageState.active`가 가리키는 manifest에서 요청 group만 준비하며 active
`dataVersion`과 `manifestHash`는 유지한다.

```csharp
PackageState state = await runtime.InstallOptionalGroupsAsync(
    "game-data",
    new[] { "maps", "voice" },
    cancellationToken: cancellationToken);
```

같은 호출의 group 집합은 하나의 activation batch다. 다운로드와 staging 도중에는
committed state를 바꾸지 않고, 모든 group의 immutable installation 승격이 끝난 뒤
`groups[]` 전체를 포함한 state를 revision 한 번으로 교체한다. 하나라도 실패하면
이전 state가 유지된다.

## target manifest 검증

Runtime은 staging 전에 다음 순서로 target을 검증한다.

0. manifest 응답을 최대 64 MiB까지만 읽는다. 이를 넘으면 더 읽지 않고
   `runtime.manifest-invalid`로 중단하며 재시도하지 않는다.
1. 전송받은 원본 manifest byte의 SHA-256을 target `manifestHash`와 비교한다.
2. strict UTF-8 단일 JSON object, 중복 key 금지, canonical JSON byte 일치를 확인한다.
3. Core schema model과 group·file·artifact·part·bundle 참조, 정렬, 중복,
   content-addressed path를 검증한다.
4. target `packageId`·`dataVersion`과 manifest 값을 비교하고 `dataVersion`을
   재계산한다.
5. 선택 group이 요구하는 모든 compression codec이 주입됐는지 확인한다.
6. `PackageRuntime` 생성자에 `TrustedSigningKeys`를 넘겼으면(11단계)
   `OpenManifestSignatureAsync`로 `manifest.sig`를 받아 검증한다 — 아래 "서명
   검증" 참고. 넘기지 않았으면 이 단계는 완전히 건너뛰고 signature stream을
   열지도 않는다(08단계 기본 동작과 동일).

0단계가 필요한 이유는 manifest만 통째로 메모리에 올라가는 유일한 응답이기 때문이다.
hash로 거부하려면 먼저 다 읽어야 하므로, 상한이 없으면 고장났거나 악의적인 endpoint가
첫 무결성 검사가 돌기도 전에 client 메모리를 고갈시킬 수 있다. artifact object는 manifest가
이미 선언한 크기를 상한으로 streaming 검증하므로 별도 상한이 필요 없다. 64 MiB는 PRD 기준
fixture(1만 파일·1GiB source)의 canonical manifest보다 약 한 자릿수 크고 peak RSS 예산
512 MiB보다 충분히 작다.

## 서명 검증 (11단계)

`TrustedSigningKeys`는 신뢰하는 raw Ed25519 public key 집합을 key ID(`ed25519-<hex64>`,
`GamePatchKit.Core.Signatures.Ed25519Signatures.DeriveKeyId`로 파생)로 색인한다. key
rotation 기간에는 구·신 key를 동시에 담아 생성자에 넘기면 된다.

```csharp
var trustedKeys = new TrustedSigningKeys(new[] { oldPublicKey, newPublicKey });
var runtime = new PackageRuntime(
    transport,
    storage,
    new[] { zstdCodec },
    trustedSigningKeys: trustedKeys,
    requireSignature: true);
```

- `trustedSigningKeys`가 `null`이면(기본값) signature를 아예 확인하지 않는다.
- `trustedSigningKeys`가 있고 `requireSignature`가 `false`면: signature가 없으면
  통과시키고(unsigned release 허용), signature가 있으면 반드시 신뢰 key로
  검증돼야 한다 — 존재하는데 검증 실패하거나 key ID가 신뢰 목록에 없으면 언제나
  거부한다.
- `requireSignature`가 `true`면 signature 누락도 거부한다.
- `requireSignature: true`인데 `trustedSigningKeys`가 없으면 생성자가
  `ArgumentException`을 던진다 — 검증 수단 없이 "필수"만 요구하면 위조와 진짜
  서명을 구분할 수 없기 때문이다(Packager `ReleaseVerifyRequest`와 동일한 규칙).
- signature 응답은 최대 4 KiB까지만 읽는다. 이를 넘거나, 문서가 canonical JSON이
  아니거나, `keyId`가 신뢰 목록에 없거나, 64-byte 서명이 검증에 실패하면 모두
  `runtime.signature-invalid`로 거부한다.
- signature를 가져오는 transport 호출이 실패하면, `ArtifactTransportException.
  IsNotFound`가 `true`인 경우(adapter가 "확인된 not-found"라고 명시한 경우,
  예: 실제 HTTP 404)만 "signature 없음"과 동일하게 취급한다 - manifest·artifact의
  그것과 달리 signature 부재는 정상적인 unsigned release일 수 있으므로,
  있고-없음의 판단은 전적으로 `requireSignature`가 맡는다. 그 외 모든 실패 —
  재시도 가능한(transient) 실패가 소진된 경우든, "확인되지 않은" non-transient
  실패(401·403처럼 서버·프록시가 다른 이유로 4xx/5xx를 돌려주는 경우)든 —
  는 `requireSignature`와 무관하게 항상 `runtime.transport-failed`로 거부한다.
  signature 요청에 지속적으로 실패를 돌려줄 수 있는 공격자나 오동작하는 중간
  장비가 있어도, 그 실패가 진짜 not-found로 확인되지 않는 한 실제로 서명된
  release를 조용히 unsigned로 넘길 수 없다는 뜻이다.

  `IArtifactTransport`를 구현하는 모든 adapter(외부 host 포함)는 `IsTransient`를
  정확히 분류해야 하는 것과 같은 수준으로, **`IsNotFound`는 "이 리소스가 존재하지
  않는다고 확인됐을 때"에만 `true`로 설정해야 한다** — 확실하지 않으면 기본값
  `false`를 그대로 둬야 한다(잘못 `true`로 설정하면 실제로 서명된 release가
  unsigned로 취급될 수 있다). `HttpArtifactTransport`와
  `UnityWebRequestArtifactTransport`는 HTTP 404를 `IsNotFound: true`로 표시하고
  401/403/410을 포함한 다른 모든 4xx/5xx는 `false`로 둔다
  (`TestHttpArtifactTransport.OpenArtifactAsync_NotFound_IsConfirmedAbsence`/
  `OpenArtifactAsync_OtherPermanentClientErrors_AreNotConfirmedAbsence`).
- 예외는 HTTP 400 하나다. 일부 object store는 없는 오브젝트에 400을 돌려주고 진짜
  상태를 본문에 담는다(Supabase Storage:
  `{"statusCode":"404","error":"not_found","message":"Object not found"}`). 400을
  "확인되지 않은 실패"로 두면 그런 host에 올린 **unsigned release는 설치 자체가
  불가능해진다** — signature 요청이 `runtime.transport-failed`로 떨어지기 때문이다.
  그래서 두 adapter는 400에 한해 응답 본문을 읽고, `ArtifactTransportResponseBody.
  ConfirmsNotFound`가 **본문이 스스로 상태를 404라고 말할 때만** `IsNotFound: true`로
  올린다. 400이라는 사실 자체가 아니라 store의 진술을 근거로 삼는 것이라 위 규칙을
  벗어나지 않는다. 본문이 없거나, JSON이 아니거나, `statusCode`가 404가 아니거나,
  `ArtifactTransportResponseBody.MaximumInspectedBytes`(4 KiB)를 넘으면 `false`로
  남는다. 이 판정이 새로운 공격 표면을 만들지도 않는다: 그 본문을 주입할 수 있는
  상대는 진짜 404도 똑같이 주입할 수 있고, 그것이 `requireSignature`가 존재하는
  이유다.
- 검증은 `ReleaseIdentity.ComputeCanonicalBytes`로 이미 확인한 canonical manifest
  byte(= `manifestHash`가 가리키는 그 byte)를 대상으로 하며, Core의
  `Ed25519Signatures.Verify`(BouncyCastle Ed25519, netstandard2.1)가 Packager의
  서명(`Ed25519ManifestSigner`, 동일 BouncyCastle 구현)과 같은 primitive를 쓴다.

cache object와 installation 파일 비교도 manifest가 선언한 크기까지만 읽고 한 byte를 더
확인해 초과 여부를 판단한다. EOF까지 다 읽고 나서 크기를 비교하면, 손상돼 커진 cache 항목이나
끝나지 않는 adapter stream이 모든 state 로드와 download plan을 멈춰 세운다.

## 복구 전이

- `InstallOrUpdateAsync`는 새 target을 먼저 검증한 뒤 로컬 state를 읽는다. 이때 **현재 active
  manifest를 어떤 이유로든 가져오거나 검증할 수 없으면** state를 신뢰 근거로 쓰지 않고
  검증된 target에서 다시 만든다. 과거 release의 manifest를 회수하면 client가 영구히 갱신
  불가 상태에 빠지던 문제를 막는다. 이때 optional group은 `notInstalled`로 떨어지므로 다시
  설치해야 한다. 데이터 자체는 지우지 않는다.
- optional group의 기존 installation을 target 기준으로 재검증하지 못했고 그 데이터가 마지막
  검증된 manifest가 **곧 target일 때**는 `stale`이 아니라 `notInstalled`로 전이한다. `stale`은
  "이전 manifest 기준으로는 유효하다"는 뜻인데 방금 그 manifest로 검증에 실패했으므로 거짓
  주장이며, `PackageStateValidator`도 active manifest를 가리키는 `stale` group을 거부한다.
  이 전이가 없으면 그 release로의 rollback이 매번 `runtime.state-invalid`로 실패한다.
- optional group 데이터가 온전하면 rollback에서 그대로 `ready`로 재연결하며 다운로드하지
  않는다.

## `PackageState`

state는 package마다 하나이며 Runtime이 canonical JSON 모델·직렬화·검증을 소유한다.
adapter는 byte를 해석하지 않는다.

```json
{
  "active": {
    "dataVersion": "v1-...",
    "manifestHash": "..."
  },
  "groups": [
    {
      "installationKey": "install/core/0001",
      "name": "core",
      "status": "ready",
      "verifiedManifestHash": "..."
    },
    {
      "name": "maps",
      "status": "notInstalled"
    }
  ],
  "packageId": "game-data",
  "schemaVersion": 1,
  "stateRevision": 1
}
```

- 최초 revision은 `1`이고 commit마다 한 번 증가한다.
- `groups[]`는 active manifest의 모든 group을 UTF-8 byte ordinal 순서로 정확히 한
  번 포함한다.
- required group과 `ready` optional group은 active `manifestHash`로 검증돼야 한다.
- `notInstalled`와 `stale`는 optional group에만 허용한다.
- `stale`는 이전 `verifiedManifestHash`와 immutable `installationKey`를 보존하지만
  활성 데이터로 노출하지 않는다.
- `installationKey`는 비어 있지 않은 opaque 식별자다. 절대 OS 경로를 넣지 않는다.
- `installing`, 진행률, signature, secret은 state에 기록하지 않는다.
- revision은 I-JSON safe integer 상한을 넘길 수 없다.

`PackageStateSerializer`는 canonical byte를 만들고 non-canonical byte, 중복·unknown
field와 상태별 field 위반을 거부한다. `PackageStateValidator`는 active manifest와
required/optional 불변 조건을 검증한다.

손상 state나 누락 installation은 활성 근거로 쓰지 않는다. 신뢰하는 target이 있는
전역 설치는 기존 cache·installation을 삭제하지 않고 state를 재구성할 수 있다.
optional 설치는 유효한 active state가 없으면 실패하며 cache나 디렉터리 이름에서
release를 추정하지 않는다.

## adapter contract

### `IArtifactTransport`

- `OpenManifestAsync`와 `OpenArtifactAsync`는 읽기 가능한 stream을 반환한다.
- Runtime이 반환 stream을 dispose한다.
- 경로는 검증된 manifest의 content-addressed 상대 경로 그대로 전달된다.
- 재시도 가능한 전송 실패는 `ArtifactTransportException(IsTransient: true)`로
  보고한다. Runtime은 최대 3회 시도하며 cancellation은 재시도하지 않는다.
- `OpenManifestSignatureAsync`가 확인된 not-found(리소스가 존재하지 않는다고
  확실할 때, 예: 실제 HTTP 404)를 보고할 때만 `ArtifactTransportException(
  IsNotFound: true)`를 쓴다. 확실하지 않으면 기본값 `false`를 유지한다 — 잘못
  `true`로 설정하면 실제로 서명된 release가 signature 검증 없이 unsigned로
  넘어갈 수 있다(11단계, "서명 검증" 참고). `OpenManifestAsync`·`OpenArtifactAsync`는
  이 값을 Runtime이 읽지 않지만, 일관성을 위해 같은 규칙을 따르는 것을 권장한다.

### `IRuntimeStorage`

- `AcquirePackageWriterLockAsync`는 같은 package의 process/thread 전체 writer를
  배타적으로 막아야 한다. lock 파일 존재만으로 판단하면 안 된다.
- `ReadPackageStateAsync`는 완전한 old 또는 new byte snapshot만 반환한다.
- `ReplacePackageStateAsync`는 같은 filesystem의 임시 파일 flush와 atomic
  rename/replace 같은 방식으로 state 전체를 한 번 교체한다. in-place write는
  금지한다.
- `CreateCacheWriterAsync`의 `Content`는 아직 공개되지 않은 임시 object다.
  `CommitAsync`가 성공한 뒤에만 `OpenCachedArtifactAsync`로 보여야 하며, commit하지
  않고 dispose하면 기존 검증 cache를 바꾸면 안 된다.
- cache commit은 같은 content-addressed 경로의 손상 object를 복구할 수 있어야 한다.
  Runtime은 전송 byte를 크기와 SHA-256으로 검증한 뒤 commit하고 다시 연다.
- `CreateStagingAreaAsync`가 만든 파일은 `PromoteAsync` 전에는 reader에게 노출하지
  않는다. promotion은 완성된 group directory를 immutable installation으로 승격하고
  비어 있지 않은 opaque key를 반환한다.
- staging을 promotion하지 않고 dispose하면 이전 installation과 state를 바꾸지
  않는다. 이미 promotion됐지만 state가 참조하지 못한 installation은 정리 가능한
  orphan이지만 활성 데이터가 아니다.
- `OpenInstallationFileAsync`는 immutable installation의 정규화 상대 경로를 연다.
- `GetInstallationFilePathsAsync`는 installation이 가진 모든 정규화 상대 경로를
  반환한다. Runtime은 target group의 경로 집합과 정확히 같은지 확인하므로 삭제된
  파일이 남은 installation을 active key로 재사용하지 않는다.
- `CreateScratchStreamAsync`는 읽기·쓰기·seek가 가능한 임시 stream을 반환하고
  dispose 시 backing resource를 정리한다. 압축 bundle을 크기 제한 안에서 해제할 때
  사용한다.

Runtime은 state commit 직전에 writer lock 안에서 작업 시작 snapshot의
`stateRevision`, package, active `dataVersion`·`manifestHash`를 다시 확인한다.
달라졌으면 stale state를 쓰지 않고 최신 state로 최대 3회 재계획한다. 앞선 시도에서
검증·commit된 content-addressed cache는 재사용한다.

## artifact 검증과 복구

- cache object는 사용할 때 manifest의 저장 크기와 object SHA-256으로 다시
  분류한다. 손상 object는 누락으로 취급해 다시 받는다.
- multipart file은 part별 크기·hash와 결합 payload의 크기·`artifactHash`를 모두
  검증한다.
- file artifact는 artifact별 compression metadata로 해제하고 최종 크기와
  `fileHash`를 확인한다.
- bundle은 저장 payload의 크기·hash, zstd 해제 크기, deterministic PAX header,
  extended record, entry 순서·byte, padding과 정확히 두 end block을 검증한다.
- 취소·실패 시 commit된 cache는 유지하고 다음 실행에서 재사용한다. staging과
  uncommitted cache write는 완성 데이터로 사용하지 않는다.

전역 갱신에서 기존 optional installation의 모든 target 파일이 경로·크기·hash로
같으면 다운로드 없이 새 manifest에 `ready`로 재연결한다. 달라지면 installation을
보존한 채 `stale`로 바꾸며, optional이 required로 바뀌면 active state 교체 전에
반드시 준비한다. compact로 artifact 위치만 바뀌고 파일 byte가 같은 경우도 기존
installation을 재사용한다.

## 진행률과 오류

`IProgress<PatchProgress>`는 `Manifest`, `Planning`, `Downloading`, `Staging`,
`Activating`, `Completed` 단계를 보고한다. artifact 상대 경로, group, 파일·byte
진행과 현재 retry 횟수를 포함한다. 재시도로 실제 전송 byte가 계획 byte보다 많아질
수 있다.

예상 가능한 실패는 `RuntimeException.Error`의 `GamePatchKitError`로 확인한다.
`OperationCanceledException`은 변환하지 않는다.

| 코드 | 의미 |
| --- | --- |
| `runtime.manifest-invalid` | manifest hash, canonical/schema/identity/참조 검증 실패 |
| `runtime.signature-invalid` | signature 누락(필수 모드)·문서 손상·알 수 없는 key ID·서명 검증 실패 |
| `runtime.state-invalid` | state schema·불변 조건·installation 참조 또는 revision 상한 위반 |
| `runtime.state-conflict` | 세 번의 activation 시도 모두 concurrent revision과 충돌 |
| `runtime.artifact-corrupted` | 전송·cache object, 압축 해제·bundle 또는 복원 file 검증 실패 |
| `runtime.missing-compression-codec` | 선택 group이 요구하는 codec 미주입 |
| `runtime.transport-failed` | 재시도 불가 전송 오류 또는 retry 소진 |
| `runtime.staging-failed` | storage의 group staging 또는 immutable promotion 실패 |
| `runtime.activation-failed` | package writer lock 또는 atomic state 교체 실패 |

## 관련 파일

- [PackageRuntime.cs](../../src/GamePatchKit.Runtime/PackageRuntime.cs)
- [PackageState.cs](../../src/GamePatchKit.Runtime/PackageState.cs)
- [PackageStateSerializer.cs](../../src/GamePatchKit.Runtime/PackageStateSerializer.cs)
- [PackageStateValidator.cs](../../src/GamePatchKit.Runtime/PackageStateValidator.cs)
- [IArtifactTransport.cs](../../src/GamePatchKit.Runtime/IArtifactTransport.cs)
- [IRuntimeStorage.cs](../../src/GamePatchKit.Runtime/IRuntimeStorage.cs)
- [TrustedSigningKeys.cs](../../src/GamePatchKit.Runtime/TrustedSigningKeys.cs)

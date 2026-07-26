# Adapter conformance suite

## 개요

`GamePatchKit.Conformance`는 `IArtifactTransport`·`IRuntimeStorage` 구현이 Runtime
adapter contract를 만족하는지 검증하는 공용 fixture와 test suite다. 외부 host(Unity
등)가 자신의 adapter 구현만 주입하면 같은 시나리오를 실행할 수 있도록 한다.

배포 형태는 소스 fixture로 정했다: xUnit 기반 `GamePatchKit.Conformance` 프로젝트를
소스로 제공하고, 외부 adapter는 `ConformanceTestBase`를 상속해 factory 메서드만
구현한다. NuGet test package로 패키징할지는 13단계(문서화·배포 산출물)에서 별도로
정한다 — 지금은 배포 파이프라인 자체가 없어 소스 형태가 가장 단순하다.

`GamePatchKit.DotNet`(step 09)과 in-memory reference adapter(이 프로젝트가 함께
제공)를 같은 suite로 실행해 결과를 비교하는 것이 PRD 검증 기준 14의 증거다.

## 구성

- `ConformanceFixture` — `GamePatchKit.Packager`의 `FilePackageBuilder`로 실제
  release를 만들어 실제 publish tree에 쓴다. `Group(name, required, mode)`로
  file·bundle 조합을, `PublishAsync(..., maxArtifactBytes:)`로 file part 분할을
  구성한다.
- `InMemoryArtifactTransport` / `InMemoryRuntimeStorage` — reference adapter.
  writer lock은 `SemaphoreSlim` 기반 실제 상호 배제이며, `installationKey`는 GUID
  하나로만 구성해 DotNet adapter의 `"packageId/localId"` 형식과 의도적으로 다르게
  만든다 — 두 형식이 같은 suite를 모두 통과하면 Runtime이 key 형식을 실제로
  해석하지 않는다는 증거가 된다.
- 계측용 decorator 4종 — 모두 `IArtifactTransport`/`IRuntimeStorage`를 감싸는
  adapter-무관 wrapper이므로 어떤 adapter에도 동일하게 적용된다.
  - `CountingArtifactTransportDecorator` — 경로별 `OpenArtifactAsync` 호출 횟수.
  - `CorruptingArtifactTransportDecorator` — 지정 경로 응답의 첫 byte를 N회 반전.
  - `HoldingArtifactTransportDecorator` — 지정 경로 응답을 해제 시점까지 보류.
  - `FailingGroupPromotionStorageDecorator` — 지정 group의 staging promotion을
    N회 실패시킨다.
  - `FailingReplaceStorageDecorator` / `ExclusivityTrackingStorageDecorator` +
    `SharedLockObserver` — DotNet adapter(09단계)에서 옮겨온 것으로, state 교체
    실패 주입과 writer lock 배타성 계측을 제공한다.
- `ConformanceTestBase` — abstract xUnit test base. 시나리오는 아래 "검증 항목
  매핑" 참고.
- `TestConformanceInMemoryAdapter`(이 프로젝트) / `TestConformanceDotNetAdapter`
  (`GamePatchKit.IntegrationTests`) — 두 concrete subclass.

## 새 adapter를 검증하는 방법

`ConformanceTestBase`를 상속하고 5개 abstract 메서드만 구현한다.

```csharp
public sealed class TestConformanceMyAdapter : ConformanceTestBase
{
    protected override IArtifactTransport CreateTransport() { /* ... */ }
    protected override IRuntimeStorage CreateStorage() { /* ... */ }
    protected override IRuntimeStorage CreateIsolatedStorage() { /* ... */ }
    protected override void RegisterRelease(FinalizedManifest release) { /* ... */ }
    protected override Task RegisterRawManifestAsync(string packageId, string manifestHash, byte[] manifestBytes) { /* ... */ }
}
```

- `CreateTransport`/`CreateStorage`는 매 호출마다 새 instance를 반환해도, 같은
  instance를 반환해도 된다 — 단, 한 테스트 안에서 여러 번 호출된 결과가 모두 같은
  영속 상태를 공유해야 한다("두 프로세스가 같은 package를 공유"하는 상황을
  표현한다).
- `CreateIsolatedStorage`는 그 반대로, 다른 무엇과도 연결되지 않은 완전히 독립된
  instance를 반환해야 한다 — cancel/resume 시나리오의 순서 확인용 probe 실행에만
  쓰인다.
- `RegisterRelease`는 방금 publish한 release를 transport가 인식하게 만든다.
  DotNet adapter처럼 매 요청마다 publish tree를 직접 읽는 adapter라면 no-op이다.

## 검증 항목 매핑

| PRD 검증 기준 | 시나리오 |
| --- | --- |
| 14 (DotNet·fake adapter 동일 결과) | 아래 모든 시나리오를 두 adapter가 동일하게 통과 |
| 22 (required-only 설치·optional 상태 전환) | `RequiredOnlyInstall_*`, `OptionalGroupInstall_*`, `OptionalGroupReconnect_*` |
| 15 (취소 후 재개) | `InstallOrUpdateAsync_CancelledMidDownload_ResumeReusesAlreadyVerifiedCache` |
| 11 (손상 artifact 거부) | `CorruptedArtifactResponse_FailsWithoutPoisoningCache` |
| 23 (state 원자적 교체·복구) | `StateReplaceFailureInjection_*` |
| 23 (multi-group batch·부분 ready 미노출) | `MultiGroupBatch_OneGroupPromotionFails_*` |
| 23 (동시 revision 충돌) | `ConcurrentOptionalGroupInstalls_*` |
| package writer 직렬화 | `WriterLock_TwoConcurrentCallers_NeverHeldSimultaneously` |
| file·bundle·part·압축 조합 | `BundleGroupWithZstdCompression_*`, `MultipartFile_*` |
| 25 (unsigned 범위: 잘못된 kind·참조·순서·중복·미참조 거부) | `UnsignedInvalidManifestFixture_IsRejected`(theory) |

manifestHash 자체의 검증(변조된 hash 거부)은 이 suite에서 별도로 반복하지 않는다
— adapter와 무관한 Runtime 내부 로직이며 `GamePatchKit.Runtime.Tests`의
`RejectsManifestHashMismatchBeforeDownloading`이 이미 증명한다. 이 suite의
모든 정상 시나리오가 hash 검증을 통과해 성공하는 것 자체가 두 adapter 모두
manifest byte를 있는 그대로 전달한다는 증거다.

download plan 동일성은 별도 비교 코드가 없다 — `DownloadPlanner`가 adapter를
전혀 모르는 순수 함수이므로, 같은 시나리오가 두 adapter에서 같은 `PackageState`로
끝나는 것 자체가 plan이 adapter와 무관했다는 증거다.

`UnsignedInvalidManifestFixture_IsRejected`의 5개 fixture는 `tests/fixtures/
release-manifest/invalid/*.json`에서 나오지만, 프로젝트 파일 경로로 읽지 않고
`GamePatchKit.Conformance.csproj`의 `EmbeddedResource`로 이 프로젝트 안에
내장한다 — 소스 fixture로 배포됐을 때도 monorepo의 `tests/fixtures/` 디렉터리
존재 여부와 무관하게 실행돼야 하기 때문이다. fixture 중 파싱에 성공하는 것들은
raw byte가 아니라 `ReleaseIdentity.ComputeCanonicalBytes`로 재직렬화한 뒤
전달한다 — fixture 원본이 pretty-print JSON이라 raw byte 그대로 쓰면 canonical
byte 불일치만으로 거부돼, `ManifestValidator`가 실제로 그 결함(중복·참조·정렬)을
잡았는지는 증명하지 못했을 것이다. 파싱 자체가 실패하는 fixture(잘못된
discriminator)는 재직렬화할 `ReleaseManifest`가 없으므로 raw byte 그대로 쓴다.

## 알려진 범위 제한

- `StateReplaceFailureInjection_*`은 `ReplacePackageStateAsync` 호출 자체가
  실패했을 때 Runtime이 이전 state를 그대로 유지하는지만 증명한다.
  adapter 내부에서 실제 쓰기 도중(예: 임시 파일 작성 후 atomic rename 전) 끊기는
  경우의 진짜 atomicity는 adapter별 저장소 구현에 직접 접근해야 검증할 수 있어
  이 공용 decorator로는 재현할 수 없다 — DotNet adapter는
  `GamePatchKit.DotNet.Tests`의 별도 테스트가 담당한다.
- `WriterLock_TwoConcurrentCallers_NeverHeldSimultaneously`에서
  `CreateStorage()`가 같은 instance를 반환하는 것은(in-memory adapter가 그렇다)
  의도된 선택이다. in-memory adapter는 애초에 "다른 object, 같은 backing"이
  성립할 수 없는 단일 프로세스 store이므로, 이 suite가 증명하는 것은 "같은
  object에 대한 동시 호출이 실제로 직렬화되는가"이며, DotNet adapter처럼
  파일시스템을 공유하는 두 개의 완전히 분리된 storage instance 사이의 배타성과는
  다른, 그러나 여전히 의미 있는 속성이다.

## 서명된 fixture

signed fixture와 signature 검증 case는 11단계(서명·key rotation)에서 이 suite의
extension으로 추가한다. 지금은 unsigned 범위만 다룬다.

## 관련 파일

- [ConformanceTestBase.cs](../../tests/GamePatchKit.Conformance/ConformanceTestBase.cs)
- [ConformanceFixture.cs](../../tests/GamePatchKit.Conformance/ConformanceFixture.cs)
- [InMemoryArtifactTransport.cs](../../tests/GamePatchKit.Conformance/InMemoryArtifactTransport.cs)
- [InMemoryRuntimeStorage.cs](../../tests/GamePatchKit.Conformance/InMemoryRuntimeStorage.cs)
- [TestConformanceDotNetAdapter.cs](../../tests/GamePatchKit.IntegrationTests/TestConformanceDotNetAdapter.cs)

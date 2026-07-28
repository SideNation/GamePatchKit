# GamePatchKit.Runtime

host가 지정한 정확한 target manifest를 검증하고, 필요한 artifact만 받아 검증한 뒤
package 상태를 원자적으로 교체하는 client 상태 머신. 최신 release나 배포 환경을
스스로 선택하지 않는다.

- target framework: `netstandard2.1`
- `GamePatchKit.Core`만 참조한다. 전송·저장소·압축은 interface로 주입한다.

## 사용법

```csharp
var runtime = new PackageRuntime(transport, storage, new[] { zstdCodec });
var target = new TargetManifestReference(packageId, dataVersion, manifestHash);

PackageState state = await runtime.InstallOrUpdateAsync(target, cancellationToken: token);
```

`transport`와 `storage`는 `IArtifactTransport`·`IRuntimeStorage` 구현이다. 일반
.NET 환경에서는 `GamePatchKit.DotNet`이 두 구현을 제공하고, Unity 같은 외부 host는
같은 interface를 직접 구현한다.

optional group은 별도 target pointer 없이 현재 active manifest 기준으로 설치한다.

```csharp
await runtime.InstallOptionalGroupsAsync(packageId, new[] { "maps" }, cancellationToken: token);
```

## 보장하는 것

- manifest byte SHA-256 → canonical/schema → 참조 무결성 → `dataVersion` 재계산 순서 검증
- 신뢰 key 목록 기반 Ed25519 `manifest.sig` 검증(선택)
- content-addressed cache 재사용과 취소 후 재개
- 한 요청의 group 집합을 하나의 activation batch로 묶은 단일 state 교체
- 실패·취소 시 이전 `PackageState`와 installation 유지

## 문서

- [README](https://github.com/SideNation/GamePatchKit/blob/main/README.md)
- [Runtime 통합 가이드](https://github.com/SideNation/GamePatchKit/blob/main/docs/guide/runtime-integration.md)
- [Runtime 상태 머신 계약](https://github.com/SideNation/GamePatchKit/blob/main/docs/contracts/runtime.md)

MIT License.

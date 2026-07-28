# GamePatchKit.DotNet

`GamePatchKit.Runtime`의 전송·저장소 계약을 일반 .NET 환경에 연결하는 reference
adapter. ASP.NET, worker, console, desktop 애플리케이션에서 그대로 사용한다.

- target framework: `net10.0`

## 사용법

```csharp
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

var runtime = new PackageRuntime(
    new HttpArtifactTransport(httpClient),
    new FileSystemRuntimeStorage(runtimeRoot),
    DefaultCompressionCodecs.Create());

PackageState state = await runtime.InstallOrUpdateAsync(target, cancellationToken: token);
```

`httpClient.BaseAddress`는 publish tree의 root이며 반드시 `/`로 끝나야 한다.

## 제공하는 것

| 타입 | 역할 |
| --- | --- |
| `HttpArtifactTransport` | `HttpClient` 기반 manifest·signature·artifact streaming 다운로드와 transient/not-found 분류 |
| `FileSystemRuntimeStorage` | package별 writer lock, `package-state.json` 원자적 교체, content-addressed cache, staging과 immutable installation |
| `DefaultCompressionCodecs` | 기본 zstd codec 구성 |

Runtime의 download plan·hash 검증·retry·활성화 규칙을 복제하지 않고 전송·저장 I/O만
담당한다.

## filesystem 배치

```text
<runtimeRoot>/packages/<packageId>/
├── state/{package-state.json,package-state.lock}
├── cache/
├── installs/<installationKey>/
└── staging/<operationId>/
```

## 문서

- [README](https://github.com/SideNation/GamePatchKit/blob/main/README.md)
- [Runtime 통합 가이드](https://github.com/SideNation/GamePatchKit/blob/main/docs/guide/runtime-integration.md)
- [DotNet adapter 계약](https://github.com/SideNation/GamePatchKit/blob/main/docs/contracts/dotnet-adapter.md)

MIT License.

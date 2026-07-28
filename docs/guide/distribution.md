# 배포 산출물과 버전 정책

무엇을 배포하는지, JSON Schema와 manifest 호환 버전을 어떻게 관리하는지, adapter
conformance suite를 어떤 형태로 제공하는지, release 전에 통과해야 하는 성능 gate가
무엇인지 다룬다.

## 배포 산출물

| 산출물 | 형태 | 비고 |
| --- | --- | --- |
| `GamePatchKit.Core` | NuGet (`netstandard2.1`) | 엔진 독립 core 계약 |
| `GamePatchKit.Runtime` | NuGet (`netstandard2.1`) | client 상태 머신 |
| `GamePatchKit.Compression.NativeCompressions` | NuGet (`netstandard2.1`) | 기본 zstd codec |
| `GamePatchKit.DotNet` | NuGet (`net10.0`) | `HttpClient`·filesystem adapter |
| `gpk` (`GamePatchKit.Cli`) | NuGet .NET tool (`net10.0`) | `dotnet tool install --global GamePatchKit.Cli` |
| JSON Schema 3종 | repository release의 `schemas/` | 아래 "schema 배포와 버전 정책" |
| package별 manifest와 artifact | 각 프로젝트의 publish tree | GamePatchKit이 배포하지 않는다 |
| adapter conformance fixture·test suite | 소스 (`tests/GamePatchKit.Conformance`) | 아래 "adapter conformance suite" |

`GamePatchKit.Packager`는 독립 NuGet으로 배포하지 않는다. `gpk` tool 패키지 안에 함께
들어간다(`PackAsTool`이 project reference를 번들한다).

GamePatchKit 저장소는 **Unity 전용 assembly와 package를 빌드하거나 배포하지 않는다.**
[`unity/`](../../unity) 아래의 embedded package는 외부 host 구현 예시이자 검증용이며
NuGet·UPM registry로 배포하지 않는다.

## 패키징 메타데이터

공통 값은 [`Directory.Build.props`](../../Directory.Build.props)에 한 번만 둔다.

| 항목 | 값 |
| --- | --- |
| `Authors` | TenY |
| `PackageProjectUrl` / `RepositoryUrl` | `https://github.com/SideNation/GamePatchKit` |
| `PackageLicenseExpression` | `MIT` ([`LICENSE`](../../LICENSE)와 동일) |
| `Copyright` | Copyright (c) 2026 TenY |
| `PackageTags` | `game;patch;package;content-delivery;download` |
| `PackageReadmeFile` | 각 프로젝트 폴더의 `README.md` |

`PackageReadmeFile`은 `Exists()` 조건으로 선언되어 있어 `README.md`를 가진 프로젝트에만
적용된다. 패키지를 새로 추가할 때 그 프로젝트에 `README.md`를 두면 자동으로 포함되고,
`Description`은 각 csproj에 개별로 적는다.

## 빌드와 배포

```bash
./build.sh                                    # Compile (기본)
./build.sh Test                               # Category!=Performance 전체 테스트
./build.sh Performance                        # 성능 테스트만 (docs/perf/README.md)
./build.sh Pack                               # artifacts/에 nupkg 5종 생성
./build.sh Push --nuget-api-key <KEY>         # Pack 후 nuget.org에 push
```

- `Pack`은 `Test`에, `Push`는 `Pack`에 의존한다. **`Push`만 실행하면 Pack이 한 번만
  돈다.** `Pack`과 `Push`를 따로 실행하면 아래 version 자동 증가가 두 번 일어난다.
- `--version` 없이 `Pack`/`Push`를 실행하면 `Directory.Build.props`의 patch version을
  1 올리고 **파일을 저장한다.** 재현 가능한 release에서는 version을 명시하는 편이 낫다.

```bash
./build.sh Pack --version 1.0.0
./build.sh Push --nuget-api-key <KEY> --version 1.0.0
```

`--configuration`, `--nuget-source`도 같은 방식으로 넘긴다. `NUGET_API_KEY` 환경 변수로도
API key를 줄 수 있다.

> Nuke는 주입 대상 멤버를 **멤버 이름**으로 찾는다. 그래서 `_build/Build.cs`의 parameter
> 필드는 저장소의 `_camelCase` private 필드 규칙 대신 Nuke 관례인 PascalCase를 쓴다.
> 이름을 바꾸면 명령행 option 이름도 함께 바뀐다.

## schema 배포와 버전 정책

`schemas/` 아래 세 파일이 versioned 배포 산출물이다.

| 파일 | 검증 대상 |
| --- | --- |
| `package-config.schema.json` | `gamepatchkit.yml`을 파싱한 **설정 데이터 구조** (YAML 문서 자체가 아니다) |
| `release-manifest.schema.json` | Packager가 생성한 canonical release manifest |
| `manifest-signature.schema.json` | `manifest.sig` canonical JSON |

### 배포 방식

- 세 파일은 **repository release에 그대로 포함된다.** 별도 NuGet 패키지나 host된 URL로
  배포하지 않는다.
- `$id`는 `package-config.schema.json`처럼 **파일명뿐인 상대 참조**다. 해석 가능한
  네트워크 URL이 아니므로, 소비자는 사용할 repository tag의 `schemas/` 내용을 자기
  저장소에 복사하거나 submodule로 고정한다. `$id`를 원격에서 fetch하려고 하면 안 된다.
- `GamePatchKit.Packager`는 세 파일을 **assembly에 embedded resource로 내장한다.**
  Packager·CLI를 쓰는 경로에서는 디스크의 `schemas/` 존재 여부와 무관하게 같은 schema로
  검증한다.
- 세 schema는 모두 `additionalProperties: false`이므로 **알 수 없는 필드를 거부한다.**
  새 필드를 추가하는 것은 언제나 schema 변경이다.

### 버전 정책

- 각 schema의 `schemaVersion`은 `const`로 고정돼 있다. v1에서는 셋 다 `1`이다.
- **schema와 manifest 호환 버전은 같은 repository release에서 함께 관리한다.** 어떤
  release의 `schemas/`와 그 release의 `GamePatchKit.Core`/`Packager`/`Runtime`는 항상 짝을
  이룬다. schema만 따로 올리거나 내리지 않는다.
- manifest `schemaVersion`이 바뀌면 `dataVersion`은 유지되지만 `manifestHash`는 바뀐다
  (canonical byte가 달라지므로). 논리 데이터가 그대로여도 client는 새 manifest를 받는다.
- schema가 표현하지 못하는 규칙 — 배열 정렬, 중복, 참조 무결성, content-addressed 경로
  재구성 — 은 Core 의미 검증의 몫이다. **schema 통과가 manifest 유효를 뜻하지 않는다.**
  세 계층 검증은 [release identity와 canonical
  JSON](identity.md#세-계층-검증)을 참조한다.

## adapter conformance suite

`IArtifactTransport`·`IRuntimeStorage` 구현이 Runtime adapter contract를 만족하는지
검증하는 공용 fixture와 test suite다. `GamePatchKit.DotNet`과 in-memory reference
adapter를 같은 suite로 실행해 결과를 비교하는 것이 PRD 검증 기준 14의 증거다.

### 배포 형태: 소스 fixture

**NuGet test package로 패키징하지 않고 소스로 제공한다.** xUnit 기반
`tests/GamePatchKit.Conformance` 프로젝트를 그대로 참조하거나 복사해서 쓴다.

이유는 두 가지다. 첫째, 이 suite는 xUnit·`GamePatchKit.Packager`(fixture용 release 생성)에
의존하는 **테스트 프로젝트**라, 패키지로 만들면 소비자의 테스트 프레임워크 선택을 강제하게
된다. 둘째, Unity처럼 xUnit을 그대로 실행할 수 없는 host는 어차피 시나리오를 자기
테스트 프레임워크로 옮겨야 하므로 소스가 있는 편이 낫다.

### 새 adapter 검증하기

`ConformanceTestBase`를 상속해 abstract 멤버만 구현한다.

```csharp
public sealed class TestConformanceMyAdapter : ConformanceTestBase
{
    protected override IArtifactTransport CreateTransport() { /* ... */ }
    protected override IRuntimeStorage CreateStorage() { /* ... */ }
    protected override IRuntimeStorage CreateIsolatedStorage() { /* ... */ }
    protected override void RegisterRelease(FinalizedManifest release) { /* ... */ }
    protected override Task RegisterRawManifestAsync(string packageId, string manifestHash, byte[] manifestBytes) { /* ... */ }
    protected override Task RegisterSignatureAsync(string packageId, string manifestHash, byte[] signatureBytes) { /* ... */ }
}
```

- `CreateTransport`/`CreateStorage`는 한 테스트 안에서 여러 번 호출된 결과가 모두 같은
  영속 상태를 공유해야 한다("두 프로세스가 같은 package를 공유"하는 상황).
- `CreateIsolatedStorage`는 반대로 어떤 것과도 연결되지 않은 독립 instance를 반환한다.
- `RegisterRelease`는 방금 publish한 release를 transport가 인식하게 만든다. 매 요청마다
  publish tree를 직접 읽는 adapter라면 no-op이다.

```bash
dotnet test tests/GamePatchKit.Conformance
```

포함 시나리오와 PRD 검증 기준 매핑, 알려진 범위 제한은
[adapter conformance suite 계약](../contracts/adapter-conformance.md)에 있다.

## 성능 blocking gate

PRD 검증 기준 17의 release acceptance 조건이다.

| 항목 | 기준 |
| --- | --- |
| 기준 fixture | 최소 **1만 파일**, 원본 합계 **1GiB** |
| 기준 환경 | **Ubuntu 24.04 x64**, repository에 고정된 .NET 10, Release build, Workstation GC, 4 vCPU, 8GiB RAM, local SSD, 병렬도 4 |
| 측정 방법 | debugger·profiler 없이 **별도 process로 3회** 실행하고 GNU `/usr/bin/time -v`의 `Maximum resident set size`를 MiB로 환산해 **3회 최대값**을 peak RSS로 사용 |
| 상한 | package·compact·verify·download 각 process peak RSS **512MiB 이하** |
| scaling 상한 | 같은 1만 파일·seed·group 분포의 **256MiB fixture 대비 1GiB fixture**의 peak RSS 증가가 **64MiB 이하** |
| 다른 OS | **참고값으로만 기록하고 blocking gate로 쓰지 않는다** |

macOS·Windows에서는 `/usr/bin/time -v`가 없거나 의미가 달라 같은 수치를 얻을 수 없다.
harness는 macOS에서 `/usr/bin/time -l`로 참고값을 내지만, 이 값으로 512MiB/64MiB를
판정하지 않는다.

```bash
# 스모크 규모 (로컬 확인용, gate 판정 없음)
./build.sh Performance

# 공식 gate (Ubuntu 24.04 x64 기준 환경에서만)
GPK_PERF_FULL_SCALE=1 GPK_PERF_OFFICIAL_GATE=1 \
  dotnet test tests/GamePatchKit.PerformanceTests --filter "Category=Performance" -c Release
```

`GPK_PERF_FULL_SCALE`는 fixture 크기만 바꾸고, `GPK_PERF_OFFICIAL_GATE`를 **추가로** 켜야
pass/fail이 assert되고 [`docs/perf/results.md`](../perf/results.md)에 gate 결과로
기록된다. Linux라는 사실만으로 공식 gate로 취급하지 않는 이유는 임의의 Linux 머신이 위
기준 환경과 다를 수 있기 때문이다.

**현재 상태: 측정 인프라는 완성됐고 공식 Ubuntu 24.04 x64 gate는 아직 실행되지 않았다.**
자세한 실행 절차는 [성능·메모리 검증 harness](../perf/README.md)를 참조한다.

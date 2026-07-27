# 성능·메모리 검증 harness

`docs/plan/12-performance-validation.md` 12단계의 측정 인프라. PRD "성능·관측
기준" 절이 요구하는 1만 파일·1GiB fixture 성능 gate를 재현 가능하게 측정한다.

## 현재 상태

**인프라 구축 완료. 공식 Linux blocking-gate 수치는 아직 미실행이다.**

blocking 기준 환경은 Ubuntu 24.04 x64 + GNU `/usr/bin/time -v`인데, 이 저장소를
개발 중인 macOS 머신에는 Docker가 없고 macOS의 `/usr/bin/time`에는 `-v` 옵션
자체가 없다(BSD time). 그래서 이 harness는 macOS에서 `/usr/bin/time -l`로 얻은
참고값만 낼 수 있고, PRD의 512MiB/64MiB gate는 CI 또는 별도 Linux 환경이 준비된
뒤 실행해서 기록한다.

## 구성

- `tests/GamePatchKit.PerformanceTests/Fixtures/PerformanceFixtureGenerator.cs` —
  고정 seed로 1만 파일을 생성한다. 파일 경로·group 배정은 fixture 크기(256MiB/
  1GiB/스모크)와 무관하게 항상 같고, 절대 byte 크기만 비례해서 바뀐다.
- `tests/GamePatchKit.PerformanceTests.Harness/` — `HttpArtifactTransport` +
  `FileSystemRuntimeStorage` + `PackageRuntime`를 실제 loopback 소켓
  (`System.Net.HttpListener`) 위에서 구동하는 별도 프로세스. "Runtime download·
  활성화" 시나리오 전용이다 — 배포되는 `gpk` CLI에는 Runtime/DotNet 의존성을
  넣지 않는다. `InstallOrUpdateAsync`가 끝나면 도달한 `active` identity·
  group 상태·누적 `retryCount`를 CLI의 `--json`과 같은 관례로 stdout에 출력한다
  (`ObservabilityMetricsTests`가 파싱해서 검증).
- `tests/GamePatchKit.PerformanceTests/Measurement/ProcessRssMeasurement.cs` —
  시나리오를 3회 별도 프로세스로 실행하고 `/usr/bin/time`으로 peak RSS를 측정한다.
- `tests/GamePatchKit.PerformanceTests/Scenarios/` — PRD의 7개 시나리오(최초
  package/incremental/compact/verify/signed verify/plan-download/Runtime
  install)를 실제 `gpk` CLI·harness 인자로 구성해 실행한다.
- `docs/perf/results.md` — 실행 결과가 쌓이는 표.

## 실행 방법

```bash
# 스모크 규모(로컬 개발 확인용, gate 판정 없음)
dotnet test tests/GamePatchKit.PerformanceTests --filter "Category=Performance"

# 실제 1만 파일·256MiB/1GiB fixture로 전환. IsLinux만으로는 공식 gate로 취급하지
# 않으므로 GPK_PERF_OFFICIAL_GATE=1도 함께 켜야 pass/fail이 assert된다(아래 참고).
GPK_PERF_FULL_SCALE=1 dotnet test tests/GamePatchKit.PerformanceTests --filter "Category=Performance"
```

**`dotnet test GamePatchKit.sln`을 필터 없이 직접 실행하면 이 성능 테스트도 함께
돈다.** `Category!=Performance` 필터는 `_build/Build.cs`의 `Test` target
(`./build.sh Test`)에만 있고, `[Trait]`만으로는 필터 없는 `dotnet test`에서
자동으로 빠지지 않는다. 이 프로젝트를 피하려면 `./build.sh Test`를 쓰거나
직접 `--filter "Category!=Performance"`를 넘긴다. 이 프로젝트만 실행하려면
`Target Performance`(`./build.sh Performance`)를 쓴다.

## gate 판정의 두 단계

- `GPK_PERF_FULL_SCALE=1` — fixture 크기를 스모크(20MiB)에서 실제 256MiB/1GiB로
  전환한다.
- `GPK_PERF_OFFICIAL_GATE=1` — **추가로** 켜야 pass/fail이 `docs/perf/results.md`에
  기록되고 테스트가 그 결과를 assert한다. Linux라는 사실만으로는 공식 gate로
  취급하지 않는다 — 임의의 Linux 머신은 PRD가 고정한 기준 환경(4 vCPU, 8GiB RAM,
  Workstation GC 등)과 다를 수 있기 때문이다. 이 값은 PRD의 기준 환경과 실제로
  일치하도록 프로비저닝된 특정 CI job에서만 설정해야 한다. 꺼져 있으면(Linux
  여부와 무관하게) 결과는 항상 "reference only"로 기록되고 gate는 assert되지
  않는다.

## Linux blocking-gate를 실제로 실행하려면

1. Ubuntu 24.04 x64, repository에 고정된 .NET 10, Release build, Workstation
   GC, 4 vCPU, 8GiB RAM, local SSD, 병렬도 4 환경을 준비한다(PRD 성능·관측 기준).
2. `GPK_PERF_FULL_SCALE=1 GPK_PERF_OFFICIAL_GATE=1 dotnet test tests/GamePatchKit.PerformanceTests --filter "Category=Performance" -c Release`를
   debugger·profiler 없이 실행한다.
3. `docs/perf/results.md`에 `pass`/`fail`로 기록된 행이 공식 gate 결과다. 그 외
   "reference only"로 남는 행(같은 머신이라도 `GPK_PERF_OFFICIAL_GATE`를 켜지
   않았거나 non-Linux인 실행)과 구분해서 읽는다.

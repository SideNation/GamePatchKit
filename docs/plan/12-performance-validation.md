# 12. 성능·메모리 검증

> PRD 섹션: 성능·관측 기준, 재현성과 멱등성

## 목표

1만 파일·1GiB 기준 fixture로 package·verify·diff·download 전 구간의 streaming
동작과 결정성을 검증하고, 관측 지표 출력을 확정한다.

## 선행 단계

05~11

## 작업 항목

- [x] deterministic fixture generator: 최소 1만 파일·원본 합계 1GiB, seed 고정
      (파일 크기 분포와 file·bundle group 구성 포함)
      (`tests/GamePatchKit.PerformanceTests/Fixtures/PerformanceFixtureGenerator.cs`)
- [x] scaling fixture: 같은 1만 파일·seed·group 분포에서 payload 크기만 조정한
      256MiB와 1GiB 입력 생성 (`PlanFiles()`가 totalBytes와 무관하게 같은 경로·
      group을 내고, 크기만 비례 조정)
- [ ] blocking 환경 고정: Ubuntu 24.04 x64, repository 고정 .NET 10, Release build,
      Workstation GC, 4 vCPU, 8GiB RAM, local SSD, 병렬도 4, debugger·profiler 없음 —
      **미실행**. 개발 머신이 macOS ARM64이고 Docker가 없어 이 환경을 지금 만들 수
      없다. harness 자체는 Linux(`time -v`)·macOS(`time -l`) 둘 다 지원하도록
      구현했다(`docs/perf/README.md` 참고).
- [x] 시나리오 harness 구현: 최초 package, incremental(소수 파일 변경), compact,
      verify, signed verify, plan-download, Runtime download·활성화 7개 전부
      실제 `gpk` CLI/전용 harness exe로 구성해 스모크 규모(20MiB, 1만 파일)에서
      end-to-end 실행 검증 완료(`tests/GamePatchKit.PerformanceTests/Scenarios/`).
      **실제 256MiB/1GiB 규모 실행은 아직 하지 않았다** — `GPK_PERF_FULL_SCALE=1`로
      전환하면 같은 코드가 실제 규모를 돈다.
- [x] 각 시나리오를 별도 process로 3회 실행하고 GNU `/usr/bin/time -v`(Linux)
      또는 `/usr/bin/time -l`(macOS, 참고용)의 peak RSS를 MiB로 변환해 3회 중
      최댓값을 판정하는 로직 구현(`Measurement/ProcessRssMeasurement.cs`) — 파서는
      고정 샘플 문자열 단위 테스트로 검증됨. **실제 512MiB/64MiB gate 판정은
      Linux·full-scale에서만 assert하도록 만들어 뒀고, 아직 그 조건으로 실행한
      적은 없다.** Codex 표준 리뷰에서 "Linux라는 사실만으로 임의의 Linux 머신을
      공식 gate로 취급하게 된다"는 지적을 받아, `GPK_PERF_FULL_SCALE`(fixture
      크기 전환)과 별도로 `GPK_PERF_OFFICIAL_GATE`(PRD 기준 환경에 실제로
      프로비저닝된 특정 CI job만 켜는 값)를 추가했다 — 둘 다 켜져야 pass/fail이
      assert되고, 그 외에는 Linux 여부와 무관하게 항상 "reference only"로
      기록된다.
- [ ] 1GiB fixture의 package·compact·verify·download 각 process peak RSS ≤ 512MiB
      — **미실행** (위와 동일한 이유)
- [ ] 256MiB에서 1GiB로 입력을 늘렸을 때 각 streaming 시나리오의 peak RSS 증가
      ≤ 64MiB — **미실행**
- [ ] 다른 지원 OS의 같은 측정값은 참고 결과로 기록하고 Linux 기준만 blocking gate로
      사용 — macOS 스모크 규모 참고값은 `docs/perf/results.md`에 자동 기록되는 것을
      확인했다. 256MiB/1GiB 규모의 참고값은 아직 없다.
- [x] 병렬 처리 결정성: 병렬도를 바꿔도 파일 순서, bundle 경계, manifest byte가
      바뀌지 않는다 — **범위 조정**: `src/GamePatchKit.Packager/FilePackageBuilder.cs`를
      확인한 결과 packaging 파이프라인은 현재 전부 순차 처리이고 조절 가능한
      병렬도 옵션이 어디에도 없어 바꿔볼 대상이 없다. 대신 1만 파일 규모에서
      같은 입력을 두 번 package해 byte가 완전히 같은지 재확인했다(검증 기준 1을
      10k 규모에서 재검증, `Determinism/TestDeterminismAtScale.cs`). 병렬도
      옵션이 나중에 추가되면 이 테스트를 확장해야 한다.
- [x] 관측 지표 출력 검증: 전체·group별 파일 수·byte, 생성·재사용 artifact 수·byte,
      추가·변경·삭제 수, 예상 다운로드 byte·요청 수, 임시 저장공간, cache hit byte,
      단계별 실행 시간이 package·diff·verify·plan-download의 기존 `--json` 출력에
      이미 있음을 자동 테스트로 확인(`Observability/TestObservabilityMetrics.cs`).
      "cache hit byte"만 discrete 필드가 아니라 `--cache-root` 유무에 따른
      `plan.downloadBytes` 차이로 유도해야 하는 값이라는 점을 테스트로 증명해
      뒀다. "compact 전후 신규 설치 byte 차이"·"취소·재시도·검증 실패 결과"는
      기존 `estimatedFirstInstall`/`PatchProgress.RetryCount`/타입이 있는
      예외(`RuntimeErrorCodes`)로 이미 표현되어 있어 새 product 코드는 필요
      없었다. **Codex 표준 리뷰 지적으로 보완**: 처음에는 Runtime 쪽(harness)이
      `InstallOrUpdateAsync`의 결과와 `IProgress<PatchProgress>`를 버리고 아무
      것도 출력하지 않아 이 항목이 CLI만 검증된 채 완료로 잘못 표시돼 있었다.
      harness가 도달한 `active` identity·group 상태·누적 `retryCount`를 CLI와
      같은 JSON 관례로 stdout에 출력하도록 고치고, `TestObservabilityMetrics`가
      그 출력도 파싱해서 검증하도록 확장했다.
- [ ] 환경·명령·3회 개별값·최대 peak RSS·scaling delta와 기준 통과 여부를 문서로
      기록해 회귀 비교 기준으로 삼는다 — `docs/perf/results.md` 틀과 자동 기록
      로직은 만들어 뒀다. 실제 Linux blocking-gate 행은 아직 없다.

## 산출물

- fixture generator, 성능 테스트(일반 테스트와 분리된 category), 측정 결과 문서 —
  전부 구현됨(`tests/GamePatchKit.PerformanceTests*/`, `docs/perf/`). `_build/Build.cs`의
  `Test` target은 `Category!=Performance`로 이 프로젝트를 제외하고, 별도
  `Target Performance`로 돌린다.

## 완료 기준

- 기준 Linux 환경에서 1만 파일·1GiB fixture의 package·compact·verify·download
  peak RSS가 각각 512MiB 이하이고 256MiB 대비 peak RSS 증가가 64MiB 이하다
  (검증 기준 17). **미충족 — 인프라만 구축, 실측은 Linux 환경 준비 후 진행.**
- 병렬 실행이 artifact·manifest byte를 바꾸지 않는다(검증 기준 1 재확인). 위
  "범위 조정" 참고 — 1만 파일 규모의 반복 실행 결정성으로 재확인 완료.
- PRD 성능·관측 기준의 지표가 package·diff·Runtime 결과에 모두 출력된다. 확인
  완료.

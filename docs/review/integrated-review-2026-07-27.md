# GamePatchKit 통합 리뷰 — 2026-07-27

## 1. 결론

현재 구현은 Core, Packager, CLI, Runtime, DotNet adapter의 주요 동작과 실패 복구
경로가 폭넓게 테스트되어 있다. 직접 실행한 비성능 테스트 537개도 모두 통과했다.
그러나 아래 release blocker가 남아 있어 현재 상태를 배포 완료로 판정할 수는 없다.

1. 저장소가 안내하는 공식 진입점 `./build.sh Test`가 Restore 전에 실패한다.
2. PRD 검증 기준 17의 Ubuntu 24.04 x64 공식 성능 gate가 아직 실행되지 않았다.
3. Unity 6.4 macOS Mono의 무압축 기본 경로는 탐색성 smoke에서 동작했지만,
   UnityWebRequest·persistentDataPath adapter와 IL2CPP·모바일 검증이 없다.

판정은 **조건부 통과, release 보류**다. 먼저 빌드 진입점과 zstd strict decoding을
고치고, Unity 지원 범위와 Linux 성능 gate를 재현 가능한 자동 검증으로 고정해야 한다.

## 2. 리뷰 범위와 기준

- 기준 branch/commit: `develop` / `c5537b8`
- 비교 기준: `4dc7268` 이후 현재 `develop`
- 검토 범위:
  - `src/` 전체
  - `tests/` 전체
  - `_build/`, 중앙 package/version 설정
  - PRD 검증 기준 1~26
  - 01~13 개발 계획과 README·성능 문서
  - NuGet 4종의 실제 pack 결과와 dependency 취약점
  - Unity 6에서의 Core/Runtime 소비 가능성
- 제외:
  - 사용자 작업 중이던 `docs/worklog/2026-07.md` 변경 내용
  - Ubuntu 24.04 x64 공식 성능 환경 실측
  - Android, iOS, WebGL 및 IL2CPP 실제 player build

## 3. 검증 결과 요약

| 검증 | 결과 | 비고 |
| --- | --- | --- |
| 비성능 테스트 | PASS | Release, `Category!=Performance`, 9개 test project, 537개 |
| 공식 `./build.sh Test` | FAIL | `_solution`이 `null`인 채 Restore 진입 |
| NuGet 4종 direct pack | PASS | Core, Runtime, NativeCompressions, DotNet |
| NuGet metadata | PARTIAL | license/repository는 있으나 4종 모두 package README 경고 |
| 취약 package 검사 | FAIL | build project의 `System.Security.Cryptography.Xml 10.0.9`에 High advisory 5개 |
| `dotnet format --verify-no-changes` | FAIL | 저장소 전반의 whitespace/final-newline 차이 |
| 공식 Linux 성능 gate | NOT RUN | macOS smoke 결과만 존재 |
| Unity 6.4 Editor import/compile | EXPLORATORY PASS | Core/Runtime + BouncyCastle + Unity Newtonsoft |
| Unity 6.4 macOS Mono player | EXPLORATORY PASS | 무압축 단일 파일 설치·활성화 성공 |
| Unity IL2CPP/모바일/WebGL | NOT RUN | 지원 판정 불가 |

Unity smoke는 임시 프로젝트에서 한 번 실행한 탐색 결과다. 저장소에 fixture와 실행
script가 없으므로 공식 acceptance evidence에는 포함하지 않는다.

## 4. 주요 발견 사항

### R1. [High, 확인됨] 공식 build/test 진입점이 동작하지 않는다

**근거**

- `_build/Build.cs:28-29`는 `[Solution] private readonly Solution _solution;`을
  선언하고 Restore에서 사용한다.
- `.nuke/parameters.json`은 `"Solution": "GamePatchKit.sln"`을 제공한다.
- 현재 Nuke binding에서는 해당 값이 `_solution`에 들어오지 않았고,
  `_build/Build.cs:55-56`에서 `NullReferenceException`이 발생했다.
- 직접 `dotnet test`를 실행하면 537개가 통과하므로 제품 코드 compile/test 문제가
  아니라 build orchestration의 parameter binding 문제다.

**영향**

- README에 안내된 Build, Test, Performance, Pack, Push 경로가 모두 Restore 단계에서
  막힌다.
- 로컬에서 직접 `dotnet` 명령으로 우회할 수 있어도 CI·release의 공식 재현 경로가 없다.

**권장 수정**

1. Nuke 관례에 맞춰 attribute parameter를 underscore 없는 이름으로 바꾸고 모든
   참조를 함께 변경한다.
2. stale `.nuke/build.schema.json`을 현재 Nuke version으로 다시 생성한다.
3. `./build.sh Test`, `./build.sh Pack --version <test-version>`을 깨끗한 worktree에서
   회귀 테스트한다.

### R2. [High, 범위/수용 기준 공백] Unity production 지원을 아직 선언할 수 없다

현재 PRD는 Unity 전용 assembly/package를 의도적으로 제외하고, 외부 host가
`IArtifactTransport`, `IRuntimeStorage`, `ICompressionCodec`을 구현하도록 정한다
(`docs/prd/game-patch-kit-prd.md:899-917`). 따라서 “Core와 Runtime이 Unity API를
참조하지 않는다”는 기존 검증 기준 21은 충족한다.

다만 “Unity 엔진에서 실제 사용할 수 있어야 한다”를 새 수용 기준으로 적용하면 다음
항목이 비어 있다.

- UnityWebRequest 기반 transport 구현 및 실제 HTTP download 검증
- `Application.persistentDataPath` 기반 writer lock, atomic state replace,
  immutable installation promotion 구현
- Unity lifecycle/app suspend와 `CancellationToken` 연결
- Unity Test Framework에서 실행할 conformance runner
- managed stripping을 고려한 `link.xml` 또는 preserve 정책
- Mono/IL2CPP 및 macOS/Windows/Android/iOS/WebGL 지원 matrix
- Unity에서의 zstd native library 배치와 target별 player build

탐색성 smoke에서는 Unity 6000.4.4f1 Editor와 macOS Mono Development Player가
Core/Runtime assembly를 읽었고, 무압축 단일 파일의 manifest 검증, download,
staging, promotion, state commit까지 성공했다. 이는 엔진 독립 managed code의
기본 방향이 맞다는 증거다. 다만 실제 Unity adapter가 아니라 memory fake를 사용했고,
IL2CPP module도 설치되어 있지 않아 production 지원 증거로는 부족하다.

Unity 6은 .NET Standard 2.1 managed plugin을 지원하고, Unity의 공식 Newtonsoft
package는 GamePatchKit이 사용하는 Newtonsoft.Json 13.0.2 계열을 제공한다.
반면 NativeCompressions는 preview이며 iOS IL2CPP를 지원하지 않는다고 명시한다.

**권장 최소 범위**

1. 기존 Runtime 책임은 바꾸지 않고, Unity host integration을 별도 경계로 둔다.
2. 첫 공식 범위는 `Unity 6 + compression:none`으로 고정한다.
3. repository에 최소 Unity fixture와 batchmode script를 코드로 남긴다.
4. 실제 UnityWebRequest/persistentDataPath adapter를 conformance suite에 연결한다.
5. 최소 Mono와 IL2CPP player build/run을 자동화한 뒤 Android를 추가한다.
6. iOS IL2CPP에서는 기본 zstd를 명시적으로 비활성화하고, 별도 codec이 생기기 전까지
   `compression:none`만 지원한다.

참고:

- [Unity 6 .NET profile support](https://docs.unity3d.com/kr/current/Manual/dotnet-profile-support.html)
- [Unity Newtonsoft.Json package](https://docs.unity3d.com/Packages/com.unity.nuget.newtonsoft-json@3.2/manual/index.html)
- [Unity managed code stripping](https://docs.unity3d.com/kr/6000.0/Manual/managed-code-stripping.html)
- [Unity preservation rules](https://docs.unity3d.com/kr/current/Manual/managed-code-stripping-preserving.html)
- [Unity IL2CPP introduction](https://docs.unity3d.com/jp/current/Manual/il2cpp-introduction.html)
- [Unity DownloadHandlerScript](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Networking.DownloadHandlerScript.html)
- [Unity DownloadHandlerFile](https://docs.unity3d.com/6000.0/Documentation/ScriptReference/Networking.DownloadHandlerFile.html)
- [NativeCompressions.Zstandard package](https://www.nuget.org/packages/NativeCompressions.Zstandard/)

### R3. [High, 확인됨] PRD의 공식 Linux 성능 gate가 미실행 상태다

PRD 검증 기준 17은 Ubuntu 24.04 x64에서 1만 파일·1GiB fixture의 package,
compact, verify, download peak RSS 512MiB 이하와 256MiB→1GiB 증가분 64MiB 이하를
요구한다.

현재 harness와 macOS smoke 결과는 존재하지만,
`docs/perf/README.md`와 `docs/plan/12-performance-validation.md`가 공식 gate
미실행을 명시한다. `.github` workflow도 없어 이를 실행하는 자동 환경이 없다.

**영향**

- 구현 결함을 의미하지는 않지만 명시적인 release acceptance criterion이 미충족이다.
- macOS smoke 수치는 기준 환경과 측정 도구가 달라 pass/fail 근거로 사용할 수 없다.

**권장 수정**

- Ubuntu 24.04 x64, 4 vCPU, 8GiB RAM, local SSD, Workstation GC 환경을 CI로 고정한다.
- 문서의 명령대로
  `GPK_PERF_FULL_SCALE=1 GPK_PERF_OFFICIAL_GATE=1`을 설정해 3회 측정한다.
- 결과를 `docs/perf/results.md`에 기록하고 release required check로 만든다.

### R4. [Medium, 확인됨] zstd decoder가 정상 frame 뒤의 trailing byte를 무시한다

`ZstdCompressionCodecFactory.DecompressAsync`는 decoder가 첫
`OperationStatus.Done`을 반환하면 loop를 끝낸다
(`src/GamePatchKit.Compression.NativeCompressions/ZstdCompressionCodecFactory.cs:64-106`).
그 시점에 현재 buffer에 남은 byte가 있는지, source가 EOF인지 확인하지 않는다.

리뷰 중 `valid zstd frame + garbage` 입력을 추가한 임시 회귀 테스트는 예외가 발생하지
않아 실패했고, 테스트 파일은 원복했다.

**영향**

- codec이 malformed artifact를 strict하게 거부하지 않는다.
- 일반 전송 손상은 상위 artifact hash 검증이 차단한다. 그러나 manifest가 trailing
  byte까지 포함한 artifact hash를 정상 값으로 선언한 경우 decoder는 선언된 byte
  일부를 소비하지 않고도 성공할 수 있다.

**권장 수정**

- `Done` 시 현재 buffer의 미소비 byte와 source EOF를 모두 확인한다.
- format이 concatenated frame을 허용하지 않는 계약이면 추가 frame도 동일하게
  거부한다.
- valid frame + 1 byte, valid frame + second frame, non-seekable stream을 각각
  회귀 테스트한다.

### R5. [Medium, 확인됨] build dependency에 알려진 High 취약점이 남아 있다

`Directory.Packages.props`는 build project가 직접 참조하는
`System.Security.Cryptography.Xml`을 `10.0.9`로 고정한다. dependency audit에서
High severity advisory 5개가 확인됐고, 모두 `10.0.10`에서 수정됐다.

- [GHSA-cvvh-rhrc-wg4q](https://github.com/advisories/GHSA-cvvh-rhrc-wg4q)
- [GHSA-g8r8-53c2-pm3f](https://github.com/advisories/GHSA-g8r8-53c2-pm3f)
- [GHSA-23rf-6693-g89p](https://github.com/advisories/GHSA-23rf-6693-g89p)
- [GHSA-8q5v-6pqq-x66h](https://github.com/advisories/GHSA-8q5v-6pqq-x66h)
- [GHSA-mmjf-rqrv-855v](https://github.com/advisories/GHSA-mmjf-rqrv-855v)

제품 Runtime dependency는 아니어서 직접 노출은 제한적이지만, official build chain의
취약 dependency이므로 `10.0.10` 이상으로 즉시 올리고 build/test를 재검증하는 것이
안전하다.

### R6. [Medium, 확인됨] Pack이 tracked version 파일을 실행 중 변경한다

`_build/Build.cs:103-120`은 `--version`이 없으면 patch version을 올린 뒤
`Directory.Build.props`에 저장한다. 이 동작은 README에 적혀 있어 숨겨진 동작은
아니지만 다음 위험이 있다.

- 실패한 Pack도 source tree의 version을 먼저 변경할 수 있다.
- Pack과 Push를 따로 실행하면 version이 두 번 증가한다.
- release 재시도와 artifact 재현이 worktree 상태에 의존한다.

**권장 수정**

- Pack은 source를 변경하지 않고 명시적 version 또는 CI tag/version을
  `dotnet pack -p:Version=...`으로 전달한다.
- version 파일 변경은 별도의 명시적 release/version-bump 작업으로 분리한다.

### R7. [Low, 확인됨] 문서·배포 단계가 완료되지 않았다

`docs/plan/13-documentation.md`의 모든 항목이 미완료다. 현재 README는 build와
version 관리만 설명하며 다음 사용 경로가 빠져 있다.

- `gpk` 설치와 package/incremental/compact/publish 예제
- `gamepatchkit.yml`, glob, schema/canonical 규칙
- Runtime/DotNet 및 Unity host 통합
- signing/key rotation 운영
- conformance suite 실행
- target manifest와 publisher 계약

NuGet 4종은 direct pack에 성공했지만 모두 package README가 없다는 warning을
출력했다. 외부 사용자가 package만 보고 적용하기 어려운 상태다.

### R8. [Low, 확인됨] 자동 품질 gate와 형식 기준이 고정되지 않았다

- 저장소에 GitHub Actions 등 CI workflow가 없다.
- `dotnet format GamePatchKit.sln --verify-no-changes --no-restore`가 repository
  전반의 whitespace/final-newline 차이로 실패한다.
- client package의 server-private 정보 미포함은 전용 fixture/test 이름으로
  추적하기 어렵다.
- Unity 적합성과 Linux 성능 gate도 자동화되어 있지 않다.

대규모 일괄 format은 이번 리뷰 범위를 벗어난다. formatter version과 `.editorconfig`
기준을 먼저 고정하고 별도 mechanical commit으로 처리하는 편이 안전하다.

## 5. Unity 지원성 상세 판정

| 항목 | 현재 판정 | 근거/제약 |
| --- | --- | --- |
| Core/Runtime의 UnityEngine 비의존성 | PASS | architecture test와 assembly reference 확인 |
| Unity 6의 netstandard2.1 소비 | PASS | Unity 공식 지원 범위, 탐색성 Editor compile 성공 |
| Newtonsoft.Json | PASS | Unity 공식 package가 13.0.2 계열 제공 |
| BouncyCastle Ed25519 | EXPLORATORY PASS | Unity 6.4 Mono smoke에서 key ID 계산 성공 |
| 무압축 Runtime 설치 | EXPLORATORY PASS | Unity 6.4 macOS Mono player에서 1-file install 성공 |
| UnityWebRequest transport | NOT IMPLEMENTED | interface만 존재 |
| persistentDataPath storage | NOT IMPLEMENTED | interface만 존재 |
| 실제 HTTP·disk·재시도·복구 | NOT TESTED | Unity adapter 부재 |
| Unity conformance runner | NOT IMPLEMENTED | 현재 xUnit/net10 conformance를 Unity에서 직접 실행 불가 |
| managed stripping/link.xml | NOT VERIFIED | IL2CPP build/run 및 preservation 검증 없음 |
| macOS/Windows IL2CPP | NOT TESTED | 지원 판정 불가 |
| Android IL2CPP | NOT TESTED | native codec ABI 포함 검증 필요 |
| iOS IL2CPP + 기본 zstd | UNSUPPORTED | NativeCompressions preview의 명시적 제한 |
| WebGL | NOT TESTED | native zstd 없이 `compression:none` 또는 별도 codec 필요 |

즉, 현재 상태는 **Unity에서 사용할 수 있는 엔진 독립 Runtime 기반**까지는 확인됐지만
**Unity용으로 검증·배포된 Runtime integration**은 아니다.

## 6. PRD 검증 기준 1~26 추적

| 기준 | 판정 | 리뷰 메모 |
| --- | --- | --- |
| 1 | PASS | 동일 입력 artifact/manifest 결정성 테스트 |
| 2 | PASS | 기본 file artifact |
| 3 | PASS | file group과 mode 전환 |
| 4 | PASS | deterministic bundle과 크기 상한 |
| 5 | PASS | oversized bundle entry part fallback |
| 6 | PASS | incremental artifact 재사용 |
| 7 | PASS | 삭제 파일 반영 |
| 8 | PASS | compact override 통합과 file 재사용 |
| 9 | PASS | compact version/no-op identity |
| 10 | PASS | 이동한 동일 fileHash의 download 생략 테스트 |
| 11 | PARTIAL | part/bundle/manifest/signature 거부는 검증, zstd trailing byte 공백 |
| 12 | PASS | artifact 상한과 decode bound |
| 13 | PARTIAL | package scope로 분리되나 server-private 전용 fixture 추적성이 약함 |
| 14 | PASS | DotNet/fake conformance |
| 15 | PASS | 취소·재개와 cache 재사용 |
| 16 | PASS | 활성화 실패 시 이전 release 유지 |
| 17 | FAIL | 공식 Ubuntu 24.04 x64 성능 gate 미실행 |
| 18 | PASS | 실패 package/compact 기존 결과 불변 |
| 19 | PASS | 고정 zstd 결정성과 streaming round-trip |
| 20 | PASS | 미지원 codec 사전 실패 |
| 21 | PASS | 요구 문구대로 Unity API 비참조; 실제 Unity 사용성은 별도 미검증 |
| 22 | PASS | required-only와 optional group 전환 |
| 23 | PASS | atomic state, batch, 손상 복구 |
| 24 | PASS | YAML 제한과 schema/model 검증 |
| 25 | PASS | union, canonical, signature, key ID/vector |
| 26 | PASS | glob 결정성과 filesystem snapshot 경합 방어 |

## 7. 권장 진행 순서

### P0 — 공식 개발·배포 경로 복구

1. Nuke parameter binding 수정
2. `System.Security.Cryptography.Xml` 10.0.10 이상으로 업데이트
3. zstd trailing byte 거부와 회귀 테스트 추가
4. `./build.sh Test`와 clean-worktree Pack 검증

### P1 — Unity 지원 범위 확정과 재현 가능한 검증

1. PRD에서 “Unity portable”과 “Unity officially supported”를 분리해 정의
2. Unity 6 + `compression:none` 최소 fixture와 adapter 구현
3. Editor/Mono/IL2CPP batch build 및 player smoke 자동화
4. Unity adapter conformance 실행
5. 지원 OS·backend·codec matrix 문서화

### P1 — 성능 gate 완료

1. Ubuntu 24.04 x64 기준 CI job 생성
2. 256MiB/1GiB 3회 공식 측정
3. 결과 기록 및 required release check 지정

### P2 — 배포 문서와 package UX

1. `docs/plan/13-documentation.md` 완료
2. NuGet package README 포함
3. Pack의 version mutation 제거
4. formatter와 dependency audit를 CI gate로 추가

## 8. 최종 승인 조건

다음이 모두 충족되면 다시 release review를 진행한다.

- `./build.sh Test` 성공
- clean worktree에서 `./build.sh Pack --version <version>` 성공 및 source 불변
- zstd trailing/concatenated input 회귀 테스트 통과
- vulnerability audit에 High/ Critical 없음
- Unity의 합의된 최소 matrix에서 재현 가능한 conformance/player smoke 통과
- Ubuntu 24.04 x64 공식 512MiB/64MiB 성능 gate 통과
- README만으로 package → publish → Runtime install 시나리오 재현 가능

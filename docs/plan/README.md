# GamePatchKit 개발 계획

[PRD](../prd/game-patch-kit-prd.md)의 구현 순서를 기준으로 v1 개발을 13단계로 나눈다.
각 단계는 개별 파일로 관리하고, 진행은 각 파일의 체크박스로 추적한다. 완료 기준은
PRD 검증 기준 번호(1~26)로 연결한다.

## 단계 목록

| 단계 | 계획 | 선행 단계 |
| --- | --- | --- |
| 01 | [solution·프로젝트 구성](01-solution-setup.md) | - |
| 02 | [JSON Schema·canonical 규칙 고정](02-schema-canonicalization.md) | 01 |
| 03 | [Core identity·manifest hash·diff·download plan](03-core-identity-diff-plan.md) | 02 |
| 04 | [zstd codec adapter](04-zstd-codec-adapter.md) | 03 |
| 05 | [Packager file artifact·package](05-packager-file-artifacts.md) | 03, 04 |
| 06 | [deterministic bundle·compact](06-bundle-compact.md) | 05 |
| 07 | [CLI `gpk`](07-cli.md) | 05, 06 |
| 08 | [Runtime 상태 머신](08-runtime.md) | 03 |
| 09 | [DotNet adapter·통합 테스트](09-dotnet-adapter.md) | 04, 08 (e2e는 05·06 이후) |
| 10 | [adapter conformance](10-adapter-conformance.md) | 08, 09 |
| 11 | [서명·key rotation](11-signing-key-rotation.md) | 07, 08, 10 |
| 12 | [성능·메모리 검증](12-performance-validation.md) | 05~11 |
| 13 | [문서화·배포 산출물](13-documentation.md) | 01~12 |

## 트랙과 병렬 진행

Packager와 Runtime은 Core 계약(03)으로만 연결되므로 03 이후 두 트랙을 병렬로
진행할 수 있다.

- 공통 기반: 01 → 02 → 03 → 04
- Packager 트랙: 05 → 06 → 07 (04 이후)
- Runtime 트랙: 08 (03 이후 시작 가능) → 09 (04·08 이후, e2e는 05·06 이후) → 10
- 서명 통합: 11 (07·08·10 이후)
- 마무리: 12 (05~11 이후) → 13

## 공통 규칙

- 각 단계는 완료 기준의 테스트가 통과해야 종료한다.
- 단계 진행 중 확정한 결정 사항은 해당 단계 파일에 기록한다.
- 계획과 PRD가 충돌하면 PRD를 우선하고, PRD 수정이 필요하면 먼저 논의한다.

## PRD 검증 기준 매핑

| 검증 기준 | 내용 요약 | 담당 단계 |
| --- | --- | --- |
| 1 | 같은 입력 → 같은 artifact·manifest byte | 05, 06, 12 |
| 2 | 기본 설정은 모두 file artifact | 05 |
| 3 | file group의 bundle 미포함·group/mode 전환 | 05, 06 |
| 4 | deterministic bundle·크기 상한 | 06 |
| 5 | 큰 bundle entry의 file part fallback | 06 |
| 6 | incremental의 기존 artifact 재사용·compression 변경 시 재생성 안 함 | 05, 06 |
| 7 | 삭제 파일 반영 | 05 |
| 8 | compact의 override 통합·file 재사용 | 06 |
| 9 | compact 물리 변경 시 version 증가, 동일 배치는 no-op 재사용 | 03, 06, 07 |
| 10 | compact 후 재다운로드 없음 | 08, 09 |
| 11 | 손상 part·bundle·manifest·signature 거부 | 05, 06, 07, 08, 09, 11 |
| 12 | 모든 artifact ≤ `maxArtifactBytes` | 05, 06 |
| 13 | client package에 private 정보 없음 | 05 |
| 14 | DotNet·fake adapter 동일 결과 | 10 |
| 15 | 취소 후 재개·cache 재사용 | 08, 09 |
| 16 | 활성화 실패 시 이전 release 유지 | 08, 09 |
| 17 | 1만 파일·1GiB streaming·peak RSS 512MiB·scaling delta 64MiB | 12 |
| 18 | 실패한 실행이 기존 결과 불변 | 05, 06 |
| 19 | zstd deterministic round-trip | 04 |
| 20 | 미지원 codec 요구 시 활성화 전 실패 | 08 |
| 21 | Core·Runtime의 Unity API 비참조 | 01 |
| 22 | required-only 설치·optional group 상태 전환 | 03, 08, 09, 10 |
| 23 | `PackageState` 원자적 교체·multi-group batch·손상 복구 | 08, 09, 10 |
| 24 | 단일-document YAML 설정·금지 기능·schema 검증 | 02, 07, 13 |
| 25 | manifest union·참조 무결성·canonical/signature·key ID·RFC 8032 vector | 02, 03, 05, 06, 07, 08, 10, 11 |
| 26 | deterministic glob 선택·filesystem snapshot·경합 거부 | 02, 05, 13 |

## 진행 상태

- [ ] 01 solution·프로젝트 구성
- [x] 02 JSON Schema·canonical 규칙 고정
- [x] 03 Core identity·manifest hash·diff·download plan
- [ ] 04 zstd codec adapter
- [ ] 05 Packager file artifact·package
- [ ] 06 deterministic bundle·compact
- [ ] 07 CLI `gpk`
- [ ] 08 Runtime 상태 머신
- [ ] 09 DotNet adapter·통합 테스트
- [ ] 10 adapter conformance
- [ ] 11 서명·key rotation
- [ ] 12 성능·메모리 검증
- [ ] 13 문서화·배포 산출물

## 개발 v2 진행 상태

v1 진행 상태를 보존하며 아래 항목이 같은 단계의 최신 상태를 덮어쓴다.

- [x] 04 zstd codec adapter
- [x] 05 Packager file artifact·package
- [x] 06 deterministic bundle·compact
- [x] 07 CLI `gpk`
- [x] 08 Runtime 상태 머신
- [x] 09 DotNet adapter·통합 테스트
- [x] 10 adapter conformance
- [x] 11 서명·key rotation

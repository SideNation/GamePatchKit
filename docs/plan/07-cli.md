# 07. CLI (`gpk`)

> PRD 섹션: CLI 기능, 프로젝트 책임 `GamePatchKit.Cli`, 서명과 무결성

## 목표

Packager·Core 기능을 명령행으로 노출한다: `package`, `diff`, `verify`, `compact`,
`plan-download`, `sign`. CI에서 사용할 수 있는 exit code와 machine-readable 결과를
제공한다.

## 선행 단계

05, 06

## 작업 항목

### 공통 기반

- [ ] `gamepatchkit.yml` 로드·schema 검증·오류 보고
- [ ] exit code 체계 고정: 성공 `0`, 입력 오류·무결성 오류·실행 실패를 구분하는
      non-zero 코드표
- [ ] `--json` machine-readable 결과 출력
- [ ] 파일을 만들지 않는 `--dry-run` (package, compact, sign 등 의미 있는 명령)
- [ ] secret과 개인키 내용을 로그·결과에 기록하지 않는다

### 명령

- [ ] `package`: 최초 또는 incremental release 생성 (이전 manifest 입력 시 incremental)
- [ ] `diff`: 두 release의 논리 파일 차이와 물리 artifact 차이 출력
- [ ] `verify`: source·artifact·manifest 검증 (signature 검증 통합은 11 단계)
- [ ] `compact`: 선택 bundle group의 새 baseline 생성
- [ ] `plan-download`: 로컬 상태에서 목표 release까지 필요한 artifact·byte 계산
      (로컬 상태 수집 helper는 Packager에 구현하고 Core의 download plan을 사용)
- [ ] `sign`: canonical manifest에 Ed25519 signature 생성, `manifest.sig`에는
      알고리즘·key ID·signature만 기록 (개인키는 파일 경로 또는 환경 변수로 입력)

### 관측 지표

- [ ] package·diff 결과에 PRD 성능·관측 기준의 지표를 포함한다: 전체·group별 파일
      수·byte, 생성·재사용 artifact 수, 추가·변경·삭제 수, 예상 다운로드, 단계별 시간

## 결정 사항

- [ ] Ed25519 서명 구현 선정: 서명 생성(net10.0)과 Core 검증(netstandard2.1,
      11 단계)이 같은 구현을 공유할 수 있는지 확인하고 선택한다. 구체 라이브러리
      type은 public API에 노출하지 않는다.

## 산출물

- `gpk` 실행 파일(.NET tool)과 명령별 통합 테스트

## 완료 기준

- 성공·입력 오류·무결성 오류·실행 실패가 문서화된 exit code로 구분된다.
- `--json` 출력이 안정된 형식을 가지고 CI에서 파싱 가능하다.
- `--dry-run`이 어떤 파일도 만들지 않는다.
- `sign`이 개인키 내용을 어디에도 출력하지 않고 유효한 `manifest.sig`를 생성한다.

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

- [ ] `gamepatchkit.yml`을 정확히 하나의 non-empty YAML document와 mapping root로
      로드
- [ ] 두 번째 document, anchor, alias, merge key(`<<`), custom tag와 중복 mapping
      key를 schema 검증 전에 거부
- [ ] YAML을 JSON-compatible 데이터로 변환한 뒤 `package-config.schema.json`과 설정
      모델을 순서대로 검증
- [ ] YAML 문법 오류·schema 오류·설정 의미 오류를 입력 오류로 구분해 보고
- [ ] 02의 유효·무효 YAML fixture로 입력 계약의 허용·거부 동작을 테스트한다
      (검증 기준 24의 동작 검증은 07에서 완료)
- [ ] exit code 체계 고정: 성공 `0`, 입력 오류·무결성 오류·실행 실패를 구분하는
      non-zero 코드표
- [ ] `--json` machine-readable 결과 출력
- [ ] package·compact 결과에 `dataVersion`·`compactVersion`·`manifestHash` 출력
- [ ] compact 결과에 `changed`를 포함하고 no-op이면 기존 identity를 그대로 출력
- [ ] 파일을 만들지 않는 `--dry-run` (package, compact, sign 등 의미 있는 명령)
- [ ] secret과 개인키 내용을 로그·결과에 기록하지 않는다

### Packager 재사용 API

- [ ] CLI와 독립적인 asynchronous streaming verify API를 Packager에 구현해 schema →
      Core 의미·참조 무결성 → 실제 payload byte 검증을 orchestration하고 typed report와
      공통 오류를 반환
- [ ] verify API에 signature·신뢰 키 option 확장점을 정의하되 07에서는 unsigned
      검증을 완성하고 실제 signature primitive 검증은 11의 Core API로 연결
- [ ] canonical manifest와 `manifestHash`를 검증한 뒤 주입된 signing key handle 또는
      signer로 `manifest.sig` 모델·canonical byte를 만드는 Packager sign API 구현
- [ ] Packager API는 console·환경 변수·CLI option을 읽지 않고 streaming·cancellation을
      지원하며, CLI는 키 파일·환경 변수 로드와 결과·exit code 변환만 담당
- [ ] Packager API 직접 테스트와 같은 fixture를 사용한 CLI 통합 테스트의 결과 일치 검증

### 명령

- [ ] `package`: 최초 또는 incremental release 생성 (이전 manifest 입력 시 incremental)
- [ ] `diff`: 두 release의 논리 파일 차이와 물리 artifact 차이 출력
- [ ] `verify`: schema → Core 의미·참조 무결성 → 실제 artifact byte 순서로 source·
      artifact·manifest 검증 (signature 검증 통합은 11 단계)
- [ ] `compact`: 선택 bundle group의 새 baseline 생성, 물리 배치가 같으면 성공 no-op
- [ ] `plan-download`: 로컬 상태에서 목표 release까지 필요한 artifact·byte 계산
      (`--required-only` 또는 명시적인 `--group` 집합 지원, 로컬 상태 수집 helper는
      Packager에 구현하고 Core의 download plan을 사용)
- [ ] `sign`: canonical manifest에 Ed25519 signature 생성,
      raw public key에서 `ed25519-<SHA-256 lowercase hex>` key ID를 파생하고
      `manifest-signature.schema.json`의 필드만 canonical `manifest.sig`에 기록
      (개인키는 CLI가 파일 경로 또는 환경 변수로 로드해 Packager signer에 전달)
- [ ] 기존 `manifest.sig`가 같은 byte면 검증 후 재사용하고 다른 key ID·signature면
      불변 경로를 덮어쓰지 않고 실패
- [ ] sign 테스트는 선정 Ed25519 구현의 verify로 서명 byte의 암호학적 유효성을
      확인한다 (Core 공용 검증 API·signed golden vector 연결은 11 단계)

### 관측 지표

- [ ] package·diff 결과에 PRD 성능·관측 기준의 지표를 포함한다: 전체·group별 파일
      수·byte, 생성·재사용 artifact 수, 추가·변경·삭제 수, 예상 다운로드, 단계별 시간

## 결정 사항

- [ ] Ed25519 서명 구현 선정: 서명 생성(net10.0)과 Core 검증(netstandard2.1,
      11 단계)이 같은 구현을 공유할 수 있는지 확인하고 선택한다. 구체 라이브러리
      type은 public API에 노출하지 않는다.
- [ ] YAML 파서 라이브러리 선정: 단일 document 강제, anchor·alias·merge key·custom
      tag 거부와 중복 mapping key 거부를 구현할 수 있는지를 기준으로 선택한다.
      파서 type은 public API에 노출하지 않는다.

## 산출물

- Packager의 재사용 가능한 streaming verify·sign API와 직접 테스트
- 해당 API를 호출하는 `gpk` 실행 파일(.NET tool)과 명령별 통합 테스트

## 완료 기준

- 성공·입력 오류·무결성 오류·실행 실패가 문서화된 exit code로 구분된다.
- `--json` 출력이 안정된 형식을 가지고 CI에서 파싱 가능하다.
- `--dry-run`이 어떤 파일도 만들지 않는다.
- Packager API와 CLI가 같은 fixture에서 동일한 verify·sign 결과와 오류를 만든다.
- `verify`가 손상된 part·bundle·manifest payload를 무결성 오류 exit code로 거부한다
  (검증 기준 11, Packager verify 범위).
- compact no-op이 exit code `0`, `changed: false`와 기존 identity를 반환하고 파일을
  만들지 않는다(검증 기준 9).
- `sign`이 개인키 내용을 어디에도 출력하지 않고 파생 `keyId`의 유효한
  `manifest.sig`를 생성하며 불변 signature 충돌을 거부한다(검증 기준 25,
  signature 생성 범위).
- 유효한 단일-document `gamepatchkit.yml`은 로드되고, 금지한 YAML 기능과 다중
  document는 package 실행 전에 실패한다(검증 기준 24).

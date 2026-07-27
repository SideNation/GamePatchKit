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

- [x] Ed25519 서명 구현 선정: 서명 생성(net10.0)과 Core 검증(netstandard2.1,
      11 단계)이 같은 구현을 공유할 수 있는지 확인하고 선택한다. 구체 라이브러리
      type은 public API에 노출하지 않는다.
- [x] YAML 파서 라이브러리 선정: 단일 document 강제, anchor·alias·merge key·custom
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

## 개발 v2

v1 계획 본문은 보존하며 아래 내용이 현재 구현 상태를 나타낸다.

### 구현 결과

- [x] `YamlConfigDocument`가 YamlDotNet event stream을 직접 읽어 비어 있지 않은 단일
      document와 mapping root만 허용하고, anchor·alias·merge key·custom tag·중복
      mapping key를 규칙별 오류 코드로 거부한다
- [x] YAML을 JSON-compatible 데이터로 변환한 뒤 Packager의 `PackageConfigReader`가
      `package-config.schema.json` → `PackageConfig.TryParse` →
      `PackageConfigValidator` 순서로 검증한다
- [x] exit code를 성공 `0`, 입력 오류 `1`, 무결성 오류 `2`, 실행 실패 `3`으로 고정하고
      오류 코드에서 분류한다(`ExitCode`)
- [x] `--json`이 canonical JSON envelope(`command`·`ok`·`exitCode`·`dryRun`·
      `result`|`errors`)을 stdout에 출력한다
- [x] package·compact 결과가 `dataVersion`·`compactVersion`·`manifestHash`를 출력하고
      compact는 `changed`를 포함하며 no-op이면 source의 세 값을 그대로 반환한다
- [x] `--dry-run`이 package·compact·sign에서 전체 계산을 수행하고 output tree를 전혀
      바꾸지 않는다
- [x] Packager에 CLI와 독립적인 `ReleaseVerifier`(schema → Core 의미·참조 무결성 →
      payload byte → signature document)와 `ReleaseSigner` streaming API를 추가했다
- [x] verify API가 `IManifestSignatureVerifier`·`RequireSignature` 확장점을 노출하고
      07에서는 unsigned 검증을 완성한다(`SignatureState`: `absent`·`present`·`verified`).
      `RequireSignature`는 verifier 없이 지정할 수 없고, CLI는 서명 필수 option을 제공하지
      않는다
- [x] `ReleaseSigner`가 canonical manifest·`manifestHash` 검증 후 주입된
      `IManifestSigner`로 `manifest.sig` 모델과 canonical byte를 만들고, 같은 byte는
      검증 후 재사용하며 다른 key ID·signature는 불변 경로 충돌로 실패한다
- [x] `Ed25519ManifestSigner`가 raw public key SHA-256에서 `ed25519-<hex64>` key ID를
      파생하고 개인키를 어떤 출력에도 남기지 않는다
- [x] `package`·`diff`·`verify`·`compact`·`plan-download`·`sign`을 구현했다
- [x] `plan-download`가 `--required-only` 또는 명시적 `--group` 집합만 받고 Packager의
      `LocalInstallationScanner`로 설치 상태·cache를 수집해 Core download plan을 쓴다
- [x] package·diff 결과가 전체·group별 파일 수·byte, 생성·재사용 artifact 수·byte,
      추가·변경·삭제·group 이동 수, 예상 다운로드와 단계별 시간을 포함한다

### 확정 결정

- Ed25519 구현은 `BouncyCastle.Cryptography`로 고정한다. .NET 10의
  `System.Security.Cryptography`에는 Ed25519가 없고, netstandard2.0 target을 가진 순수
  관리 코드 구현이므로 07의 Packager 서명(net10.0)과 11의 Core 검증(netstandard2.1)이
  같은 구현을 공유할 수 있다. `IManifestSigner`·`Ed25519ManifestSigner`는 byte와
  문자열만 노출하고 라이브러리 type을 public API에 넣지 않는다.
- YAML 파서는 `YamlDotNet`으로 고정하되 object model이 아니라 low-level event parser를
  사용한다. deserializer는 anchor·alias·merge key를 해석한 뒤 결과만 돌려주고 중복 key를
  마지막 값으로 덮어쓰므로, 금지 규칙을 강제할 수 있는 지점이 event stream뿐이다.
- YAML plain scalar는 `null`·boolean·10진 정수만 typed 값으로 해석하고 나머지는
  문자열로 둔다. hex·octal·부동소수점·I-JSON 안전 범위를 넘는 정수는 문자열이 되어
  schema의 type 오류로 거부되므로 canonical JSON이 표현할 수 없는 값이 설정에서
  만들어지지 않는다. 인용된 scalar는 항상 문자열이다.
- YAML 문서 규칙은 CLI, schema·모델 검증은 Packager가 맡는다. 다른 host는
  `PackageConfigReader`에 JSON-compatible 데이터를 넘겨 같은 검증을 재사용한다.
- exit code는 첫 오류로 분류한다. 파이프라인이 가장 구체적인 검사부터 수행하므로 첫
  오류가 실행이 멈춘 이유이고, 묶여 오는 나머지는 같은 종류의 다른 사례다.
- 이전 release·compact source·retained release는 모두 `manifestHash`로만 지정한다.
  이미 같은 output tree에 게시된 release이므로 경로를 따로 받을 이유가 없다.
- compact의 `--retained`는 자동 수집하지 않는다. 디렉터리 목록은 보관 중인 release의
  object와 잔여물을 구분할 수 없으므로 운영자가 명시한 release만 inventory에 넣는다.
- `--dry-run`은 계산을 생략하지 않는다. artifact를 만들어야 hash를 알 수 있으므로 실제
  identity를 그대로 보고하고 게시만 하지 않는다. 대신 게시 후 수행하는 published byte
  재검증은 건너뛴다.
- `verify`는 caller가 준 `manifestHash`와 비교하고 manifest byte에서 다시 계산하지
  않는다. 재계산하면 자기 일관적인 모든 manifest가 통과한다.
- `SignatureState.Present`는 서명 증거가 아니다. 형식만 맞는 `keyId`와 아무 64 byte면
  도달하므로 "서명된 release만 배포" 게이트로 쓸 수 없다. 따라서
  `ReleaseVerifyRequest.RequireSignature`는 `SignatureVerifier` 없이 지정하면 거부하고,
  CLI는 07에서 서명 필수 option을 아예 제공하지 않는다. 신뢰 키 집합과 primitive 검증이
  들어오는 11 단계에서 `verified`를 요구하는 option과 함께 추가한다.
- 명시적 YAML tag가 있으면 철자·인용보다 tag가 우선한다. `!!str 1`은 문자열이다. 허용
  tag는 값 모델이 정의한 `!!str`·`!!int`·`!!bool`·`!!null`·`!!map`·`!!seq`뿐이며,
  `!!float`·`!!binary`는 내장 tag여도 canonical JSON이 표현할 수 없으므로 거부한다.
- CLI 최상위에 마지막 `catch (Exception)`을 둔다. 모든 종료가 문서화된 네 exit code 중
  하나에 들어가야 하며, stack trace와 exit 134는 CI가 "입력이 잘못됐다"와 "도구가
  깨졌다"를 구분할 수 없게 만든다.

### 검증

- 02가 고정한 `tests/fixtures/gamepatchkit-yml/` fixture로 유효 2건의 로드와 무효 8건의
  규칙별 거부를 테스트한다(검증 기준 24). merge key 단독, 미해결 alias, 내장 tag 허용,
  인용 scalar 타입 보존도 함께 확인한다.
- 손상된 file part·bundle archive·manifest payload를 각각 원본과 같은 길이로 덮어써
  크기 검사가 아니라 hash로 걸리게 한 뒤 exit code `2`를 확인한다(검증 기준 11).
- compact no-op이 exit code `0`, `changed: false`, source identity를 반환하고 output
  tree를 바꾸지 않는지 확인한다(검증 기준 9).
- package·compact·sign `--dry-run`이 output tree hash 맵을 그대로 유지하고, package는
  실제 실행과 같은 identity를 보고하는지 확인한다.
- sign이 raw public key SHA-256에서 파생한 key ID를 쓰고, 같은 key 재실행은 재사용,
  다른 key는 불변 경로 충돌로 실패하며, 개인키 문자열이 stdout·stderr에 나타나지 않는지
  확인한다(검증 기준 25, signature 생성 범위).
- 같은 fixture를 Packager API와 CLI로 각각 실행해 verify identity·totals·group별 값,
  손상 payload의 오류 코드, sign의 key ID와 `manifest.sig` byte가 일치하는지 확인한다.
- 서명 byte는 BouncyCastle Ed25519 verify로 독립 검증한다.
- 위조 `manifest.sig`(형식만 맞는 `keyId`와 64 byte `0xff`)가 `verified`로 보고되지 않고
  통과할 게이트도 없는지, verifier 없는 `RequireSignature`가 거부되는지 확인한다.
- `!!str 1`이 문자열로 읽히는지 token type으로 확인한다. `(string?)` 캐스팅은 정수 token도
  `"1"`로 바꾸므로 캐스팅 기반 단언은 잘못된 타입을 통과시킨다.
- `long.MinValue`·`long.MaxValue`·I-JSON 경계 정수 literal이 crash 없이 문자열로 떨어지고
  CLI가 exit `1`과 JSON envelope를 반환하는지 확인한다.

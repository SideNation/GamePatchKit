# 02. JSON Schema·canonical 규칙 고정

> PRD 섹션: package 설정, 배포 대상 파일 규칙, release manifest, 배포 산출물

## 목표

`gamepatchkit.yml` 입력 계약과 package 설정·release manifest·manifest signature의
JSON Schema를 확정하고, canonical JSON·manifest 참조 무결성과 경로·glob 순수 규칙을
Core에 구현해 이후 모든 단계가 사용할 데이터 계약을 고정한다.

## 선행 단계

01

## 작업 항목

### JSON Schema 작성 (`schemas/`)

- [x] `package-config.schema.json`
  - 사용자가 작성하는 YAML 자체나 생성 manifest가 아니라, YAML 파싱 후의 설정 데이터
    구조를 검증하는 versioned 배포 산출물
  - `schemaVersion`, `packageId`(소문자 kebab-case), `inputRoot`,
    `include`(비어 있을 수 없는 allowlist), `exclude`,
    `maxArtifactBytes`(기본 `10,485,760`), `defaultArtifactMode`(기본 `file`),
    `compression`(`none` 또는 codec ID·level·frame option이 고정된 `zstd`), `groups[]`
  - group 필드: `name`(package 안 유일, kebab-case), `include`, `artifactMode`,
    `required`, `compression`
  - compression은 새 artifact 생성 정책이며 기존 artifact의 유효성 조건이 아님을
    description으로 고정
- [x] `release-manifest.schema.json`
  - `schemaVersion`, `packageId`, `dataVersion`, `compactVersion`, `groups[]`,
    `artifacts[]`, `files[]`
  - 모든 object에 `additionalProperties: false` 적용
  - `artifacts[].kind`를 `file`·`bundle`로 구분하는 `oneOf`와 `const` discriminator
  - file `payload.kind`를 단일 payload `single`과 전체 `artifactHash`·순서 있는
    `parts[]`를 가진 `parts`로 구분
  - `files[].source.kind`를 file artifact 참조 `file`과 bundle·entry 참조
    `bundleEntry`로 구분
  - compression metadata를 `kind: none`·`kind: zstd` union으로 정의하고 branch별
    필수·금지 필드 고정
  - hash는 lowercase hexadecimal 64자, 정수는 I-JSON safe integer 범위로 제한
  - `manifestHash`는 manifest 내부에 기록하지 않는다
  - `groups[]`에는 build-time compression 설정을 기록하지 않고 실제 compression은
    `artifacts[]`에만 기록한다
  - 같은 release·group 안에서 artifact별 compression metadata가 달라도 허용한다
- [x] `manifest-signature.schema.json`
  - `schemaVersion: 1`, `algorithm: Ed25519`, `keyId`, `signature`만 허용
  - `keyId`는 raw 32-byte public key의 SHA-256으로 파생한
    `^ed25519-[0-9a-f]{64}$` 형식
  - signature는 padding 없는 base64url로 표현한 정확히 64-byte 값

### `gamepatchkit.yml` 입력 계약 (계약·fixture 정의)

YAML 로드·문법 검증의 구현과 동작 테스트는 07 CLI 책임이다. 02는 입력 계약과
fixture만 고정한다.

- [x] 입력 계약 문서화: 비어 있지 않은 YAML document 정확히 하나와 mapping root만
      허용하고 두 번째 document, anchor, alias, merge key(`<<`), custom tag와 중복
      mapping key를 거부한다 (`docs/contracts/gamepatchkit-yml.md`)
- [x] 변환·검증 순서 고정: JSON-compatible scalar·array·mapping만 설정 데이터로
      변환하며 YAML 문법 검증 → JSON-compatible 데이터 변환 →
      `package-config.schema.json` 검증 → 설정 모델 의미 검증 순서를 따른다
- [x] 07 CLI parser 테스트가 추가 fixture 없이 검증 기준 24를 구성할 수 있는
      유효·무효 YAML fixture 제공 (`tests/fixtures/gamepatchkit-yml/`)
- [x] release manifest는 설정 파일이 아니라 Packager가 생성하는 별도 canonical JSON
      산출물임을 schema 설명에 명시

### Core 모델·검증

- [x] config·manifest 모델 클래스 (YAML 파일 로드·문법 검증은 CLI 책임이며,
      Core는 filesystem과 YAML parser에 의존하지 않는다) — `manifest-signature`
      모델도 함께 구현
- [x] schema 규칙에 대응하는 모델 검증: packageId·group 이름 규칙, group 중복 일치
      오류, 예약 group `default` 처리, 음수가 아닌 `compactVersion`
- [x] config의 모든 glob pattern을 Core parser로 검증하고 전역 include → exclude →
      group matching 순서와 중복 group 오류를 filesystem 비의존 함수로 제공
      (`GamePatchKit.Core.Globbing.FileSelector`)
- [x] `ManifestValidator`의 filesystem 비의존 의미 검증
  - group 이름, file 경로, artifact 경로·hash의 namespace별 유일성
  - file→group, file→file artifact, file→bundle entry 참조의 존재·kind·group 일치
  - file `parts[]` index `0`부터 연속, path 유일성과 part 크기 합계 검증
  - bundle entry와 file의 일대일 대응, manifest 내부 artifact·entry의 미참조 거부
  - content-addressed artifact 경로가 kind·group·hash의 예상 경로와 같은지 검증
  - 공유 저장소의 추가 artifact 파일은 manifest 검증 대상이 아님을 contract로 분리

### canonical JSON

- [x] RFC 8785 JCS·I-JSON 기반 직렬화: UTF-8, BOM·공백·trailing newline 없음,
      object property의 UTF-16 code unit 재귀 정렬과 JCS 문자열 escape
      (`GamePatchKit.Core.Json.CanonicalJsonWriter`)
- [x] JSON 숫자는 `-(2^53-1)`~`2^53-1`의 integer만 허용하고 field별 부호·범위는
      schema로 제한 (`GamePatchKit.Core.Json.JsonNumbers`)
- [x] domain 배열 정렬: `groups[]` 이름, `files[]` 정규화 경로,
      `artifacts[]` content-addressed 경로, `parts[]` index, bundle `entries[]`
      정규화 경로 (writer는 배열을 재정렬하지 않고, `ManifestValidator`가 순서를
      강제한다)
- [x] 문자열 배열은 정규화한 UTF-8 byte의 ordinal 오름차순이며 JCS는 array 원소
      순서를 바꾸지 않음을 명시
- [x] 같은 모델은 항상 같은 byte를 생성하는 canonical writer 구현
      (라이브러리 기본 직렬화에 의존하지 않는다 — Newtonsoft.Json은 파싱에만 사용)
- [x] `dataVersion`은 canonical identity byte를, `manifestHash`와 signature는 같은
      canonical manifest 원본 byte를 기준으로 계산함을 코드 주석·문서에 고정
      (`GamePatchKit.Core.Manifests.ManifestIdentity`)

### 공용 golden vector

- [x] 최소 `single-file`, `multipart-file`, `bundle-entry` vector를 고정하고 optional
      group·혼합 compression branch도 포함 (`mixed` vector)
- [x] vector별 입력 모델, canonical identity byte, 예상 `dataVersion`, canonical
      manifest byte와 예상 `manifestHash` 저장 (`tests/fixtures/golden-vectors/`,
      입력 모델은 `GoldenVectorManifests`에 C#으로 재현)
- [x] 정확한 byte 비교, parse→write 동일성, 배열 순서·참조·encoding 변형 negative
      vector 제공
- [x] 11단계가 같은 vector에 raw test public key, 파생 `keyId`, 예상 canonical
      `manifest.sig` byte를 추가할 수 있는 디렉터리 계약 정의
      (`tests/fixtures/golden-vectors/README.md`)

### 경로·glob 순수 규칙

- [x] Core는 filesystem을 조회하지 않고 전달받은 경로 문자열·pattern·후보 목록만
      처리한다. symlink·reparse point 판정과 source 경합 검증은 05 Packager 책임이다.
- [x] canonical 상대 경로는 `/`와 NFC를 사용하고 절대 경로, 빈 segment, `.`·`..`,
      역슬래시와 NUL을 거부한다.
- [x] NFC 정규화 후 UTF-8 byte가 같거나 `OrdinalIgnoreCase` 비교에서만 같아지는
      경로를 OS와 관계없이 중복으로 거부하고, 최종 목록은 정규화 UTF-8 byte의
      ordinal 오름차순으로 정렬한다.
- [x] 자체 glob parser·matcher는 literal, segment 내부 `*`, 완전한 segment `**`만
      지원한다. `**`는 0개 이상 segment와 일치해 `**/*.json`이 root와 하위를 모두
      포함한다.
- [x] 부분 `**`, 절대 pattern, 빈 segment, `.`·`..`, 역슬래시, NUL, `?`,
      character class, brace expansion, extglob과 negation은 검증 단계에서 거부한다.
      (괄호·`@`·`+`는 실제 파일명에 흔히 쓰이므로 별도로 금지하지 않는다 — extglob은
      구현하지 않으므로 `*(...)` 형태는 그냥 리터럴로 취급되어 사실상 무력화된다.
      이 범위 판단은 `GlobPattern.cs` 주석에 기록했다.)
- [x] glob matching은 모든 OS에서 ordinal case-sensitive이며 locale·filesystem
      case 규칙에 의존하지 않는다.
- [x] segment가 `.`으로 시작하면 숨김 경로로 정의한다. `*`와 `**`는 숨김 segment를
      소비하지 않고 대응 pattern segment가 literal `.`으로 시작할 때만 일치시킨다.
      Windows Hidden attribute는 사용하지 않는다.
- [x] `include[]` OR → `exclude[]` OR(항상 우선, re-include 없음) → group
      `include[]` OR 순서로 적용한다. 둘 이상 group 일치는 오류, 미일치는 `default`다.
- [x] table fixture에 `**`의 0·1·여러 segment, `/`·역슬래시, case, NFC, exclude
      우선순위, 숨김 명시 포함, group 중복과 다른 입력 순서를 포함한다.

### 오류 모델 기초

- [x] packageId·상대 경로·group·단계를 포함하고 데이터 내용·secret은 제외하는 공통
      오류 타입 정의 (`GamePatchKit.Core.Errors.GamePatchKitError`)

## 결정 사항

- [x] JSON 파싱 라이브러리 선정: **Newtonsoft.Json 13.0.2 이상**(중앙 버전 관리에서
      exact pin이 아닌 최소 버전으로 고정). netstandard2.0 이상과 Unity 공식 UPM
      패키지(`com.unity.nuget.newtonsoft-json`)로 외부 host 호환성이 검증되어
      있다. canonical 출력은 이 선택과 무관하게 `CanonicalJsonWriter`가 JCS 규칙에
      맞춰 직접 구현한다(Newtonsoft의 직렬화는 사용하지 않는다).
      exact pin을 쓰지 않은 이유: `Microsoft.NET.Test.Sdk`가 전이적으로
      `Newtonsoft.Json >= 13.0.3`을 요구해 exact pin(`[13.0.2]`)과 NU1107 버전
      충돌이 발생했다. 테스트 전용 schema validator는 이 충돌을 피하기 위해
      Newtonsoft가 아닌 System.Text.Json 기반 `JsonSchema.Net`을 사용한다.
- [x] `dataVersion` 형식: PRD가 `manifestHash`는 "canonical manifest byte의
      SHA-256, lowercase hex 64자"로 명시하지만 `dataVersion`은 "canonical
      identity byte의 digest"라고만 하여 골든 벡터 값을 고정하려면 형식을 정해야
      했다. **`v1-` + canonical identity byte의 SHA-256 lowercase hex 64자**로
      고정한다(예: `v1-a382501f...`). 접두어를 둔 이유는 `manifestHash`·
      `fileHash`·`artifactHash`(모두 bare hex64)와 로그·디버깅 시 혼동하지 않기
      위함이다.

## 산출물

- `schemas/` JSON Schema 3종
- Core 모델, `ManifestValidator`, canonical JSON writer, 경로 정규화·glob
  parser·matcher·파일 선택 함수, 공통 오류 타입
- 유효·무효 fixture와 공용 canonical golden vector 기반 테스트

## 완료 기준

- 유효 fixture는 schema·모델 검증을 통과하고, 무효 fixture(빈 `include`, 잘못된
  `packageId`, group 중복 일치, 경로 규칙 위반, 잘못된 `compactVersion`·
  `manifestHash`)는 정확한 오류로 실패한다.
- 유효·무효 YAML fixture가 단일 mapping document 허용과 빈·다중 document,
  anchor·alias·merge key·custom tag·중복 key 거부 경우를 모두 포함한다
  (거부 동작 검증은 07 완료 기준, 검증 기준 24).
- single file·multipart file·bundle entry branch의 유효 fixture는 통과하고 잘못된
  discriminator·참조·순서·중복·미참조 fixture는 실패한다(검증 기준 25).
- canonical writer의 출력, `dataVersion`과 `manifestHash`가 공용 golden vector의
  예상 byte·값과 정확히 일치한다(검증 기준 25).
- `manifest-signature` fixture가 raw public key에서 파생한 `keyId` 형식을 검증하고
  임의·잘못된 fingerprint를 거부한다(검증 기준 25, signature 형식 범위).
- 경로·glob table-driven fixture가 지원 OS와 후보 입력 순서에 관계없이 같은 선택·
  group·정렬 결과를 만들고 무효 pattern과 숨김 암묵 일치를 거부한다(검증 기준 26).

모든 완료 기준은 `dotnet test`(`GamePatchKit.Core.Tests`, 124개 테스트)로
검증했으며, golden vector는 Python으로 독립 구현한 RFC 8785 인코더 및 `shasum`과
교차 검증했다(생성·검증 스크립트는 `tests/fixtures/golden-vectors/tools/`에 커밋).

구현 직후 Codex adversarial review에서 8건을 지적받아 모두 수정했다(unknown
property가 TryParse를 실패시키지 않던 전 모델 공통 버그, `ManifestValidator`의
case-insensitive 파일 경로·공유 file artifact 내용 일관성·bundle entry 중복
경로·entryPath 불일치·multipart 크기 합 overflow 미검증, `GlobPattern`의 NFC
미정규화, `CanonicalJsonWriter`의 unpaired surrogate 무음 치환). 상세 내역과
회귀 테스트는 worklog 참고.

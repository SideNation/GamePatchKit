# 02. JSON Schema·canonical 규칙 고정

> PRD 섹션: package 설정, 배포 대상 파일 규칙, release manifest, 배포 산출물

## 목표

`gamepatchkit.yml` 입력 계약과 package 설정·release manifest·channel의 JSON Schema를
확정하고, canonical JSON과 경로 정규화 규칙을 Core에 구현해 이후 모든 단계가 사용할
데이터 계약을 고정한다.

## 선행 단계

01

## 작업 항목

### JSON Schema 작성 (`schemas/`)

- [ ] `package-config.schema.json`
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
- [ ] `release-manifest.schema.json`
  - `schemaVersion`, `packageId`, `dataVersion`, `compactVersion`, `groups[]`,
    `artifacts[]`, `files[]`
  - `manifestHash`는 manifest 내부에 기록하지 않는다
  - `groups[]`에는 build-time compression 설정을 기록하지 않고 실제 compression은
    `artifacts[]`에만 기록한다
  - 같은 release·group 안에서 artifact별 compression metadata가 달라도 허용한다
- [ ] `channel.schema.json`
  - 환경이 사용할 `packageId`·`manifestHash`·`dataVersion` pointer

### `gamepatchkit.yml` 입력 계약

- [ ] 비어 있지 않은 YAML document 정확히 하나와 mapping root만 허용
- [ ] 두 번째 document, anchor, alias, merge key(`<<`), custom tag와 중복 mapping
      key 거부
- [ ] JSON-compatible scalar·array·mapping만 설정 데이터로 변환
- [ ] YAML 문법 검증 → JSON-compatible 데이터 변환 → `package-config.schema.json`
      검증 → 설정 모델 의미 검증 순서 고정
- [ ] 유효·무효 YAML fixture를 07 CLI parser 테스트에서도 재사용할 수 있게 제공
- [ ] release manifest는 설정 파일이 아니라 Packager가 생성하는 별도 canonical JSON
      산출물임을 schema 설명에 명시

### Core 모델·검증

- [ ] config·manifest·channel 모델 클래스 (YAML 파일 로드·문법 검증은 CLI 책임이며,
      Core는 filesystem과 YAML parser에 의존하지 않는다)
- [ ] schema 규칙에 대응하는 모델 검증: packageId·group 이름 규칙, group 중복 일치
      오류, 예약 group `default` 처리, 음수가 아닌 `compactVersion`, channel의
      lowercase hex 64자 `manifestHash`

### canonical JSON

- [ ] 직렬화 규칙 고정: UTF-8, 공백 없음, object key 순서, 배열 순서, 문자열 escape,
      숫자 표기
- [ ] 같은 모델은 항상 같은 byte를 생성하는 canonical writer 구현
      (라이브러리 기본 직렬화에 의존하지 않는다)
- [ ] `dataVersion`은 canonical identity byte를, `manifestHash`와 signature는 같은
      canonical manifest 원본 byte를 기준으로 계산함을 코드 주석·문서에 고정

### 경로 정규화

- [ ] 구분자 `/` 통일, Unicode 정규화 형식 고정(NFC)
- [ ] 거부 규칙: 절대 경로, 빈 segment, `.`·`..`, 역슬래시, NUL, symlink
- [ ] 대소문자만 다른 경로·정규화 후 중복 경로 검출
- [ ] 정규화 상대 경로의 ordinal byte 순서 정렬 구현

### 오류 모델 기초

- [ ] packageId·상대 경로·group·단계를 포함하고 데이터 내용·secret은 제외하는 공통
      오류 타입 정의

## 결정 사항

- [ ] JSON 파싱 라이브러리 선정: netstandard2.1과 외부 host 호환성을 기준으로
      선택한다. canonical 출력은 선택과 무관하게 자체 writer로 고정한다.
- [ ] canonical object key 순서 규칙(사전순 vs schema 정의 순서)을 확정하고 문서화한다.

## 산출물

- `schemas/` JSON Schema 3종
- Core 모델, canonical JSON writer, 경로 정규화, 공통 오류 타입
- 유효·무효 fixture 기반 테스트

## 완료 기준

- 유효 fixture는 schema·모델 검증을 통과하고, 무효 fixture(빈 `include`, 잘못된
  `packageId`, group 중복 일치, 경로 규칙 위반, 잘못된 `compactVersion`·
  `manifestHash`)는 정확한 오류로 실패한다.
- 단일 mapping document fixture는 통과하고 빈·다중 document, anchor·alias·merge
  key·custom tag·중복 key fixture는 package 실행 전에 실패한다(검증 기준 24).
- 같은 모델을 반복 직렬화하면 항상 같은 byte가 나온다.
- 경로 정규화 table-driven 테스트가 통과한다.

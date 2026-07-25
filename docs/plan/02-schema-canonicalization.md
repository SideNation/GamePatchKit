# 02. JSON Schema·canonical 규칙 고정

> PRD 섹션: package 설정, 배포 대상 파일 규칙, release manifest, 배포 산출물

## 목표

package 설정·release manifest·channel의 JSON Schema를 확정하고, canonical JSON과
경로 정규화 규칙을 Core에 구현해 이후 모든 단계가 사용할 데이터 계약을 고정한다.

## 선행 단계

01

## 작업 항목

### JSON Schema 작성 (`schemas/`)

- [ ] `package-config.schema.json`
  - `schemaVersion`, `packageId`(소문자 kebab-case), `inputRoot`,
    `include`(비어 있을 수 없는 allowlist), `exclude`,
    `maxArtifactBytes`(기본 `10,485,760`), `defaultArtifactMode`(기본 `file`),
    `compression`(`none` 또는 codec ID·level·frame option이 고정된 `zstd`), `groups[]`
  - group 필드: `name`(package 안 유일, kebab-case), `include`, `artifactMode`,
    `required`, `compression`
- [ ] `release-manifest.schema.json`
  - `schemaVersion`, `packageId`, `dataVersion`, `releaseId`, `groups[]`, `artifacts[]`,
    `files[]`
- [ ] `channel.schema.json`
  - 환경이 사용할 `packageId`·`releaseId`·`dataVersion` pointer

### Core 모델·검증

- [ ] config·manifest·channel 모델 클래스 (Core는 filesystem을 참조하지 않으므로
      문자열/stream 입력을 파싱한다; 파일 로드는 Packager·CLI 책임)
- [ ] schema 규칙에 대응하는 모델 검증: packageId·group 이름 규칙, group 중복 일치
      오류, 예약 group `default` 처리

### canonical JSON

- [ ] 직렬화 규칙 고정: UTF-8, 공백 없음, object key 순서, 배열 순서, 문자열 escape,
      숫자 표기
- [ ] 같은 모델은 항상 같은 byte를 생성하는 canonical writer 구현
      (라이브러리 기본 직렬화에 의존하지 않는다)
- [ ] identity와 signature 계산이 canonical byte 기준임을 코드 주석·문서에 고정

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
  `packageId`, group 중복 일치, 경로 규칙 위반)는 정확한 오류로 실패한다.
- 같은 모델을 반복 직렬화하면 항상 같은 byte가 나온다.
- 경로 정규화 table-driven 테스트가 통과한다.

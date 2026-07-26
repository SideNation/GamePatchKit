# `gamepatchkit.yml` 입력 계약

> PRD 섹션: package 설정
> 담당: 02(계약·fixture 고정), 07 CLI(YAML 로드·문법 검증 구현과 동작 테스트)

이 문서는 `gamepatchkit.yml`을 읽어 `package-config.schema.json` 검증 대상인
설정 데이터로 만들기까지의 입력 계약을 고정한다. YAML parser 구현과 그 동작을
검증하는 테스트(검증 기준 24)는 07 CLI의 책임이며, 02는 계약과 07이 재사용할
fixture만 제공한다.

## 문서 구조 제약

- 파일 하나에는 비어 있지 않은 YAML document가 **정확히 하나**만 있어야 한다.
  - 빈 document(파일 전체가 공백·주석뿐이거나 완전히 빈 파일)는 거부한다.
  - `---`로 구분된 두 번째 document가 있으면 거부한다.
- document의 root는 **mapping**이어야 한다. sequence나 scalar가 root면 거부한다.
- 다음 YAML 기능은 사용을 허용하지 않는다.
  - anchor(`&name`)
  - alias(`*name`)
  - merge key(`<<`)
  - custom tag(`!!` 내장 tag가 아닌 `!Foo` 형태 등)
- 같은 mapping 안에서 key가 중복되면 거부한다(마지막 값으로 덮어쓰지 않는다).

## 값 표현 제약

- 모든 값은 JSON-compatible scalar(문자열·정수·실수·boolean·null), array,
  mapping으로만 표현할 수 있어야 한다.
- 주석과 YAML 표현 방식(스칼라 스타일, 들여쓰기 등)은 설정값이 아니며 이후
  어떤 산출물(release manifest 포함)에도 기록하지 않는다.

## 처리 순서

1. YAML 문법을 검증하고 위 문서 구조 제약을 적용한다(07 CLI 구현).
2. 통과한 document를 JSON-compatible 데이터 구조(mapping/array/string/number/
   boolean/null)로 변환한다(07 CLI 구현).
3. 변환된 데이터를 `schemas/package-config.schema.json`으로 검증한다.
4. schema를 통과한 데이터를 Core의 설정 모델로 만들고 의미 검증을 적용한다
   (packageId·group 이름 규칙, group 중복 일치, 예약 group `default` 처리 등은
   `GamePatchKit.Core`의 `PackageConfig`/`PackageConfigValidator` 책임).

release manifest는 사용자가 작성하는 설정 파일이 아니라 검증된 설정과 source로
Packager가 생성하는 별도의 canonical JSON 산출물이다(`release-manifest.schema.json`
참조).

## fixture

`tests/fixtures/gamepatchkit-yml/`에 07 CLI parser 테스트가 추가 fixture 없이
검증 기준 24를 구성할 수 있는 유효·무효 YAML을 제공한다.

- `valid/`: 문서 구조·기능 제약을 모두 만족하는 예시
  - `minimal.yml`: 필수 필드만 채운 최소 설정
  - `groups-and-compression.yml`: `groups[]`와 `compression`(zstd 포함)을 포함한 설정
- `invalid/`: 위 제약을 하나씩 위반하는 예시. 파일명이 위반 사유를 나타낸다.
  - `empty-document.yml`: document가 비어 있음
  - `multiple-documents.yml`: `---`로 구분된 두 번째 document 존재
  - `non-mapping-root-sequence.yml`: root가 mapping이 아니라 sequence
  - `non-mapping-root-scalar.yml`: root가 mapping이 아니라 scalar
  - `anchor-alias.yml`: anchor·alias 사용
  - `merge-key.yml`: merge key(`<<`) 사용
  - `custom-tag.yml`: custom tag 사용
  - `duplicate-key.yml`: 같은 mapping 안 중복 key

각 `invalid/` fixture는 나열된 위반을 정확히 하나만 포함하도록 구성해 07의
테스트가 "어떤 규칙 위반으로 거부됐는지"를 fixture당 하나씩 명확히 검증할 수
있게 한다.

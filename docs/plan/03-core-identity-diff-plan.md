# 03. Core identity·manifest hash·diff·download plan

> PRD 섹션: `dataVersion`·`compactVersion`·`manifestHash`, diff와 download plan,
> 프로젝트 책임
> `GamePatchKit.Core`, Runtime adapter contract

## 목표

Packager와 Runtime이 공유하는 순수 로직을 Core에 구현한다: streaming hash,
`dataVersion`·`manifestHash` 계산, release diff, download plan, `ICompressionCodec`
contract.

## 선행 단계

02

## 작업 항목

### hash

- [ ] streaming SHA-256 유틸리티 (전체 입력을 메모리에 올리지 않는다)

### `dataVersion` 계산

- [ ] 입력: `packageId`, group 이름과 `required` 같은 소비 의미, 각 파일의 정규화
      경로·group·원본 크기·`fileHash`
- [ ] 제외: artifact 종류, bundle 경계·압축 방식, artifact 경로·part 구성,
      build 시각·source revision·실행 환경
- [ ] 계산 입력의 canonical 직렬화 형식을 고정하고 digest를 계산한다

### `compactVersion` 규칙

- [ ] 최초 package는 `0`, incremental package는 이전 값을 상속한다
- [ ] compact는 source manifest의 `compactVersion + 1`을 사용한다
- [ ] `compactVersion`은 물리 packaging 세대이며 `dataVersion` 계산에서 제외한다

### `manifestHash` 계산

- [ ] `manifestHash` 필드가 없는 최종 canonical manifest 원본 byte의 SHA-256을
      lowercase hex 64자로 표현한다
- [ ] `schemaVersion`, `compactVersion`, artifact 배치와 file 참조를 포함한 manifest의
      모든 필드는 hash 입력에 포함된다
- [ ] 선택적 `manifest.json.zst` 전송본과 `manifest.sig`는 hash 입력에서 제외한다
- [ ] manifest 원본 byte와 기대 `manifestHash`를 비교하는 검증 API를 제공한다

### release diff

- [ ] 두 manifest의 최종 `files[]`를 기준으로 추가·변경·삭제·group 이동을 분류한다
- [ ] 경로와 `fileHash`가 같고 artifact 참조도 재사용됐더라도 group이 다르면 논리
      group 이동으로 분류한다
- [ ] 논리 데이터 차이와 물리 artifact 차이를 별도 결과로 출력한다

### download plan

- [ ] 입력으로 명시적인 target group 집합을 받고 해당 group의 목표 `files[]`만 계획한다
- [ ] 로컬 상태(경로 + 검증된 `fileHash`)와 선택된 목표 `files[]`를 비교한다
- [ ] artifact 위치가 달라도 경로·`fileHash`가 같으면 다운로드하지 않는다
- [ ] 필요한 파일이 bundle에만 있으면 해당 bundle 전체를 계획에 한 번만 포함한다
- [ ] file part는 누락 part만 포함하되 최종 결합 hash 검증을 요구한다
- [ ] 예상 다운로드 byte, 임시 공간, 파일 수, bundle 수를 계산한다

### compression contract

- [ ] `ICompressionCodec` contract와 zstd codec 식별자를 정의한다
- [ ] Core는 구체 압축 구현을 참조하지 않는다

## 산출물

- Core identity·diff·download plan API와 단위 테스트

## 완료 기준

- 파일 내용 또는 group 의미가 바뀌면 `dataVersion`·`manifestHash`가 모두 바뀐다.
- 같은 논리 상태에서 물리 배치만 바꾸면(compact 상황 모사) `dataVersion`은 유지되고
  `compactVersion`과 `manifestHash`만 바뀐다(검증 기준 9의 단위 수준).
- 같은 데이터를 다른 압축으로 표현해도 `dataVersion`이 같다.
- compression 설정만 달라지고 artifact 참조가 모두 같으면 두 manifest의 논리·물리
  diff가 없다.
- manifest의 `schemaVersion`만 바꾸면 `dataVersion`은 유지되고 `manifestHash`는
  바뀐다.
- canonical manifest golden fixture의 원본 byte와 예상 `manifestHash`가 일치한다.
- diff가 추가·변경·삭제·group 이동을 정확히 분류한다(table-driven).
- download plan이 재사용·bundle 전체 포함·누락 part 규칙을 만족하고 예상 수치를
  계산한다.
- required group만 선택한 최초 plan과 optional group 하나만 선택한 후속 plan이 다른
  group의 artifact를 포함하지 않는다(검증 기준 22의 Core 수준).

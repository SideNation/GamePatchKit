# 05. Packager 파일 탐색·file artifact·최초/incremental package

> PRD 섹션: 배포 대상 파일 규칙, file artifact, release manifest, 최초 package,
> incremental package, 재현성과 멱등성, 오류 처리, 보안·공개 경계

## 목표

source root에서 deterministic하게 파일을 수집해 file artifact와 최초·incremental
release manifest를 생성한다. bundle group은 06 단계에서 추가하며, 이 단계의
pipeline은 bundle 생성 단계를 끼울 수 있는 구조로 만든다.

## 선행 단계

03, 04

## 작업 항목

### source 탐색·선별

- [ ] `inputRoot` 자체와 하위 entry를 symlink·junction·reparse point를 따르지 않고
      열거한다. 발견한 link·reparse point와 일반 파일·디렉터리가 아닌 entry는 실패다.
- [ ] native 경로의 segment를 Core canonical 상대 경로(`/`, NFC)로 변환하고, Core의
      문자열 경로 검증·대소문자/NFC 중복 검사를 적용한다.
- [ ] 02의 자체 glob matcher로 전역 `include` OR → `exclude` OR(항상 우선) →
      group `include` OR 순서로 선별한다. editor metadata·임시 파일에는 암묵 규칙을
      두지 않고 설정의 명시적 `exclude`만 적용한다.
- [ ] `*`·`**`가 숨김 segment를 암묵적으로 소비하지 않게 하고 대응 pattern
      segment가 literal `.`으로 시작한 경우에만 숨김 경로를 포함한다.
- [ ] group 둘 이상 일치는 오류, 미일치는 예약 group `default`로 처리한다.
- [ ] Core가 반환한 정규화 UTF-8 byte ordinal 순서로 파일 목록을 고정한다.

### 입력 무결성

- [ ] 최초 열거 결과의 정규화 상대 경로·entry 종류·stable file identity·크기·수정
      시각을 source snapshot으로 보관한다.
- [ ] stable file identity는 Windows volume ID·file ID 또는 Unix 계열 device·inode
      조합으로 읽고 manifest identity에는 넣지 않는다. 지원하지 않는 filesystem에서는
      검증을 생략하지 않고 실패한다.
- [ ] 파일은 link를 follow하지 않는 방식으로 열고 열린 handle의 identity가 snapshot과
      같은지 확인한 뒤 원본 byte 크기와 `fileHash`(SHA-256)를 stream으로 계산한다.
- [ ] 각 stream hash 전·후의 identity·크기·수정 시각을 snapshot과 비교하고 하나라도
      다르면 입력 경합으로 실패한다.
- [ ] manifest 확정 전에 동일한 no-follow 열거와 Core 선별을 다시 수행해 선택된
      정규화 경로·entry 종류·identity 집합을 최초 snapshot과 비교한다.
- [ ] 파일 추가·삭제·교체·변경이나 entry 종류 변경이 감지되면 manifest를 생성하지
      않고 staging만 폐기하며 완성된 기존 결과를 유지한다.

### file artifact 생성

- [ ] 무압축 파일은 원본 byte payload, zstd는 `ICompressionCodec`으로 압축
- [ ] 압축 시 원본 `fileHash`와 압축 payload의 `artifactHash`를 모두 기록
- [ ] payload가 `maxArtifactBytes` 초과 시 고정 크기의 순서 있는 part로 분할하고
      part별 크기·SHA-256 기록
- [ ] content-addressed 배치: `artifacts/files/<artifactHash>/…`
      (한 artifact 디렉터리에는 실제 형식에 맞는 payload 한 종류만 둔다)
- [ ] 같은 `artifactHash`가 이미 있으면 byte 검증 후 재사용, byte 불일치는 실패

### 최초 package

- [ ] PRD 최초 package 1~10 순서의 pipeline 구성 (bundle 단계는 06에서 연결)
- [ ] 모든 artifact의 크기·SHA-256 재검증 → `dataVersion` 계산 →
      `compactVersion = 0`인 canonical manifest 생성 → `manifestHash` 계산 →
      build report 생성 → 전체 참조 검증

### incremental package

- [ ] 이전 manifest의 schema·packageId·canonical byte `manifestHash`·참조 검증
- [ ] 현재 source와 이전 `files[]`를 비교해 추가·내용 변경·삭제·group 이동을 별도로
      분류
- [ ] 경로와 `fileHash`가 같은 기존 file artifact는 group·`artifactMode`·compression
      설정이 바뀌어도 새 group의 file override로 재사용
- [ ] 기존 bundle 참조는 group이 그대로이고 현재 `artifactMode`도 `bundle`일 때만
      재사용
- [ ] group 이동 또는 `artifactMode: file` 전환으로 기존 bundle을 재사용할 수 없으면
      개별 file artifact override를 생성하거나 검증된 기존 file artifact를 재사용
- [ ] 추가·내용 변경 파일은 group mode와 관계없이 개별 file artifact override로
      생성하고, 새 artifact에만 현재 compression 설정 적용
- [ ] compression 설정만 바뀐 기존 artifact는 실제 compression metadata와 참조를
      그대로 유지하며, 이 변경만으로는 새 manifest를 생성하지 않음
- [ ] 삭제 파일은 새 `files[]`에서 제외하고, 새 manifest는 patch chain이 아니라
      최종 상태 전체를 기록
- [ ] 기존 bundle을 수정하거나 동일 경로에 다시 업로드하지 않는다
- [ ] 새 manifest는 이전 `compactVersion`을 상속한다

### manifest·build report

- [ ] canonical JSON manifest와 선택적 `.json.zst` 전송본 생성
- [ ] manifest 출력 전에 02의 JSON Schema와 filesystem 비의존 `ManifestValidator`를
      실행하고 실제 file artifact stream의 크기·hash·part 결합 결과를 검증
- [ ] canonical manifest 원본 byte의 SHA-256을 `manifestHash`로 사용하고
      `manifests/<manifestHash>/manifest.json`에 배치
- [ ] `dataVersion`·`compactVersion`·`manifestHash`와 추가·변경·삭제 목록, 생성 시각,
      머신, source revision, 적용한 compression 설정은 build report에 기록
- [ ] 이 단계의 publish tree에는 불변 artifact·manifest만 포함하며 환경별 target
      manifest 선택 자료는 생성하지 않는다 (`manifest.sig`는 07의 `sign`이 추가)

### 재현성·멱등성

- [ ] 임시 파일은 최종 output과 같은 filesystem에 만들고 검증 후 rename
- [ ] hash 경로에 다른 byte가 있으면 덮어쓰지 않고 실패
- [ ] 같은 `manifestHash` 재생성은 기존 결과를 검증하고 재사용
- [ ] 실패한 package가 완성된 기존 결과를 변경하지 않는다

## 산출물

- Packager 탐색·file artifact·최초/incremental package pipeline과 단위 테스트

## 완료 기준

- 같은 입력·설정으로 두 번 package하면 artifact와 manifest byte가 같다
  (검증 기준 1, file 범위).
- 기본 설정은 모든 파일을 file artifact로 생성한다(검증 기준 2).
- group·`artifactMode` 전환별 table-driven 테스트가 기존 file artifact 재사용, 유효한
  bundle 참조 재사용, bundle에서 file override 전환을 정확히 구분한다.
- compression 설정만 바꾼 incremental package가 기존 file·bundle artifact와
  `manifestHash`를 그대로 재사용한다(검증 기준 6).
- 삭제 파일이 새 artifact 없이 최종 `files[]`와 `dataVersion`에 반영된다(검증 기준 7).
- 모든 file artifact·part가 `maxArtifactBytes` 이하다(검증 기준 12, file 범위).
- 손상된 file artifact·part payload를 재사용·package 검증에서 거부한다
  (검증 기준 11, file 범위).
- 서로 다른 package의 경로·hash·artifact 위치가 manifest에 섞이지 않는다
  (검증 기준 13).
- single·parts file manifest가 공용 golden vector와 일치하고 잘못된 참조·part
  순서·중복·미참조 artifact를 거부한다(검증 기준 25, file 범위).
- glob fixture가 `**` 0·1·여러 segment, case, NFC, exclude 우선순위, 숨김 명시
  포함, group 중복과 임의 열거 순서에서 02 Core와 정확히 같은 결과를 만든다
  (검증 기준 26).
- symlink·junction·reparse point와 source 추가·삭제·교체·변경을 주입한 테스트가
  manifest 생성 전에 실패하고 기존 결과를 변경하지 않는다(검증 기준 18, 26).
- hash 경로 충돌을 포함한 나머지 PRD 입력 오류도 실패하고 기존 결과를 변경하지
  않는다(검증 기준 18).

## 개발 v2

v1 계획은 보존하며, 아래 내용으로 05단계 구현 결과와 확정 계약을 추가한다.

### 구현 결정

- 공개 진입점은 `FilePackageBuilder.BuildAsync(FilePackageRequest,
  CancellationToken)`이며 이전 canonical manifest byte와 `manifestHash`의 유무로
  최초·incremental package를 구분한다.
- `package-config.schema.json`과 `release-manifest.schema.json`은 Packager assembly에
  embedded resource로 포함한다. config와 생성·이전 manifest는 JSON Schema →
  Core parser·의미 검증 → 실제 payload 검증 순서로 확인한다.
- source 탐색은 Windows의 volume/file ID, Unix 계열의 device/inode를 snapshot
  identity로 사용한다. source root와 모든 발견 entry를 기록하고, 선택 파일은 열린
  no-follow handle의 identity·크기·수정 시각을 hash 전후에 비교한다.
- output은 source root 밖이어야 한다. staging은 output과 같은 filesystem에 만들고
  final source 재열거가 최초 snapshot과 같을 때만 게시한다.
- 이 단계에서는 bundle을 새로 만들지 않는다. 최초 package의 bundle-mode 파일과
  incremental의 새·변경 파일은 file override로 만들며 06의 bundle 단계가 이후
  baseline 생성을 연결한다.
- 기존 bundle은 모든 entry가 현재도 같은 경로·크기·`fileHash`·group이고 target
  mode가 `bundle`일 때만 전체를 재사용한다. entry 일부만 달라지면 02의 미참조
  bundle entry 금지 계약을 지키기 위해 그 bundle의 현재 파일 전체를 file
  override로 전환한다.
- `default` group의 `required` 설정 필드가 없으므로, 실제 default 파일이 있으면
  manifest에 `required: true`로 기록한다.
- build report는 `PackageBuildReport` typed 결과로 반환하며 생성 시각·machine·source
  revision처럼 비결정적인 값을 불변 publish tree에 기록하지 않는다.
- `PackagePayloadVerifier`는 실제 object 크기·hash, part 결합과 압축 해제 후 원본
  크기·`fileHash`를 streaming으로 검증한다. signature를 포함한 통합 verify API는
  07·11단계에서 이 API를 조합한다.

자세한 호출·오류 계약은 [file package 생성 문서](../contracts/file-packager.md)에
기록한다.

### 완료 상태

- [x] no-follow source 탐색, canonical 경로 변환, Core glob 선별·정렬
- [x] stable identity·크기·수정 시각 snapshot과 hash 전후·최종 재검증
- [x] 무압축·zstd file artifact, 고정 크기 part와 content-addressed 불변 배치
- [x] 기존 hash 경로 byte 검증·재사용과 충돌 거부
- [x] 최초 package와 `compactVersion = 0` canonical manifest
- [x] incremental 추가·변경·삭제·group 이동 분류와 file·bundle 재사용 정책
- [x] compression 정책 변경 시 기존 metadata·manifest 재사용
- [x] config·manifest schema, Core 참조와 실제 payload 검증
- [x] 선택적 `manifest.json.zst`, typed build report와 source revision 기록
- [x] same-input 결정성, single·parts, default file mode, mode 전환, bundle 전체
  재사용·fallback, 삭제, 손상 object·part, hash 충돌, symlink와 source
  추가·삭제·교체·변경 회귀 테스트

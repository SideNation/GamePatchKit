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

- [ ] `include` allowlist와 `exclude` glob 적용
- [ ] 숨김 파일, editor metadata, 임시 파일은 명시하지 않는 한 제외
- [ ] 02의 경로 정규화·거부 규칙 적용 (symlink, 대소문자 중복, 정규화 후 중복 등)
- [ ] group 결정: 둘 이상 일치는 오류, 미일치는 예약 group `default`
- [ ] 정규화 상대 경로의 ordinal byte 순서로 파일 목록 고정

### 입력 무결성

- [ ] 각 파일의 원본 byte 크기와 `fileHash`(SHA-256) 계산
- [ ] package 실행 중 입력 파일 변경을 감지하면 일관되지 않은 입력으로 실패
      (감지 방식: 시작·종료 시점 크기·수정 시각 비교 등에서 결정)

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
- [ ] canonical manifest 원본 byte의 SHA-256을 `manifestHash`로 사용하고
      `manifests/<manifestHash>/manifest.json`에 배치
- [ ] `dataVersion`·`compactVersion`·`manifestHash`와 추가·변경·삭제 목록, 생성 시각,
      머신, source revision, 적용한 compression 설정은 build report에 기록

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
- 서로 다른 package의 경로·hash·artifact 위치가 manifest에 섞이지 않는다
  (검증 기준 13).
- PRD 오류 처리 목록의 입력 오류(중복 group 일치, symlink, 실행 중 변경, hash 경로
  충돌)가 모두 실패로 끝나고 기존 결과를 변경하지 않는다(검증 기준 18).

# 06. deterministic bundle·compact

> PRD 섹션: bundle artifact, compact, group 설계 규칙, 재현성과 멱등성

## 목표

bundle group의 deterministic tar(+zstd) baseline 생성과 compact를 구현해,
incremental override가 누적된 bundle group을 새 baseline으로 통합할 수 있게 한다.

## 선행 단계

05

## 작업 항목

### deterministic tar writer

- [ ] entry는 같은 group의 파일만 포함하고, 정규화된 전체 상대 경로를 사용하며,
      순서를 ordinal byte 순서로 고정한다
- [ ] timestamp, UID, GID, 이름, permission과 tar header 형식을 고정한다
- [ ] symlink, hardlink, device, sparse entry와 확장 attribute를 생성하지 않는다
- [ ] tar header 형식(ustar/pax 등)을 결정하고 문서화한다

### bundle 조립

- [ ] 압축은 `ICompressionCodec`의 zstd 또는 무압축 tar
- [ ] 최초 package와 compact에서 새로 만드는 bundle에만 현재 compression 설정을
      적용하고, 재사용하는 기존 bundle은 실제 compression metadata를 유지한다
- [ ] 실제 bundle payload ≤ `maxArtifactBytes`, 초과 시 마지막 entry를 다음 bundle로
      이동해 다시 생성
- [ ] 단일 파일이 제한을 만족하지 못하면 file artifact part로 fallback
- [ ] bundle payload SHA-256을 `bundleHash`와 `artifactHash`로 사용
- [ ] 배치: `artifacts/bundles/<groupName>/<bundleHash>.tar(.zst)`
- [ ] 같은 입력 파일 집합은 같은 bundle 경계와 byte를 생성한다(병렬 처리 포함)

### 최초 package 통합

- [ ] 05 pipeline에 bundle group baseline 생성 단계를 연결한다

### compact

- [ ] source release의 `manifestHash`·`compactVersion`과 대상 bundle group을 실행
      시작 시 고정한다
- [ ] source manifest와 참조 artifact를 검증해 최종 상태를 복원한다
- [ ] file group은 기존 file artifact를 그대로 재사용한다
- [ ] 선택한 bundle group만 현재 최종 파일로 다시 묶고, file override를 새 bundle에
      포함하며, 삭제 파일과 미참조 byte는 제외한다
- [ ] compact 전후 경로·크기·group·`fileHash`가 같은지 검증한다
- [ ] `dataVersion`은 유지하고 `compactVersion`은 source 값보다 1 증가시킨다
- [ ] 새 canonical manifest의 `manifestHash`를 계산하고 새 bundle과 manifest를 불변
      경로에 생성한다
- [ ] channel 변경과 이전 artifact 삭제는 수행하지 않는다

## 산출물

- deterministic tar writer, bundle 조립, compact 구현과 단위·통합 테스트

## 완료 기준

- 같은 입력·설정 재실행 시 bundle byte가 같다(검증 기준 1, bundle 범위).
- file group은 최초·incremental·compact 모두에서 bundle에 포함되지 않는다
  (검증 기준 3).
- bundle이 group 경계를 넘지 않고 최대 크기 이하다(검증 기준 4, 12).
- 큰 단일 bundle entry가 file part로 fallback한다(검증 기준 5).
- 파일 하나만 변경한 incremental release가 기존 bundle을 다시 만들지 않는다
  (검증 기준 6).
- compact가 bundle override를 통합하고 file group artifact를 재사용한다(검증 기준 8).
- compression 설정 변경 후 compact하면 선택한 group의 새 bundle에만 현재 설정을
  적용하고 다른 group의 기존 artifact는 재사용한다.
- compact 전후 `dataVersion`은 같고 `compactVersion`은 1 증가하며
  `manifestHash`는 달라진다(검증 기준 9).
- 실패한 compact가 기존 artifact·manifest를 변경하지 않는다(검증 기준 18).

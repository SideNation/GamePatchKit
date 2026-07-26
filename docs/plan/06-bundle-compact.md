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
- [ ] bundle artifact·entry가 02의 schema와 `ManifestValidator` 참조 무결성 검증을
      통과한 뒤 manifest를 출력한다

### compact

- [ ] source release의 `manifestHash`·`compactVersion`과 대상 bundle group을 실행
      시작 시 고정한다
- [ ] source manifest와 참조 artifact를 검증해 최종 상태를 복원한다
- [ ] file group은 기존 file artifact를 그대로 재사용한다
- [ ] 선택한 bundle group만 현재 최종 파일로 다시 묶고, file override를 새 bundle에
      포함하며, 삭제 파일과 미참조 byte는 제외한다
- [ ] compact 전후 경로·크기·group·`fileHash`가 같은지 검증한다
- [ ] candidate manifest에 source `compactVersion`을 적용해 canonicalize하고 source
      manifest byte와 비교한다
- [ ] 같으면 staging을 폐기하고 `changed: false`와 기존 `dataVersion`·
      `compactVersion`·`manifestHash`를 반환하며 artifact·manifest를 만들지 않는다
- [ ] 다르면 `dataVersion`은 유지하고 `compactVersion`을 1 증가시켜 새 canonical
      manifest의 `manifestHash`를 계산하고 새 bundle·manifest를 불변 경로에 생성한다
- [ ] host의 target manifest 선택 변경과 이전 artifact 삭제는 수행하지 않는다

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
- compact로 물리 배치가 바뀌면 `dataVersion`은 같고 `compactVersion`은 1 증가하며
  `manifestHash`는 달라진다. override·compression·bundle 경계 변화가 없어 candidate가
  source와 같으면 성공 no-op으로 기존 세 값을 재사용한다(검증 기준 9).
- no-op compact는 새 artifact·manifest를 만들지 않고 machine-readable 결과에
  `changed: false`를 기록한다.
- bundle manifest가 공용 golden vector와 일치하고 잘못된 entry 참조·순서·중복·
  미참조 bundle을 거부한다(검증 기준 25, bundle 범위).
- 손상된 bundle payload를 재사용·package 검증에서 거부한다(검증 기준 11, bundle
  범위).
- 실패한 compact가 기존 artifact·manifest를 변경하지 않는다(검증 기준 18).

## 개발 v2

v1 계획 본문은 보존하며 아래 내용이 현재 구현 상태를 나타낸다.

### 구현 결과

- [x] 실행별 값을 포함하지 않는 직접 구현 PAX writer로 entry header와 record byte를
      고정했다
- [x] entry 경로·순서, UID/GID `0`, 빈 user/group name, mode `0644`, Unix epoch
      mtime과 정확히 두 개의 tar end block을 검증한다
- [x] 최초 package의 bundle group baseline 생성, 실제 payload 크기 기준 entry 경계
      분할과 큰 단일 entry의 file part fallback을 연결했다
- [x] 무압축 tar와 고정 `ICompressionCodec` zstd bundle을 content-addressed 불변
      경로에 게시하고 기존 byte를 검증해 재사용한다
- [x] `PackagePayloadVerifier`가 bundle object hash뿐 아니라 PAX metadata, entry
      순서·경로와 각 파일 크기·`fileHash`까지 검증한다
- [x] `BundleCompactor`가 source artifact에서 선택 group을 복원하고 file override를
      새 bundle로 통합하며 비선택 group과 file artifact를 재사용한다
- [x] 전체 retained object inventory를 `CompactVersionRule`에 전달해 과거 release의
      part 경로와 충돌하는 candidate를 거부한다
- [x] 동일 물리 배치는 `Changed: false` no-op으로 게시하지 않고, 변경 시에만
      `compactVersion`을 1 증가시킨다

### 확정 결정

- tar 형식은 긴 UTF-8 경로를 보존할 수 있는 POSIX PAX로 고정한다.
- 플랫폼·process에 따라 달라지는 framework 기본 PAX extended-header 이름을 사용하지
  않고 `PaxHeaders/<8자리 index>`와 `PaxEntry/<8자리 index>`를 직접 기록한다.
- PAX record는 `path`, `size`, `mtime`만 허용하며 순서와 값 표현을 고정한다.
- bundle 경계는 원본 tar 크기의 보수적 상한으로 먼저 나눈 뒤 실제 압축 payload가
  제한을 넘을 때 마지막 entry를 이동해 재생성한다. 단일 entry는 실제 zstd payload가
  제한 안에 들 수 있으므로 반드시 실제 byte를 만든 뒤 fallback 여부를 결정한다.
- `maxArtifactBytes`는 새로 생성하는 payload에 적용한다. content-addressed 불변 경로의
  검증된 기존 file artifact를 fallback에서 재사용할 때는 현재 제한보다 크더라도 기존
  표현과 compression metadata를 유지한다.
- compact의 `retainedObjects`는 선택 인자가 아니다. 빈 목록은 source만 보관하는
  저장소에만 사용할 수 있다.

### 검증

- 고정 2-entry PAX fixture의 bundle SHA-256을
  `ec4d4fa68091291c8ea847f1c21acbaa59a4f7b61af6d9d39ee12425e0182aa0`으로
  고정했다.
- 별도 process 재실행 결정성, 긴 UTF-8 경로, group 격리, size 분할, file part
  fallback, zstd round-trip, 손상 payload·비정상 metadata·PAX header 이름·추가
  end block 거부와 zstd 해제 출력 상한을 테스트한다.
- compact의 no-op, override 통합, file group 재사용, 선택 group만 재압축,
  retained object 충돌, `compactVersion` 안전 정수 상한과 손상 source 실패를
  테스트한다.

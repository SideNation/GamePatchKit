# 08. Runtime 상태 머신

> PRD 섹션: Runtime 동작, Runtime adapter contract, 프로젝트 책임
> `GamePatchKit.Runtime`

## 목표

platform 독립 Runtime을 구현한다: host가 지정한 target manifest 검증 → download
plan 실행 → content-addressed cache → group별 staging → required group 확인 →
`PackageState` 원자적 활성화 → optional group 후속 설치 → 취소·재시도·중단 복구.

## 선행 단계

03 (04·05와 병렬 진행 가능. Packager 출력물을 fixture로 쓰는 통합 테스트는 05 이후)

## 작업 항목

### adapter contract 정의

- [ ] `IArtifactTransport`: immutable manifest·signature·artifact를 읽기 전용
      stream으로 연다
- [ ] `IRuntimeStorage`: package별 writer lock, `PackageState` byte의 읽기·원자적 교체,
      content-addressed cache, staging stream, immutable installation 승격 제공
- [ ] adapter가 해석하지 않는 opaque `installationKey` contract
- [ ] Core `ICompressionCodec`의 주입 지점 (Runtime은 구체 압축 구현을 참조하지 않는다)
- [ ] `CancellationToken`과 `IProgress<PatchProgress>` 전달 규칙
- [ ] `PatchProgress` 모델: 단계, 파일·byte 진행, 재시도 횟수 등 관측 기준 값

### target manifest 수신·검증

- [ ] 신뢰하는 host 입력 또는 서버 응답에서 target manifest reference인
      `packageId`·`dataVersion`·`manifestHash`를 수신
- [ ] Runtime은 최신 release, stage/live, rollout과 rollback 대상을 결정하지 않고
      환경별 target 선택 모델·schema·저장 형식을 제공하지 않는다
- [ ] 목표 canonical manifest 원본 byte의 SHA-256이 `manifestHash`와 같은지 확인하고
      02의 schema·Core 의미·참조 무결성 검증 (signature 검증 연결은 11 단계)
- [ ] 공용 golden vector의 모든 manifest union branch와 invalid ref·order fixture가
      Packager와 같은 결과를 내는지 검증

### `PackageState` 모델·검증

- [ ] Runtime 소유 모델: `schemaVersion`, `stateRevision`, `packageId`,
      `active.dataVersion`, `active.manifestHash`, `groups[]`
- [ ] v1 `schemaVersion = 1`; 파일 없음은 active package 없음, 최초
      `stateRevision = 1`, 이후 commit마다 1 증가
- [ ] UTF-8 canonical JSON 직렬화와 group 이름 ordinal byte 정렬
- [ ] group 상태 `ready`·`notInstalled`·`stale`와 상태별
      `verifiedManifestHash`·`installationKey` 필드 규칙
- [ ] active manifest의 모든 group을 정확히 한 번 포함하고 unknown·중복 group 거부
- [ ] required group과 `ready` optional group은 `active.manifestHash` 기준으로 검증됨을
      요구하는 불변 조건
- [ ] `installationKey`는 절대 경로가 아닌 adapter의 opaque 식별자로 취급
- [ ] `installing`·진행률은 committed state에 넣지 않고 staging·cache에서만 관리
- [ ] state는 신뢰 근거가 아니므로 로드 시 schema·packageId·manifest·installation
      참조를 검증
- [ ] state 손상 시 host의 신뢰 가능한 target manifest reference를 기준으로만
      재구성하고, reference가 없으면 cache·디렉터리 이름에서 active release를
      추정하지 않음
- [ ] local state 자체에는 signature·secret을 기록하지 않음

### download plan 실행

- [ ] 최초 설치·전역 release 갱신은 target manifest의 required group만 선택해 Core
      download plan 사용
- [ ] 최초 state는 required group을 `ready`, optional group을 `notInstalled`로 기록
- [ ] optional group 요청은 현재 active manifest와 요청 group만 대상으로 계획
- [ ] 전역 갱신 시 설치된 optional group이 이전·목표 manifest에서 경로·크기·
      `fileHash`가 같으면 다운로드 없이 새 manifest로 재연결
- [ ] 변경된 optional group은 installation을 보존하되 `stale`, 삭제된 group은 state에서
      제거하고 활성 데이터로 노출하지 않음
- [ ] optional group이 required로 바뀌면 전역 활성화 전에 download plan에 포함
- [ ] 누락 artifact를 content-addressed cache로 다운로드
- [ ] release·group 안에서 compression이 혼합될 수 있으므로 artifact별 manifest
      compression metadata로 codec을 선택한다
- [ ] artifact hash와 압축 해제 결과 검증, 손상 artifact 거부·재분류
- [ ] manifest가 요구하는 codec이 미지원·미주입이면 활성화 전에 실패

### staging·활성화

- [ ] 한 요청의 target group 집합을 하나의 activation batch로 고정하고, 서로 다른
      요청은 별도 batch로 취급
- [ ] batch의 group별 다운로드·staging을 병렬 수행할 수 있지만 committed
      `PackageState`는 변경하지 않음
- [ ] batch의 모든 목표 파일을 staging에서 크기·`fileHash` 검증 후 각각 immutable
      installation으로 승격
- [ ] group 하나라도 준비 실패 시 batch 전체의 state 변경을 취소
- [ ] state commit 직전에 package writer lock 안에서 작업 시작 시점의
      `active.manifestHash`·`stateRevision`을 다시 확인하고 불일치하면 최신 state
      기준으로 재계획
- [ ] 모든 target group 상태를 반영하고 `stateRevision`을 한 번만 증가시킨
      `PackageState` 전체를 한 번 원자적으로 교체하며 group별 commit 금지
- [ ] 모든 required group 준비 후에만 전역 active pointer 교체
- [ ] optional group batch는 전역 active pointer를 유지하고 요청한 모든 group 상태를
      단일 revision으로 갱신
- [ ] 활성화 실패 시 이전 `PackageState`와 referenced installation을 계속 사용

### 취소·재시도·복구

- [ ] 오류 분류·retry 정책·복구 상태를 Runtime이 소유한다 (adapter는 구현하지 않는다)
- [ ] 취소·실패·프로세스 종료 시 검증된 cache를 보존하고 다음 실행에서 이어받는다
- [ ] state commit 전 실패는 이전 state를 유지하고, commit 후 reader는 새 state 전체만
      관찰한다

### 테스트

- [ ] in-memory fake `IArtifactTransport`·`IRuntimeStorage`로 상태 머신 단위 테스트
- [ ] 실패 주입: 전송 실패, 손상 byte, staging 중단, installation 승격 실패, stale
      `stateRevision`, state 교체 실패, 손상 state
- [ ] required-only 최초 설치, optional 후속 설치, unchanged optional 재연결, changed
      optional `stale`, optional→required 전환 시나리오
- [ ] 2개 이상 group batch에서 group별 준비 완료 순서와 무관하게 단 한 번 state를
      commit하고, 한 group 실패·revision 충돌 시 부분 `ready` state가 없는지 검증

## 산출물

- Runtime API·adapter contract, fake adapter 기반 테스트

## 완료 기준

- 다운로드 취소 후 재개해 검증된 cache를 재사용한다(검증 기준 15).
- staging 또는 활성화 실패 시 이전 `PackageState`와 installation을 유지한다
  (검증 기준 16, 23).
- 손상된 part·bundle·manifest를 거부한다(검증 기준 11, Runtime 범위).
- malformed union·참조·정렬·중복·미참조 manifest를 staging 전에 거부한다
  (검증 기준 25).
- 지원하지 않거나 주입되지 않은 codec을 manifest가 요구하면 활성화 전에 실패한다
  (검증 기준 20).
- 같은 group에 무압축·zstd artifact가 함께 있는 fixture를 올바르게 staging한다.
- compact로 artifact 위치만 바뀐 동일 파일을 다시 다운로드하지 않는다
  (검증 기준 10, fake adapter 수준).
- 최초·전역 plan은 required group만 다운로드하고 optional group은 요청 시 별도로
  준비한다(검증 기준 22).
- optional group의 `notInstalled`·`ready`·`stale` 전환과 동일 group의 무다운로드
  재연결이 정확하다(검증 기준 22).
- state commit 경계의 모든 실패 주입에서 old 또는 new state 전체만 관찰된다
  (검증 기준 23).
- 여러 group activation batch는 모든 target group을 한 revision에 반영하거나 아무
  group도 반영하지 않는다(검증 기준 23).
- public API가 `UnityEngine`·`NativeCompressions` type을 노출하지 않는다.

## 개발 v2

> v1 계획 본문은 보존하고 아래 내용이 현재 구현 상태를 덮어쓴다.

### 구현 완료

- [x] `IArtifactTransport`, `IRuntimeStorage`, cache writer와 group staging contract를
      추가하고 stream 수명, package writer lock, atomic state 교체, opaque
      `installationKey`와 exact installation file-set 검증 경계를 고정했다.
- [x] host가 전달한 `packageId`·`dataVersion`·`manifestHash`만 target으로 받고
      raw byte hash → strict/canonical JSON → Core schema·참조 → identity 순서로
      manifest를 검증한다. channel·latest release 선택 모델은 추가하지 않았다.
- [x] Runtime 소유 `PackageState` v1 모델, canonical serializer와 manifest 기반
      validator를 구현했다. state별 field, 전체 group 집합·정렬, required/optional,
      active manifest hash, opaque key와 I-JSON revision 상한을 검증한다.
- [x] 최초·전역 설치는 required group만, optional API는 현재 active manifest의 요청
      group만 Core download plan으로 처리한다.
- [x] content-addressed cache object의 크기·SHA-256을 재검증하고 누락·손상 object만
      재다운로드한다. multipart 결합 hash, file 해제 결과와 deterministic PAX bundle
      byte 계약을 staging에서 검증한다.
- [x] artifact별 compression metadata로 codec을 선택하고, 선택 group에 필요한
      codec이 없으면 다운로드 전에 실패한다. Runtime assembly는 구체 zstd 구현을
      참조하지 않는다.
- [x] 한 요청의 group 전체를 staging·immutable promotion한 뒤 state 전체를 한 번
      교체한다. commit 직전 lock 안에서 snapshot revision과 active identity를
      재확인하고 충돌 시 최신 state로 재계획한다.
- [x] unchanged optional installation과 compact의 동일 파일을 artifact 위치와
      무관하게 재연결하고, changed optional은 기존 key를 보존한 `stale`,
      optional→required는 active 교체 전 `ready`로 만든다.
- [x] transient transport retry, cancellation 전파, 검증 cache 재사용, 손상 state의
      trusted target 기반 재구성과 `PatchProgress` 단계·파일·byte·retry 보고를
      구현했다.

### 테스트 완료

- [x] in-memory transport/storage로 required-only 최초 설치와 optional 후속 설치
- [x] multi-group batch 단일 revision, promotion·state 교체 실패와 부분 state 부재
- [x] cancellation 후 cache 재사용, transient retry와 손상 cache 재분류
- [x] concurrent revision 충돌 재계획과 I-JSON revision 상한
- [x] changed/unchanged optional, optional→required와 누락 installation 복구
- [x] canonical·semantic manifest, single·multipart file, deterministic PAX bundle,
      무압축·주입 zstd와 손상 payload 거부

### 후속 단계 경계

- manifest signature와 trusted key 검증은 `IArtifactTransport.OpenManifestSignatureAsync`
  연결 지점만 제공하며 11단계에서 활성화한다.
- 실제 filesystem/HTTP atomicity, process 간 lock과 crash recovery는 09
  `GamePatchKit.DotNet` adapter에서 같은 contract로 검증한다.

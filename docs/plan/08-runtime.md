# 08. Runtime 상태 머신

> PRD 섹션: Runtime 동작, Runtime adapter contract, 프로젝트 책임
> `GamePatchKit.Runtime`

## 목표

platform 독립 Runtime을 구현한다: channel·manifest 검증 → download plan 실행 →
content-addressed cache → staging → 필수 group 확인 → 원자적 활성화 →
취소·재시도·중단 복구.

## 선행 단계

03 (04·05와 병렬 진행 가능. Packager 출력물을 fixture로 쓰는 통합 테스트는 05 이후)

## 작업 항목

### adapter contract 정의

- [ ] `IArtifactTransport`: immutable channel·manifest·artifact를 읽기 전용 stream으로
      연다
- [ ] `IRuntimeStorage`: content-addressed cache와 staging stream 제공, current
      pointer의 원자적 조회·교체
- [ ] Core `ICompressionCodec`의 주입 지점 (Runtime은 구체 압축 구현을 참조하지 않는다)
- [ ] `CancellationToken`과 `IProgress<PatchProgress>` 전달 규칙
- [ ] `PatchProgress` 모델: 단계, 파일·byte 진행, 재시도 횟수 등 관측 기준 값

### 목표 release 결정·검증

- [ ] 신뢰하는 channel 또는 서버 응답에서 `packageId`·`dataVersion`·`releaseId` 수신
- [ ] 목표 manifest의 schema·참조 무결성 검증 (signature 검증 연결은 11 단계)

### download plan 실행

- [ ] 로컬 상태 평가 후 Core download plan 사용
- [ ] 누락 artifact를 content-addressed cache로 다운로드
- [ ] artifact hash와 압축 해제 결과 검증, 손상 artifact 거부·재분류
- [ ] manifest가 요구하는 codec이 미지원·미주입이면 활성화 전에 실패

### staging·활성화

- [ ] 목표 파일을 staging에 구성하고 크기·`fileHash` 검증
- [ ] 모든 필수 group 준비 확인 후 current release pointer를 원자적으로 교체
- [ ] 활성화 실패 시 이전 current release를 계속 사용

### 취소·재시도·복구

- [ ] 오류 분류·retry 정책·복구 상태를 Runtime이 소유한다 (adapter는 구현하지 않는다)
- [ ] 취소·실패·프로세스 종료 시 검증된 cache를 보존하고 다음 실행에서 이어받는다

### 테스트

- [ ] in-memory fake `IArtifactTransport`·`IRuntimeStorage`로 상태 머신 단위 테스트
- [ ] 실패 주입: 전송 실패, 손상 byte, 중단, 활성화 실패

## 산출물

- Runtime API·adapter contract, fake adapter 기반 테스트

## 완료 기준

- 다운로드 취소 후 재개해 검증된 cache를 재사용한다(검증 기준 15).
- staging 또는 활성화 실패 시 이전 release를 유지한다(검증 기준 16).
- 손상된 part·bundle·manifest를 거부한다(검증 기준 11, Runtime 범위).
- 지원하지 않거나 주입되지 않은 codec을 manifest가 요구하면 활성화 전에 실패한다
  (검증 기준 20).
- compact로 artifact 위치만 바뀐 동일 파일을 다시 다운로드하지 않는다
  (검증 기준 10, fake adapter 수준).
- public API가 `UnityEngine`·`NativeCompressions` type을 노출하지 않는다.

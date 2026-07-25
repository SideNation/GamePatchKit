# 11. 서명·key rotation

> PRD 섹션: 서명과 무결성, release manifest, CLI 기능

## 목표

Ed25519 canonical manifest 서명 체계를 완성한다: Core 검증 API, Runtime의 신뢰
key 목록·key ID 검증, key rotation, 서명 필수 모드.

## 선행 단계

07 (`sign` 생성, Ed25519 구현 선정), 08 (Runtime 검증 연결)

## 작업 항목

### 서명 형식·검증

- [ ] `manifest.sig` 형식 고정: 알고리즘, key ID, signature만 기록
- [ ] identity 계산과 동일한 canonical JSON 원본 byte를 기준으로 서명·검증
- [ ] Core에 서명 검증 API 구현 (Runtime·CLI 공용, 07에서 선정한 Ed25519 구현이
      netstandard2.1에서 동작하지 않으면 검증 전용 managed 구현을 채택)

### Runtime 신뢰 모델

- [ ] 신뢰하는 public key 목록과 key ID 매칭으로 manifest 검증
- [ ] key rotation: 둘 이상의 public key를 동시에 신뢰할 수 있다
- [ ] 서명 필수 모드: 서명 누락, 알 수 없는 key ID, 검증 실패를 모두 거부
- [ ] CLI `verify`에 signature 검증 통합

### 키 취급

- [ ] 개인키를 설정, Git, manifest, build report와 로그에 저장하지 않는 규칙을
      테스트로 검증
- [ ] 테스트 전용 키 생성 유틸리티 (fixture 용도)

## 산출물

- 서명 검증·rotation·필수 모드 구현과 테스트, CLI `verify` 통합

## 완료 기준

- 손상된 manifest와 signature를 모두 거부한다(검증 기준 11, signature 범위).
- rotation 시나리오: 구 키와 신 키 서명이 모두 검증되고, 신뢰 목록에서 제거된 키는
  거부된다.
- 서명 필수 모드에서 누락·알 수 없는 key ID·검증 실패가 모두 실패로 끝난다.
- 로그·결과·build report 어디에도 개인키 내용이 없다.

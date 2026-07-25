# 11. 서명·key rotation

> PRD 섹션: 서명과 무결성, release manifest, CLI 기능

## 목표

Ed25519 canonical manifest 서명 체계를 완성한다: Core 검증 API, Runtime의 신뢰
key 목록·key ID 검증, key rotation, 서명 필수 모드.

## 선행 단계

07 (`sign` 생성, Ed25519 구현 선정), 08 (Runtime 검증 연결), 10 (conformance suite)

## 작업 항목

### 서명 형식·검증

- [ ] 02의 `manifest-signature.schema.json` 구현:
      `schemaVersion: 1`, `algorithm: Ed25519`, key ID와 signature만 허용
- [ ] Ed25519 signature 64-byte와 public key 32-byte를 padding 없는 base64url로
      표현하고 길이·alphabet을 schema와 모델에서 검증
- [ ] `keyId`는 raw 32-byte public key의 SHA-256 lowercase hexadecimal 64자 앞에
      `ed25519-`를 붙여 파생하고, 호출자가 지정한 임의 alias는 manifest에 기록하지 않음
- [ ] `manifestHash` 계산과 동일한 canonical manifest 원본 byte를 기준으로 서명·검증
- [ ] signature와 key가 달라져도 canonical manifest의 `manifestHash`는 바뀌지 않는다
- [ ] Core에 서명 검증 API 구현 (Runtime·CLI 공용, 07에서 선정한 Ed25519 구현이
      netstandard2.1에서 동작하지 않으면 검증 전용 managed 구현을 채택)
- [ ] `manifests/<manifestHash>/manifest.sig`는 최초 생성 후 불변: 같은 manifest·key
      재실행은 byte 검증 후 재사용하고 다른 key ID·signature로 교체하려 하면 실패

### Runtime 신뢰 모델

- [ ] 신뢰하는 public key 목록과 key ID 매칭으로 manifest 검증
- [ ] v1 key rotation은 기존 release를 재서명하지 않고 새 `manifestHash`의 release부터
      새 key를 사용하며, 서명만 바꾸는 release나 no-op compact를 만들지 않는다
- [ ] 구·신 public key 동시 신뢰 → 신 key release 서명·channel 전환 → 구 key
      release가 active·rollback 대상과 지원 client에서 사라진 뒤 구 key 제거
- [ ] 유출 key로 서명된 기존 release의 즉시 재서명은 단일 `manifest.sig` v1 범위에서
      지원하지 않으며 필요하면 다중 immutable signature 계약으로 schema를 변경해야 함
- [ ] 서명 필수 모드: 서명 누락, 알 수 없는 key ID, 검증 실패를 모두 거부
- [ ] CLI `verify`에 signature 검증 통합
- [ ] 10의 conformance suite에 valid signature, 손상 signature, unknown key ID,
      서명 누락 case를 signed extension으로 추가

### 키 취급

- [ ] 개인키를 설정, Git, manifest, build report와 로그에 저장하지 않는 규칙을
      테스트로 검증
- [ ] 테스트 전용 키 생성 유틸리티 (fixture 용도)
- [ ] 02의 공용 golden vector에 raw test public key, 파생 `keyId`, canonical
      `manifest.sig` byte와 예상 Ed25519 signature를 추가하고 byte 단위로 비교
- [ ] [RFC 8032](https://www.rfc-editor.org/rfc/rfc8032.html) Section 7.1의 모든
      Ed25519 known-answer vector에서 raw secret seed·public key·message·64-byte
      signature를 사용해 key derivation·sign·verify 결과를 exact byte로 검증
- [ ] message·public key·signature bit flip, 잘못된 길이와 non-canonical signature를
      거부하는 negative test 추가

## 산출물

- 서명 검증·신규-release-only rotation·필수 모드 구현과 테스트, Packager·CLI 통합

## 완료 기준

- 손상된 manifest와 signature를 모두 거부한다(검증 기준 11, signature 범위).
- rotation 시나리오에서 기존 release의 signature byte는 바뀌지 않고 새 release만
  신 key로 서명되며, 신뢰 목록에서 제거된 구 key release는 거부된다.
- 서명 필수 모드에서 누락·알 수 없는 key ID·검증 실패가 모두 실패로 끝난다.
- Core·Packager·Runtime·CLI가 공용 signed golden vector의 canonical manifest와
  signature를 동일하게 생성·검증한다(검증 기준 25, signature 범위).
- Ed25519 구현이 RFC 8032 Section 7.1 vector의 public key·signature byte를 정확히
  재현하고 모든 negative signature fixture를 거부한다(검증 기준 25).
- 동일 `manifest.sig` 재생성은 재사용되고 다른 key로의 기존 release 재서명은 불변
  경로 충돌로 실패한다.
- 로그·결과·build report 어디에도 개인키 내용이 없다.

# 11. 서명·key rotation

> PRD 섹션: 서명과 무결성, release manifest, CLI 기능

## 목표

Ed25519 canonical manifest 서명 체계를 완성한다: Core 검증 API, Runtime의 신뢰
key 목록·key ID 검증, key rotation, 서명 필수 모드.

## 선행 단계

07 (`sign` 생성, Ed25519 구현 선정), 08 (Runtime 검증 연결), 10 (conformance suite)

## 작업 항목

### 서명 형식·검증

- [x] 02의 `manifest-signature.schema.json` 구현:
      `schemaVersion: 1`, `algorithm: Ed25519`, key ID와 signature만 허용
- [x] Ed25519 signature 64-byte와 public key 32-byte를 padding 없는 base64url로
      표현하고 길이·alphabet을 schema와 모델에서 검증
- [x] `keyId`는 raw 32-byte public key의 SHA-256 lowercase hexadecimal 64자 앞에
      `ed25519-`를 붙여 파생하고, 호출자가 지정한 임의 alias는 manifest에 기록하지 않음
- [x] `manifestHash` 계산과 동일한 canonical manifest 원본 byte를 기준으로 서명·검증
- [x] signature와 key가 달라져도 canonical manifest의 `manifestHash`는 바뀌지 않는다
- [x] Core에 서명 검증 API 구현 (Runtime·CLI 공용, 07에서 선정한 Ed25519 구현이
      netstandard2.1에서 동작하지 않으면 검증 전용 managed 구현을 채택)
- [x] `manifests/<manifestHash>/manifest.sig`는 최초 생성 후 불변: 같은 manifest·key
      재실행은 byte 검증 후 재사용하고 다른 key ID·signature로 교체하려 하면 실패

### Runtime 신뢰 모델

- [x] 신뢰하는 public key 목록과 key ID 매칭으로 manifest 검증
- [x] v1 key rotation은 기존 release를 재서명하지 않고 새 `manifestHash`의 release부터
      새 key를 사용하며, 서명만 바꾸는 release나 no-op compact를 만들지 않는다
- [x] 구·신 public key 동시 신뢰 → 신 key release 서명 → host가 새 manifest를
      target으로 선택 → 구 key release가 active·rollback 대상과 지원 client에서
      사라진 뒤 구 key 제거
- [x] 유출 key로 서명된 기존 release의 즉시 재서명은 단일 `manifest.sig` v1 범위에서
      지원하지 않으며 필요하면 다중 immutable signature 계약으로 schema를 변경해야 함
- [x] 서명 필수 모드: 서명 누락, 알 수 없는 key ID, 검증 실패를 모두 거부
- [x] CLI `verify`에 signature 검증 통합
- [x] 10의 conformance suite에 valid signature, 손상 signature, unknown key ID,
      서명 누락 case를 signed extension으로 추가

### 키 취급

- [x] 개인키를 설정, Git, manifest, build report와 로그에 저장하지 않는 규칙을
      테스트로 검증
- [x] 테스트 전용 키 생성 유틸리티 (fixture 용도)
- [x] 02의 공용 golden vector에 raw test public key, 파생 `keyId`, canonical
      `manifest.sig` byte와 예상 Ed25519 signature를 추가하고 byte 단위로 비교
- [x] [RFC 8032](https://www.rfc-editor.org/rfc/rfc8032.html) Section 7.1의 모든
      Ed25519 known-answer vector에서 raw secret seed·public key·message·64-byte
      signature를 사용해 key derivation·sign·verify 결과를 exact byte로 검증
- [x] message·public key·signature bit flip, 잘못된 길이와 non-canonical signature를
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

## 개발 v2

v1 계획 본문은 보존하며 아래 내용이 현재 구현 상태를 나타낸다. 07이 이미
`ManifestSignature` 모델, `Ed25519ManifestSigner`, `ReleaseSigner`/`ReleaseVerifier`,
`IManifestSignatureVerifier` 확장점, CLI `sign`을 구현해뒀으므로 11의 작업은 그
확장점에 실제 암호 검증을 채우고 Runtime 신뢰 모델을 추가하는 것이었다.

### 구현 결과

- [x] `GamePatchKit.Core.Signatures.Ed25519Signatures`(신규): `DeriveKeyId`와
      `Verify`를 Core에 구현했다. BouncyCastle.Cryptography가 netstandard2.0
      대상 순수 관리 코드라 07의 결정대로 netstandard2.1인 Core에서도 동일
      구현을 그대로 쓸 수 있었다 — 검증 전용 별도 구현은 필요 없었다.
      `Ed25519ManifestSigner.DeriveKeyId`(Packager)는 이 함수로 위임하도록
      바꿔 중복 로직을 없앴다.
- [x] `ManifestSignature.GetSignatureBytes()`(Core)를 추가해 base64url 디코드
      로직을 한 곳에 모았다. `TrustedKeySignatureVerifier`(Packager)와
      Runtime의 서명 검증이 모두 이 메서드를 쓴다.
- [x] `TrustedKeySignatureVerifier`(Packager, 신규): `IManifestSignatureVerifier`
      구현체. raw public key 목록을 keyId로 색인하고, `FromBase64UrlPublicKeys`로
      CLI 인자 문자열에서 직접 만들 수 있다. `PackageErrorCodes.InvalidTrustedKey`
      (신규)를 잘못된 key 인코딩에 쓴다.
- [x] CLI `verify`에 `--trusted-key <base64url-public-key>`(반복 가능)와
      `--require-signature`를 추가했다. `--trusted-key` 없이 `--require-signature`만
      주면 `ReleaseVerifyRequest`의 기존 검증(`ArgumentException` → 입력 오류)이 거부한다.
- [x] `TrustedSigningKeys`(Runtime, 신규): Packager의 `TrustedKeySignatureVerifier`와
      같은 모양의 keyId→publicKey map. 여러 key를 동시에 신뢰할 수 있어 rotation
      기간에 구·신 key를 함께 넘길 수 있다.
- [x] `PackageRuntime` 생성자에 `trustedSigningKeys`·`requireSignature` 선택
      인자를 추가했다. `trustedSigningKeys`가 없으면(기본값) `OpenManifestSignatureAsync`를
      전혀 호출하지 않아 08 단계 동작과 완전히 같다. 있으면 `LoadManifestAsync`
      마지막 단계에서 서명을 가져와 검증하며, 있는데 신뢰 목록에 없거나 검증이
      실패하면 `requireSignature`와 무관하게 항상 거부하고, 누락은
      `requireSignature`일 때만 거부한다. `RuntimeErrorCodes.SignatureInvalid`(신규)
      하나로 세 실패(누락·문서 손상·미신뢰 key·검증 실패)를 모두 보고한다 —
      Packager가 `packager.invalid-signature` 하나로 같은 종류의 실패를 묶는 것과
      같은 선택이다.
- [x] signature 응답을 못 가져오는 것(등록 안 됨, 재시도 소진)과 "signature가
      없음"을 같은 것으로 취급한다 - manifest·artifact와 달리 signature 부재는
      정상적인 unsigned release일 수 있으므로, 있고-없음의 판단은 전적으로
      `requireSignature`가 맡는다. 이 결정 때문에 `FakeArtifactTransport`
      (Runtime.Tests)와 `InMemoryArtifactTransport`(Conformance)의
      `OpenManifestSignatureAsync`가 등록되지 않은 manifestHash에 빈 stream을
      돌려주던 placeholder 동작을 실제 not-found 예외로 고쳐야 했다 - 08~10에서는
      아무도 이 메서드를 실제로 쓰지 않아 드러나지 않았던 자리다.
- [x] golden vector(`tests/fixtures/golden-vectors/<name>/`)에 `public-key.bin`,
      `key-id.txt`, `manifest-sig.canonical.json`을 추가했다. 서명 키는
      `tests/GamePatchKit.Packager.Tests/SigningKeys.cs`와 같은 고정 32-byte 값을
      재사용해 테스트 스위트 전체가 하나의 canonical 테스트 key를 공유한다.
      `tools/generate_golden_vectors.py`는 Python `cryptography`(OpenSSL Ed25519 -
      BouncyCastle과 다른 구현)로 서명해 `jcs.py`가 canonicalization에 대해 하는
      역할을 signing에 대해서도 한다.
- [x] RFC 8032 Section 7.1의 5개 vector(TEST 1/2/3/1024/SHA(abc))를 모두
      `tests/GamePatchKit.Core.Tests/Signatures/TestEd25519Signatures.cs`에
      추가했다. rfc-editor.org의 원문을 그대로 가져와 스크립트로 파싱해 손으로
      옮겨 적다 생기는 실수를 없앴다.
- [x] bit-flip(message·public key·signature R·signature S), 잘못된 길이
      (signature·public key), non-canonical signature(S + curve order L) negative
      test를 추가했다. non-canonical 케이스는 BouncyCastle의 `Ed25519Signer`가
      이미 거부한다는 것을 실행해서 확인했을 뿐, Core에 별도 canonical 검사를
      추가하지는 않았다.
- [x] adapter conformance suite(10단계)에 signed 시나리오 4개를 추가했다: 신뢰
      key로 검증되는 유효 서명, `requireSignature: true`에서 서명 누락 거부,
      신뢰 목록에 없는 key로 서명된 release 거부(`requireSignature` 무관), bit-flip
      손상 서명 거부. 새 abstract 메서드 `RegisterSignatureAsync`가
      `RegisterRawManifestAsync`와 같은 자리에서 signed fixture를 두 adapter
      각각의 위치(in-memory dictionary / `<packageId>/manifests/<hash>/manifest.sig`
      파일)에 놓는다.
- [x] 개인키 미노출은 07의 `AssertNoKeyMaterial`(CLI sign 테스트)이 이미 검증하며,
      11에서 추가한 `verify`의 `--trusted-key`는 애초에 public key만 받으므로
      새로 노출될 비밀이 없다.
- [x] 테스트 전용 키 생성 유틸리티는 프로젝트마다 작은 `SigningKeys.cs`로
      복제했다(Packager.Tests는 07에서 이미 있었고, Runtime.Tests·Conformance에
      새로 추가) - 이 저장소에 공용 테스트 유틸리티 프로젝트가 없어 기존
      패턴을 그대로 따랐다.

### 확정 결정

- Core·Packager·Runtime·CLI가 "공용 signed golden vector"를 똑같이 생성·검증한다는
  완료 기준은, 02 단계의 unsigned golden vector가 이미 그랬듯, 네 곳이 전부
  golden-vector 파일을 직접 읽는 것이 아니라 전부 같은 Core 함수
  (`Ed25519Signatures.Verify`/`DeriveKeyId`, `CanonicalJsonWriter`)를 호출하는
  것으로 만족한다. golden vector는 그 Core 함수 자체의 정확성을 독립
  구현(Python `cryptography`)으로 증명하고, Packager·Runtime·CLI·conformance
  테스트는 각자 그 함수를 올바른 지점에서 호출하는지만 증명한다.
- key rotation의 "구·신 key 동시 신뢰 → ... → 구 key 제거" 절차는 운영 순서이지
  코드가 강제하는 상태 기계가 아니다. `TrustedSigningKeys`가 여러 key를 동시에
  담을 수 있다는 것과 신뢰 목록에 없는 key의 서명은 언제나 거부된다는 것,
  이 두 성질만으로 절차 전체가 성립한다 - 별도의 "rotation 모드"나 이력 추적은
  추가하지 않았다.
- Runtime의 서명 검증은 세 가지가 아니라 두 축(`trustedSigningKeys` 유무 ×
  `requireSignature`)으로 충분했다. `trustedSigningKeys`가 없으면 완전
  08단계 그대로(호출 자체가 없음), 있으면 존재하는 서명은 항상 검증하고
  누락만 `requireSignature`가 gate한다 - Packager `ReleaseVerifyRequest`와
  같은 모양이라 서명 관련 두 층의 동작을 따로 설명할 필요가 없다.
- Codex adversarial review(1·2회차)가 지적한 대로, signature를 가져오는
  transport 호출은 원래 "확인된 not-found"와 "이번엔 이유 불문 실패"를 구분하지
  못했다. 2회차는 이걸 더 정밀하게 짚었다: `ArtifactTransportException.
  IsTransient`가 이미 있으므로, transient 실패가 재시도까지 다 소진된 경우는
  "이건 404가 아니다"가 확정적이라 구분 불가능이 아니었다 — `PackageRuntime.
  ReadManifestSignatureBytesAsync`를 고쳐 transient 소진은 `requireSignature`와
  무관하게 항상 `runtime.transport-failed`로 하드 실패시키도록 바꿨다.
  남은 진짜 구분 불가 지점(non-transient 실패가 404인지 401/403인지)은 이후
  `ArtifactTransportException`에 `IsNotFound`(기본값 `false`, 세 번째 optional
  생성자 매개변수)를 추가해 닫았다. adapter가 "확인된 not-found"일 때만
  `IsNotFound: true`를 설정해야 하는 새 계약 의무가 생겼고(`docs/contracts/
  runtime.md`의 adapter contract 절에 `IsTransient`와 나란히 명시),
  `HttpArtifactTransport`는 HTTP 404만 이걸 설정하고 401/403/410을 포함한 다른
  모든 client error는 `false`를 유지한다. `Ed25519Signatures.Verify` 자체를
  건드리지 않는, 계약의 boolean flag 하나를 추가하는 수준의 변경으로 끝났다 —
  당초 "계약 변경이 필요해 미룬다"고 판단했던 범위보다 훨씬 작았다.
  `PackageRuntime.ReadManifestSignatureBytesAsync`는 이제 `IsNotFound`인 경우만
  "서명 없음"으로 tolerate하고, 그 외(transient 소진이든 확인 안 된 non-transient
  실패든)는 전부 하드 실패한다. 이 프로젝트가 아직 외부에 배포하는 어셈블리가
  없는 v1 단계라 기존 두 생성자의 시그니처를 그대로 보존하는 별도 오버로드까지는
  두지 않고 optional parameter로 추가했다.
- 2회차 review는 또한 `TrustedKeySignatureVerifier.Verify`가
  `IManifestSignatureVerifier`의 "거부는 `false`, throw는 신뢰 key 집합 자체가
  못 쓸 때만"이라는 계약을 어기고, `TryParse`를 거치지 않은 malformed
  `ManifestSignature`(신뢰하는 keyId를 가졌지만 signature 문자열이 깨진 경우)에
  대해 `InvalidOperationException`을 흘린다는 것을 찾았다. `ManifestSignature`에
  non-throwing `TryGetSignatureBytes`를 추가하고 `TrustedKeySignatureVerifier`·
  Runtime의 서명 검증 양쪽 모두 이걸 쓰도록 바꿨다(Runtime 쪽은 실제로는
  도달 불가능한 경로였지만 방어적으로 함께 고쳤다).
- 2회차 review는 새로 추가한 테스트 두 개가 주장을 실제로 증명하지 못한다는
  것도 짚었다: retry-exhaustion 테스트는 `SignatureOpenCount`를 확인하지 않아
  첫 실패에서 바로 포기해도 통과했고, oversized 테스트는 5,000-byte 배열이
  4 KiB 검사 없이도 JSON 파싱 실패로 같은 에러 코드를 내 4 KiB 검사가 실제로
  도는지 증명하지 못했다. 전자는 `SignatureOpenCount == 3` assertion을,
  후자는 에러 메시지에 "byte limit" 문자열이 포함되는지(malformed-document
  메시지와 구분되는) assertion을 추가해 고쳤다.

### 검증

- `dotnet test`로 GamePatchKit.Core.Tests, Packager.Tests, Cli.Tests,
  Runtime.Tests, Conformance, IntegrationTests 전체가 통과한다.
- golden vector의 `manifest-sig.canonical.json`은 Python `cryptography`
  (BouncyCastle이 아닌 독립 구현)로 서명한 뒤 C#의 `Ed25519Signatures.Verify`가
  검증해, 서명 primitive 자체가 구현 하나에만 자기증명되지 않는다.
- RFC 8032 vector는 원문에서 스크립트로 파싱해 옮겼고 5개 모두 byte 단위로
  일치한다.
- non-canonical signature(S + L) 거부는 실제로 그 값을 만들어 `Verify`가
  `false`를 반환하는지 실행해 확인했다(가정이 아니라 실행 결과).

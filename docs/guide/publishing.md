# publish와 서명 운영

Packager가 만든 로컬 publish tree를 외부 저장소에 올리는 순서, target manifest 선택의
책임 경계, Ed25519 서명 운영과 key rotation을 다룬다.

## GamePatchKit이 제공하는 것과 제공하지 않는 것

Packager는 로컬에 publish tree만 만든다.

1. 불변 file·bundle artifact
2. 불변 release manifest와 signature

**GamePatchKit은 Supabase Storage·S3·Steam 같은 특정 원격 저장소에 직접 업로드하는
publisher를 제공하지 않는다.** 또한 환경별 최신 release, stage/live, rollout과 rollback
상태를 나타내는 target 선택 모델·schema·파일도 제공하지 않는다.

각 애플리케이션의 host 또는 서버가 자신의 설정·DB·배포 시스템에서 target manifest를
선택하고, 신뢰 가능한 `packageId`·`dataVersion`·`manifestHash`를 Runtime에 전달한다.
Storage credential, 승인, target 선택의 원자성은 publisher·host 운영 계층의 책임이다.

| 책임 | 소유자 |
| --- | --- |
| artifact·manifest·signature 생성과 검증 | GamePatchKit (Packager/CLI) |
| 원격 저장소 업로드와 credential | 외부 publisher |
| "지금 어떤 release가 live인가" 결정·저장·전환 | host 또는 서버 |
| 전달받은 target manifest의 검증과 설치 | GamePatchKit (Runtime) |

## publisher 순서

업로드는 반드시 이 순서로 한다.

1. **새 artifact 업로드** — `<packageId>/artifacts/...` 아래의 불변 object
2. **원격 크기와 SHA-256 검증** — 올라간 byte가 로컬과 같은지 확인
3. **manifest와 signature 업로드** — `<packageId>/manifests/<manifestHash>/`
4. **원격 manifest와 모든 참조 재검증**

artifact가 먼저 올라가야 하는 이유는 manifest가 곧 "이 artifact들이 전부 있다"는
선언이기 때문이다. 순서를 뒤집으면 client가 아직 존재하지 않는 artifact를 가리키는
manifest를 target으로 받을 수 있다.

## immutable cache와 rollback

- hash 경로 artifact와 `manifestHash` 경로 manifest는 **immutable 장기 cache**한다. 같은
  경로의 byte는 절대 바뀌지 않으므로 CDN·브라우저 cache를 길게 잡아도 안전하다.
- **한번 publish한 object를 덮어쓰지 않는다.** 같은 경로에 다른 byte를 쓰려는 시도는
  `packager.immutable-path-conflict`로 실패한다.
- **rollback은 artifact를 복사하지 않는다.** host 또는 서버가 이전 immutable manifest를
  다시 target으로 선택하기만 하면 된다. client는 경로와 `fileHash`가 같은 파일을 다시
  받지 않는다.
- compact는 host의 target manifest 선택을 바꾸지 않고 이전 artifact를 삭제하지도 않는다.
  artifact garbage collection은 v1 범위 밖이며 운영 계층이 결정한다.
- compact를 실행할 때는 아직 보관 중인 과거 release를 `--retained`로 **모두** 알려야
  한다. 디렉터리 목록만으로는 보관 중인 release의 object와 잔여물을 구분할 수 없어서
  자동 수집하지 않는다. 빠뜨리면 그 release의 part 경로를 덮어쓰는 candidate가 통과할 수
  있다.

## 서명

SHA-256은 **손상 검출**이지 출처 보장이 아니다. 공개 배포는 Ed25519 canonical manifest
서명을 기본 운영 조건으로 한다.

### `manifest.sig`

`manifest-signature.schema.json`을 따르는 canonical JSON이며 네 필드만 기록한다.

```json
{"algorithm":"Ed25519","keyId":"ed25519-<hex64>","schemaVersion":1,"signature":"<base64url 86자>"}
```

| 필드 | v1 값 |
| --- | --- |
| `schemaVersion` | `1` |
| `algorithm` | `Ed25519` |
| `keyId` | `ed25519-` + raw 32-byte public key의 SHA-256 lowercase hex64 |
| `signature` | 64-byte Ed25519 값의 padding 없는 base64url |

**`keyId`는 임의 alias가 아니라 public key의 fingerprint다.** 그래서 manifest만 보고도
어떤 key가 서명했는지 판정할 수 있고, raw public key와 `keyId`가 어긋나면 오류다.
canonical `manifest.sig`는 항상 정확히 225 byte다.

서명 대상은 `manifestHash`가 가리키는 **바로 그 canonical manifest byte**다. 압축
전송본(`manifest.json.zst`)과 `manifest.sig` 자신은 hash·서명 입력에서 제외된다.

### 서명하기

```bash
# 개인키는 padding 없는 base64url 32 byte 텍스트. 파일 또는 환경 변수로 전달한다.
gpk sign --output-root publish --package-id sample-game-client-data \
  --manifest-hash <hex64> --key-env GPK_SIGNING_KEY --json
```

- 개인키는 설정·Git·manifest·build report·로그 어디에도 저장하지 않는다. CLI 결과에는
  파생된 `ed25519-<hex64>` key ID로만 나타난다.
- `manifests/<manifestHash>/manifest.sig`는 **최초 생성 후 불변이다.** 같은 manifest를
  같은 key로 다시 서명하면 기존 byte를 검증한 뒤 재사용하고 `created`가 `false`가 된다.
  다른 key ID나 signature byte로 교체하려 하면 불변 경로 충돌로 실패한다.

### 검증하기

```bash
gpk verify --output-root publish --package-id sample-game-client-data \
  --manifest-hash <hex64> \
  --trusted-key <base64url-public-key> --require-signature --json
```

`signature.state`는 세 값 중 하나다.

| 값 | 의미 |
| --- | --- |
| `absent` | `manifest.sig`가 없다 |
| `present` | 문서가 schema·canonical byte 검증을 통과했지만 `--trusted-key`가 없어 **어떤 key와도 대조하지 않았다** |
| `verified` | `--trusted-key`로 준 public key 중 하나가 `keyId`와 일치하고 서명이 실제로 검증됐다 |

> **`present`는 서명 증거가 아니다.** 형식만 맞는 `keyId`와 아무 64 byte면 이 상태에
> 도달한다. "서명된 release만 배포" 게이트로 쓰려면 반드시 `--trusted-key`를 준다.

- `--trusted-key`는 반복 가능하다. rotation 기간에 구·신 key를 동시에 줄 수 있다.
- `--require-signature`는 signature 누락도 실패로 만든다. `--trusted-key` 없이 주면
  `cli.invalid-arguments`로 거부한다 — 검증 수단 없이 "필수"만 요구하면 위조된
  `manifest.sig`와 진짜 서명을 구분할 수 없기 때문이다.
- `--trusted-key`가 있으면 signature가 있는데 알 수 없는 `keyId`이거나 검증에 실패하면
  `--require-signature` 여부와 무관하게 항상 실패한다.

Runtime 쪽 동작은 [Runtime 통합 가이드](runtime-integration.md#net에서-시작하기)의
`TrustedSigningKeys`·`requireSignature` 절과 같은 규칙이다.

### key rotation

v1은 **기존 release를 재서명하지 않는다.** 새 `manifestHash`의 release부터 새 key를
사용한다. 서명만 바꾸려고 no-op compact나 새 release를 만들지 않는다.

```text
1. 구·신 public key를 동시에 신뢰 목록에 넣어 배포한다
   (gpk verify --trusted-key <old> --trusted-key <new>,
    Runtime은 new TrustedSigningKeys(new[] { old, new }))
2. 새 key로 새 release를 서명한다
3. host가 새 manifest를 target으로 선택한다
4. 구 key로 서명된 release가 active·rollback 대상에서 빠지고
   지원하는 모든 client에서 사라진 뒤에 구 key를 신뢰 목록에서 제거한다
```

3번과 4번 사이에는 시간이 필요하다. **rollback 대상으로 남겨 둔 release가 구 key로
서명돼 있으면 구 key를 지우는 순간 그 rollback이 불가능해진다.**

> **유출 key 복구의 한계.**
> 유출된 key로 서명된 **기존** release를 즉시 새 key로 다시 서명해야 한다면, 단일
> `manifest.sig` v1 계약으로는 지원하지 않는다. 다중 immutable signature 계약이
> 필요하다. v1에서 유출에 대응하는 방법은 새 key로 새 release를 만들어 host가 그것을
> target으로 선택하게 하고, 유출 key를 신뢰 목록에서 제거하는 것이다.

### Ed25519 구현 검증

구현은 [RFC 8032](https://www.rfc-editor.org/rfc/rfc8032.html) Section 7.1의 모든
Ed25519 known-answer vector와 GamePatchKit signed golden vector를 통과해야 한다. 실행
방법과 두 검증의 역할 차이는
[release identity와 canonical JSON](identity.md#rfc-8032-known-answer-test)을 참조한다.

## 보안·공개 경계

- 하나의 package에 포함된 모든 파일은 **같은 접근 정책**을 가진다고 간주한다.
- client가 공개 CDN에서 읽는 데이터와 서버만 읽는 비공개 데이터는 서로 다른
  `packageId`로 패키징한다.
- group은 다운로드·compact 단위이며 **보안 경계로 사용하지 않는다.**
- 서로 다른 공개 경계를 넘는 artifact deduplication은 수행하지 않는다.
- package manifest에는 다른 package의 파일 경로·hash·artifact 위치를 기록하지 않는다.

| `packageId` 예 | 대상 | 공개 정책 |
| --- | --- | --- |
| `<game>-client-data` | 클라이언트와 공개 가능한 공용 데이터 | public read |
| `<game>-server-data` | 서버 판정·운영 전용 데이터 | private |

# release identity와 canonical JSON

`dataVersion`·`compactVersion`·`manifestHash`의 의미, release manifest 구조와 검증
계층, canonical JSON 규칙, golden vector 사용법을 다룬다.

## 세 가지 version 값

| 값 | 무엇을 나타내나 | 형식 |
| --- | --- | --- |
| `dataVersion` | 최종 **논리** 파일 상태와 전달 의미의 digest | `v1-` + lowercase hex64 |
| `compactVersion` | 현재 **물리** packaging 세대 | 0 이상 정수 |
| `manifestHash` | canonical manifest 원본 byte의 SHA-256이자 **불변 manifest 식별자** | lowercase hex64 |

`dataVersion`만 `v1-` 접두사를 가진다. `manifestHash`·`fileHash`·`artifactHash`처럼
접두사 없는 hex64 계열과 한눈에 구분하기 위해서다.

### `dataVersion`

계산 입력에 **포함**하는 것:

- `packageId`
- group 이름과 `required` 같은 소비 의미
- 각 파일의 정규화 경로, group, 원본 크기, `fileHash`

계산에서 **제외**하는 것:

- file/bundle artifact 종류, bundle 경계, 압축 방식
- artifact 경로와 part 구성
- `compactVersion`과 `manifestHash`
- build 시각, source revision, 실행 환경

즉 **같은 데이터를 어떻게 포장했는지는 `dataVersion`을 바꾸지 않는다.**

### `compactVersion`

- 최초 package는 `0`이다.
- incremental package는 이전 manifest 값을 그대로 상속한다.
- compact 결과의 물리 배치가 실제로 바뀌면 source manifest 값보다 1 증가한다.
- candidate manifest를 source `compactVersion`으로 canonicalize한 byte가 source
  manifest와 같으면 **성공 no-op**이며 기존 `compactVersion`과 `manifestHash`를
  재사용한다.
- 같은 `compactVersion`이 같은 manifest를 뜻하지 않는다. **manifest 식별에는 절대
  사용하지 않는다.**

### `manifestHash`

- `manifestHash` 필드가 **없는** 최종 canonical manifest 원본 byte의 SHA-256이다.
  그래서 `release-manifest.schema.json`에는 `manifestHash` property 자체가 없다.
- lowercase hexadecimal 64자로 표현한다.
- `schemaVersion`, `compactVersion`, group·artifact·file 참조를 포함해 manifest의 모든
  필드가 hash 입력에 들어간다.
- 선택적 `.json.zst` 전송본과 `manifest.sig`는 hash 입력에서 제외한다.

### 무엇이 무엇을 바꾸나

| 상황 | `dataVersion` | `compactVersion` | `manifestHash` |
| --- | --- | --- | --- |
| 파일 내용·group 의미 변경 | 변경 | 상속 | 변경 |
| compact로 물리 배치 변경 | 유지 | +1 | 변경 |
| compact 결과가 source와 동일 (no-op) | 유지 | 유지 | 유지 |
| 같은 데이터를 다른 압축 배치로 패키징 | 유지 | - | 변경 |
| compression 설정만 변경 + 모든 artifact 재사용 | 유지 | 유지 | 유지 |
| manifest `schemaVersion`만 변경 | 유지 | - | 변경 |

기존 설치는 경로와 `fileHash`가 같으면 artifact 위치가 달라져도 다시 다운로드하지
않는다. compact가 `dataVersion`을 유지하는 이유이자, compact 후 client가 재다운로드
없이 새 manifest로 재연결되는 근거다.

## publish tree

```text
<outputRoot>/
└── <packageId>/
    ├── artifacts/
    │   ├── files/<artifactHash>/
    │   │   ├── content         # 무압축
    │   │   ├── content.zst     # zstd
    │   │   └── part-#####      # 분할된 경우
    │   └── bundles/<groupName>/
    │       ├── <bundleHash>.tar
    │       └── <bundleHash>.tar.zst
    └── manifests/<manifestHash>/
        ├── manifest.json
        ├── manifest.json.zst   # 선택 (--compressed-manifest)
        └── manifest.sig        # 선택 (gpk sign)
```

한 artifact 디렉터리에는 실제 형식에 맞는 payload 한 종류만 존재한다. hash 경로는
불변이며, 같은 경로에 다른 byte가 있으면 덮어쓰지 않고
`packager.immutable-path-conflict`로 실패한다.

## release manifest 구조

manifest는 patch chain이 아니라 **최종 상태 전체**를 기록한다. `files[]`는 그 release의
모든 파일을 정확히 한 번 포함하고, 삭제된 파일은 그냥 빠진다. 추가·변경·삭제 목록은
`PackageBuildReport`에만 있고 manifest에는 없다.

| 필드 | 내용 |
| --- | --- |
| `schemaVersion` | manifest 형식 버전 (v1은 `1`) |
| `packageId` | manifest package |
| `dataVersion` | 최종 논리 상태 digest |
| `compactVersion` | 현재 물리 packaging 세대 |
| `groups[]` | group 이름, `required`와 논리 정책 |
| `artifacts[]` | kind, 경로, 크기, hash, compression, part |
| `files[]` | 최종 경로, group, 원본 크기, `fileHash`, artifact 참조 |

### discriminated union

schema는 모든 object에서 알 수 없는 필드를 거부하고, 아래 union을 `oneOf` +
`const` discriminator로 정의한다.

| 위치 | discriminator | 내용 |
| --- | --- | --- |
| `artifacts[].kind` | `file` + `payload.kind: single` | 단일 payload의 경로·크기·`artifactHash` |
| `artifacts[].kind` | `file` + `payload.kind: parts` | 전체 payload `artifactHash`와 순서 있는 `parts[]`(index·경로·크기·`partHash`) |
| `artifacts[].kind` | `bundle` | 단일 bundle payload의 경로·크기·`artifactHash`·실제 compression metadata와 순서 있는 `entries[]` |
| `files[].source.kind` | `file` | 존재하는 file artifact의 `artifactHash` 참조 |
| `files[].source.kind` | `bundleEntry` | 존재하는 bundle artifact의 `artifactHash`와 entry 경로 참조 |
| compression metadata | `kind: none` / `kind: zstd` | 실제 payload 형식에 필요한 필드만 허용 |

### 참조 무결성 (Core 의미 검증)

schema로는 표현할 수 없고 `ManifestValidator`가 검사하는 규칙들이다.

- group 이름, 정규화 file 경로, artifact 경로, `artifactHash`는 각 namespace에서 유일해야 한다.
- 모든 `files[].group`은 선언된 group을 참조한다.
- `source.kind: file`은 file artifact만, `source.kind: bundleEntry`는 bundle과 그 안의
  entry만 참조한다.
- bundle은 같은 group의 file만 포함하고, **각 bundle entry는 정확히 하나의 file과
  대응한다.** 반면 하나의 file artifact는 같은 byte를 가진 여러 file이 공유할 수 있다.
- part index는 `0`부터 연속되고, part 경로는 유일하며, 선언한 part 크기의 합이 전체
  payload 크기와 일치해야 한다.
- manifest의 모든 artifact와 bundle entry는 최소 한 번 참조되어야 한다. 중복·미참조는
  오류다.
- content-addressed artifact 경로는 artifact kind·group·hash에서 계산한 예상 경로와
  일치해야 한다.
- 공유 저장소에 있지만 그 manifest가 참조하지 않는 과거 release artifact 파일은
  허용하고 검증 대상에서 제외한다.

### 세 계층 검증

| 계층 | 무엇을 보나 | 담당 |
| --- | --- | --- |
| 1. JSON·Schema | I-JSON 파싱, 필수 필드·type·enum·discriminator·encoding, 알 수 없는 필드 거부 | `release-manifest.schema.json` |
| 2. Core 의미 | 배열 정렬, 중복, 참조 무결성, path·hash 불변 조건 | `ManifestValidator` |
| 3. payload | 실제 artifact stream의 크기·hash, part 결합, 압축 해제 결과 | `PackagePayloadVerifier` |

`gpk verify`와 Runtime은 모두 이 순서로 검사한다. Runtime은 여기에 앞서 전송받은
manifest byte의 SHA-256이 target `manifestHash`와 같은지부터 확인한다.

## canonical JSON

canonical JSON은 [RFC 8785 JCS](https://www.rfc-editor.org/rfc/rfc8785.html)와 I-JSON을
기준으로 한다.

- object property는 JCS 규칙대로 **UTF-16 code unit 순서**로 재귀 정렬한다.
- array 순서는 정렬하지 않고 아래 domain 규칙을 유지한다.
- 숫자는 `-(2^53-1)`부터 `2^53-1`까지의 JSON integer만 허용한다. 각 필드의 schema가
  음수 허용 여부를 추가로 제한한다.
- 출력은 UTF-8이며 **BOM·공백·trailing newline이 없다.** 비교는 텍스트가 아니라 raw
  byte로 한다.

### domain 배열 순서

| 배열 | 정렬 기준 |
| --- | --- |
| `groups[]` | group 이름 |
| `files[]` | 정규화 상대 경로 |
| `artifacts[]` | content-addressed artifact 경로 |
| file `parts[]` | part index |
| bundle `entries[]` | 정규화 entry 경로 |

문자열 배열 정렬은 정규화한 **UTF-8 byte의 ordinal 오름차순**이며 locale과 filesystem
순서에 의존하지 않는다.

> object key 정렬(UTF-16 code unit)과 domain 배열 정렬(UTF-8 byte ordinal)은 **서로 다른
> 비교자**다. BMP 밖 문자(surrogate pair)에서 두 순서가 실제로 갈리므로 한쪽 비교자를
> 다른 쪽에 재사용하면 안 된다. Core는
> `CanonicalJsonWriter`(`StringComparer.Ordinal`)와
> `Utf8OrdinalStringComparer`로 분리해 둔다.

### encoding

| 값 | 표현 |
| --- | --- |
| 모든 SHA-256 (`manifestHash`, `fileHash`, `artifactHash`, `partHash`, `bundleHash`) | lowercase hexadecimal 64자 |
| `dataVersion` | `v1-` + lowercase hex64 |
| Ed25519 public key (32 byte) | padding 없는 base64url |
| Ed25519 signature (64 byte) | padding 없는 base64url (86자) |
| `keyId` | `ed25519-` + public key SHA-256의 lowercase hex64 |

## golden vector

[`tests/fixtures/golden-vectors/`](../../tests/fixtures/golden-vectors)에 구현이 달라도
같은 값이 나와야 하는 공용 vector가 있다. `single-file`, `multipart-file`,
`bundle-entry`, `mixed` 네 시나리오가 각각 자기 완결적인 디렉터리다.

| 파일 | 내용 |
| --- | --- |
| `manifest.canonical.json` | canonical release manifest의 **정확한 byte** (BOM·trailing newline 없음) |
| `manifest-hash.txt` | 기대 `manifestHash` (hex64, trailing newline 없음) |
| `identity.canonical.json` | `dataVersion` hash 입력이 되는 canonical identity projection byte |
| `data-version.txt` | 기대 `dataVersion` |
| `public-key.bin` | raw 32-byte 테스트 Ed25519 public key |
| `key-id.txt` | 기대 `keyId` |
| `manifest-sig.canonical.json` | 그 vector의 `manifest.canonical.json`에 대한 canonical `manifest.sig` byte |

### 다른 구현을 검증할 때

C#이 아닌 구현(다른 언어의 publisher, 자체 검증 도구 등)을 만든다면 이 vector로
확인한다.

1. `manifest.canonical.json`을 파싱한 뒤 자신의 canonicalizer로 다시 직렬화해 **원본
   byte와 정확히 같은지** 비교한다.
2. 그 byte의 SHA-256이 `manifest-hash.txt`와 같은지 확인한다.
3. identity projection을 계산해 `identity.canonical.json` byte와 비교하고, SHA-256에
   `v1-` 접두사를 붙인 값이 `data-version.txt`와 같은지 확인한다.
4. `public-key.bin`의 SHA-256으로 `keyId`를 파생해 `key-id.txt`와 비교한다.
5. `manifest-sig.canonical.json`의 signature를 `public-key.bin`으로
   `manifest.canonical.json` byte에 대해 검증한다.

### 교차 검증 도구

`tools/generate_golden_vectors.py`는 RFC 8785 canonicalization을 Python으로 처음부터
다시 구현(`tools/jcs.py`)하고, 서명도 BouncyCastle이 아닌 OpenSSL 기반 Python
`cryptography`로 수행한다. fixture가 그것을 검증하려는 C# 구현에만 의존해 자기
검증되는 것을 막기 위해 일부러 독립적으로 만들었다.

```bash
python3 tests/fixtures/golden-vectors/tools/generate_golden_vectors.py           # 재생성
python3 tests/fixtures/golden-vectors/tools/generate_golden_vectors.py --check   # 검증만
```

`pip install cryptography`가 필요하며 `dotnet test`나 CI가 자동으로 돌리지 않는다.
vector의 입력 모델을 바꾼 뒤 직접 실행한다.

### RFC 8032 known-answer test

Ed25519 구현은 golden vector와 별개로
[RFC 8032](https://www.rfc-editor.org/rfc/rfc8032.html) Section 7.1의 모든 Ed25519
known-answer vector를 통과해야 한다. 이 저장소에서는 `GamePatchKit.Core.Tests`가 두
가지를 함께 실행한다.

```bash
dotnet test tests/GamePatchKit.Core.Tests
```

두 검증의 역할이 다르다. RFC 8032 KAT는 **Ed25519 primitive 자체**가 표준과 일치하는지
보고, GamePatchKit signed golden vector는 **무엇에 서명하는지**(= `manifestHash`가
가리키는 바로 그 canonical manifest byte)와 `keyId` 파생이 일치하는지 본다. 둘 다
통과해야 다른 구현과 서명을 주고받을 수 있다.

## Packager API와 CLI의 책임 경계

verify와 sign 규칙은 Packager가 소유하고, CLI는 그것을 부르기만 한다. 같은 fixture를
API와 CLI로 실행하면 같은 결과와 같은 오류 코드가 나온다.

| 책임 | Packager API | CLI |
| --- | --- | --- |
| schema → 의미 → payload → signature 검증 순서 | `ReleaseVerifier.VerifyAsync` | 호출만 |
| canonical `manifest.sig` 생성 | `ReleaseSigner.SignAsync` | 호출만 |
| 키 파일·환경 변수 로드 | 하지 않는다 | 담당 |
| CLI option 해석, exit code, 출력 형식 | 하지 않는다 | 담당 |

- verify API는 manifest byte, artifact stream provider, 선택적 signature·신뢰 키를
  입력받아 typed report와 공통 오류를 반환한다. console·환경 변수·CLI option을 읽지
  않는다.
- sign API는 canonical manifest와 `manifestHash`를 검증한 뒤, 주입된 signing key handle
  또는 signer로 `manifest.sig` 모델과 canonical byte를 만든다.
- manifest 문서 자체만 확인하면 되는 diff·download plan·retained inventory는
  `ReleaseManifestReader`를 쓴다.
- CLI가 아닌 host도 같은 API를 쓰며 verify·sign 규칙을 복제하지 않는다.

자세한 signature는 [file package 생성 계약](../contracts/file-packager.md)을 참조한다.

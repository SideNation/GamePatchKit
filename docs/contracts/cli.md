# `gpk` CLI

## 개요

`GamePatchKit.Cli`는 Packager와 Core 기능의 명령행 진입점이다. 설정 파일 로드, 키 로드,
출력 형식과 exit code 변환만 담당하고 verify·sign 규칙은 Packager API를 그대로 쓴다.
같은 fixture를 API와 CLI로 실행하면 같은 결과와 같은 오류 코드가 나온다.

## exit code

| code | 의미 | 예 |
| --- | --- | --- |
| `0` | 성공 | compact no-op을 포함한 모든 정상 종료 |
| `1` | 입력 오류 | 잘못된 인자, `gamepatchkit.yml` 규칙 위반, schema·모델 오류, 없는 `manifestHash`, 잘못된 서명 키 |
| `2` | 무결성 오류 | `manifestHash` 불일치, 손상된 artifact object, 불변 경로 byte 충돌 |
| `3` | 실행 실패 | 실행 중 source 변경, codec 누락, I/O 실패, 취소 |

분류는 **첫 오류**를 기준으로 한다. 파이프라인이 가장 구체적인 검사부터 수행하므로 첫
오류가 실행이 멈춘 이유이고, 함께 오는 나머지는 같은 종류의 다른 사례다.

`manifest.*`와 `manifest-signature.*` 오류는 무결성, `yaml.*`·`package-config.*`·
`glob.*`·`path.*` 오류는 입력 오류다.

## 출력

`--json`을 주면 canonical JSON envelope 한 줄을 stdout에 출력한다. key는 정렬되고 불필요한
공백이 없으므로 값이 바뀌지 않은 두 실행의 출력은 byte 단위로 같다.

```json
{"command":"package","dryRun":false,"exitCode":0,"ok":true,"result":{ ... }}
```

실패하면 `result` 대신 `errors`가 들어간다.

```json
{"command":"verify","dryRun":false,"errors":[{"code":"packager.artifact-corrupted","message":"...","packageId":"...","stage":"package-verify"}],"exitCode":2,"ok":false}
```

`--json`이 없으면 성공 결과는 stdout에 사람이 읽는 형식으로, 오류는 stderr에
`error: [stage/code] message` 형식으로 나간다.

`durationsMs`는 단계별 실행 시간(정수 밀리초)이다. package와 diff 결과는 전체·group별
파일 수·byte, 생성·재사용 artifact 수·byte, 추가·변경·삭제·group 이동 수와 예상 다운로드를
함께 포함한다.

개인키 내용은 결과·로그·오류 메시지 어디에도 기록하지 않는다. 키는 파생된
`ed25519-<hex64>` key ID로만 나타난다.

## 명령

이전 release, compact source와 retained release는 모두 `manifestHash`로 지정한다. 이미
같은 output tree에 게시된 release이므로 경로가 따로 필요하지 않다.

### `package`

```bash
gpk package --config gamepatchkit.yml --output-root publish [--previous <manifestHash>] \
  [--source-revision <text>] [--compressed-manifest] [--dry-run] [--json]
```

`--config`의 기본값은 `gamepatchkit.yml`이다. `--previous`를 주면 incremental package다.

### `diff`

```bash
gpk diff --output-root publish --package-id <id> --from <manifestHash> --to <manifestHash> [--json]
```

논리 파일 차이(`files`)와 물리 object 차이(`artifacts`)를 따로 보고하고,
`estimatedUpdate`에 `from`을 완전히 설치한 client의 갱신 비용을 계산한다.

### `verify`

```bash
gpk verify --output-root publish --package-id <id> --manifest-hash <hex64> [--json]
```

schema → Core 의미·참조 무결성 → 실제 artifact byte → signature document 순서로 검증한다.
`signature.state`는 다음 중 하나다.

| 값 | 의미 |
| --- | --- |
| `absent` | `manifest.sig`가 없다 |
| `present` | `manifest.sig`가 schema·모델·canonical byte 검증을 통과했다. signature byte는 어떤 key와도 대조하지 않았다 |
| `verified` | 주입된 verifier가 서명을 검증했다(11 단계) |

**`present`는 서명 증거가 아니다.** 형식만 맞는 `keyId`와 아무 64 byte면 이 상태에 도달하므로
"서명된 release만 배포" 게이트로 쓸 수 없다. 07의 CLI가 서명 필수 option을 제공하지 않는
이유도 같다. 신뢰 키 집합과 signature primitive 검증이 들어오는 11 단계에서 `verified`를
요구하는 option과 함께 추가한다. Packager API의
`ReleaseVerifyRequest.RequireSignature`는 `SignatureVerifier` 없이 지정하면 거부한다.

### `compact`

```bash
gpk compact --config gamepatchkit.yml --output-root publish --source <manifestHash> \
  --group <name> [--group <name> ...] [--retained <manifestHash> ...] \
  [--compressed-manifest] [--dry-run] [--json]
```

`--group`은 최소 하나가 필요하다. `--retained`는 source 외에 아직 보관 중인 release를 모두
지정한다. 자동으로 수집하지 않는 이유는 디렉터리 목록이 보관 중인 release의 object와
잔여물을 구분할 수 없기 때문이다. 빠뜨리면 그 release의 part 경로를 덮어쓰는 candidate가
통과할 수 있다.

`changed`가 `false`면 성공 no-op이며 `identity`는 source release의 세 값 그대로다.

### `plan-download`

```bash
gpk plan-download --output-root publish --package-id <id> --manifest-hash <hex64> \
  (--required-only | --group <name> [--group <name> ...]) \
  [--install-root <dir>] [--cache-root <dir>] [--json]
```

group 집합은 절대 암묵적으로 정해지지 않는다. 최초 설치와 전역 갱신은 `--required-only`,
optional group 설치는 `--group`을 쓴다. 둘을 함께 주거나 둘 다 생략하면 입력 오류다.

`--install-root`와 `--cache-root`는 실제 byte를 hash해 상태를 만든다. 없는 경로는 빈
설치·빈 cache로 취급한다.

### `sign`

```bash
gpk sign --output-root publish --package-id <id> --manifest-hash <hex64> \
  (--key-file <path> | --key-env <name>) [--dry-run] [--json]
```

개인키는 padding 없는 base64url 32 byte 텍스트다. 앞뒤 공백은 무시한다. 같은 manifest를
같은 key로 다시 서명하면 기존 `manifest.sig`를 검증한 뒤 재사용하고 `created`가 `false`가
된다. 다른 key ID나 signature byte로 교체하려 하면 불변 경로 충돌로 실패한다.

## `--dry-run`

package, compact, sign에서 지원한다. 계산을 생략하지 않는다. artifact를 만들어야 hash를 알
수 있으므로 실제 identity를 그대로 보고하고 게시만 하지 않으며, output tree는 실행 전과
byte 단위로 같다. 게시 후 수행하는 published byte 재검증은 게시가 없으므로 건너뛴다.

## `gamepatchkit.yml`

문서 규칙은 [gamepatchkit.yml 입력 계약](gamepatchkit-yml.md)을 따른다. CLI가 YAML 문법과
문서 규칙을, Packager의 `PackageConfigReader`가 schema와 모델 검증을 담당한다.

## 관련 파일

- [Program.cs](../../src/GamePatchKit.Cli/Program.cs)
- [ExitCode.cs](../../src/GamePatchKit.Cli/ExitCode.cs)
- [YamlConfigDocument.cs](../../src/GamePatchKit.Cli/Configuration/YamlConfigDocument.cs)
- [ReleaseVerifier.cs](../../src/GamePatchKit.Packager/ReleaseVerifier.cs)
- [ReleaseSigner.cs](../../src/GamePatchKit.Packager/ReleaseSigner.cs)

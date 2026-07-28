# GamePatchKit

여러 게임이 엔진과 Storage 제품에 종속되지 않고 게임 데이터를
패키징·배포·다운로드·검증·활성화할 수 있게 하는 범용 도구 모음.

같은 입력과 설정이면 항상 같은 artifact와 manifest byte를 만들고, artifact는 콘텐츠
해시 경로에 한 번만 쓰며, release manifest는 patch chain이 아니라 그 release의 **최종
파일 상태 전체**를 기록한다. 그래서 client는 이전에 어떤 release에 있었든 목표 release
까지 필요한 것만 계산해 받을 수 있다.

- [빠른 시작](#빠른-시작) · [핵심 개념](#핵심-개념) · [CLI](#cli) · [문서](#문서)
- 바로 돌려보려면: `./samples/quickstart/run.sh` ([샘플 설명](samples/quickstart/README.md))
- 바로 붙이려면: [5분 QuickStart 가이드](docs/guide/quickstart.md) · Unity라면
  [Unity 10분 QuickStart](docs/guide/unity-quickstart.md)
- PRD: [docs/prd/game-patch-kit-prd.md](docs/prd/game-patch-kit-prd.md)
- 개발 계획: [docs/plan/README.md](docs/plan/README.md)

## 구성 요소

| 이름 | 형태 | 역할 |
| --- | --- | --- |
| `GamePatchKit.Core` | NuGet (`netstandard2.1`) | identity·canonical JSON·glob·diff·download plan. filesystem/HTTP/엔진 비의존 |
| `GamePatchKit.Runtime` | NuGet (`netstandard2.1`) | 검증·다운로드·staging·원자적 활성화 상태 머신 |
| `GamePatchKit.Compression.NativeCompressions` | NuGet (`netstandard2.1`) | 기본 zstd codec |
| `GamePatchKit.DotNet` | NuGet (`net10.0`) | `HttpClient`·filesystem Runtime adapter |
| `gpk` (`GamePatchKit.Cli`) | .NET tool (`net10.0`) | package·diff·verify·compact·plan-download·sign |

Packager는 `Core`로만 Runtime과 연결되고 서로를 참조하지 않는다. Unity를 포함한 외부
host는 Runtime interface를 직접 구현한다.

## 설치

### `gpk` CLI

```bash
dotnet tool install --global GamePatchKit.Cli
gpk --help
```

> 아직 nuget.org에 게시하기 전이라면 저장소를 clone한 뒤 로컬 패키지로 설치한다.
>
> ```bash
> ./build.sh Pack --version 0.1.0
> dotnet tool install --global --add-source ./artifacts GamePatchKit.Cli --version 0.1.0
> ```
>
> 설치 없이 쓰려면 `dotnet run --project src/GamePatchKit.Cli -- <command> ...`도 된다.

### 라이브러리

애플리케이션이 데이터를 받아 설치하는 쪽이라면:

```bash
dotnet add package GamePatchKit.DotNet     # Core·Runtime·zstd codec을 함께 가져온다
```

Unity처럼 자체 adapter를 구현하는 host는 `GamePatchKit.Core`와 `GamePatchKit.Runtime`만
소비한다.

## 빠른 시작

최초 package → publish tree 확인 → 검증 → 서명 → Runtime 다운로드·활성화까지의
전체 흐름이다. 아래 1~7단계를 그대로 자동화한 것이
[`./samples/quickstart/run.sh`](samples/quickstart)이니 먼저 한 번 돌려 봐도 된다.

### 1. source tree와 설정

```bash
mkdir -p demo/game-data/core demo/game-data/maps && cd demo
printf '{"volume":0.8}\n'      > game-data/core/config.json
printf '{"title":"Sample"}\n'  > game-data/core/strings.json
printf 'forest-map-payload\n'  > game-data/maps/forest.dat
printf 'desert-map-payload\n'  > game-data/maps/desert.dat
```

`gamepatchkit.yml`:

```yaml
schemaVersion: 1
packageId: sample-game-client-data
inputRoot: ./game-data
include:
  - "**/*"
exclude:
  - "**/*.tmp"
compression:
  kind: zstd
  codecId: zstd
groups:
  - name: core
    include:
      - "core/**/*"
    artifactMode: file
    required: true
  - name: maps
    include:
      - "maps/**/*"
    artifactMode: bundle
    required: false
```

- `include`는 비울 수 없는 allowlist이고 `exclude`가 항상 우선한다.
- `core`는 `required: true`라 활성화 전에 반드시 준비되고, `maps`는 필요할 때 따로
  받는 optional group이다.
- zstd codec이 없는 host(예: 현재 Unity 지원 범위, iOS IL2CPP)를 대상으로 하면
  `compression: {kind: none}`을 쓴다.

전체 필드와 glob 규칙은 [package 설정과 파일 선택](docs/guide/package-config.md)에 있다.

### 2. package 생성

```bash
gpk package --config gamepatchkit.yml --output-root publish --json
```

```json
{"command":"package","dryRun":false,"exitCode":0,"ok":true,"result":{"identity":{"compactVersion":0,"dataVersion":"v1-...","manifestHash":"..."}, ...}}
```

`result.identity.manifestHash`가 이 release의 불변 식별자다. 이후 명령에 그대로 쓴다.

```bash
MANIFEST_HASH=$(gpk package --config gamepatchkit.yml --output-root publish --json \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["identity"]["manifestHash"])')
```

> 같은 입력으로 다시 실행해도 기존 결과를 검증하고 재사용하므로 identity는 그대로다.

### 3. publish tree

```text
publish/sample-game-client-data/
├── artifacts/
│   ├── files/<artifactHash>/content.zst      # core group (artifactMode: file)
│   └── bundles/maps/<bundleHash>.tar.zst     # maps group (artifactMode: bundle)
└── manifests/<manifestHash>/
    └── manifest.json                          # canonical JSON, BOM·trailing newline 없음
```

hash 경로는 불변이다. 같은 경로에 다른 byte를 쓰려는 시도는 실패한다.

### 4. 검증

```bash
gpk verify --output-root publish --package-id sample-game-client-data \
  --manifest-hash "$MANIFEST_HASH" --json
```

schema → Core 의미·참조 무결성 → 실제 artifact byte → signature 문서 순서로 검사한다.

### 5. 서명 (선택)

```bash
# 32 byte Ed25519 개인키를 padding 없는 base64url로 (예시 생성)
export GPK_SIGNING_KEY=$(python3 -c "import os,base64; print(base64.urlsafe_b64encode(os.urandom(32)).decode().rstrip('='))")

gpk sign --output-root publish --package-id sample-game-client-data \
  --manifest-hash "$MANIFEST_HASH" --key-env GPK_SIGNING_KEY --json
```

`manifests/<manifestHash>/manifest.sig`가 만들어진다. 이 파일은 **불변**이라 같은 key로
다시 서명하면 기존 byte를 재사용하고, 다른 key로 교체하려 하면 실패한다. 검증할 때
`--trusted-key <public key>`를 주지 않으면 `signature.state`가 `present`에 그치며 이는
서명 증거가 아니다. 운영 규칙은 [publish와 서명 운영](docs/guide/publishing.md)을 참조한다.

### 6. Runtime에서 다운로드·활성화

publish tree를 HTTP로 서빙한다.

```bash
python3 -m http.server 8080 --directory publish
```

client 애플리케이션:

```csharp
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

using var httpClient = new HttpClient { BaseAddress = new Uri("http://localhost:8080/") };

var runtime = new PackageRuntime(
    new HttpArtifactTransport(httpClient),
    new FileSystemRuntimeStorage(runtimeRoot),
    DefaultCompressionCodecs.Create());

// packageId·dataVersion·manifestHash는 신뢰하는 host나 서버가 알려준다.
var target = new TargetManifestReference(packageId, dataVersion, manifestHash);

PackageState state = await runtime.InstallOrUpdateAsync(target);
// state.Active.DataVersion 이 활성화됐고, required group("core")만 ready다.

// optional group은 필요한 시점에 따로 받는다. active pointer는 그대로 유지된다.
state = await runtime.InstallOptionalGroupsAsync(packageId, new[] { "maps" });
```

`BaseAddress`는 반드시 `/`로 끝나야 한다. 설치된 파일은
`<runtimeRoot>/packages/<packageId>/installs/<installationKey>/` 아래에 있고, 활성 상태는
`state/package-state.json`에 원자적으로 기록된다.

> **Runtime은 최신 release를 스스로 고르지 않는다.** 어떤 release가 live인지는 host나
> 서버가 결정해 target manifest reference로 전달한다.

### 7. incremental release와 compact

bundle group의 파일을 하나 바꾸고 이전 release를 기준으로 다시 package한다.

```bash
printf 'forest-map-payload-v2\n' > game-data/maps/forest.dat

gpk package --config gamepatchkit.yml --output-root publish \
  --previous "$MANIFEST_HASH" --json
```

바뀌지 않은 `core` artifact는 재사용된다(`reusedFileArtifactCount: 2`). bundle은 entry
하나만 바뀌어도 미참조 entry를 남길 수 없으므로 그 group의 현재 파일 전체가 개별 file
override로 전환된다(`createdFileArtifactCount: 2`). **기존 bundle은 수정하지도, 같은
경로에 다시 올리지도 않는다.**

누적된 override를 새 baseline으로 합치려면 compact한다.

```bash
gpk compact --config gamepatchkit.yml --output-root publish \
  --source <새 manifestHash> --group maps --retained "$MANIFEST_HASH" --json
```

```json
{"changed":true,"identity":{"compactVersion":1,"dataVersion":"v1-aaef...","manifestHash":"2b36..."}, ...}
```

**`dataVersion`은 그대로, `compactVersion`은 0 → 1, `manifestHash`는 바뀐다.** 논리
데이터는 그대로이고 물리 배치만 바뀌었기 때문이며, 그래서 이미 설치된 client는 경로와
`fileHash`가 같은 파일을 다시 받지 않는다.

`changed`가 `false`면 물리 배치가 같았다는 뜻의 성공 no-op이고 기존 identity 세 값을
그대로 돌려준다. `--retained`에는 아직 보관 중인 과거 release를 **모두** 넘겨야 한다.
디렉터리 목록만으로는 보관 중인 release의 object와 잔여물을 구분할 수 없어 자동으로
수집하지 않으며, 빠뜨리면 그 release의 part 경로를 덮어쓰는 candidate가 통과할 수 있다.

## 핵심 개념

| 용어 | 의미 |
| --- | --- |
| package | 같은 공개 정책과 버전 계약을 공유하는 배포 단위. `packageId`로 식별 |
| group | 다운로드 시점·변경 주기·소비 목적이 같은 파일 집합. **보안 경계가 아니다** |
| file artifact | 원본 파일 하나를 표현하는 불변 object 또는 part 집합 |
| bundle artifact | 같은 group의 여러 파일을 묶은 불변 deterministic PAX tar |
| compact | 현재 최종 상태로 bundle group의 새 baseline을 만드는 작업 |
| `dataVersion` | 최종 **논리** 파일 상태의 digest (`v1-` + hex64) |
| `compactVersion` | **물리** packaging 세대. 최초 `0`, compact로 배치가 바뀔 때만 +1 |
| `manifestHash` | canonical manifest 원본 byte의 SHA-256. 불변 manifest 식별자 |
| target manifest reference | host/서버가 Runtime에 주는 `packageId`·`dataVersion`·`manifestHash` |
| `PackageState` | active pointer와 group별 설치 상태를 원자적으로 기록하는 로컬 상태 |
| activation batch | 한 요청의 target group 집합을 하나의 state revision으로 활성화하는 단위 |

핵심 규칙 몇 가지:

- **공개 범위가 다르면 group이 아니라 package를 분리한다.**
- **compression은 새 artifact를 만들 때만 적용된다.** 설정을 바꿔도 이전 artifact는 그대로
  재사용되므로, Runtime은 목표 manifest가 참조하는 모든 codec을 계속 지원해야 한다.
- **artifact 위치가 달라져도 경로와 `fileHash`가 같으면 다시 다운로드하지 않는다.**
- **실패·취소는 이전 release를 건드리지 않는다.** state 교체 전 실패는 이전 state와
  installation을 그대로 유지한다.

## CLI

| 명령 | 책임 |
| --- | --- |
| `package` | 최초 또는 incremental release 생성 |
| `diff` | 두 release의 논리 파일 차이와 물리 artifact 차이 |
| `verify` | source·artifact·manifest·signature 검증 |
| `compact` | 선택한 bundle group의 새 baseline 생성 |
| `plan-download` | 로컬 상태에서 목표 release까지 필요한 artifact 계산 |
| `sign` | canonical manifest signature 생성 |

이전 release, compact source, retained release는 모두 **`manifestHash`로 지정한다.** 이미
같은 output tree에 게시된 release라 경로가 따로 필요 없다.

```bash
gpk package        --config gamepatchkit.yml --output-root publish [--previous <hash>] [--source-revision <text>] [--compressed-manifest] [--dry-run] [--json]
gpk diff           --output-root publish --package-id <id> --from <hash> --to <hash> [--json]
gpk verify         --output-root publish --package-id <id> --manifest-hash <hash> [--trusted-key <key>]... [--require-signature] [--json]
gpk compact        --config gamepatchkit.yml --output-root publish --source <hash> --group <name>... [--retained <hash>]... [--compressed-manifest] [--dry-run] [--json]
gpk plan-download  --output-root publish --package-id <id> --manifest-hash <hash> (--required-only | --group <name>...) [--install-root <dir>] [--cache-root <dir>] [--json]
gpk sign           --output-root publish --package-id <id> --manifest-hash <hash> (--key-file <path> | --key-env <name>) [--dry-run] [--json]
```

### exit code

| code | 의미 | 예 |
| --- | --- | --- |
| `0` | 성공 | compact no-op을 포함한 모든 정상 종료 |
| `1` | 입력 오류 | 잘못된 인자, `gamepatchkit.yml` 규칙 위반, schema·모델 오류, 없는 `manifestHash`, 잘못된 키 |
| `2` | 무결성 오류 | `manifestHash` 불일치, 손상된 artifact, 불변 경로 byte 충돌 |
| `3` | 실행 실패 | 실행 중 source 변경, codec 누락, I/O 실패, 취소 |

분류는 **첫 오류**를 기준으로 한다. `manifest.*`·`manifest-signature.*`는 무결성,
`yaml.*`·`package-config.*`·`glob.*`·`path.*`는 입력 오류다.

### `--json`

canonical JSON envelope 한 줄을 stdout에 출력한다. key가 정렬되고 불필요한 공백이 없어
값이 바뀌지 않은 두 실행의 출력은 **byte 단위로 같다.**

```json
{"command":"package","dryRun":false,"exitCode":0,"ok":true,"result":{ ... }}
{"command":"verify","dryRun":false,"errors":[{"code":"packager.artifact-corrupted","message":"...","stage":"package-verify"}],"exitCode":2,"ok":false}
```

`package`와 `diff` 결과에는 전체·group별 파일 수·byte, 생성·재사용 artifact 수·byte,
추가·변경·삭제·group 이동 수, 예상 다운로드와 단계별 실행 시간(`durationsMs`)이 들어 있다.
**개인키 내용은 결과·로그·오류 어디에도 기록하지 않는다.**

`--json`이 없으면 성공 결과는 stdout에 사람이 읽는 형식으로, 오류는 stderr에
`error: [stage/code] message` 형식으로 나간다.

### `--dry-run`

`package`, `compact`, `sign`에서 지원한다. **계산을 생략하지 않는다.** artifact를 만들어야
hash를 알 수 있으므로 실제 identity를 그대로 보고하고 게시만 하지 않으며, output tree는
실행 전과 byte 단위로 같다.

전체 계약은 [`gpk` CLI](docs/contracts/cli.md)에 있다.

## 문서

### 사용 가이드

| 문서 | 내용 |
| --- | --- |
| [5분 QuickStart](docs/guide/quickstart.md) | 최소 설정으로 바로 붙이는 절차, 체크리스트, 자주 막히는 곳 |
| [package 설정과 파일 선택](docs/guide/package-config.md) | `gamepatchkit.yml` 전체 필드, YAML 제약, v1 glob dialect, 선택 순서, group 설계, compression 정책 |
| [release identity와 canonical JSON](docs/guide/identity.md) | `dataVersion`·`compactVersion`·`manifestHash`, manifest union과 참조 무결성, RFC 8785 JCS, golden vector |
| [Runtime 통합 가이드](docs/guide/runtime-integration.md) | DotNet adapter, 외부 host 구현, `PackageState`, activation batch, codec 제약 |
| [Unity 10분 QuickStart](docs/guide/unity-quickstart.md) | Unity에서 로컬 release를 받아 설치하는 최단 경로 |
| [Unity 통합 가이드](docs/guide/unity.md) | managed plugin 준비, Unity 프로젝트에 붙이는 절차, 지원 범위, IL2CPP 주의사항 |
| [publish와 서명 운영](docs/guide/publishing.md) | publisher 순서, target 선택 책임 경계, Ed25519 서명과 key rotation |
| [배포 산출물과 버전 정책](docs/guide/distribution.md) | 패키징 메타데이터, JSON Schema 버전 정책, conformance suite, 성능 gate |

### 샘플

| 샘플 | 내용 |
| --- | --- |
| [quickstart](samples/quickstart) | `./samples/quickstart/run.sh` 한 번으로 package → verify → sign → Runtime 설치 → incremental → compact 전체 실행 |
| [unity-quickstart](samples/unity-quickstart) | `./samples/unity-quickstart/serve.sh`로 `compression: none` release를 만들고 Unity client에 로컬 서빙 |

### 구현 계약

| 문서 | 대상 |
| --- | --- |
| [`gamepatchkit.yml` 입력 계약](docs/contracts/gamepatchkit-yml.md) | YAML 파싱 규칙과 fixture |
| [file package 생성](docs/contracts/file-packager.md) | `FilePackageBuilder`, verify·sign API, 오류 코드 |
| [deterministic bundle과 compact](docs/contracts/bundle-compact.md) | PAX byte 계약, `BundleCompactor` |
| [`gpk` CLI](docs/contracts/cli.md) | 명령별 인자, exit code, 출력 |
| [Runtime 상태 머신](docs/contracts/runtime.md) | 검증 순서, adapter contract, 복구 전이 |
| [DotNet adapter](docs/contracts/dotnet-adapter.md) | HTTP 실패 분류, 원자적 교체, writer lock |
| [Unity Runtime adapter](docs/contracts/unity-adapter.md) | `UnityWebRequest`·`persistentDataPath` 구현과 지원 범위 |
| [zstd codec](docs/contracts/zstd-codec.md) | 고정 압축 계약과 지원 platform |
| [Adapter conformance suite](docs/contracts/adapter-conformance.md) | 외부 adapter 검증 방법 |
| [성능·메모리 검증 harness](docs/perf/README.md) | 측정 방법과 blocking gate 실행 절차 |

## 개발

```bash
./build.sh                                # Compile (기본)
./build.sh Test                           # Category!=Performance 전체 테스트
./build.sh Performance                    # 성능 테스트만
./build.sh Pack --version 1.0.0           # artifacts/에 nupkg 생성
./build.sh Push --nuget-api-key <KEY> --version 1.0.0
```

`--version` 없이 `Pack`/`Push`를 실행하면 `Directory.Build.props`의 patch version을 1
올리고 파일을 저장한다. `Push`는 내부적으로 `Pack`에 의존하므로 둘을 따로 실행하면
version이 두 번 증가한다.

> `dotnet test GamePatchKit.sln`을 필터 없이 실행하면 성능 테스트도 함께 돈다.
> `Category!=Performance` 필터는 `./build.sh Test`에만 있다.

Unity adapter:

```bash
./unity/GamePatchKit.Unity/scripts/prepare.sh
./unity/GamePatchKit.Unity/scripts/test.sh
./unity/GamePatchKit.Unity/scripts/build-macos-il2cpp.sh
```

## 라이선스

MIT. [LICENSE](LICENSE)를 참조한다.

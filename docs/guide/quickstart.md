# 5분 QuickStart

GamePatchKit을 처음 붙일 때 필요한 최소한만 모았다. 개념 설명은 링크로 미루고 복사해서
바로 쓸 수 있는 것만 남겼다.

## 0. 먼저 한 번 돌려보기

```bash
git clone <this repo> && cd GamePatchKit
./samples/quickstart/run.sh
```

package → verify → sign → Runtime 설치 → incremental → compact가 한 번에 돈다. 무엇이
왜 그렇게 나오는지는 [샘플 README](../../samples/quickstart/README.md)에 있다.

## 1. gpk 설치

```bash
dotnet tool install --global GamePatchKit.Cli
gpk --help
```

아직 nuget.org에 게시하기 전이라면:

```bash
./build.sh Pack --version 0.1.0
dotnet tool install --global --add-source ./artifacts GamePatchKit.Cli --version 0.1.0
```

## 2. 설정 파일 만들기

게임 데이터 root 옆에 `gamepatchkit.yml`을 둔다. 최소 형태는 이것뿐이다.

```yaml
schemaVersion: 1
packageId: my-game-client-data   # 소문자 kebab-case
inputRoot: ./game-data
include:
  - "**/*"
compression:
  kind: none
groups: []
```

group을 나눌 준비가 됐다면:

```yaml
groups:
  - name: core                   # 로그인·첫 화면에 필요한 최소 데이터
    include: ["core/**/*"]
    artifactMode: file           # 자주 바뀌는 데이터는 개별 파일
    required: true               # 활성화 전에 반드시 준비
  - name: maps                   # 필요할 때 받는 큰 정적 콘텐츠
    include: ["maps/**/*"]
    artifactMode: bundle         # 작은 파일이 많으면 묶는다
    required: false
```

압축을 켜려면 `compression`을 바꾼다. **desktop(.NET) client만 대상일 때 켠다.**

```yaml
compression:
  kind: zstd
  codecId: zstd
```

체크리스트:

- [ ] `packageId`가 소문자 kebab-case인가
- [ ] `include`가 비어 있지 않은가 (비울 수 없다)
- [ ] 공개 범위가 다른 데이터를 같은 package에 넣지 않았는가 (**group이 아니라 package를
      나눈다**)
- [ ] 모든 group에 `required`를 명시했는가 (기본값이 없다)

전체 필드와 glob 규칙: [package 설정과 파일 선택](package-config.md)

## 3. release 만들기

```bash
gpk package --config gamepatchkit.yml --output-root publish --json
```

출력의 `result.identity`에 세 값이 들어 있다. **`manifestHash`가 이 release의 불변
식별자**이고 이후 모든 명령이 이 값을 받는다.

```bash
MANIFEST_HASH=$(gpk package --config gamepatchkit.yml --output-root publish --json \
  | python3 -c 'import json,sys; print(json.load(sys.stdin)["result"]["identity"]["manifestHash"])')

gpk verify --output-root publish --package-id my-game-client-data \
  --manifest-hash "$MANIFEST_HASH" --json
```

## 4. 올리기

`publish/` 아래 tree를 CDN이나 오브젝트 스토리지에 그대로 올린다. 순서가 중요하다.

1. artifact 업로드
2. 원격 크기·SHA-256 검증
3. manifest와 signature 업로드
4. 원격에서 재검증

manifest가 곧 "이 artifact들이 전부 있다"는 선언이라, 순서를 뒤집으면 client가 아직
없는 파일을 가리키는 manifest를 받을 수 있다. hash 경로는 불변이므로 **cache를 길게
잡아도 안전하다.**

자세히: [publish와 서명 운영](publishing.md)

## 5. client 붙이기

```bash
dotnet add package GamePatchKit.DotNet
```

```csharp
using GamePatchKit.DotNet;
using GamePatchKit.Runtime;

using var httpClient = new HttpClient { BaseAddress = new Uri("https://cdn.example.com/gpk/") };

var runtime = new PackageRuntime(
    new HttpArtifactTransport(httpClient),
    new FileSystemRuntimeStorage(runtimeRoot),
    DefaultCompressionCodecs.Create());

// 세 값은 서버가 알려준다. Runtime은 최신 release를 스스로 고르지 않는다.
var target = new TargetManifestReference(packageId, dataVersion, manifestHash);

PackageState state = await runtime.InstallOrUpdateAsync(target);              // required group만
state = await runtime.InstallOptionalGroupsAsync(packageId, new[] { "maps" }); // 필요한 시점에
```

체크리스트:

- [ ] `BaseAddress`가 `/`로 끝나는가
- [ ] `packageId`·`dataVersion`·`manifestHash`를 서버에서 받아오는가
- [ ] `runtimeRoot`가 앱이 쓸 수 있는 영구 저장 경로인가
- [ ] 앱 종료·화면 전환에 `CancellationToken`을 연결했는가

자세히: [Runtime 통합 가이드](runtime-integration.md) · Unity라면 [Unity 통합
가이드](unity.md)

## 6. 데이터를 바꿨을 때

```bash
# 이전 release를 기준으로 incremental
gpk package --config gamepatchkit.yml --output-root publish --previous "$MANIFEST_HASH" --json

# bundle group에 override가 쌓이면 새 baseline으로 합친다
gpk compact --config gamepatchkit.yml --output-root publish \
  --source <새 manifestHash> --group maps --retained "$MANIFEST_HASH" --json
```

`--retained`에는 **아직 보관 중인 과거 release를 모두** 넘긴다. 빠뜨리면 그 release의
object를 덮어쓰는 candidate가 통과할 수 있다.

## 자주 막히는 곳

| 증상 | 원인 | 해결 |
| --- | --- | --- |
| `.DS_Store`·`.vscode/` 같은 숨김 파일이 안 잡힌다 | 의도된 동작. `*`·`**`는 숨김 segment를 암묵적으로 일치시키지 않는다 | 넣고 싶으면 pattern segment를 literal `.`으로 시작한다 (`.vscode/**/*`) |
| `?`·`[]`·`{}` pattern이 거부된다 | v1 glob dialect는 literal·`*`·완전한 segment `**`만 지원 | pattern을 나눠서 여러 개로 적는다 |
| 파일 하나가 두 group에 걸린다 | group `include`가 겹친다 | 겹치지 않게 나눈다. 실패로 중단되는 것이 의도된 동작 |
| exit 1 `packager.source-changed` | 실행 중 source가 바뀌었다 | source에 쓰는 프로세스를 멈추고 재실행 |
| `runtime.missing-compression-codec` | 목표 manifest가 요구하는 codec이 주입되지 않았다 | codec을 주입하거나, 해당 group을 `compression: none`으로 compact해 새 baseline을 만든다 |
| `verify`가 `signature.state: present`만 준다 | `--trusted-key`가 없다 | **`present`는 서명 증거가 아니다.** `--trusted-key`를 준다 |
| `./build.sh Pack`이 `Directory.Build.props`를 수정한다 | `--version` 없이 실행하면 patch version을 자동 증가시킨다 | release에서는 `--version`을 명시한다 |

## 다음에 읽을 것

| 하고 싶은 일 | 문서 |
| --- | --- |
| 설정·glob을 정확히 이해하기 | [package 설정과 파일 선택](package-config.md) |
| 버전 값과 manifest 구조 이해하기 | [release identity와 canonical JSON](identity.md) |
| client 동작·복구 규칙 이해하기 | [Runtime 통합 가이드](runtime-integration.md) |
| 배포·서명 운영하기 | [publish와 서명 운영](publishing.md) |
| Unity에 붙이기 | [Unity 통합 가이드](unity.md) |
| 패키지·schema 버전 정책 | [배포 산출물과 버전 정책](distribution.md) |

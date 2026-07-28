# 13. 문서화·배포 산출물 정리

> PRD 섹션: 배포 산출물, publish와 target manifest 선택, 외부 host 연동, 서명과
> 무결성

## 목표

README와 배포 산출물을 정리해 외부 사용자가 문서만으로 CLI, package 설정,
Runtime 통합, publisher 계약을 적용할 수 있게 한다.

## 선행 단계

01~12

## 문서 구성

항목 수가 많아 [README.md](../../README.md)는 개요·설치·전체 시나리오·CLI 요약·문서
지도를 담고, 세부 계약은 `docs/guide/` 아래로 나눴다. 아래 체크박스의 "→"는 그
항목이 실제로 어디에 있는지를 가리킨다.

| 문서 | 범위 |
| --- | --- |
| [README.md](../../README.md) | 개요, 설치, 빠른 시작(package → publish → verify → sign → Runtime 설치 → incremental·compact), 핵심 개념, CLI 요약, 문서 지도, 빌드 |
| [guide/quickstart.md](../guide/quickstart.md) | 최소 설정으로 바로 붙이는 절차, 단계별 체크리스트, 자주 막히는 곳 |
| [guide/package-config.md](../guide/package-config.md) | `gamepatchkit.yml`, YAML 제약, glob dialect, 선택 순서, group 설계, compression 정책, Core·Packager 책임 경계 |
| [guide/identity.md](../guide/identity.md) | 세 version 값, publish tree, manifest union·참조 무결성·3계층 검증, canonical JSON, golden vector, Packager API 경계 |
| [guide/runtime-integration.md](../guide/runtime-integration.md) | DotNet adapter, 네 상태 개념, activation batch, `package-state.json`, 외부 host 구현, 오류 코드 |
| [guide/unity-quickstart.md](../guide/unity-quickstart.md) | Unity에서 로컬 release를 받아 설치하는 최단 경로, Console 확인, Unity 전용 실패 표 |
| [guide/unity.md](../guide/unity.md) | managed plugin 준비, Unity 프로젝트에 붙이는 절차, 지원 범위 표, IL2CPP·link.xml 주의사항 |
| [guide/publishing.md](../guide/publishing.md) | publisher 순서, immutable cache·rollback, target 선택 책임 경계, 서명 운영·key rotation, 보안 경계 |
| [guide/distribution.md](../guide/distribution.md) | 배포 산출물, 패키징 메타데이터, 빌드·배포 명령, schema 버전 정책, conformance suite, 성능 gate |
| [samples/quickstart](../../samples/quickstart) | 실행 가능한 샘플. `run.sh`가 전체 흐름을 한 번에 돌리고 README가 출력을 해설한다 |
| [samples/unity-quickstart](../../samples/unity-quickstart) | Unity client용 `compression: none` release를 만들고 `serve.sh`가 로컬 HTTP로 서빙한다 |

## 작업 항목

### README

- [x] 개요와 설치: .NET tool `gpk`, NuGet 4종(`Core`, `Runtime`,
      `Compression.NativeCompressions`, `DotNet`) → README "구성 요소"·"설치".
      `gpk`는 이 단계에서 `_build/Build.cs`의 pack 대상에 추가해 실제 배포 산출물이
      됐다.
- [x] `gamepatchkit.yml` 설정: 전체 필드 표, group 설계 규칙, 단일-document·mapping
      root와 금지 YAML 기능, 예시 설정 → guide/package-config.md
- [x] v1 glob dialect: literal·`*`·완전한 segment `**`, `/`·NFC·case-sensitive
      규칙, 지원하지 않는 문법, `**/*.json`의 root 포함 예시 → guide/package-config.md
- [x] include OR → exclude 우선 → group matching 순서, re-include 없음, 숨김
      segment의 명시적 `.` 포함 조건과 editor metadata·임시 파일의 명시적 exclude 예시
      → guide/package-config.md
- [x] `gamepatchkit.yml`(사용자 입력), `package-config.schema.json`(versioned 설정
      계약), release manifest(Packager 생성 canonical JSON)의 역할과 검증 순서
      → guide/package-config.md "세 가지 계약의 역할"
- [x] CLI 명령별 사용법, exit code 표, `--json` 출력, `--dry-run` → README "CLI"
      (전체 계약은 contracts/cli.md)
- [x] `dataVersion`·`compactVersion`·`manifestHash`의 역할과 package·incremental·
      compact 시 변화 규칙, 물리 배치가 같은 compact의 성공 no-op·기존 identity 재사용
      → guide/identity.md
- [x] Packager streaming verify·sign API의 입력·typed result·오류·cancellation과 CLI가
      키·option·출력 adapter만 담당하는 책임 경계 → guide/identity.md "Packager API와
      CLI의 책임 경계"
- [x] release manifest의 file single·parts·bundle discriminated union, reference
      integrity와 schema → Core 의미 검증 → payload 검증 계층 → guide/identity.md
- [x] RFC 8785 JCS·I-JSON, domain 배열 정렬, hash·base64url encoding과 공용 golden
      vector 사용법 → guide/identity.md
- [x] compression은 새 artifact 생성 정책이며 incremental에서는 기존 artifact를
      실제 compression metadata 그대로 재사용할 수 있음을 설명 → guide/package-config.md
- [x] compression 설정을 바꿔도 재사용 artifact가 요구하는 codec은 계속 지원해야 함을
      명시 → guide/package-config.md "compression 정책" 경고 블록
- [x] Runtime 통합 가이드: DotNet adapter 사용 예시, 외부 host(Unity)의
      `IArtifactTransport`·`IRuntimeStorage`·codec 주입 구현 가이드,
      iOS IL2CPP 기본 codec 미지원 제약 명시 → guide/runtime-integration.md
- [x] host의 target manifest reference·active pointer·`PackageState`·group install
      state의 차이와 required-only 최초 설치·optional 후속 설치 흐름
      → guide/runtime-integration.md "네 가지 상태 개념의 차이"
- [x] `package-state.json` 필드·형식·최초 상태·불변 조건·filesystem 배치·writer
      lock·원자적 교체·trusted target 기반 손상 복구 규칙 → guide/runtime-integration.md
- [x] 여러 group 요청의 activation batch 경계, 병렬 다운로드·staging과 단일 state
      commit, 실패·revision 충돌 시 all-or-nothing 규칙 → guide/runtime-integration.md
- [x] publisher 계약: 불변 artifact → 검증 → manifest·signature → 재검증 업로드 순서,
      immutable cache 정책, host가 이전 manifest를 다시 선택하는 rollback
      → guide/publishing.md
- [x] 환경별 target 선택 모델·schema·파일은 제공하지 않고 host 또는 서버가 target
      manifest 선택·저장·원자적 전환을 소유한다는 책임 경계 → guide/publishing.md
- [x] Core의 문자열 경로·glob 책임과 Packager의 no-follow filesystem 열거·source
      snapshot·경합 실패 책임을 구분하고 안정된 source 입력 조건 문서화
      → guide/package-config.md "Core와 Packager의 책임 경계"
- [x] 서명 운영: `ed25519-<public key SHA-256>` key ID, immutable `manifest.sig`,
      기존 release 재서명 금지, 신규-release-only rotation, 구 key 제거 조건과
      단일 signature의 유출 key 복구 제약 → guide/publishing.md
- [x] RFC 8032 Section 7.1 known-answer test와 GamePatchKit signed golden vector의
      역할·실행 방법 → guide/identity.md "RFC 8032 known-answer test"

### schema·버전 정책

- [x] versioned JSON Schema 3종의 배포 방식과 manifest 호환 버전 정책 문서화
      (schema와 manifest 호환 버전은 같은 repository release에서 함께 관리)
      → guide/distribution.md "schema 배포와 버전 정책". `$id`가 파일명뿐인 상대
      참조라 원격 fetch 대상이 아니라는 점과, Packager가 세 schema를 embedded
      resource로 내장한다는 점을 함께 적었다.
- [x] adapter conformance fixture·test suite 사용법 문서화
      → guide/distribution.md "adapter conformance suite". 10단계가 13단계로 미뤄
      뒀던 **배포 형태 결정**도 여기서 확정했다: NuGet test package로 만들지 않고
      소스 fixture를 유지한다(테스트 프레임워크 강제 회피, Unity 등 xUnit을 그대로
      실행할 수 없는 host 고려). contracts/adapter-conformance.md의 보류 문구도
      함께 갱신했다.

### 배포 검증

- [x] NuGet 4종과 `gpk` tool의 패키징 메타데이터 정리 (license, repository URL 등)
      - `LICENSE`가 "license to be determined" placeholder인데
        `Directory.Build.props`는 `PackageLicenseExpression=MIT`를 선언하고 있었다.
        MIT 전문으로 채워 둘을 일치시켰다.
      - 5개 패키지 각각에 `README.md`를 추가하고 `PackageReadmeFile`을
        `Directory.Build.props`에서 `Exists()` 조건으로 한 번만 선언했다. 리뷰가
        지적한 "4종 모두 package README 없음" 경고가 사라졌다.
      - `GamePatchKit.Cli`에 `Description`을 추가하고 `PackableProjects`에 넣었다.
- [x] 문서의 예시 명령·설정·코드가 실제 산출물로 동작하는지 확인 → 아래 "검증" 참고
- [x] Ubuntu 24.04 x64 기준 환경, 3회 peak RSS 측정 방식, 512MiB 상한과
      256MiB→1GiB scaling delta 64MiB 상한 및 비-Linux 참고 결과 문서화
      → guide/distribution.md "성능 blocking gate" (실행 절차는 docs/perf/README.md)

## 범위 조정

이 단계의 작업 항목은 아니지만, "문서의 예시 명령이 실제로 동작한다"와 "PRD 배포
산출물이 모두 빌드 가능하다"를 만족시키려면 먼저 고쳐야 했던 것들이다.

- **`./build.sh`가 동작하지 않았다.** Nuke는 주입 대상을 멤버 이름으로 찾는데
  `_build/Build.cs`의 parameter 필드가 `_configuration`·`_version`처럼 underscore로
  시작해, 실제 노출된 option이 `--_configuration`·`--_version`·`--_-nuget-api-key`였고
  `[Solution]` 주입은 아예 실패해 `Restore`가 `NullReferenceException`으로 죽었다
  (통합 리뷰 R1). Nuke 관례인 PascalCase로 바꾸고 그 이유를 코드 주석으로 남겼다.
- **build dependency에 High severity 취약점이 있었다.**
  `System.Security.Cryptography.Xml`을 `10.0.9` → `10.0.10`으로 올려 매 빌드마다 뜨던
  NU1903 경고 5개를 없앴다 (통합 리뷰 R5).

`Pack`이 `Directory.Build.props`의 version을 실행 중에 변경하는 동작(통합 리뷰 R6)은
그대로 뒀다. README에 명시돼 있고 이 단계의 범위 밖이다.

## 산출물

- README, `docs/guide/` 8종(quickstart·package-config·identity·runtime-integration·
  unity-quickstart·unity·publishing·distribution), 5개 패키지의 package README와 패키징
  메타데이터, 실행 가능한 `samples/quickstart`·`samples/unity-quickstart`

## 추가 요청 반영

작업 중 사용자가 추가로 요청한 것들을 함께 만들었다.

- **[guide/unity.md](../guide/unity.md)** — 기존 `contracts/unity-adapter.md`는 adapter가
  무엇을 보장하는지의 계약이라, "내 Unity 프로젝트에 붙이는 순서"를 따로 정리했다.
  managed plugin 준비(`prepare.sh`), Newtonsoft를 DLL로 넣으면 안 되는 이유,
  `link.xml`을 빠뜨리면 IL2CPP에서만 깨지는 문제, 지원 범위 표, 체크리스트.
- **[guide/quickstart.md](../guide/quickstart.md)** — README의 빠른 시작은 개념을 함께
  설명하는 전체 시나리오라, 복사해서 바로 쓰는 용도의 짧은 적용 가이드를 따로 뒀다.
  단계별 체크리스트와 "자주 막히는 곳" 표가 중심이다.
- **[samples/quickstart](../../samples/quickstart)** — 문서만 읽고 조립하지 않아도
  되도록 실행 가능한 샘플을 만들었다. `run.sh` 하나가 package → verify → sign →
  Runtime 설치 → incremental → compact를 순서대로 실행하고, `QuickStartClient`가
  `HttpArtifactTransport` + `FileSystemRuntimeStorage` + `PackageRuntime` 조립을
  보여준다. 샘플이 조용히 낡지 않도록 `QuickStartClient`를 solution에 넣어
  `./build.sh Compile`에서 함께 빌드되게 했고, 실행 산출물 `.work/`는 gitignore에
  추가했다.
- **[guide/unity-quickstart.md](../guide/unity-quickstart.md)와
  [samples/unity-quickstart](../../samples/unity-quickstart)** — `guide/unity.md`는
  프로젝트에 제대로 붙이는 절차라, "일단 Play를 눌러 데이터가 설치되는 것까지"만 보는
  최단 경로를 따로 만들었다. 서버 쪽은 `serve.sh`가 `compression: none` release를 만들고
  Inspector에 넣을 네 값을 출력한다. client 쪽 `PatchQuickStart.cs`는 Unity 검증
  프로젝트의 `Assets/`에 두어 `scripts/test.sh`가 돌 때 함께 컴파일되게 했다 — 문서
  코드가 조용히 낡지 않게 하려는 것으로, `QuickStartClient`를 solution에 넣은 것과 같은
  이유다.

## 완료 기준

- [x] 신규 사용자가 README만으로 최초 package → publish tree → Runtime 다운로드·활성화
      시나리오를 재현할 수 있다. — README 빠른 시작 1~7단계를 빈 디렉터리에서 그대로
      실행해 확인했다(아래 "검증").
- [x] README의 glob 예시와 fixture가 지원 OS에서 같은 선택·group·정렬 결과를 만든다
      (검증 기준 26). — 아래 "검증"의 glob 항목.
- [x] README의 YAML 단일-document·금지 기능 설명이 02·07 fixture의 허용·거부 경우와
      일치한다(검증 기준 24, 문서 범위). — guide/package-config.md의 제약 목록이
      `tests/fixtures/gamepatchkit-yml/{valid,invalid}/`의 2 + 8개 fixture와 1:1로
      대응한다.
- [x] 문서의 명령·설정·API가 구현과 일치한다.
- [x] PRD 배포 산출물 목록이 모두 빌드 가능한 상태로 준비된다. — `./build.sh Pack`이
      경고 없이 nupkg 5종을 만들고, JSON Schema 3종과 conformance suite는 저장소에
      포함된다.

## 검증

macOS arm64, .NET 10.0.302에서 실행했다.

- `./build.sh Test` — Restore·Compile·Test 모두 성공, 9개 test project 537개 통과,
  NU1903 경고 0개.
- `./build.sh Pack --version 0.1.0` — 경고 없이 nupkg 5종 생성. `.nuspec`에
  `license type="expression">MIT`, `readme`, `projectUrl`, `repository url/commit`,
  `description`, `copyright`, `tags`가 모두 들어 있다.
- `dotnet tool install --tool-path <dir> --add-source ./artifacts GamePatchKit.Cli
  --version 0.1.0` → `gpk --help` 동작. (사용자 머신의 global tool store를 건드리지
  않으려고 `--global` 대신 `--tool-path`로 확인했다. 설치 경로만 다르고 같은 경로다.)
- README 빠른 시작 전체를 빈 디렉터리에서 실행:
  1. 4개 파일 source tree와 README의 `gamepatchkit.yml`(zstd, file group `core` +
     bundle group `maps`) 작성
  2. `gpk package --json` → exit 0. `result.identity.manifestHash` 추출 one-liner도
     그대로 동작.
  3. publish tree가 README에 적은 배치와 정확히 일치
     (`artifacts/files/<hash>/content.zst`, `artifacts/bundles/maps/<hash>.tar.zst`,
     `manifests/<hash>/manifest.json`)
  4. `gpk verify --json` → exit 0, `signature.state: absent`
  5. `gpk sign --key-env` → `manifest.sig` 생성, **정확히 225 byte**.
     `gpk verify --trusted-key <pub> --require-signature` → `signature.state:
     verified`. `--trusted-key` 없이 `--require-signature`만 주면
     `cli.invalid-arguments`로 exit 1.
  6. `python3 -m http.server 8080 --directory publish` + README의 C# 스니펫을 그대로
     담은 console app → `InstallOrUpdateAsync`로 required group만 설치(revision 1),
     `InstallOptionalGroupsAsync("maps")`로 revision 2. `package-state.json`이
     guide/runtime-integration.md의 예시와 같은 형태이고, 설치된 4개 파일의 내용이
     원본과 일치했다(zstd 해제 + bundle 추출 포함).
  7. `maps/forest.dat` 변경 후 `--previous` incremental → `reusedFileArtifactCount: 2`,
     `createdFileArtifactCount: 2`(bundle group 전체가 file override로 전환).
     `gpk compact --group maps --retained <이전 hash>` → `changed: true`,
     `dataVersion` 유지, `compactVersion` 0 → 1, `manifestHash` 변경.
- `gpk diff --from <hash> --to <hash> --json` → 논리 차이(`files.contentChanged: 1`)와
  물리 차이(`artifacts.added/removed`), `estimatedUpdate`가 모두 출력됨.
- 결정성: 같은 source로 `gpk package`를 두 번 실행해 `manifestHash`가 동일.
- `--dry-run`: 실제 identity를 그대로 보고하고 output tree는 byte 단위로 불변.
- glob (검증 기준 26): `include: ["**/*"] / exclude: ["**/*.tmp"]`인 tree에서
  `root.json`·`core/data.json`·`nested/deep.json`만 선택되고 `scratch.tmp`,
  `.DS_Store`, `.vscode/settings.json`, `core/.hidden.json`은 제외됐다. 즉 `**/*`가
  root 파일을 포함하고 숨김 segment는 암묵적으로 일치시키지 않는다는 문서 설명 그대로다.
  `include`에 `.vscode/**/*`와 `**/.hidden.json`을 명시하면 두 숨김 경로가 선택됐다.
  group을 선언하지 않은 파일은 전부 예약 group `default`로 들어갔다.
- guide/package-config.md의 exclude 예시(`**/*.tmp`, `**/*.bak`, `**/.DS_Store`,
  `.vscode/**/*`, `**/Thumbs.db`)가 전부 parse되고 의도대로 동작한다.
- `./samples/quickstart/run.sh` — 8단계 전부 성공. 두 번 연속 실행해 `manifestHash`가
  동일한 것도 확인했다(결정성).
- 저장소 markdown 48개의 상대 링크와 cross-file anchor가 전부 유효하다.
- `QuickStartClient`를 solution에 추가한 뒤 `./build.sh Test` 재실행 — 여전히 537개 통과.
- `./samples/unity-quickstart/serve.sh` — `compression: none` release 생성, Inspector에
  넣을 네 값 출력, publish tree 서빙까지 성공.
  `http://127.0.0.1:8080/unity-sample-data/manifests/<hash>/manifest.json`(Unity
  transport가 만드는 바로 그 경로)이 200을 반환한다.
- **codec을 하나도 주입하지 않은** client로 같은 서버에서 설치 — required 후
  `stateRevision: 1`, optional `maps` 후 2, `OpenInstallationFileAsync(installationKey,
  "core/config.json")`가 원본과 같은 내용을 돌려줬다. Unity 구성(codec 없음)이 실제로
  성립하는지 확인한 것이다.
- `PatchQuickStart.cs`를 Unity 검증 프로젝트에서 컴파일 — `6000.4.4f1` batchmode에서
  `Assembly-CSharp.dll` 빌드 성공, `error CS` 없음.

Ubuntu 24.04 x64 공식 성능 gate는 12단계와 마찬가지로 **여전히 미실행**이다. 이 단계는
그 기준과 실행 절차를 문서로 고정했을 뿐 수치를 만들지 않는다.

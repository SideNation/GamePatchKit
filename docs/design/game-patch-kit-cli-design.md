# GamePatchKit CLI 개발 계획

> 기준 문서: [`docs/prd/gamepatch-kit-cli-prd.md`](../prd/gamepatch-kit-cli-prd.md)
>
> 상태: 구현 전 합의용 초안

## 1. 기능 요약

`gpk build` 명령으로 Git이 추적하는 게임 데이터 파일을 읽어 그룹 아카이브, 파일 객체, `manifest.json`을 만든다. 첫 빌드와 그룹 버전 증가 시 그룹 전체를 만들고, 같은 그룹 버전에서는 Git 변경 경로에 해당하는 파일만 새 리비전의 오버레이 객체로 추가한다. 이전 성공 버전보다 작은 그룹 버전은 거부한다.

현재 `develop` 브랜치는 기존 구현을 제거한 초기 상태이므로, 삭제된 구조를 복원하지 않고 PRD에 필요한 CLI 한 개와 테스트 프로젝트 한 개만 새로 만든다.

## 2. 가정과 확정 사항

### 확정 사항

- `--output`은 필수이고 `--source`만 현재 디렉터리를 기본값으로 사용한다.
- `git` 실행 파일이 PATH에 있으며 CLI는 Git 라이브러리 대신 Git 프로세스를 호출한다.
- `--source`는 Git 저장소 최상위 경로이거나 그 하위 디렉터리다. Git이 반환한 저장소 기준 경로는 source-relative 경로로 바꾼 뒤 그룹 탐색과 변경 판정에 사용한다.
- `--output`은 `--source`가 속한 Git 저장소 바깥에 둔다. 산출물이 입력 파일 탐색이나 워킹 트리 상태에 섞이는 경우를 막기 위한 확정 제약이다.
- `schemaVersion`은 정수 `1`로 시작한다.
- `<source>/gamepatchkit.yml`과 빌드 데이터는 Git이 추적해야 한다. source 아래의 untracked 파일은 입력과 dirty 판정에서 제외하고, source 바깥의 변경도 dirty 판정에서 제외한다.
- 그룹 버전과 파일 리비전은 0 이상의 `int`, 파일 크기·offset·length·저장 크기는 `long`으로 표현한다. 이전 매니페스트에 같은 `id`의 그룹이 있으면 yaml 그룹 버전은 이전 성공 버전과 같거나 더 커야 한다.
- 매니페스트의 `name`은 `--output` 기준 상대 경로이며 구분자는 `/`로 고정한다.
- 매니페스트의 `sourcePath`는 저장소 루트면 `.`, 하위 source면 `/` 구분자를 쓰는 저장소 기준 상대 경로다. 이전 매니페스트의 값과 현재 canonical source가 다르면 중단한다.
- Git 커밋 ID는 길이를 40자로 가정하지 않고 Git이 반환한 문자열을 그대로 기록한다.
- CLI는 nuget.org의 `GamePatchKit.Cli` 패키지로 배포하는 framework-dependent .NET tool이다. `PackAsTool`은 `true`, `ToolCommandName`은 `gpk`로 지정하며 self-contained, single-file, RID별 도구 패키지는 만들지 않는다.
- GitHub Actions가 세 운영체제의 검증과 GitHub Release 기반 NuGet 게시를 수행한다.

## 3. 요구사항 정리

### 입력

- 명령: `gpk build --source <데이터 루트> --output <패치 데이터 폴더>`
- 설정: `<source>/gamepatchkit.yml`
- 데이터: 설정의 각 `groups[].id`가 가리키는 폴더 아래에서 Git이 추적하는 파일. 설정 파일도 Git 추적 대상이어야 한다.
- 증분 기준: 기존 `<output>/manifest.json`의 `sourceCommit`
- 현재 기준: source 아래의 추적 파일에 커밋되지 않은 변경이 없는 `HEAD`

### 출력

- 그룹 아카이브: `archives/<그룹 id>/<그룹 버전>.gpka`
- 파일 객체: `files/<그룹 id>/<그룹 버전>/<상대 경로>.v<파일 버전>`
- 매니페스트: `<output>/manifest.json`
- 콘솔 요약: 그룹별 엔트리 수, 그룹 버전, 아카이브 생성 여부, 새 파일 객체 수, 새로 쓴 바이트와 전체 합계

### 제약

- 대상 런타임은 `win-x64`, `linux-x64`, `osx-arm64`다.
- 대상 프레임워크와 SDK는 `net10.0`, SDK `10.0.302`다.
- 패키지는 PRD에 고정된 버전을 사용한다.
  - `NativeCompressions.Zstandard.Core` 0.6.1
  - `NativeCompressions.Zstandard.Runtime.win-x64` 0.6.1
  - `NativeCompressions.Zstandard.Runtime.linux-x64` 0.6.1
  - `NativeCompressions.Zstandard.Runtime.osx-arm64` 0.6.1
  - `Newtonsoft.Json` 13.0.2
  - `YamlDotNet` 18.1.0
- 그룹과 엔트리는 ordinal 오름차순으로 직렬화한다.
- 경로는 source/group/output 기준을 구분하고 매니페스트에는 `/` 구분자만 기록한다.
- `--output`이 `--source`가 속한 Git 저장소 내부면 빌드를 중단한다.
- `packing: group` 아카이브는 전체 payload를 한 번만 압축한 zstd 1프레임이어야 한다.
- SHA-256은 저장된 산출물 바이트를 대상으로 하고 소문자 hex로 기록한다.
- NuGet package id는 `GamePatchKit.Cli`, tool command는 `gpk`, 패키지 출력 폴더는 `artifacts`다.
- 패치 산출물 업로드, 런타임 적용, 서명, 객체 정리, 병렬 처리, 재시도, 로깅 프레임워크는 구현하지 않는다.

## 4. 실행 방식

- [x] skill 단독 (`csharp-feature-architect` skill만 사용)
- [ ] agent + skill

선택 사유: 요구사항은 넓지만 단일 CLI 프로세스와 로컬 파일 시스템 안에서 끝난다. 외부 bounded context나 기존 애플리케이션 계층과의 통합이 없으므로 별도 설계 에이전트 없이 현재 저장소와 PRD만으로 계획을 확정할 수 있다.

## 5. 가장 단순한 구조 후보

- 솔루션 1개, 실행 프로젝트 `GamePatchKit.Cli` 1개, 테스트 프로젝트 `GamePatchKit.Cli.Tests` 1개를 둔다.
- `Program`은 인자 파싱, `BuildCommand` 호출, 요약·오류 출력만 담당한다.
- `BuildCommand`가 설정, Git 스냅샷, 이전 매니페스트, 그룹별 증분 판단을 순서대로 조정한다.
- Git 호출, YAML 로드, JSON 매니페스트, 산출물 스트림 쓰기는 각각 구체 클래스로만 분리한다.
- DI 컨테이너, 인터페이스, 별도 Core/Infrastructure 프로젝트, 명령 프레임워크는 추가하지 않는다.
- CLI 프로젝트 자체를 `gpk` .NET tool로 패키징하고 GitHub Actions가 기존 dotnet 명령을 직접 실행한다. 배포만을 위한 별도 빌드 애플리케이션은 만들지 않는다.
- 아카이브 payload는 메모리에 모두 적재하지 않고 정렬된 원본 파일 스트림을 하나의 출력 스트림에 순서대로 복사한다. zstd 그룹은 이 출력 스트림에 압축기 한 개를 적용해 1프레임을 만든다.
- zstd 스트리밍은 `NativeCompressions.ZstandardStream`을 쓴다. `ZstandardStream(Stream inner, in ZstandardCompressionOptions options, bool leaveOpen)` 생성자가 0.6.1에 있는 것을 확인했다. 저수준 `ZstandardEncoder`(`OperationStatus` 기반 streamless API)를 직접 다루지 않는다. 테스트의 해제 검증도 같은 타입을 `CompressionMode.Decompress`로 쓴다.

## 6. 단순 구조 실패 조건

`Program.cs` 한 파일에 전부 구현하는 구조는 다음 이유로 사용하지 않는다.

- Git 프로세스 결과와 파일 시스템 산출물을 함께 다루면 Git 변경 감지와 패키징 규칙을 독립적으로 검증하기 어렵다.
- YAML 입력 모델과 JSON 출력 모델은 필드명·기본값·조건부 필드가 서로 달라 한 모델로 합칠 수 없다.
- zstd 1프레임 쓰기, 저장 바이트 체크섬, 원본 payload offset 계산은 증분 버전 판단과 다른 책임이다.

위 세 경계까지만 분리한다. 구현 중 `BuildCommand`의 그룹 판단 로직이 독립 테스트 없이는 검증 불가능할 정도로 커질 때만 `GroupBuildPlanner` 같은 순수 계산 클래스를 추가로 검토한다. 현재 계획에는 포함하지 않는다.

## 7. 책임/도메인 분해

| 책임 단위 | 종류 | 설명 |
| --- | --- | --- |
| `Program` | Entry point | 명령 실행, 성공/실패 종료 코드, 사용자 출력 |
| `BuildArguments` | Input model | `build`, `--source`, `--output` 파싱 결과와 기본값 |
| `BuildCommand` | Application service | 전체 빌드 순서와 그룹별 전체/증분 판단 조정 |
| `BuildConfiguration` / `GroupConfiguration` | YAML DTO | 루트 설정과 그룹 설정, 기본값과 유효성 검사 대상 |
| `BuildConfigurationLoader` | Loader | `gamepatchkit.yml` 역직렬화와 설정 검증 |
| `GitRepository` | External process boundary | 저장소 루트와 source prefix 확인, source 범위의 tracked clean 상태, HEAD, tracked 파일, 두 커밋 간 변경 경로 조회와 source-relative 변환 |
| `SourceEntry` | Internal DTO | 그룹 소유가 확정된 원본 파일의 경로와 크기 |
| `PatchManifest` 계열 | JSON DTO | PRD의 루트·그룹·아카이브·엔트리 계약 |
| `ManifestStore` | Loader/Writer | 이전 매니페스트 읽기·관계 검증과 정렬된 새 매니페스트의 임시 파일 작성·원자적 교체 |
| `ArtifactWriter` | File writer | 임시 후보 쓰기, zstd 적용, stored size와 SHA-256 계산, 아카이브 재사용·충돌 판정, 파일 리비전 확정, 아카이브 엔트리별 offset·length 산출 |
| `IncrementalPrecondition` 검사 | `BuildCommand` 내부 절차 | 증분 대상이 있을 때의 이전 커밋 객체 존재 여부, 승계 산출물의 존재 여부·저장 크기·SHA-256, `packing`·`compression` 동일 여부 확인 |
| `BuildSummary` / `GroupBuildSummary` | Output model | 그룹별·전체 빌드 요약과 자동 파일 리비전 증가 내역 |
| `BuildException` | Error | 사용자가 조치할 수 있는 빌드 중단 사유 전달 |

별도 Entity, Value Object, Domain Event는 만들지 않는다. 이 기능은 파일·설정·매니페스트 변환 작업이며 독립 수명주기를 가진 도메인 엔티티가 필요하지 않다.

## 8. 적용 패턴과 정당화

없음.

- Strategy: `packing`과 `compression` 조합이 4개뿐이고 `BuildCommand`/`ArtifactWriter`의 명시적 분기로 충분하다.
- Factory: 생성할 대체 구현이 없다.
- Repository: 데이터베이스가 없고 Git·파일 시스템을 감싸는 구체 클래스면 충분하다.
- Mediator/Command framework: 외부 명령은 `build` 하나뿐이다.

## 9. 인터페이스 생성 근거

없음.

Git, YAML, JSON, 파일 쓰기 모두 구현이 한 개다. 테스트는 임시 Git 저장소와 임시 디렉터리를 사용하는 통합 테스트로 실제 경계를 검증하므로 테스트 대역만을 위한 인터페이스를 만들지 않는다.

## 10. 인터페이스·클래스 시그니처

아래는 책임과 데이터 계약을 확인하기 위한 시그니처 초안이다. 메서드 본문은 구현 단계에서 작성한다.

```csharp
namespace GamePatchKit.Cli;

internal enum PackingKind
{
    Group,
    File
}

internal enum CompressionKind
{
    Zstd,
    None
}

internal enum EntrySource
{
    Archive,
    File
}

internal sealed record BuildArguments(string SourcePath, string OutputPath);

internal static class BuildArgumentsParser
{
    public static BuildArguments Parse(string[] arguments);
}

internal sealed class BuildCommand
{
    public BuildSummary Execute(BuildArguments arguments);
}

internal sealed class GitRepository
{
    public GitRepository(string sourcePath);

    public string GetHeadCommit();
    public IReadOnlyList<string> GetChangedPaths(string previousCommit, string currentCommit);
    public IReadOnlyList<string> GetTrackedPaths();
    public void EnsureConfigurationTracked();
    public void EnsureSourceIsInRepository();
    public void EnsureTrackedSourceClean();
}

internal sealed class BuildConfiguration
{
    public IReadOnlyList<GroupConfiguration> Groups { get; init; }
}

internal sealed class GroupConfiguration
{
    public string Id { get; init; }
    public int Version { get; init; }
    public PackingKind Packing { get; init; }
    public CompressionKind Compression { get; init; }
    public int? Level { get; init; }
}

internal static class BuildConfigurationLoader
{
    public static BuildConfiguration Load(string path);
}

internal sealed record SourceEntry(string Path, string FullPath, long Size);

internal sealed class PatchManifest
{
    [JsonProperty("schemaVersion")]
    public int SchemaVersion { get; init; }

    [JsonProperty("sourcePath")]
    public string SourcePath { get; init; }

    [JsonProperty("sourceCommit")]
    public string SourceCommit { get; init; }

    [JsonProperty("groups")]
    public IReadOnlyList<ManifestGroup> Groups { get; init; }
}

internal sealed class ManifestGroup
{
    [JsonProperty("id")]
    public string Id { get; init; }

    [JsonProperty("version")]
    public int Version { get; init; }

    [JsonProperty("packing")]
    public PackingKind Packing { get; init; }

    [JsonProperty("compression")]
    public CompressionKind Compression { get; init; }

    [JsonProperty("archive")]
    public ManifestArchive? Archive { get; init; }

    [JsonProperty("entries")]
    public IReadOnlyList<ManifestEntry> Entries { get; init; }
}

internal sealed class ManifestArchive
{
    [JsonProperty("name")]
    public string Name { get; init; }

    [JsonProperty("payloadSize")]
    public long PayloadSize { get; init; }

    [JsonProperty("storedSize")]
    public long StoredSize { get; init; }

    [JsonProperty("checksum")]
    public string Checksum { get; init; }
}

internal sealed class ManifestEntry
{
    [JsonProperty("path")]
    public string Path { get; init; }

    [JsonProperty("version")]
    public string Version { get; init; }

    [JsonProperty("size")]
    public long Size { get; init; }

    [JsonProperty("source")]
    public EntrySource Source { get; init; }

    [JsonProperty("offset")]
    public long? Offset { get; init; }

    [JsonProperty("length")]
    public long? Length { get; init; }

    [JsonProperty("name")]
    public string? Name { get; init; }

    [JsonProperty("storedSize")]
    public long? StoredSize { get; init; }

    [JsonProperty("checksum")]
    public string? Checksum { get; init; }
}

internal static class ManifestStore
{
    public static PatchManifest? ReadPrevious(string outputPath);
    public static void WriteAtomically(string outputPath, PatchManifest manifest);
}

internal sealed record WrittenArtifact(
    string Name,
    string Version,
    long StoredSize,
    string Checksum,
    bool IsCreated);

internal sealed record ArchiveEntryLayout(string Path, long Offset, long Length);

internal sealed record WrittenArchive(
    string Name,
    long PayloadSize,
    long StoredSize,
    string Checksum,
    bool IsCreated,
    IReadOnlyList<ArchiveEntryLayout> Layout);

internal sealed class ArtifactWriter
{
    public WrittenArchive WriteArchive(
        string outputPath,
        GroupConfiguration group,
        IReadOnlyList<SourceEntry> entries);

    public WrittenArtifact WriteFile(
        string outputPath,
        GroupConfiguration group,
        SourceEntry entry,
        string fileVersion);
}

internal sealed record GroupBuildSummary(
    string GroupId,
    int GroupVersion,
    int EntryCount,
    bool IsArchiveCreated,
    int FileObjectCount,
    long WrittenBytes);

internal sealed record FileRevisionAdjustment(
    string GroupId,
    string Path,
    string RequestedVersion,
    string ActualVersion);

internal sealed record BuildSummary(
    IReadOnlyList<GroupBuildSummary> Groups,
    IReadOnlyList<FileRevisionAdjustment> FileRevisionAdjustments);

internal sealed class BuildException : Exception
{
    public BuildException(string message);
}
```

`packing`, `compression`, `source`는 JSON에서 PRD 값과 같은 소문자 문자열로 직렬화한다. `source: archive`일 때 파일 객체 필드는 생략하고, `source: file`일 때 offset·length는 생략한다.

`WriteArchive`는 실제로 쓴 바이트에서 얻은 `Layout`을 반환한다. 호출자가 `SourceEntry.Size`를 누적해 offset을 따로 계산하면 payload를 쓰는 쪽과 offset을 계산하는 쪽이 갈려 완료 조건 2(offset·length가 원본과 일치)가 깨질 수 있다. offset의 유일한 출처는 writer다.

`ManifestStore.ReadPrevious`는 JSON 역직렬화 직후 별도 타입을 만들지 않고 매니페스트 DTO를 직접 검증한다. root 필수 필드와 정규화된 `sourcePath`, 그룹·엔트리 중복과 정규화 상대 경로, 0 이상의 버전·크기, 64자리 소문자 hex checksum, source별 필수·금지 필드, 엔트리 버전의 그룹 버전 일치, 정식 산출물 `name` 일치, archive 구간의 범위·단조 증가·비중첩을 확인한다. 위반 시 필드 경로와 이유를 담은 `BuildException`을 던진다. 유효한 매니페스트를 읽은 뒤 `BuildCommand`가 현재 canonical source와 `sourcePath`의 일치를 검사한다.

`WriteArchive`와 `WriteFile`은 최종 경로와 같은 디렉터리의 임시 파일에 후보를 완성한 뒤 게시한다. `WriteArchive`는 yaml 그룹 버전을 바꾸지 않으며 동일 아카이브 재사용 여부를 `IsCreated`로 반환한다. `WriteFile`은 실제로 선택한 `Version`을 반환하고, `BuildCommand`는 요청 버전과 다를 때 `FileRevisionAdjustment`를 만든다.

## 11. 파일·폴더 배치 제안

생산 코드 안에는 추가 하위 계층을 만들지 않는다. 파일 수가 늘어나 실제 탐색 비용이 생길 때만 책임별 폴더 분리를 별도 작업으로 검토한다.

```text
GamePatchKit.sln
global.json
Directory.Build.props
Directory.Packages.props
artifacts/                  (Git 제외, NuGet 패키지 출력)
.github/
└── workflows/
    └── dotnet.yml
src/
└── GamePatchKit.Cli/
    ├── GamePatchKit.Cli.csproj
    ├── Program.cs
    ├── BuildArguments.cs
    ├── BuildCommand.cs
    ├── BuildConfiguration.cs
    ├── BuildConfigurationLoader.cs
    ├── GitRepository.cs
    ├── PatchManifest.cs
    ├── ManifestStore.cs
    ├── ArtifactWriter.cs
    ├── BuildSummary.cs
    └── BuildException.cs
tests/
└── GamePatchKit.Cli.Tests/
    ├── GamePatchKit.Cli.Tests.csproj
    ├── TestBuildArguments.cs
    ├── TestBuildConfiguration.cs
    ├── TestGitRepository.cs
    └── TestBuildCommand.cs
```

- 실행 파일 이름: `GamePatchKit.Cli.csproj`의 `AssemblyName`과 `ToolCommandName`을 `gpk`로 지정한다.
- NuGet 패키지: `PackageId=GamePatchKit.Cli`, `PackAsTool=true`, `PackageOutputPath=<repo>/artifacts`로 지정한다. `RuntimeIdentifiers`는 설정하지 않아 framework-dependent 도구 패키지 하나를 만든다.
- 프로젝트 참조: 별도 라이브러리 프로젝트 없이 CLI 프로젝트가 PRD의 런타임 패키지를 직접 참조한다.
- 테스트 참조: 테스트 프로젝트는 CLI 프로젝트를 참조하고 xUnit 및 .NET test SDK만 테스트 전용으로 사용한다.
- DI 등록 위치: 없음. `Program`이 필요한 구체 객체를 직접 만든다.

### NuGet 배포와 GitHub Actions

TrackableData의 배포 방식에서 공통 패키지 메타데이터, `artifacts` 출력, Test → Pack → Push 순서, nuget.org source, `NUGET_API_KEY`, 중복 패키지 건너뛰기를 가져온다. 이 저장소는 배포 대상이 CLI 프로젝트 하나이므로 별도 NUKE 빌드 프로젝트 없이 GitHub Actions에서 동일한 dotnet 명령을 직접 실행한다.

- pull request와 일반 push: `windows-latest`(`win-x64`), `ubuntu-latest`(`linux-x64`), `macos-26`(`osx-arm64`) 매트릭스에서 restore → test → pack → 임시 tool path에 로컬 패키지 설치 → zstd smoke run 순서로 검증한다.
- GitHub Release 게시: release tag는 `v<NuGet version>` 형식으로 두고, `v`를 제외한 값을 `PackageVersion`으로 전달한다. 세 운영체제 검증이 모두 성공해야 Ubuntu 게시 job이 실행된다.
- 게시 job: `dotnet pack --configuration Release --output artifacts`, 이어서 `dotnet nuget push artifacts/*.nupkg --source https://api.nuget.org/v3/index.json --skip-duplicate`를 실행한다.
- 인증: repository secret `NUGET_API_KEY`를 같은 이름의 환경 변수로 게시 step에만 전달한다.
- 설치 계약: `dotnet tool install GamePatchKit.Cli --global` 또는 local tool manifest로 설치하고 `gpk`로 실행한다. 실행 환경에는 .NET 10 런타임이 있어야 한다.

## 12. 도입하지 않은 구조

- 별도 `GamePatchKit.Core` 프로젝트: 현재 소비자는 CLI 하나뿐이다. Unity 런타임이 실제 범위에 들어올 때 매니페스트 모델 공유 여부를 다시 결정한다.
- `IBuildService`, `IGitRepository`, `IArtifactWriter`: 모두 단일 구현·단일 호출자이므로 제외한다.
- System.CommandLine: 명령 하나와 옵션 두 개를 위해 새 의존성을 추가하지 않는다.
- LibGit2Sharp: PRD가 Git 저장소를 전제로 하고 Git CLI로 필요한 조회를 모두 수행할 수 있으므로 제외한다.
- DI 컨테이너: 객체 수명이나 대체 구현 관리가 필요하지 않다.
- JSON Schema와 별도 canonical JSON writer: PRD가 요구하지 않는다. `[JsonProperty]`, 정렬, 고정 serializer 설정으로 필요한 계약과 no-op 동일성을 검증한다.
- 전체 빌드용 staging·rollback·복구 journal 계층: 중간 실패 후 미참조 객체 정리는 제외 범위다. 산출물은 기존 공통 쓰기 규칙대로 게시하고, `manifest.json` 한 파일만 같은 디렉터리의 임시 파일에서 원자적으로 교체한다.
- NUKE 빌드 프로젝트: 참고한 TrackableData는 여러 패키지의 Pack/Push 조정에 사용하지만, 현재는 pack 대상이 CLI 하나이고 GitHub Actions의 dotnet 명령만으로 같은 순서를 재현할 수 있어 제외한다.
- 로깅·메트릭·성능 계층: 빌드 요약 외에는 제외 범위다.

## 13. 단순화 자가 검토 결과

- 새 인터페이스 수: 0 (예산 0~2 충족)
- 적용 패턴 수: 0 (예산 0 충족)
- 생산 프로젝트 수: 1
- 테스트 프로젝트 수: 1
- 생산 프로젝트 내부 새 폴더 계층: 0
- 단일 구현 인터페이스: 없음
- 요구사항에서 직접 도출되지 않은 도메인 객체: 없음
- 새 타입 수가 기본 예산을 초과하는 이유: YAML 계약, JSON 계약, Git 프로세스, 스트리밍 산출물, CLI 결과는 입력·출력과 실패 조건이 서로 달라 한두 타입에 합치면 책임과 검증 경계가 깨진다.
- 종합 판정: 패턴·인터페이스 없이 요구사항상 필요한 I/O 경계만 분리한 최소 구조다.

## 14. 위임 다음 단계

- 구현: 일반 C# 코딩 작업으로 진행하고 `csharp-coding-standards`를 적용한다.
- 단위·통합 테스트: `csharp-unit-test` 절차를 적용하되 NSubstitute 대신 임시 Git 저장소와 임시 디렉터리로 실제 경계를 검증한다.
- API/Controller 테스트: 해당 없음.
- Repository/DbContext 테스트: 해당 없음.

## 15. 핵심 처리 흐름

```text
인자 파싱
  → source/output 경로와 source가 속한 Git 저장소 루트·source prefix 확인
  → source 범위의 tracked clean 상태·HEAD 확인
  → gamepatchkit.yml의 Git 추적 여부 확인
  → YAML 로드 및 그룹 검증
  → 이전 manifest 로드 및 관계 검증
  → 이전 manifest의 sourcePath와 현재 source 일치 확인
  → 그룹별 첫 빌드·전체 빌드·증분 빌드 대상 판정
  → 이전 커밋 객체와 설정에 대한 증분 전제 검증
  → tracked 파일 탐색 및 가장 깊은 그룹 할당
  → 증분 대상이 있으면 이전 sourceCommit 대비 changed path 계산
  → 새 매니페스트가 승계할 산출물 확정 및 무결성 검증
  → 판정한 방식으로 그룹별 빌드
  → 그룹/엔트리 ordinal 정렬
  → manifest.json을 같은 디렉터리의 임시 파일에서 완성한 뒤 마지막에 원자적으로 교체
  → 그룹별·전체 요약 출력
  → 파일 리비전 자동 증가가 있으면 마지막에 경고 한 묶음 출력
```

### 증분 전제 검증

PRD "증분 빌드의 전제"를 구현하는 사전 검사다. 증분 빌드는 이전 상태를 이어받고 식별이 해시가 아니므로, 전제가 깨졌을 때 다시 계산해 복구할 수단이 없다. 이전 커밋 객체와 설정은 그룹 분류 직후 검사하고, 승계 산출물은 tracked 파일과 changed path로 새 매니페스트의 승계 대상을 확정한 뒤 검사한다. 모든 검사는 산출물을 하나도 쓰기 전에 끝내 실패 시 `--output`이 변하지 않게 한다.

아래 "통과 조건"은 **성립해야 정상인 상태**다. 성립하지 않을 때만 중단한다.

| 검사 | 통과 조건 (성립해야 정상) | 위반 시 |
| --- | --- | --- |
| 이전 커밋 객체 존재 | 증분 대상 그룹이 하나 이상이면 이전 매니페스트의 `sourceCommit`을 git diff 입력으로 쓸 수 있게 현재 저장소에 **있다** (`git cat-file -e <commit>^{commit}`) | 중단. `이전 상태가 없습니다.`라고만 알리고 이후 조치는 사용자에게 맡김 |
| 승계 산출물 무결성 | 증분 대상 그룹이 승계할 아카이브·파일 객체가 `--output`에 **실재하고**, 실제 크기와 SHA-256이 이전 매니페스트의 `storedSize`·`checksum`과 **일치한다** | 중단. 누락 또는 불일치 경로와 이유를 나열 |
| 설정 동일 | 그룹 버전을 그대로 둔 그룹은 `packing`·`compression`이 이전 매니페스트와 **같다** | 중단. 바뀐 그룹 id와 함께 그룹 버전을 올리도록 안내 |

이전 매니페스트와 `id`·`version`이 같은 그룹만 증분 대상이다. 증분 대상이 하나도 없으면 이전 커밋 객체 확인과 git diff를 생략한다. 승계 산출물 검사도 증분 대상 그룹에만 수행한다. `BuildCommand`는 `packing` 분기 전에 모든 증분 그룹의 승계 대상을 모아 공통 검사를 한 번 수행하므로 `packing: group`의 아카이브·오버레이와 `packing: file`의 파일 객체에 같은 규칙이 적용된다. 각 승계 산출물은 존재 여부 → 실제 크기 → SHA-256 순서로 검사하며, 크기가 다르면 해시 계산 없이 실패로 판정한다. 그룹 버전의 동일·증가·감소는 이전 매니페스트의 같은 `id`와 yaml 설정값을 비교해 판단하며 CLI가 자동으로 버전을 바꾸지 않는다.

### 그룹 판단 규칙

| 현재 그룹 상태 | 처리 |
| --- | --- |
| 이전 매니페스트에 그룹 없음 | 첫 빌드 |
| 그룹 버전 증가 | 전체 빌드, 각 파일의 요청 리비전 0 |
| 그룹 버전 감소 | 산출물을 쓰기 전에 중단하고 이전 성공 버전보다 큰 값을 사용하도록 안내 |
| 같은 버전 + `packing: group` | 기존 아카이브 유지, 변경·신규 파일만 오버레이 작성 |
| 같은 버전 + `packing: file` | 변경·신규 파일 객체만 작성 |
| 현재 tracked 목록에서 사라짐 | 새 매니페스트에서 제거 |
| 그룹의 엔트리가 0개 | 아카이브를 만들지 않고 `archive` 생략, `entries`는 빈 배열 |
| YAML에서 그룹 제거 | 새 매니페스트에서 그룹 제거, 기존 산출물은 보존 |

### 산출물 게시와 충돌 처리

1. `ArtifactWriter`는 최종 경로와 같은 디렉터리의 정식 이름과 겹치지 않는 임시 파일에 후보를 완성하고, 저장 크기와 SHA-256을 계산한다. 버전 탐색은 임시 파일을 무시하고 게시·재사용·충돌 중단을 결정한 뒤 임시 파일을 제거한다.
2. 아카이브 최종 경로가 없으면 후보를 게시한다. 기존 아카이브의 저장 크기와 SHA-256이 후보와 같으면 기존 파일을 재사용한다. 다르면 그룹 버전 충돌로 중단하며 yaml 그룹 버전과 기존 아카이브를 바꾸지 않는다.
3. 파일 객체는 요청 버전 이상의 정식 객체 중 가장 높은 리비전을 숫자로 비교한다. 해당 객체가 없으면 요청 버전으로 게시한다. 있으면 후보와 저장 크기·SHA-256을 비교해 같을 때 그 버전을 재사용하고, 다를 때 가장 높은 리비전 + 1의 새 경로로 게시한다.
4. 아카이브와 파일 객체 모두 기존 최종 파일을 덮어쓰지 않는다. 파일 리비전만 자동 증가할 수 있으며 그룹 버전은 항상 yaml 값과 같다.
5. 매니페스트를 마지막에 기록하므로 중간 실패 시 `--output`에는 미참조 객체만 남을 수 있다. 같은 후보는 다음 실행에서 재사용하며 미참조 객체 정리는 제외 범위다.

### 실패 후 재실행

빌드가 중간에 실패했을 때 어느 커밋부터 다시 계산하는지는 `manifest.json`의 기록 시점 하나로 결정된다.

1. `ManifestStore.WriteAtomically`는 모든 산출물 처리가 끝난 성공 경로에서만 호출한다. 같은 디렉터리의 정식 이름과 겹치지 않는 임시 파일에 직렬화를 끝낸 뒤 `manifest.json`으로 원자적으로 교체하고, 후보 작성이나 교체에 실패하면 기존 매니페스트를 유지한다.
2. 따라서 `sourceCommit`은 **마지막으로 성공한 빌드의 커밋**으로 남는다. 다음 빌드는 그 커밋과 현재 `HEAD`를 diff하므로 실패한 빌드 구간의 변경을 다시 계산한다.
3. 실패한 빌드가 이미 쓴 산출물과 재실행 후보가 같으면 공통 쓰기 규칙으로 재사용한다. 파일 객체가 다르면 마지막 리비전 + 1로 게시한다.
4. 같은 그룹 버전의 기존 아카이브와 재실행 후보가 다르면 그룹 버전 충돌로 중단한다. 사용자가 yaml의 그룹 버전을 올려 재실행하면 해당 그룹은 현재 `HEAD`로 전체 빌드되고, 다른 증분 그룹은 마지막 성공 `sourceCommit` 이후의 실패 구간까지 반영한다.
5. 진행 상황이나 미완료 작업을 기록하는 별도 상태 파일과 복구 코드는 만들지 않는다. 실패 중 게시된 미참조 객체는 보존하며 정리는 제외 범위다.

### 파일 소유 그룹 결정

1. `GitRepository`는 `git rev-parse --show-toplevel`과 `git rev-parse --show-prefix`로 저장소 루트와 source의 저장소 기준 prefix를 구한다. source가 저장소 바깥이면 중단하고, prefix는 저장소 루트일 때 `.`, 그 외에는 끝의 `/`를 제거한 `sourcePath`로 정규화한다.
2. tracked 파일과 changed path 조회는 source prefix로 pathspec을 제한하고 `-z` 결과를 저장소 기준에서 source-relative 경로로 바꾼다. source 밖의 경로는 이후 로직에 전달하지 않는다.
3. 그룹 `id`를 source 기준 정규화된 디렉터리 경로로 바꾼다.
4. source 아래 Git tracked 파일 중 그룹 경로 아래의 일반 파일만 후보로 둔다. untracked 파일은 열거나 상태 검사에 사용하지 않는다.
5. 여러 그룹 경로에 포함되면 경로 세그먼트가 가장 긴 그룹에 배정한다.
6. 그룹 루트 기준 상대 경로를 `/`로 정규화한다.
7. 그룹은 `id`, 엔트리는 `path`를 `StringComparer.Ordinal`로 정렬한다.

## 16. 단계별 구현 계획

`P0 → P1 → P2 → P3 → P4 → P5 → P6 → P7 → P8 → P9` 순서로 진행한다. 각 단계는 해당 검증을 통과한 뒤 다음 단계로 넘어간다.

### P0. 출력 계약과 정책 확정

- 수행:
  - 2절의 확정 사항과 PRD가 일치하는지 확인한다.
  - 매니페스트 샘플 JSON 한 개로 필드 타입, 조건부 필드, 상대 경로를 확정한다.
- 검증:
  - PRD의 매니페스트 필드가 샘플에 모두 대응한다.
  - 구현자가 임의로 정해야 할 외부 계약 항목이 남지 않는다.
- 완료 조건 연결: 전체 완료 조건의 선행 단계

### P1. 솔루션과 실행 뼈대

- 수행:
  - `global.json`, `Directory.Build.props`, `Directory.Packages.props`, 솔루션, CLI 프로젝트, 테스트 프로젝트를 만든다.
  - SDK·TargetFramework·AssemblyName·패키지 버전을 PRD와 맞추고 `PackageId=GamePatchKit.Cli`, `PackAsTool=true`, `ToolCommandName=gpk`를 설정한다.
  - 수동 인자 파서와 `build` 명령의 빈 실행 경계를 만든다.
- 검증:
  - `dotnet --version`이 `10.0.302`를 선택한다.
  - `dotnet restore`, `dotnet build`, `dotnet test`가 성공한다.
  - source 생략, output 누락, 알 수 없는 명령·옵션의 테스트가 통과한다.
  - output을 source가 속한 Git 저장소 내부로 지정하면 빌드가 중단되고 산출물이 생기지 않는다.
- 완료 조건 연결: 13, 17

### P2. YAML·매니페스트 계약

- 수행:
  - YAML 기본값과 허용 값 검증을 구현한다.
  - 그룹 id 중복, 절대 경로, `..`, 존재하지 않는 그룹 폴더를 거부한다.
  - `[JsonProperty]`가 붙은 매니페스트 DTO와 정렬된 직렬화를 구현한다.
  - 이전 매니페스트의 schema, 필수 필드, 중복 그룹·엔트리, 정규화 상대 경로, 버전·크기·checksum 형식을 검증한다.
  - 이전 매니페스트의 `sourcePath` 형식을 검증하고 현재 canonical source와 다르면 산출물을 쓰기 전에 중단한다.
  - `packing`·`source`별 필수·금지 필드와 정식 산출물 `name`을 검증한다.
  - archive 엔트리의 `length == size`, `offset + length <= payloadSize`, path 순서상 단조 증가·비중첩을 검증한다.
- 검증:
  - 최소 YAML, 모든 옵션 YAML, 잘못된 id/enum/버전 테스트가 통과한다.
  - 매니페스트 round-trip 후 필드명과 값이 유지된다.
  - 입력 순서를 바꿔도 출력 그룹·엔트리 순서는 동일하다.
  - JSON으로는 유효하지만 `sourcePath`, 중복, 경로, 버전, checksum, source 조건부 필드, archive 범위 중 하나가 잘못된 이전 매니페스트를 각각 거부하고 `--output`을 바꾸지 않는다.
- 완료 조건 연결: 1, 10, 21

### P3. Git 스냅샷과 그룹 탐색

- 수행:
  - source가 Git 저장소 루트 또는 하위 경로인지 확인하고 저장소 루트와 source prefix를 구한다.
  - source prefix로 제한한 tracked clean 상태, HEAD, tracked path, changed path 조회를 구현한다. untracked 파일과 source 바깥 변경은 dirty 판정과 결과에서 제외한다.
  - `gamepatchkit.yml`이 Git 추적 대상인지 확인하고, 아니면 YAML을 읽기 전에 중단한다.
  - `-z` 형식과 rename 비활성화를 사용해 저장소 기준 경로를 안전하게 읽고 source-relative로 변환하며 rename을 삭제+추가로 취급한다.
  - tracked 파일만 가장 깊은 그룹에 할당한다.
  - 이전 매니페스트에 같은 `id`의 그룹이 있는데 yaml 그룹 버전이 더 작으면 산출물을 쓰기 전에 중단한다.
  - 증분 대상 그룹이 하나 이상일 때만 이전 `sourceCommit` 커밋 객체가 현재 저장소에 있는지 `git cat-file -e <commit>^{commit}`으로 확인하고, 없으면 중단한다.
  - 증분 대상 그룹이 없으면 이전 커밋 객체 확인과 changed path 조회를 생략한다.
- 검증:
  - 테스트가 임시 Git 저장소를 만들고 최초 커밋, 수정, 추가, 삭제, rename, dirty 상태를 재현한다.
  - 저장소 루트와 하위 폴더를 각각 source로 사용할 수 있고, 하위 source의 tracked·changed 경로가 source-relative로 변환된다.
  - 저장소 루트는 `sourcePath: "."`, 하위 폴더는 정규화된 저장소 기준 상대 경로로 매니페스트에 기록되며, 다른 source에서 같은 output을 재사용하면 `데이터 루트가 이전 빌드와 다릅니다.`를 출력하고 중단된다.
  - source 아래의 untracked 파일과 source 바깥 변경은 빌드와 dirty 판정에서 제외되며, source 아래 tracked 파일의 미커밋 변경은 중단된다.
  - `gamepatchkit.yml`이 untracked이면 산출물 없이 중단된다.
  - 중첩 그룹에서 파일이 가장 깊은 그룹에 한 번만 들어간다.
  - yaml 그룹 버전이 이전 성공 버전보다 작으면 더 큰 값을 사용하라는 안내와 함께 중단되고 `--output`이 변하지 않는다.
  - 이전 매니페스트의 커밋 객체가 없고 같은 버전 그룹이 남아 있으면 `이전 상태가 없습니다.`를 출력하고 빌드가 중단되며 `--output`이 변하지 않는다.
  - 이전 매니페스트의 커밋 객체가 없어도 모든 현재 그룹이 신규이거나 버전이 올라갔다면 전체 빌드에 성공한다.
- 완료 조건 연결: 5, 7, 8, 9, 10, 16

### P4. 산출물 writer와 체크섬

- 수행:
  - `compression: none` 파일 복사와 zstd 스트림 압축을 구현한다.
  - 그룹 아카이브는 정렬된 파일을 한 압축 스트림에 이어 써서 zstd 1프레임을 만든다.
  - 저장 스트림에서 stored size와 SHA-256을 동시에 계산한다.
  - 최종 경로와 같은 디렉터리의 임시 파일에 후보를 완성한 뒤 저장 크기·SHA-256 비교 결과에 따라 새 경로 게시 또는 기존 파일 재사용을 결정한다.
  - 파일 객체의 정식 이름만 파싱해 요청 리비전 이상의 최댓값을 구하고, 다른 후보와 충돌하면 그 값 + 1을 실제 리비전으로 반환한다.
- 검증:
  - none 산출물이 원본과 바이트 단위로 같다.
  - zstd 아카이브를 해제한 payload가 원본 연결 결과와 같다.
  - 저장 파일을 독립적으로 SHA-256 해시한 값이 기록값과 같다.
  - 같은 후보는 기존 파일을 재사용하고 다른 후보는 아카이브 충돌 또는 파일 리비전 증가로 분기되며, 어떤 경우에도 기존 파일 바이트가 바뀌지 않는다.
- 완료 조건 연결: 2, 11, 12, 18, 19

### P5. `packing: group` 전체 빌드

- 수행:
  - 엔트리가 하나 이상인 그룹의 첫 빌드와 그룹 버전 증가 시 새 `.gpka`를 만든다.
  - `WriteArchive`가 반환한 `Layout`으로 offset·length를 채우고 `<그룹 버전>.0` 버전을 만든다.
  - 그룹 버전 증가 시 이전 오버레이 참조를 제거한다.
  - 엔트리가 0개면 아카이브를 만들지 않고 `archive`를 생략한다.
  - 대상 아카이브가 후보와 같으면 재사용하고, 다르면 yaml 그룹 버전을 유지한 채 그룹 버전 충돌로 중단한다.
- 검증:
  - 엔트리가 하나 이상인 그룹의 최초 빌드에서 아카이브 하나와 archive 엔트리만 생긴다.
  - offset이 0부터 단조 증가하고 각 구간이 원본 파일과 같다.
  - 그룹 버전 증가 시 모든 리비전이 0으로 돌아간다.
  - 대상 파일이 없는 그룹이 `archive` 없이 빈 `entries`로 기록된다.
  - 아카이브만 쓴 뒤 매니페스트 기록 전에 실패한 상태에서 재실행하면 같은 아카이브를 재사용해 성공한다.
  - YAML에서 뺐던 그룹을 같은 버전의 다른 내용으로 되살리면 중단되고 기존 아카이브와 매니페스트가 그대로 남는다.
- 완료 조건 연결: 1, 2, 5, 15, 18

### P6. `packing: group` 증분 빌드

- 수행:
  - 같은 그룹 버전이면 이전 아카이브를 다시 쓰지 않는다.
  - `BuildCommand`의 공통 승계 무결성 검사로 이 그룹이 승계할 아카이브와 오버레이 파일 객체를 검증한다.
  - 그룹 버전이 같은데 `packing` 또는 `compression`이 이전 매니페스트와 다르면 그룹 id와 함께 중단한다.
  - 변경 파일은 이전 리비전 + 1, 신규 파일은 리비전 0을 요청한다. 이전 리비전은 이전 매니페스트 엔트리 `version`의 `.` 뒤 정수를 파싱해 얻고, 실제 리비전은 산출물 충돌 규칙으로 확정한다.
  - 미변경 엔트리의 버전과 위치를 그대로 승계하고 삭제 파일은 제외한다.
- 검증:
  - 파일 하나 수정 시 오버레이 하나만 추가되고 아카이브 checksum·수정 시각·내용은 유지된다.
  - 재수정 시 해당 파일 리비전만 다시 증가한다.
  - 추가는 같은 경로의 기존 파일 객체가 없으면 `<그룹 버전>.0`, 삭제는 매니페스트 제거로 반영된다.
  - 요청 버전 이상의 마지막 오버레이가 같은 바이트면 재사용하고, 다른 바이트면 마지막 리비전 + 1을 사용하며 기존 객체는 유지된다.
  - 같은 HEAD 재실행 전후의 파일 목록과 매니페스트 바이트가 같다.
  - `packing: group`의 승계 대상 아카이브와 오버레이 파일 객체를 각각 삭제한 경우 빌드가 중단되고 산출물과 매니페스트가 바뀌지 않는다.
  - `packing: group`의 승계 대상 아카이브와 오버레이 파일 객체를 각각 truncate해 실제 크기를 바꾸면 빌드가 중단되고 산출물과 매니페스트가 바뀌지 않는다.
  - `packing: group`의 승계 대상 아카이브와 오버레이 파일 객체의 크기는 유지한 채 바이트를 바꾸면 SHA-256 불일치로 빌드가 중단되고 산출물과 매니페스트가 바뀌지 않는다.
  - `packing`을 `group`에서 `file`로 바꾸고 버전을 두면 중단되고 그룹 버전을 올리라는 안내가 나온다. `compression`도 같다.
  - 그룹의 마지막 파일을 지우면 `archive` 없이 빈 `entries`로 기록된다.
- 완료 조건 연결: 3, 4, 7, 8, 14, 15, 16, 19

### P7. `packing: file` 빌드

- 수행:
  - 첫 빌드와 그룹 버전 증가 시 모든 파일 객체를 쓴다.
  - 같은 그룹 버전에서는 변경·신규 파일 객체만 쓴다.
  - `BuildCommand`의 공통 승계 무결성 검사로 변경되지 않아 그대로 승계할 파일 객체를 검증한다.
  - 각 파일의 요청 버전과 output의 마지막 리비전을 비교해 실제 리비전을 확정한다.
  - 모든 엔트리를 `source: file`로 기록한다.
- 검증:
  - 최초 빌드에 아카이브가 없다.
  - 파일 하나 수정 시 그 객체 하나만 추가되고 다른 엔트리는 유지된다.
  - compression none/zstd 양쪽의 stored size와 checksum이 실제 파일과 같다.
  - 삭제 후 같은 그룹 버전으로 다시 추가한 파일이 기존 마지막 객체와 다르면 마지막 리비전 + 1로 생성되고 그룹 버전은 yaml과 같다.
  - 변경되지 않아 승계할 파일 객체를 각각 삭제, truncate, 동일 크기 바이트 변조한 경우 빌드가 중단되고 산출물과 매니페스트가 바뀌지 않는다.
- 완료 조건 연결: 6, 11, 12, 16, 19

### P8. CLI 요약과 전체 인수 테스트

- 수행:
  - 그룹별 요약과 합계를 출력한다.
  - 파일 리비전 자동 증가가 있으면 모든 정상 요약 뒤에 그룹 id, 경로, 요청 버전, 실제 버전을 경고 한 묶음으로 출력한다.
  - 사용자가 조치할 수 있는 실패에는 원인과 대상 경로/그룹을 포함한다.
  - 산출물 작성 후 매니페스트를 같은 디렉터리의 임시 파일에서 완성해 마지막에 원자적으로 교체한다. 성공 경로가 아니거나 후보 작성·교체에 실패하면 기존 매니페스트를 유지한다.
- 검증:
  - PRD 예시의 최초→수정→재수정→추가→삭제→버전 증가 흐름을 하나의 임시 Git 저장소에서 순서대로 실행한다.
  - 각 단계의 출력 파일 집합, 매니페스트, 요약 값이 기대와 같다.
  - 자동 증가 항목이 여러 개여도 경고 헤더는 마지막에 한 번만 출력되고 모든 조정 항목이 포함된다.
  - source 아래 tracked 파일이 dirty 상태이면 산출물과 매니페스트가 바뀌지 않고, untracked 파일이나 source 바깥 변경만 있으면 정상 빌드된다.
  - 매니페스트 후보 작성 전에 실패시키면 기존 `manifest.json`의 바이트와 `sourceCommit`이 직전 성공 빌드 그대로다.
  - 동일한 후보 산출물을 일부 쓴 실패 상태에서 재실행하면 해당 산출물을 재사용하고 실패 구간의 변경을 반영한다.
  - 실패 후 같은 그룹 버전의 아카이브 후보가 달라지면 중단되고, yaml 그룹 버전을 올려 재실행하면 해당 그룹의 현재 `HEAD`와 다른 증분 그룹의 실패 구간 변경이 모두 반영된다.
- 완료 조건 연결: 1~12, 18, 19, 20

### P9. NuGet tool 패키징·RID별 실행·게시 검증

- 수행:
  - `.github/workflows/dotnet.yml`에서 GitHub Actions 매트릭스(`windows-latest`, `ubuntu-latest`, `macos-26`)로 restore, test, pack, 로컬 tool 설치, smoke run을 수행한다. 각 러너는 `win-x64`, `linux-x64`, `osx-arm64`에 대응하며, 로컬에 세 운영체제 환경이 없으므로 CI가 유일한 수행 수단이다.
  - smoke run은 `artifacts`의 `GamePatchKit.Cli` 패키지를 임시 tool path에 설치한 `gpk`로 실행하며 `compression: zstd`를 포함해 RID별 네이티브 라이브러리 로딩까지 확인한다.
  - 테스트용 임시 저장소는 `core.autocrlf=false`로 만든다. Windows 러너의 줄바꿈 변환이 원본 바이트를 바꾸면 같은 커밋에서도 payload가 달라진다.
  - GitHub Release 게시 이벤트에서는 release tag 버전으로 다시 pack하고, 매트릭스 성공 후 `NUGET_API_KEY`를 사용해 nuget.org에 `--skip-duplicate`로 게시한다.
- 검증:
  - 각 환경에서 로컬 NuGet 패키지 설치와 `gpk build`가 종료 코드 0으로 끝나고 매니페스트와 산출물이 생성된다.
  - 다른 환경에서 만든 동일 입력의 매니페스트 계약과 해제 결과가 같다.
  - release workflow에서 게시 job은 세 환경 검증에 의존하고, package id·version·tool command가 `GamePatchKit.Cli`·release tag 버전·`gpk`와 일치한다.
- 완료 조건 연결: 13

## 17. 테스트 추적성

| PRD 완료 조건 | 대표 테스트 | 수준 |
| --- | --- | --- |
| 1. 엔트리가 있는 group 최초 빌드 | `Build_Group_FirstBuild_CreatesSingleArchive` | 통합 |
| 2. offset·length 원본 일치 | `Build_Group_ArchiveRangesMatchSourceFiles` | 통합 |
| 3. 수정 파일만 overlay | `Build_Group_ChangedFileCreatesOneOverlay` | 통합 |
| 4. no-op 동일성 | `Build_UnchangedHeadCreatesNoArtifactsAndKeepsManifestBytes` | 통합 |
| 5. group 버전 증가·감소 | `Build_GroupVersionIncreaseRebuildsArchiveAndResetsRevisions`<br>`Build_GroupVersionDecreaseFailsWithoutOutputChanges` | 통합 |
| 6. file packing 증분 | `Build_FilePacking_ChangedFileCreatesOneObject` | 통합 |
| 7. 파일 삭제 | `Build_DeletedFileRemovesManifestEntry` | 통합 |
| 8. 파일 추가 | `Build_AddedFileStartsAtRevisionZero` | 통합 |
| 9. source 범위 tracked dirty 중단 | `Build_TrackedDirtyUnderSourceFailsWithoutOutputChanges`<br>`Build_UntrackedUnderSourceIsIgnored`<br>`Build_TrackedChangeOutsideSourceIsIgnored`<br>`Build_UntrackedConfigurationFailsWithoutArtifacts` | 통합 |
| 10. source·commit 기록과 비교 | `Build_RecordsSourcePathAndHeadAndUsesPreviousCommitForDiff`<br>`Build_DifferentSourceWithSameOutputFailsWithoutChanges` | 통합 |
| 11. none 원본 일치 | `ArtifactWriter_NonePreservesBytes` | 단위 |
| 12. checksum 일치 | `ArtifactWriter_ChecksumMatchesStoredBytes` | 단위 |
| 13. NuGet tool 세 RID 실행·게시 | `PackInstallAndSmoke_<rid>`<br>`ReleasePublishesAfterRidMatrix` | CI 매트릭스 |
| 14. 설정 변경 중단 | `Build_PackingOrCompressionChangeWithSameVersionFails` | 통합 |
| 15. 빈 그룹 | `Build_EmptyGroupOmitsArchiveAndWritesEmptyEntries` | 통합 |
| 16. 조건부 이전 커밋 검증·승계 산출물 무결성 | `Build_MissingPreviousCommitWithIncrementalGroupFailsWithoutOutputChanges`<br>`Build_MissingPreviousCommitWithOnlyFullBuildGroupsSucceeds`<br>`Build_GroupPacking_MissingInheritedArtifactFailsWithoutOutputChanges`<br>`Build_GroupPacking_TruncatedInheritedArtifactFailsWithoutOutputChanges`<br>`Build_GroupPacking_CorruptedInheritedArtifactFailsWithoutOutputChanges`<br>`Build_FilePacking_MissingInheritedObjectFailsWithoutOutputChanges`<br>`Build_FilePacking_TruncatedInheritedObjectFailsWithoutOutputChanges`<br>`Build_FilePacking_CorruptedInheritedObjectFailsWithoutOutputChanges` | 통합 |
| 17. source 저장소 내부 output 거부 | `Build_OutputInsideSourceRepositoryFailsWithoutArtifacts` | 통합 |
| 18. 아카이브 재사용·그룹 버전 충돌 | `Build_ExistingArchiveWithSameBytesIsReused`<br>`Build_ExistingArchiveWithDifferentBytesFailsWithoutOutputChanges` | 통합 |
| 19. 파일 리비전 충돌 자동 증가 | `Build_ExistingFileWithSameBytesReusesHighestRevisionAndWarnsWhenAdjusted`<br>`Build_ExistingFileWithDifferentBytesUsesNextRevisionAndWarnsOnce` | 통합 |
| 20. 실패 후 재실행과 수동 그룹 버전 복구 | `ManifestStore_WriteAtomicallyReplacesManifest`<br>`Build_FailedRunKeepsPreviousManifest`<br>`Build_RerunAfterFailureReusesMatchingArtifacts`<br>`Build_RerunWithChangedArchiveRequiresGroupVersionBumpAndIncludesFailedRange` | 단위·통합 |
| 21. 이전 매니페스트 관계 검증 | `ManifestStore_RejectsInvalidSourcePath`<br>`ManifestStore_RejectsDuplicateGroupsOrEntries`<br>`ManifestStore_RejectsInvalidArtifactNameOrVersion`<br>`ManifestStore_RejectsInvalidSourceFields`<br>`ManifestStore_RejectsInvalidChecksumOrNegativeSize`<br>`ManifestStore_RejectsOutOfRangeOrOverlappingArchiveLayout` | 단위 |

테스트 이름은 구현 시 실제 대상 타입에 맞춰 조정할 수 있지만, 각 완료 조건을 검증하는 시나리오는 삭제하지 않는다.

## 18. 완료 정의

- PRD 완료 조건 21개가 모두 자동 테스트 또는 RID별 smoke 검증에 연결되어 있다.
- `dotnet restore`, `dotnet build`, `dotnet test`가 성공한다.
- `win-x64`, `linux-x64`, `osx-arm64` 네이티브 환경에서 zstd 빌드가 실행된다.
- `GamePatchKit.Cli`가 framework-dependent NuGet .NET tool로 패키징되어 세 환경에서 `gpk` 명령으로 설치·실행되고, GitHub Release 게시 workflow가 RID 매트릭스 성공 후 nuget.org에 게시한다.
- 동일 HEAD에서 다시 실행했을 때 새 산출물이 없고 매니페스트 바이트가 같다.
- 증분 전제 위반 세 가지가 모두 산출물을 쓰기 전에 중단되고 `--output`을 바꾸지 않는다.
- 현재 범위에서 제외한 패치 산출물 업로드·런타임·서명·정리·병렬 처리·재시도·로깅 코드가 들어오지 않는다.
- 구현 diff의 모든 파일이 PRD 요구사항, 테스트, 또는 필수 프로젝트 연결 코드로 추적된다.

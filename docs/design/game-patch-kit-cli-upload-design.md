# GamePatchKit CLI 업로드 개발 계획

> 기준 문서: [`docs/prd/gamepatch-kit-cli-upload-prd.md`](../prd/gamepatch-kit-cli-upload-prd.md)
>
> 선행 계획: [`game-patch-kit-cli-release-version-design.md`](game-patch-kit-cli-release-version-design.md)
>
> 상태: 구현 전 합의용 초안

## 1. 기능 요약

`gpk upload` 명령으로 `--output` 폴더의 패치 데이터를 Supabase Storage에 게시한다. 마지막 업로드 성공 매니페스트를 로컬에 보관해 새 산출물만 고르고, 산출물은 upsert하되 세대 매니페스트는 create-only로 게시한 뒤 성공 상태를 갱신한다.

## 2. 가정과 확인 필요 사항

### 가정

- Supabase Pro 또는 Team 프로젝트를 사용하고 전역·버킷 파일 제한을 가장 큰 산출물보다 크게 설정한다. 각 산출물은 resumable upload의 객체당 50 GB 상한 이하여야 한다.
- 하나의 `--output`은 하나의 고정된 Supabase 프로젝트·버킷에만 게시한다.
- 지정된 수동 배포 환경 한 곳만 게시하며 같은 패치 데이터 프로젝트의 `build`·`verify`·`upload` 흐름을 동시에 실행하지 않는다. 업로드 선검증부터 로컬 상태 교체까지 다른 프로세스가 같은 output의 매니페스트·산출물을 수정하지 않고, 원격 객체도 다른 프로그램이 수정하거나 삭제하지 않는다.
- `<output>/.gpk-upload-state.json`이 산출물 델타의 마지막 성공 상태이며, 없거나 읽을 수 없으면 산출물을 전량 upsert한다. 원격 세대 매니페스트는 create-only 업로드가 중복 오류로 거부됐을 때만 정확한 바이트 비교를 위해 읽는다.
- `GPK_SUPABASE_URL`은 `https://<project-ref>.storage.supabase.co/storage/v1` 형식의 직접 Storage API URL이다.
- `GPK_SUPABASE_KEY`는 `sb_secret_...` key만 허용하고 `apikey` 헤더로 전달한다.
- 버킷 이름, 현재 매니페스트가 참조하는 모든 산출물과 세대 매니페스트의 원격 경로는 첫 Storage 호출 전에 허용 문자 규칙으로 검증한다.
- 소스 저장소와 분리된 private Git 저장소가 성공적으로 게시된 `manifest.json`과 `.gpk-upload-state.json` 두 상태 파일만 보존한다.
- 지정된 배포 환경은 전체 Git 이력을 유지하며 이전 성공 `sourceCommit`과 재실행 대상 SHA를 모두 checkout할 수 있다.
- 첫 Storage 호출이 시작된 뒤 실패하면 로컬 `manifest.json.sourceCommit`의 정확한 SHA에서 같은 CLI·압축 구현 버전으로 재실행하고, 완료 전에는 더 새로운 SHA를 게시하지 않는다.
- 패치 데이터 배포에는 GitHub Actions를 사용하지 않는다. CLI NuGet 패키지 배포용 workflow는 별도 범위다.
- [`releaseVersion` 계획](game-patch-kit-cli-release-version-design.md)의 R1~R3가 먼저 완료된다.

### 확인 필요

없음. 요금제, 산출물 upsert, 세대 매니페스트 create-only, 단일 수동 게시자, SHA 재실행, 상태 Git 저장소, 원격 경로 문자, URL·키 전달 방식은 확정됐다.

## 3. 요구사항 정리

### 입력

- 명령: `gpk upload --output <패치 데이터 폴더> [--env-file <환경 변수 파일>]`
- 설정: `GPK_SUPABASE_URL`, `GPK_SUPABASE_KEY`, `GPK_SUPABASE_BUCKET`
- 현재 데이터: `<output>/manifest.json`과 참조 산출물
- 이전 업로드 성공 상태: `<output>/.gpk-upload-state.json`; 없거나 읽을 수 없으면 `null`

### 출력

- 원격 객체: 선택한 산출물, `manifests/<releaseVersion>.json`
- 로컬 상태: 모든 원격 업로드 성공 뒤 현재 매니페스트로 교체한 `.gpk-upload-state.json`
- 콘솔 요약: 올린 산출물 수와 바이트, 건너뛴 산출물 수, 세대 매니페스트, 게시한 `releaseVersion`
- 종료 코드: 성공 0, 중단 0이 아닌 값

### 제약

- `Supabase.Storage` 2.7.0만 참조하고 메타 패키지 `Supabase`는 참조하지 않는다.
- 산출물은 파일 경로 `UploadOrResume` 오버로드와 `Upsert=true`를 사용한다.
- 세대 매니페스트는 일반 파일 업로드와 `Upsert=false`로 생성한다. 중복 객체 오류일 때만 원격 바이트를 내려받아 로컬 `manifest.json`과 정확히 비교하고, 동일하면 재사용하며 다르면 버전 충돌로 중단한다.
- URL은 직접 Storage API URL이어야 하며 일반 프로젝트 URL은 입력 오류로 거부한다.
- `GPK_SUPABASE_KEY`가 `sb_secret_`로 시작하지 않으면 네트워크 요청 전에 거부한다. 유효한 key는 `Authorization: Bearer`로 보내지 않고 `apikey` 헤더로만 전달한다.
- `sb_secret_...` key는 RLS를 우회한다. publishable·legacy `anon`·사용자 JWT를 이용한 제한 권한 업로드는 지원하지 않는다.
- 키 값은 표준 출력·표준 에러·예외 메시지에 나타나지 않는다.
- `GPK_SUPABASE_BUCKET`은 설정 해석 단계에서는 필수·비어 있지 않은 값인지만 확인한다. 문자와 세그먼트 규칙은 객체 경로 선검증 단계에서 다른 모든 원격 경로와 함께 검사한다.
- 원격 객체 경로의 각 `/` 세그먼트에는 ASCII 영문 대소문자, 숫자, `.`, `_`, `-`만 허용하고, 빈 세그먼트와 `.`·`..`는 거부한다.
- 버킷 이름, 현재 매니페스트의 모든 산출물 경로와 세대 매니페스트 경로가 유효한지 확인하기 전에는 Storage client를 호출하지 않는다.
- TUS의 6 MiB 청크는 객체당 50 GB와 전역·버킷 파일 제한을 우회하지 않는다. 모든 산출물은 첫 Storage 호출 전에 이 상한을 검증한다.
- 원격 산출물은 다운로드하거나 조회하지 않는다. 원격 세대 매니페스트 읽기는 create-only 중복 충돌 판정에만 허용하고 업로드 계획에는 사용하지 않는다.
- 같은 `--output`을 사용하는 `build`·`verify`·`upload`는 지정된 수동 배포 스크립트가 직렬화하고 CLI 내부 락은 추가하지 않는다.
- 성공 상태의 Git 보존, 상태 복원, 소스 SHA checkout과 버전 포인터 갱신은 외부 운영 스크립트 책임이며 CLI가 Git이나 Postgres를 호출하지 않는다.

## 4. 실행 방식

- [x] skill 단독 (`csharp-feature-architect` skill만 사용)
- [ ] agent + skill

선택 사유: 단일 CLI 프로젝트 안의 명령 하나이며, 외부 SDK 경계 하나 외에는 기존 매니페스트·파일 구조를 그대로 사용한다.

## 5. 가장 단순한 구조 후보

- `UploadSettingsResolver`가 환경 변수와 env 파일을 해석한다.
- `ManifestStore`가 기존 매니페스트 파서와 원자적 쓰기를 재사용해 `.gpk-upload-state.json`을 읽고 쓴다.
- 기존 `RelativePathValidator`에 원격 객체 경로 허용 문자 검사를 추가한다.
- `UploadCommand`가 버킷과 모든 원격 경로를 한 번에 먼저 검증하고, 현재 매니페스트와 로컬 성공 상태를 비교해 새 이름만 선택한 뒤 게시 순서를 실행한다.
- `SupabaseUploadStorage` 한 클래스가 `Supabase.Storage` 초기화, 산출물 upsert와 세대 매니페스트 create-or-verify를 담당한다.
- `UploadCommand`의 순서와 실패 시 상태 보존을 자동 검증하기 위해 외부 SDK 경계에 `IUploadStorage` 하나만 둔다.
- 새 레이어나 DI 컨테이너는 만들지 않고 기존 `GamePatchKit.Cli` 폴더에 배치한다.

## 6. 단순 구조 실패 조건

`UploadCommand`가 `Supabase.Storage.Client`를 직접 만들면 산출물 → 세대 매니페스트 순서와 중간 실패 시 후속 업로드·상태 기록 금지를 자동 검증할 수 없다.

이 순서는 소비자가 완성되지 않은 릴리스를 보지 않게 하는 완료 조건이므로 외부 호출만 감싸는 `IUploadStorage`를 도입한다. 산출물 upsert와 불변 세대 매니페스트 게시의 계약이 다르므로 의미가 분명한 메서드 두 개로 제한한다.

그 밖의 단순 구조 실패 조건은 없다.

## 7. 책임/도메인 분해

| 책임 단위 | 종류 | 설명 |
| --- | --- | --- |
| `UploadArguments` | Input model | `--output`과 선택 `--env-file` 파싱 결과 |
| `UploadSettings` | Input model | 직접 Storage URL·API key·버킷 |
| `UploadSettingsResolver` | Loader | 환경 변수와 env 파일 우선순위, 누락 값, URL·secret key 형식 해석 |
| `ManifestArtifact` | DTO | 업로드 대상의 `name`·`storedSize`·`checksum` |
| `PatchManifest.EnumerateArtifacts` | DTO 조회 | 아카이브와 `source: file` 객체 열거 |
| `ManifestStore` | Local state store | 현재 매니페스트 검증과 업로드 성공 상태 읽기·원자적 교체 |
| `RelativePathValidator` | Validator | 기존 상대 경로 검증과 별도로 원격 객체 경로의 엄격한 ASCII 허용 문자 검사 |
| `IUploadStorage` | External boundary | 산출물 upsert와 세대 매니페스트 create-or-verify |
| `SupabaseUploadStorage` | SDK adapter | Storage client 초기화, TUS 산출물 업로드, 불변 매니페스트 게시·중복 비교 |
| `UploadCommand` | Application service | 로컬 선검증, 델타 선택, 게시 순서, 성공 상태 갱신 |
| `UploadSummary` | Output model | 올린 수·바이트, 건너뛴 수, 게시한 릴리스 버전 |

Entity, Value Object, Domain Event는 만들지 않는다. 업로드 명령의 입력·출력·외부 경계만 분리한다.

## 8. 적용 패턴과 정당화

없음.

`IUploadStorage`는 교체 가능한 스토리지 전략을 위한 패턴이 아니라 Supabase SDK 호출을 한 곳에 제한하는 외부 경계다. Factory, Strategy, Mediator를 도입하지 않는다.

## 9. 인터페이스 생성 근거

| 인터페이스 | 근거 |
| --- | --- |
| `IUploadStorage` | 외부 SDK 호출을 대역으로 바꿔 게시 순서, 중간 실패 시 후속 호출 금지, 세대 중복의 동일 바이트 재사용·다른 바이트 중단과 로컬 상태 미갱신을 자동 검증해야 한다. 구현은 `SupabaseUploadStorage` 하나이며 두 게시 계약만 노출한다. |

추가 인터페이스는 만들지 않는다. 설정 loader와 로컬 상태 저장소는 정적 파일 절차로 충분하다.

## 10. 인터페이스·클래스 시그니처

```csharp
internal sealed record UploadArguments(string OutputPath, string? EnvFilePath);

internal static class ArgumentsParser
{
    public static UploadArguments ParseUpload(string[] arguments);
}

internal sealed record UploadSettings(string StorageUrl, string Key, string Bucket);

internal static class UploadSettingsResolver
{
    public static UploadSettings Resolve(string? envFilePath);
}

internal sealed record ManifestArtifact(string Name, long StoredSize, string Checksum);

internal sealed class PatchManifest
{
    public IEnumerable<ManifestArtifact> EnumerateArtifacts();
}

internal static class ManifestStore
{
    public static PatchManifest? ReadUploadState(string outputPath);
    public static void WriteUploadStateAtomically(string outputPath);
}

internal static class RelativePathValidator
{
    public static bool IsRemotePathSegment(string? segment);
    public static bool IsRemoteObjectPath(string? path);
}

internal interface IUploadStorage
{
    Task UpsertArtifactAsync(string localPath, string remotePath);
    Task CreateOrVerifyManifestAsync(string localPath, string remotePath);
}

internal sealed class SupabaseUploadStorage : IUploadStorage
{
    public SupabaseUploadStorage(UploadSettings settings);
    public Task UpsertArtifactAsync(string localPath, string remotePath);
    public Task CreateOrVerifyManifestAsync(string localPath, string remotePath);
}

internal sealed record UploadSummary(
    int UploadedCount,
    long UploadedBytes,
    int SkippedCount,
    int ReleaseVersion);

internal sealed class UploadCommand
{
    public UploadCommand(IUploadStorage storage, string bucket);
    public Task<UploadSummary> ExecuteAsync(string outputPath);

    internal static IReadOnlyList<ManifestArtifact> PlanArtifacts(
        PatchManifest current,
        PatchManifest? uploaded);
}

public static class Program
{
    public static Task<int> Main(string[] arguments);
    internal static Task<int> RunAsync(string[] arguments, TextWriter output, TextWriter error);
}
```

`PlanArtifacts`는 `uploaded`가 `null`이면 현재 참조 산출물 전체를 반환한다. 값이 있으면 이전 성공 상태의 `name` 집합에 없는 현재 산출물만 반환한다. 원격 상태나 바이트는 산출물 계획에 사용하지 않는다.

`ReadUploadState`는 상태 파일이 없거나 같은 매니페스트 규칙으로 읽을 수 없으면 `null`을 반환한다. 업로드 상태는 정확성의 원본이 아니라 재전송을 줄이는 로컬 성공 기록이므로, 손상 시 전량 upsert로 복구한다.

`IsRemotePathSegment`는 값이 비어 있지 않고 ASCII 영문 대소문자, 숫자, `.`, `_`, `-`로만 구성되며 `.`·`..`가 아닌지 확인한다. `IsRemoteObjectPath`는 `/`로 나눈 모든 값에 이 검사를 적용한다. `UploadSettingsResolver`는 버킷의 문자 형식을 검사하지 않는다. `UploadCommand`가 버킷 이름, 현재 매니페스트의 모든 산출물 `name`과 `manifests/<releaseVersion>.json`을 한 단계에서 검사해 잘못된 항목을 모두 모은 뒤 첫 Storage 호출 전에 실패한다. 기존 `RelativePathValidator.IsNormalized`와 `gpk build`의 경로 허용 범위는 좁히지 않는다.

`Program`은 같은 `UploadSettings.Bucket`을 `SupabaseUploadStorage`와 `UploadCommand`에 전달한다. `UploadCommand`가 생성자로 받은 버킷을 다른 원격 경로와 함께 검증하므로 별도 설정 검증 단계에서 먼저 실패하지 않는다.

로컬 산출물 검증은 존재 여부와 실제 크기 대 `storedSize`뿐 아니라 `storedSize <= 50 GB`를 확인한다. 하나라도 실패하면 모든 원격 게시를 시작하지 않는다.

`WriteUploadStateAtomically`는 현재 `manifest.json` 바이트를 같은 디렉터리의 임시 파일에 쓴 뒤 `.gpk-upload-state.json`으로 교체한다. 세대 매니페스트 업로드까지 성공한 뒤에만 호출한다.

`SupabaseUploadStorage` 생성자는 설정만 보관하고 Storage client와 버킷 handle은 첫 게시 메서드 호출 때 만든다. 따라서 잘못된 버킷과 객체 경로를 모두 모으는 `UploadCommand` 선검증보다 SDK의 버킷 처리가 먼저 실행되지 않는다. client를 만들 때는 `StorageUrl`의 끝 `/`를 제거하고 `apikey` 헤더를 사용한다. `UpsertArtifactAsync`는 파일 경로 `UploadOrResume`, `Upsert=true`, `application/octet-stream`을 사용한다. `CreateOrVerifyManifestAsync`는 일반 파일 업로드, `Upsert=false`, `application/json`을 사용한다. 실제 중복 객체 오류에서만 기존 객체를 다운로드해 로컬 파일과 바이트 단위로 비교한다. 같으면 정상 반환하고 다르면 `BuildException`으로 중단하며, 중복 이외의 업로드·다운로드 오류는 그대로 전달한다.

## 11. 파일·폴더 배치 제안

```text
src/GamePatchKit.Cli/
├── Program.cs                    (변경: async 전환, 설정 해석과 upload 연결)
├── CommandArguments.cs           (변경: UploadArguments, ParseUpload)
├── PatchManifest.cs              (변경: ManifestArtifact, EnumerateArtifacts)
├── ManifestStore.cs              (변경: 업로드 성공 상태 읽기·원자적 쓰기)
├── RelativePathValidator.cs       (변경: 원격 객체 경로 허용 문자 검증)
├── VerifyCommand.cs              (변경: EnumerateArtifacts 사용)
├── UploadSettings.cs             UploadSettings, UploadSettingsResolver
├── UploadStorage.cs              IUploadStorage, SupabaseUploadStorage
└── UploadCommand.cs              UploadCommand, UploadSummary

tests/GamePatchKit.Cli.Tests/
├── TestBuildCommand.cs           (변경: Program.RunAsync await 전환)
├── TestCommandArguments.cs       (변경: upload 인자, Program.RunAsync await 전환)
├── TestVerifyCommand.cs          (변경: Program.RunAsync await 전환, 공용 산출물 열거 회귀)
├── TestUploadSettings.cs         신규
└── TestUploadCommand.cs          신규, 테스트 내부 fake storage 포함
```

`releaseVersion` 변경 파일과 테스트는 [별도 계획](game-patch-kit-cli-release-version-design.md)에만 둔다. upload 계획에서 중복 정의하지 않는다.

새 생산 코드 파일은 3개다.

- `UploadSettings.cs`: 네트워크 없이 검증하는 입력 해석 책임이다.
- `UploadStorage.cs`: 외부 SDK와 `apikey`, 산출물 upsert, 세대 매니페스트 create-or-verify를 한 곳에 격리한다.
- `UploadCommand.cs`: 기존 `BuildCommand.cs`·`VerifyCommand.cs`와 같은 명령 단위 배치를 따른다.

## 12. 도입하지 않은 구조

- 원격 매니페스트 parser와 의미 비교: 중복 세대 확인은 원격 파일을 내려받아 로컬 파일과 바이트만 비교하면 된다.
- 업로드 상태 전용 DTO·스키마: 기존 `PatchManifest` 전체를 그대로 저장한다.
- Storage factory와 DI 컨테이너: production 구현 하나를 `Program`이 직접 생성하면 충분하다.
- 원격 산출물 존재·checksum 검사: 단일 게시자와 산출물 upsert 전제에서 제외한다. create-only 세대 매니페스트 중복의 바이트 비교만 수행한다.
- 락과 세마포어: 지정된 수동 배포 스크립트가 한 환경에서 전체 흐름을 직렬화한다.
- `manifest.json` 바이트 snapshot: 같은 output의 동시 build·수정이 금지되어 검증한 파일이 업로드 전에 바뀌지 않는다는 운영 전제를 사용한다.
- 업로드 재시도·백오프·타임아웃 조정: 실패 후 명령 재실행으로 처리한다.
- 병렬 업로드, 진행률 콜백, 캐시 설정, `CancellationToken`: 현재 요구사항이 아니다.
- Postgres client: 포인터 갱신은 upload와 상태 Git push 성공 후 지정된 수동 배포 스크립트가 수행한다.
- Supabase 요금제와 버킷 설정 조회: 운영 사전 조건이며 Management API 의존성을 추가하지 않는다.
- publishable·legacy `anon`·사용자 JWT 인증과 Storage RLS: 업로드는 `sb_secret_...`만 사용한다.
- 원격 루트 `manifest.json`: 소비자와 업로더 모두 사용하지 않으므로 게시하지 않는다.
- 상태 Git client, 소스 checkout과 산출물 복원·백업 코드: 두 상태 파일의 저장·복원과 SHA checkout은 외부 운영 스크립트 책임이며 별도 산출물 백업은 범위 밖이다.
- CLI `--commit`·`--rebuild` 옵션: `manifest.json.sourceCommit`과 Git checkout으로 같은 SHA를 재현한다.
- 패치 데이터 배포용 GitHub Actions: 지정된 환경에서 수동으로 배포한다. CLI NuGet 패키지 workflow는 별도다.

## 13. 단순화 자가 검토 결과

- 새 인터페이스 수: 1
- 새 생산 코드 타입 수: 8 (`UploadArguments`, `UploadSettings`, `UploadSettingsResolver`, `ManifestArtifact`, `IUploadStorage`, `SupabaseUploadStorage`, `UploadSummary`, `UploadCommand`)
- 새 생산 코드 파일 수: 3
- 새 폴더 계층 증가: 0
- 적용 패턴 수: 0
- 단일 구현 인터페이스: `IUploadStorage` 1개 — 게시 순서와 실패 시 상태 보존 자동 검증을 위해 정당화됨
- 요구사항에서 직접 도출되지 않은 도메인 객체: 없음
- 종합 판정: 외부 호출 경계 하나만 추가하고 원격 조회를 세대 중복 시 바이트 비교로 제한하며 별도 상태 스키마를 두지 않는 단순 구조

## 14. 위임 다음 단계

- 선행 구현: [`releaseVersion` 계획](game-patch-kit-cli-release-version-design.md)의 R1~R3
- upload 구현: 일반 C# 코딩 작업으로 진행하며 `csharp-coding-standards`를 적용한다.
- 테스트: 기존 xUnit 구조에서 fake storage를 테스트 파일 내부에 두고 새 프레임워크나 mocking 의존성을 추가하지 않는다.
- 구현 완료 후 `docs/cli/upload.md`에 사용법, Pro·Team 사전 조건, secret key, 원격 경로 문자, 로컬 성공 상태, 상태 Git 저장소, 동시 실행 금지와 실패 후 재실행 절차를 기록한다.

## 15. 핵심 처리 흐름

```text
gpk upload --output <폴더> [--env-file <경로>]
  → 인자 파싱
  → 설정 해석과 직접 Storage URL·secret key 검증
  → 로컬 manifest.json 읽기 + 관계 검증
  → 로컬 산출물 전체 존재·storedSize·50 GB 상한 선검증
  → 버킷 + 모든 산출물 name + manifests/<releaseVersion>.json 원격 경로 일괄 선검증
  → .gpk-upload-state.json 읽기; 없거나 읽을 수 없으면 null
  → PlanArtifacts(current, uploaded)
  → 선택 산출물을 순서대로 upsert
  → manifests/<releaseVersion>.json create-only
      → 중복이면 원격 바이트와 로컬 manifest.json 비교
      → 같으면 재사용, 다르면 버전 충돌
  → .gpk-upload-state.json 원자적 교체
  → 요약과 releaseVersion 출력
```

어느 원격 업로드에서든 실패하면 그 뒤의 호출과 로컬 상태 교체를 수행하지 않는다. 이미 올라간 산출물은 다음 실행에서 같은 경로로 다시 upsert한다. 세대 매니페스트가 이미 같은 바이트로 생성돼 있으면 재사용하고, 다른 바이트면 절대 덮어쓰지 않는다.

로컬 상태 교체가 실패하면 원격 세대 매니페스트까지는 게시된 상태지만 명령은 실패한다. 같은 source commit에서 재실행하면 이전 로컬 상태에서 일부 산출물을 다시 upsert하고 동일한 원격 세대 매니페스트를 재사용한 뒤 상태를 맞춘다.

지정된 수동 배포 환경의 코드로 관리되는 운영 스크립트는 다음 순서를 따른다. 상태 저장소가 비어 있으면 실제 첫 배포로만 처리하고 복원·사전 `verify`를 생략한다.

```text
소스 저장소 전체 이력과 별도 private 상태 Git 저장소 준비
  → 게시할 정확한 source SHA checkout
  → manifest.json + .gpk-upload-state.json을 output에 복원
  → 깨끗한 output이면 이전 매니페스트 참조 산출물을 현재 Supabase 버킷에서 복원
  → gpk verify
  → gpk build
  → gpk verify
  → manifest.json.sourceCommit을 재실행 대상 SHA로 기록
  → gpk upload
  → 두 상태 파일을 상태 저장소의 단일 commit으로 push
  → Postgres 버전 포인터 갱신
```

첫 Storage 호출 전에 실패하면 원격 상태가 바뀌지 않았으므로 수정한 새 commit으로 다시 시작할 수 있다. 첫 Storage 호출이 시작된 뒤 실패하면 기록한 `manifest.json.sourceCommit`의 정확한 SHA를 checkout하고 같은 CLI·압축 구현 버전으로 위 흐름을 다시 수행한다. 상태 push가 실패한 경우도 포인터와 다음 source SHA를 진행하지 않고 같은 SHA를 재실행한다. CLI는 Git·동시성 제어·산출물 복원·별도 백업을 구현하지 않는다.

## 16. 테스트 경계

실제 TUS 프로토콜을 흉내 낸 HTTP 서버는 만들지 않는다. 자동 테스트는 production `SupabaseUploadStorage` 대신 테스트 파일 내부 fake `IUploadStorage`를 사용해 CLI가 소유한 판단과 순서를 검증한다.

자동 검증 범위:

- 설정 우선순위, 누락 값 전체 보고, env 파일 형식·부재, 직접 Storage URL과 `sb_secret_...` key 검증
- 첫 업로드, 동일 매니페스트 재실행, 증분 매니페스트의 산출물 계획
- 업로드 상태 부재·손상 시 전량 선택
- 로컬 산출물 부재·크기 불일치·50 GB 초과 시 storage 호출 0회
- 버킷 이름, 모든 현재 산출물과 세대 매니페스트 원격 경로의 허용·거부 문자, 잘못된 이름·경로 전체 보고와 storage 호출 0회
- 산출물 → 세대 매니페스트 호출 순서, 세대 신규 생성·동일 바이트 재사용·다른 바이트 충돌
- 원격 다운로드가 세대 create-only 중복 오류에서만 호출되는지 확인
- 각 산출물·세대 매니페스트 실패 시 후속 호출 금지와 로컬 상태 미변경
- 전체 성공 뒤에만 로컬 상태 교체
- 실패 출력의 key 미노출
- `TestBuildCommand`, `TestVerifyCommand`, `TestCommandArguments`의 기존 `Program.Run` 호출을 `await Program.RunAsync`로 전환한 뒤 기존 출력·종료 코드 회귀

실제 Supabase 검증 범위:

- Pro 또는 Team 프로젝트와 설정된 파일 제한에서 50 MB 초과·50 GB 이하 아카이브 업로드
- 직접 Storage API URL과 `sb_secret_...` key의 `apikey` 인증
- 최초·동일·증분·중단 후 같은 SHA 재실행과 `manifests/<releaseVersion>.json` create-only 확인
- Windows에서 여러 파일을 연속 업로드한 뒤 로컬 파일을 다시 열 수 있는지 확인해 `Supabase.Storage` 2.7.0 파일 경로 오버로드의 handle 누적 여부 점검

## 17. 단계별 구현 계획

선행 R1~R3 완료 후 `U1 → U2 → U3 → U4 → U5` 순서로 진행한다.

### U1. 인자·설정·권한 계약

- 수행:
  - `UploadArguments`, `ArgumentsParser.ParseUpload`, `UploadSettingsResolver`를 추가한다.
  - env 파일 우선순위와 누락 값 전체 보고를 구현한다.
  - `GPK_SUPABASE_URL`이 HTTPS 직접 Storage API URL이고 `/storage/v1`로 끝나는지 검증한다.
  - `GPK_SUPABASE_KEY`가 `sb_secret_`로 시작하는지 검증한다.
  - `docs/cli/upload.md` 초안에 Pro·Team, 파일 제한, `apikey`와 secret key 전용 계약을 기록한다.
- 검증:
  - `--output` 누락, `--source`, 중복 옵션, 값 누락을 거부한다.
  - 환경 변수와 env 파일 우선순위를 검증한다.
  - 일반 프로젝트 URL, HTTP URL, query·fragment가 있는 URL을 거부한다.
  - publishable·legacy key 등 `sb_secret_`가 아닌 값은 Storage 호출 전에 거부한다.
  - key 값이 오류 메시지에 나타나지 않는다.

### U2. 패키지 참조와 async 전환

- 수행:
  - `Directory.Packages.props`에 `Supabase.Storage` 2.7.0을 추가하고 CLI 프로젝트가 참조한다.
  - `Program.Main`과 `RunAsync`를 `Task<int>` 경로로 바꾸고 기존 `build`·`verify`는 동기 호출을 유지한다.
  - `IUploadStorage`와 `SupabaseUploadStorage`를 추가한다.
  - adapter 생성자는 설정만 저장하고 첫 게시 호출에서 직접 Storage URL과 `apikey` 헤더로 client를 지연 생성한다.
  - 산출물은 `UploadOrResume`·`Upsert=true`, 세대 매니페스트는 일반 업로드·`Upsert=false`를 사용한다. 세대 중복이면 다운로드한 원격 파일과 로컬 파일을 바이트 비교한다.
- 검증:
  - restore, build, 기존 test, pack이 통과한다.
  - `TestBuildCommand`, `TestVerifyCommand`, `TestCommandArguments`의 모든 `Program.Run` 호출부가 `await Program.RunAsync`로 전환되고 기존 결과를 유지한다.
  - 패키지 그래프에 불필요한 Supabase 메타 패키지가 없다.
  - 실제 프로젝트 검증 전까지 키 전달은 adapter 한 곳에만 존재한다.

### U3. 로컬 성공 상태와 업로드 대상 결정

- 수행:
  - `ManifestArtifact`와 `PatchManifest.EnumerateArtifacts`를 추가하고 `VerifyCommand`가 재사용한다.
  - `ManifestStore.ReadUploadState`와 `WriteUploadStateAtomically`를 추가한다.
  - `UploadCommand.PlanArtifacts`가 현재와 마지막 성공 매니페스트의 이름 차이만 계산한다.
  - 업로드 시작 전에 모든 로컬 산출물의 존재, 크기와 객체당 50 GB 상한을 검증한다.
  - `RelativePathValidator.IsRemoteObjectPath`를 추가하고, `UploadCommand`가 버킷 이름, 현재 매니페스트의 모든 산출물과 세대 매니페스트 경로를 한 선검증 단계에서 첫 Storage 호출 전에 검사한다.
- 검증:
  - 첫 상태와 손상 상태는 전체, 동일 상태는 없음, 증분 상태는 새 이름만 선택한다.
  - 상태 파일에는 현재 매니페스트만 있고 설정·키가 없다.
  - 상태 쓰기 실패는 기존 상태 바이트를 보존한다.
  - `storedSize`가 정확히 50 GB인 경계는 허용하고 50 GB를 넘으면 storage 호출 0회로 실패한다.
  - 안전한 ASCII 버킷·경로는 통과하고 공백·한글·URL 예약문자·역슬래시·빈 세그먼트·`.`·`..`가 있는 버킷과 경로는 모든 오류를 함께 보고한 뒤 storage 호출 0회로 실패한다.
  - 마지막 업로드 성공 상태에 있어 이번 실행에서 건너뛸 산출물의 경로도 검사한다.
  - `VerifyCommand` 회귀 테스트가 통과한다.

### U4. 게시 순서와 실패 상태 자동 검증

- 수행:
  - `UploadCommand`가 선택 산출물을 upsert한 뒤 세대 매니페스트 create-or-verify를 호출한다.
  - 모든 원격 호출이 성공한 뒤에만 로컬 성공 상태를 교체한다.
  - 테스트 내부 fake storage가 호출을 기록하고 세대 신규 생성·동일 원격 바이트·다른 원격 바이트와 지정 단계 실패를 표현하게 한다.
- 검증:
  - 최초·동일·증분 실행의 정확한 호출 목록을 확인한다.
  - 첫 게시에서 세대 매니페스트를 create-only로 만들고, 재실행의 같은 바이트는 재사용하며 다른 바이트는 기존 객체와 로컬 상태를 바꾸지 않고 중단한다.
  - `.gpk-upload-state.json`이 없는 상태에서 기존 `manifests/0.json`이 다른 바이트면 덮어쓰지 않고 중단한다.
  - 원격 세대 매니페스트 다운로드는 create-only 중복 오류에서만 발생한다.
  - 각 단계 실패 뒤에는 후속 원격 호출이 없고 상태가 이전 바이트로 남는다.
  - 상태 교체 실패 시 종료 코드가 0이 아니며 같은 source commit 재실행으로 완료된다.
  - 성공 요약과 `releaseVersion` 출력을 확인한다.

### U5. 실제 Supabase 검증과 사용자 문서 완료

- 수행:
  - Pro 또는 Team 테스트 프로젝트의 전역·버킷 제한을 테스트 파일보다 크게 설정한다.
  - 실제 secret은 지정된 배포 환경의 secret 저장소에서 주입하고 저장소에 기록하지 않는다.
  - `docs/cli/upload.md`에 사용법, 사전 설정, secret key, 원격 경로 문자, 상태 Git 저장소, 동시 실행 금지, `sourceCommit` SHA 기반 실패 복구와 수동 검증 결과를 완성한다.
- 검증:
  - 50 MB 초과·50 GB 이하 아카이브, 최초·동일·증분·중단 후 같은 SHA 재실행을 실제 버킷에서 확인한다.
  - `manifests/<N>.json` 세대 누적과 create-only 충돌 동작을 확인하고 원격 루트 `manifest.json`을 게시하지 않는지 확인한다.
  - 별도 상태 Git 저장소에 성공한 `manifest.json`과 `.gpk-upload-state.json`만 한 commit으로 보존하고 포인터보다 먼저 push하는 운영 절차를 확인한다.
  - 지정된 수동 배포 환경에서 동시에 두 배포를 시작하지 못하게 운영 스크립트가 직렬화하는지 확인한다.
  - 첫 Storage 호출 뒤 실패를 주입하고 기록한 `manifest.json.sourceCommit` SHA를 checkout해 같은 CLI·압축 구현 버전으로 완료한 뒤에만 다음 SHA를 게시하는지 확인한다.
  - Windows 다중 파일 업로드 후 file handle이 남지 않는지 확인한다.
  - 최종 restore, build, test, format, pack이 통과한다.

## 18. 테스트 추적성과 완료 정의

| PRD 완료 조건 | 대표 검증 | 수준 |
| --- | --- | --- |
| 1. 게시 순서와 상태 교체 | `Upload_FirstRunUsesRequiredOrderAndWritesStateLast` | 통합 |
| 2. 동일 실행 건너뛰기 | `Upload_SameStateSkipsArtifactsAndReusesIdenticalVersionedManifest` | 통합 |
| 3. 증분 델타 | `PlanArtifacts_IncrementalStateReturnsOnlyNewNames` | 단위 |
| 4. 상태 없음·손상 | `PlanArtifacts_MissingOrInvalidStateReturnsAllArtifacts` | 단위·통합 |
| 5. 50 MB 초과·50 GB 상한 | `ValidateArtifactSize_At50GbAllowed` + `ValidateArtifactSize_Over50GbRejected` + 실제 50 MB 초과 업로드 | 단위·수동 |
| 6. 설정 누락 | `UploadSettings_AllMissingNamesAreReportedTogether` | 단위 |
| 7. env 우선순위 | `UploadSettings_EnvFileOverridesEnvironment` | 단위 |
| 8. env 파일 부재 | `UploadSettings_MissingEnvFileFails` | 단위 |
| 9. secret key 전용·전달·미노출 | `UploadSettings_NonSecretKeyFails` + `Upload_FailureDoesNotExposeKey` + 실제 인증 | 단위·통합·수동 |
| 10. 로컬 산출물 선검증 | `Upload_InvalidArtifactsNeverCallStorage` | 통합 |
| 11. 로컬 매니페스트 검증 | `Upload_InvalidManifestNeverCallsStorage` | 통합 |
| 12. `--source` 거부 | `ParseUpload_RejectsSourceOption` | 단위 |
| 13. 패키지 참조 | restore + package graph + pack | 빌드 |
| 14. 세대 불변·누적 | `Upload_ExistingDifferentManifestFailsWithoutOverwrite` + 실제 두 세대 누적 | 통합·수동 |
| 15. 버전 출력 | `Upload_PrintsPublishedReleaseVersion` | 통합 |
| 16. 실패 상태 보존 | `Upload_FailureStopsAndKeepsPreviousState` | 통합 |
| 17. 순서·세대 충돌 자동 검증 | fake storage 호출 목록, 동일 세대 재사용, 다른 바이트 충돌과 단계별 실패 테스트 | 통합 |
| 18. URL·secret 인증 | URL 단위 테스트 + 실제 `sb_secret_...` 인증 | 단위·수동 |
| 19. 원격 경로 일괄 선검증 | `Upload_InvalidBucketAndRemotePathsAreReportedTogetherBeforeStorageCall` | 통합 |
| 20. 두 상태 파일 Git 보존 | 상태 저장소 복원·단일 commit·포인터 선행 순서 확인 | 운영 |
| 21. 동시 실행 금지·SHA 재실행 | 지정 환경의 운영 직렬화와 첫 원격 호출 후 같은 `sourceCommit` 재실행 절차 확인 | 운영 |
| 22. async 전환 회귀 | 세 기존 테스트 파일의 `Program.RunAsync` 호출 + 전체 test | 통합 |

다음 조건을 모두 만족하면 upload 구현 완료다.

- upload PRD 완료 조건 22개가 자동 테스트, 실제 Supabase 검증 또는 명시된 운영 절차에 연결된다.
- 로컬 성공 상태와 산출물 upsert, 세대 매니페스트 create-or-verify로 최초·동일·증분·실패 후 같은 SHA 재실행이 동작한다.
- 버킷, 모든 현재 산출물과 세대 매니페스트의 원격 경로가 한 선검증 단계에서 첫 Storage 호출 전에 허용 문자 규칙을 통과한다.
- 핵심 게시 순서와 실패 시 상태 보존이 fake storage를 사용한 자동 테스트로 고정된다.
- `Supabase.Storage`만 참조한 상태로 restore, build, test, format, pack이 통과한다.
- Pro 또는 Team 실제 프로젝트에서 50 MB 초과 파일, 50 GB 상한과 인증·권한을 검증한다.
- 별도 private 상태 Git 저장소에는 성공한 두 상태 파일만 보존하고, 지정된 수동 배포 환경에서 전체 게시 흐름을 동시 실행하지 않는다.
- 실패 경로 어디에서도 API key가 출력되지 않는다.
- CLI에 Postgres client, 업로드 계획용 원격 상태 조회, Git checkout, 재시도, 병렬 처리, 진행률, 캐시 정책 코드가 들어오지 않는다.

# GamePatchKit CLI 동기화 개발 계획

> 기준 문서: [`docs/prd/gamepatch-kit-distribution-prd.md`](../prd/gamepatch-kit-distribution-prd.md)
>
> 선행 계획: [`game-patch-kit-cli-upload-design.md`](game-patch-kit-cli-upload-design.md)
>
> 상태: 구현 전 합의용 초안

## 1. 기능 요약

`gpk sync` 명령으로 Supabase Postgres의 릴리스 버전 포인터를 조회하고, 로컬 폴더가 그 세대와 다르면 공개 Storage에서 세대 매니페스트와 없는 산출물만 내려받아 로컬 트리를 게시된 세대로 맞춘다. 받은 산출물은 `<output>/data` 아래에 원본 트리로 풀어 다른 서버가 압축을 모른 채 바로 읽게 한다. 스케줄러(cron 등)가 주기 실행하는 명령과 사람이 강제로 실행하는 명령이 같은 멱등 one-shot이며, 배타 파일 락으로 겹침만 방지한다.

## 2. 가정과 확인 필요 사항

### 가정

- 포인터는 Supabase Postgres의 `public.gamepatch_pointer` 테이블 한 행이다(버킷당 1행). 테이블·RLS 정책 생성은 문서화된 사전 조건 SQL을 운영자가 1회 실행하며, CLI는 테이블을 생성하지 않는다.
- 포인터 쓰기(첫 게시 시 upsert, 게시 후 갱신, 롤백)는 배포 스크립트의 몫이고 이 설계 범위 밖이다. `gpk sync`는 포인터를 읽기만 한다.
- 첫 게시 전에는 포인터 행이 없다. `releaseVersion` 0이 실제 첫 세대이므로 seed 행을 미리 넣지 않는다.
- 산출물과 세대 매니페스트는 공개 버킷의 불변 객체다. 같은 `name`은 같은 바이트라는 `build`/`upload` 계약을 신뢰한다.
- 소비 머신은 읽기용 publishable key만 가진다. secret key를 요구하지 않는다.
- 하나의 sync 대상 폴더는 하나의 프로젝트·버킷에만 연결된다. 같은 폴더에 대한 동시 sync는 배타 락으로 한 개만 진행되고, 로컬 파일시스템 기준이다(NFS 등 네트워크 파일시스템의 락 보장은 범위 밖).
- sync 대상 폴더는 소비 머신의 로컬 미러다. 게시자의 `--output`(상태 Git 저장소)과는 다른 머신·다른 폴더라는 운영 전제를 따른다.
- 로컬 트리를 읽는 소비 프로세스는 `manifest.json`을 읽고 `name`으로 산출물을 연다. `name`이 불변이라 sync 도중에도 이전 세대 읽기가 안전하다.
- 게시가 순서(산출물 → 세대 매니페스트 → 상태 Git push → 포인터 갱신)를 지켰다면, 포인터가 가리키는 세대의 모든 객체는 이미 올라가 있다. sync에서 그 객체의 404는 게시 절차 위반이며 재시도 없이 실패로 보고한다.

### 확인 필요

없음. 폴링 + 수동 강제 실행(같은 명령), 포인터 Postgres, 테이블 자동 생성 제외, 락 방식은 사용자와 확정됐다.

## 3. 요구사항 정리

### 입력

- 명령: `gpk sync --output <동기화 대상 폴더> [--env-file <환경 변수 파일>]`
- 설정 값(신규, 업로드용과 분리):

| 이름 | 형식 | 의미 |
| --- | --- | --- |
| `GPK_SUPABASE_PROJECT_URL` | `https://<project-ref>.supabase.co` (경로·쿼리 없음) | PostgREST와 공개 Storage URL의 공통 기반 |
| `GPK_SUPABASE_PUBLISHABLE_KEY` | `sb_publishable_` 접두사 | 포인터 조회용 읽기 key |
| `GPK_SUPABASE_BUCKET` | 원격 경로 세그먼트 허용 문자 | 공개 버킷 이름이자 포인터 행의 키 |

- `--env-file` 우선순위와 파싱 규칙은 `upload`와 동일하다(파일 값이 환경 변수를 덮는다).
- 원격 입력: `GET {PROJECT_URL}/rest/v1/gamepatch_pointer?bucket=eq.{bucket}&select=release_version`(포인터), `GET {PROJECT_URL}/storage/v1/object/public/{bucket}/{name}`(세대 매니페스트·산출물).

### 출력

- 성공(동기화): `동기화 완료: releaseVersion=<N> (이전: <M|없음>), downloaded=<K>, reused=<R>, downloadedBytes=<B>` + `데이터 해제: extracted=<E>, removed=<D>` — 종료 코드 0
- 성공(최신): `이미 최신입니다: releaseVersion=<N>` — 종료 코드 0
- 성공(락 겹침): `다른 sync가 진행 중입니다. 건너뜁니다.` — 종료 코드 0 (cron 겹침은 정상 시나리오이므로 실패로 알리지 않는다)
- 실패: 원인 메시지 후 종료 코드 1. 기존 명령들과 같은 `BuildException` 경로를 쓴다.
- 로컬 산출: `<output>/manifest.json`(세대 매니페스트 바이트 그대로), `<output>/archives/**`, `<output>/files/**`, `<output>/data/**`(해제한 원본 트리), `<output>/.gpk-sync.lock`(락 파일, 내용 없음, 삭제하지 않음)

### 제약

- 새 NuGet 의존성 0. 포인터 조회와 다운로드는 `HttpClient`만 쓴다(Npgsql, supabase 계열 클라이언트 추가 금지).
- 다운로드한 산출물은 스트리밍 SHA-256으로 매니페스트의 `checksum`·`storedSize`와 대조한 뒤에만 최종 이름으로 rename한다. 최종 이름의 파일은 항상 검증 완료 상태다.
- 해제 트리는 매니페스트와 정확히 일치한다. `<output>/data`는 gpk가 소유하는 폴더이며, 매니페스트에 없는 파일은 지운다. 압축 미러(`archives/**`, `files/**`)는 델타 다운로드와 `gpk verify`가 쓰므로 해제 후에도 남긴다.
- `manifest.json` 교체는 모든 산출물이 자리잡은 뒤 마지막에 원자적으로 한다. 어느 시점에 중단돼도 로컬 `manifest.json`이 가리키는 세대는 완전하다. `gpk verify`는 배포 절차가 아니라 로컬 무결성 검사 명령이고 동기화 폴더도 같은 배치이므로, 이 불변식은 "언제 실행해도 `gpk verify --output <동기화 폴더>`가 통과한다"로 확인할 수 있다(자동 실행하지는 않는다).
- 포인터와 로컬 `releaseVersion`이 "다르면" 동기화한다("크면"이 아님). 포인터 되돌리기(롤백)가 같은 경로로 처리된다.
- 실패 메시지 어디에도 key를 출력하지 않는다.
- 재시도·백오프는 넣지 않는다. 실패는 그대로 보고하고 다음 스케줄 실행이 재시도 역할을 한다.

### 사전 조건 SQL (운영자 1회, `docs/cli/sync.md`에 수록)

```sql
create table public.gamepatch_pointer (
  bucket text primary key,
  release_version bigint not null check (release_version >= 0),
  updated_at timestamptz not null default now()
);

alter table public.gamepatch_pointer enable row level security;

-- GRANT와 RLS는 별개 계층이다. 2026-05-30 이후 만든 프로젝트는 public 스키마의 새 테이블에 자동
-- 권한을 주지 않으므로, 명시하지 않으면 RLS에 닿기도 전에 42501 permission denied로 거부된다.
grant select on public.gamepatch_pointer to anon;                         -- gpk sync (publishable key)
grant select, insert, update on public.gamepatch_pointer to service_role; -- 배포 스크립트 (secret key)

-- 읽기만 공개한다. 쓰기 정책은 만들지 않는다 - 갱신은 배포 스크립트가 secret key로만 한다.
create policy gamepatch_pointer_read on public.gamepatch_pointer
  for select to anon using (true);
```

## 4. 실행 방식

- [x] skill 단독 (`csharp-feature-architect` skill만 사용)
- [ ] agent + skill

선택 사유: 단일 프로젝트(`GamePatchKit.Cli`) 안에서 기존 `upload` 명령과 같은 모양의 명령 하나를 추가하는 작업이라 skill 단독으로 충분하다.

## 5. 가장 단순한 구조 후보

`upload`와 같은 명령 단위 구성을 그대로 반복한다.

- `SyncCommand` 한 클래스가 락 → 포인터 비교 → 델타 다운로드 → 매니페스트 교체를 순서대로 수행한다.
- `SyncSettings` record + `SyncSettingsResolver` 정적 클래스가 환경 변수·`--env-file`을 해석한다(`UploadSettings` 대칭).
- `ISyncRemote` 인터페이스 + `SupabaseSyncRemote` 구현이 원격 접근(PostgREST 포인터 + 공개 객체)을 담당한다(`IUploadStorage` 대칭).
- `ArgumentsParser.ParseSync`, `Program.RunAsync`의 `case "sync"` 추가로 기존 진입점에 붙인다.
- 매니페스트 읽기·검증·원자 쓰기는 `ManifestStore`의 기존 핵심을 재사용하고, bytes 기반 진입 메서드만 추가한다.

## 6. 단순 구조 실패 조건

없음 — 단순 구조로 확정. 상주 프로세스·구독·재시도가 범위 밖으로 확정됐고, 남는 것은 one-shot 명령 하나다.

## 7. 책임/도메인 분해

| 책임 단위 | 종류 | 설명 |
| --- | --- | --- |
| `SyncCommand` | Command | 락 획득, 포인터 비교, 델타 계산, 다운로드 검증, 해제 호출, 매니페스트 원자 교체 |
| `DataExtractor` / `ExtractSummary` | 복원 | 매니페스트대로 `<output>/data` 트리를 맞춘다(사라진 파일 삭제, 바뀐 엔트리만 해제) |
| `SyncSettings` / `SyncSettingsResolver` | Options/Resolver | 읽기용 설정 3종 해석과 형식 검증 |
| `ISyncRemote` / `SupabaseSyncRemote` | 외부 경계 | 포인터 조회(PostgREST)와 공개 객체 스트림 열기(Storage) |
| `SyncSummary` / `SyncOutcome` | DTO | 실행 결과 요약(동기화/최신/락 겹침)과 콘솔 출력 재료 |
| `SyncArguments` | DTO | `--output`, `--env-file` 파싱 결과 |

## 8. 적용 패턴과 정당화

없음.

## 9. 인터페이스 생성 근거

| 인터페이스 | 근거 |
| --- | --- |
| `ISyncRemote` | 외부 의존성(HTTP) 경계 + 테스트 대역. `IUploadStorage` + fake로 이미 관례화된 경계와 동일한 이유·동일한 모양이다. |

포인터 조회와 객체 다운로드를 인터페이스 두 개로 나누지 않는다 — 소비자가 `SyncCommand` 하나뿐이고 둘 다 "게시된 원격"이라는 같은 대상이다.

## 10. 인터페이스·클래스 시그니처

```csharp
namespace GamePatchKit.Cli;

internal sealed record SyncArguments(string OutputPath, string? EnvFilePath);

internal sealed record SyncSettings(string ProjectUrl, string PublishableKey, string Bucket);

internal static class SyncSettingsResolver
{
    // 환경 변수와 --env-file을 해석한다. 누락·형식 오류는 BuildException.
    public static SyncSettings Resolve(string? envFilePath);
}

// 게시된 원격 = 포인터(PostgREST) + 공개 객체(Storage). 실패는 key를 담지 않은 BuildException.
internal interface ISyncRemote
{
    // 포인터 행의 release_version. 테이블 없음·행 없음·인증 실패를 구분된 메시지로 던진다.
    Task<long> GetReleaseVersionAsync();

    // manifests/<N>.json 또는 산출물 name의 공개 객체 스트림. 404는 게시 절차 위반 메시지.
    Task<Stream> OpenObjectAsync(string objectPath);
}

internal sealed class SupabaseSyncRemote : ISyncRemote, IDisposable
{
    public SupabaseSyncRemote(SyncSettings settings);
    internal SupabaseSyncRemote(SyncSettings settings, HttpMessageHandler handler); // 테스트 전용

    public Task<long> GetReleaseVersionAsync();
    public Task<Stream> OpenObjectAsync(string objectPath);
    public void Dispose();
}

internal enum SyncOutcome
{
    Synced,
    AlreadyUpToDate,
    SkippedBecauseLocked,
}

internal sealed record SyncSummary(
    SyncOutcome Outcome,
    long? PointerVersion,   // SkippedBecauseLocked면 null
    long? PreviousVersion,  // 로컬 manifest.json이 없던 첫 동기화면 null
    int DownloadedCount,
    long DownloadedBytes,
    int ReusedCount,
    int ExtractedCount,
    int RemovedCount);

internal sealed class SyncCommand
{
    public SyncCommand(ISyncRemote remote);

    // 락을 못 잡으면 SkippedBecauseLocked 요약을 반환한다(예외 아님). 그 외 실패는 BuildException.
    public Task<SyncSummary> ExecuteAsync(string outputPath);
}

internal sealed record ExtractSummary(int ExtractedCount, int RemovedCount);

// 압축된 산출물을 <output>/data 아래 원본 트리로 되돌린다. 경로는 <groupId>/<entry.path>라
// 원본 데이터 루트와 같은 배치가 된다.
internal static class DataExtractor
{
    internal const string DATA_DIRECTORY_NAME = "data";

    // previous는 로컬이 지금 갖고 있는 세대(첫 동기화면 null). 매니페스트에 없는 파일을 먼저 지운
    // 뒤 바뀐 엔트리만 푼다. 크기가 매니페스트와 다르면 BuildException.
    public static ExtractSummary Execute(string outputPath, PatchManifest target, PatchManifest? previous);
}
```

기존 타입에 추가·수정되는 시그니처:

```csharp
internal static class ArgumentsParser
{
    // --output 필수, --env-file 선택. 기존 ParseOptions 재사용.
    public static SyncArguments ParseSync(string[] arguments);
}

internal static class ManifestStore
{
    // 다운로드한 세대 매니페스트 검증용. 기존 private 읽기 핵심(파싱 + Validate)을 재사용한다.
    internal static PatchManifest ReadFromBytes(byte[] bytes, string errorPrefix);

    // 검증을 통과한 원격 바이트를 재직렬화 없이 그대로 임시 파일 + File.Move로 교체한다.
    internal static void WriteBytesAtomically(string outputPath, byte[] manifestBytes);
}

internal static class UploadSettingsResolver
{
    // private → internal. SyncSettingsResolver가 같은 --env-file 규칙을 재사용한다.
    internal static IEnumerable<(string Name, string Value)> ReadEnvFile(string path);
}
```

## 11. 파일·폴더 배치 제안

기존 flat 구조(`src/GamePatchKit.Cli/`)를 그대로 따른다. 새 폴더 없음.

```text
src/GamePatchKit.Cli/
├── SyncCommand.cs        (신규: SyncCommand + SyncSummary + SyncOutcome)
├── SyncSettings.cs       (신규: SyncSettings + SyncSettingsResolver)
├── SyncRemote.cs         (신규: ISyncRemote + SupabaseSyncRemote - UploadStorage.cs와 같은 관례)
├── DataExtractor.cs      (신규: DataExtractor + ExtractSummary)
├── CommandArguments.cs   (수정: SyncArguments + ParseSync)
├── Program.cs            (수정: case "sync" + WriteSyncSummary, 명령 안내 문구에 sync 추가)
├── ManifestStore.cs      (수정: ReadFromBytes + WriteBytesAtomically)
└── UploadSettings.cs     (수정: ReadEnvFile internal화)

tests/GamePatchKit.Cli.Tests/
├── TestSyncCommand.cs        (신규: FakeSyncRemote 기반)
├── TestSyncSettings.cs       (신규)
├── TestSupabaseSyncRemote.cs (신규: fake HttpMessageHandler 기반)
└── TestDataExtractor.cs      (신규: ArtifactWriter 실제 산출물 + 실제 build 출력 기반)
```

구현 예산(기본 새 파일 1개, 새 타입 1~2개) 초과 항목과 이유:

| 초과 항목 | 요청상 반드시 필요한 이유 |
| --- | --- |
| 새 파일 4개(src) | `upload`가 확립한 명령 단위 파일 분리(Command/Settings/Storage)와 동일한 배치. 한 파일로 합치면 기존 구조 관례를 깬다. `DataExtractor`는 원격을 모르고 `SyncCommand`는 압축 형식을 모르므로 파일을 나눈다. |
| 새 타입 10개 | 8개는 각각 `upload`의 대응물(UploadArguments/UploadSettings/UploadSettingsResolver/IUploadStorage/SupabaseUploadStorage/UploadSummary)과 1:1 대칭이고, `SyncOutcome`만 추가 — 락 겹침을 예외가 아닌 정상 결과로 표현하기 위해 필요하다. `DataExtractor`/`ExtractSummary`는 "다른 서버가 바로 읽는 트리"를 만드는 요구의 직접 구현이다. |
| 새 설정 3개 | 소비 머신에 secret key를 두지 않는다는 권한 분리 요구의 직접 구현. 업로드용 설정을 재사용하면 secret key 배포가 강제된다. |

## 12. 도입하지 않은 구조

- **상주 `watch` 명령·pub/sub 구독**: 폴링 + 수동 강제 실행으로 확정. 필요해지면 이 sync를 호출하는 얇은 호스트를 별도 작업으로 얹는다.
- **테이블 자동 생성(DDL)**: 소비 머신에 관리자 자격증명을 배포하게 되고, PostgREST로는 DDL이 불가능해 Npgsql + DB 비밀번호가 필요해진다. 사전 조건 SQL 문서화 + "테이블 없음" 감지 시 안내 에러로 대체.
- **Npgsql·supabase 계열 클라이언트 의존성**: 포인터 조회는 PostgREST GET 하나라 `HttpClient`로 충분.
- **재시도·백오프**: 스케줄러의 다음 실행이 재시도다. 견고성 장치를 요청 없이 넣지 않는다.
- **병렬 다운로드·진행률 표시**: 성능·UX 요구 없음. 이름 순서 순차 다운로드.
- **`--force`/수리 모드**: 최종 이름 파일은 검증 완료 상태라는 불변식이 있고, at-rest 검사는 `gpk verify`가 이미 담당한다. 크기 불일치 파일의 재다운로드만 sync에 포함한다.
- **오래된 세대 산출물 정리(GC)**: 불변 이름이라 남아 있어도 무해하다. 디스크 사용량이 실제 문제가 되면 별도 작업.
- **포인터/스토리지 인터페이스 분리(2개)**: 소비자가 하나뿐이라 `ISyncRemote` 하나로 묶는다.
- **CancellationToken 전파**: 기존 명령들과 동일하게 받지 않는다. 강제 종료 시 임시 파일이 남을 수 있으나 참조되지 않아 무해하다.
- **해제 트리 원자 교체(새 트리에 풀고 통째로 rename)**: 디스크를 두 배로 쓰고 바뀌지 않은 파일까지 다시 써야 한다. 파일 단위로는 임시 파일 + rename이라 잘린 파일이 보이지 않으므로, 세대 전환 중 트리에 두 세대가 섞이는 짧은 구간만 허용한다.
- **해제 트리 checksum 검증**: 매니페스트의 `checksum`은 압축된 산출물의 것이고 해제 결과의 해시는 없다. 존재와 크기까지만 확인한다.
- **`AlreadyUpToDate`에서도 해제 트리 점검**: 폴링마다 전체 트리를 stat하게 된다. 트리가 손상됐다면 `manifest.json`을 지우고 한 번 실행하는 것으로 복구한다(산출물은 이미 있어 다시 받지 않는다).

## 13. 단순화 자가 검토 결과

- 새 인터페이스 수: 1 (예산 0~2, `ISyncRemote` — 관례화된 외부 경계)
- 새 폴더 계층 증가: 0
- 적용 패턴 수: 0
- 단일 구현 인터페이스: `ISyncRemote` 1건 — `IUploadStorage`와 동일하게 fake 테스트 대역이 실사용처(기존 관례)
- 요구사항에서 직접 도출되지 않은 도메인 객체: 없음
- 종합 판정: 단순 구조 유지 (예산 초과분은 11절 표로 정당화)

## 14. 위임 다음 단계

- 구현: 일반 코딩 작업 (`csharp-coding-standards` 적용)
- 단위 테스트: `csharp-unit-test` 스킬 (FakeSyncRemote·fake HttpMessageHandler 기반, 기존 Cli.Tests 관례)
- 문서: 구현 완료 시 `docs/cli/sync.md`(사용법 + 사전 조건 SQL + cron 예시), distribution PRD 수정("CLI는 Postgres에 쓰지 않는다"로 완화 + 세 번째 소비자 등록 + 포인터 테이블 스키마), `docs/project-description.md` 명령 목록 갱신
- 배포 스크립트의 포인터 upsert 단계: 별도 작업 (이 설계 범위 밖)

## 15. 핵심 처리 흐름

1. `ArgumentsParser.ParseSync` → `SyncSettingsResolver.Resolve` (형식 검증까지, 원격 호출 없음)
2. `<output>/.gpk-sync.lock`을 `FileShare.None`으로 열어 배타 락 획득. 실패(공유 위반)면 `SkippedBecauseLocked` 반환 — 원격 호출 없이 종료. 락 파일은 지우지 않는다(삭제는 경합을 만든다). 프로세스 종료 시 OS가 락을 해제하므로 stale 상태가 없다.
3. 포인터 조회 `GetReleaseVersionAsync`:
   - 테이블 없음(PostgREST 404) → "포인터 테이블이 없습니다. docs/cli/sync.md의 사전 조건 SQL을 실행하세요."
   - 행 없음(빈 배열) → "bucket '<b>'의 포인터 행이 없습니다. 아직 게시 전이거나 bucket 이름 오류입니다."
   - 인증 실패(401/403) → key를 출력하지 않는 실패 메시지.
4. 로컬 `manifest.json` 읽기(`ManifestStore.ReadPrevious`). 없으면 첫 동기화. 손상이면 그대로 실패 보고(사용자가 지우면 다음 실행이 전량 동기화).
5. 로컬 `releaseVersion` == 포인터 → `AlreadyUpToDate` 반환(원격 다운로드 없음). 다름(크든 작든) → 계속.
6. `manifests/<N>.json` 다운로드 → `ManifestStore.ReadFromBytes`로 검증 → 그 `releaseVersion`이 포인터 `N`과 다르면 게시 무결성 오류로 중단.
7. 델타 계산: 세대 매니페스트의 `EnumerateArtifacts()` 중 로컬에 같은 `name`이 크기까지 일치하면 reused, 크기가 다르면 재다운로드 대상, 없으면 다운로드 대상.
8. 대상마다: 같은 디렉터리의 임시 파일로 스트리밍 다운로드하며 SHA-256 계산 → `storedSize`·`checksum` 일치 시 최종 이름으로 `File.Move`(크기 불일치 파일은 덮어쓰기) → 불일치면 임시 파일 삭제 후 무결성 오류로 중단(기존 `manifest.json` 유지).
9. `DataExtractor.Execute`로 `<output>/data`를 세대 매니페스트에 맞춘다.
   - 매니페스트에 없는 파일과 그래서 비게 된 폴더를 먼저 지운다. 지우는 것이 먼저여야 이전 세대에서 파일이던 경로에 이번 세대의 폴더를 만들 수 있다.
   - 로컬 세대와 비교해 엔트리 정보(`source`, `compression`, 산출물 checksum, `offset`·`length`)가 같고 해제된 파일이 같은 크기로 있으면 건너뛴다.
   - 아카이브 엔트리는 아카이브를 `compression`대로 열어 `offset` 순서로 한 번만 훑으며 `length`만큼 잘라낸다. 파일 엔트리는 객체를 열어 통째로 푼다.
   - 각 파일은 임시 파일에 쓰고 크기가 `size`와 같을 때만 최종 이름으로 `File.Move`한다.
10. 전부 성공하면 `ManifestStore.WriteBytesAtomically`로 세대 매니페스트 바이트를 그대로 `manifest.json`에 원자 교체 — 이 시점에 로컬 세대가 전환된다.
11. `SyncSummary` 출력 후 종료 코드 0.

실패 지점 어디서든: 기존 `manifest.json`이 가리키던 세대는 온전히 남고, 내려받다 만 임시 파일은 참조되지 않는다. 해제 도중 실패하면 `data` 트리에 두 세대가 섞인 채 남지만 `manifest.json`은 이전 세대 그대로이므로, 같은 명령을 다시 실행하면 바뀐 엔트리를 다시 풀어 수렴한다.

## 16. 테스트 경계

| 대상 | 방식 | 핵심 케이스 |
| --- | --- | --- |
| `SyncSettingsResolver` | 단위 | 누락 조합, PROJECT_URL 형식(경로·쿼리·포트·userinfo 거부), key 접두사, bucket 문자 규칙, `--env-file` 우선순위 |
| `SyncCommand` + FakeSyncRemote | 단위 | 첫 동기화 전량 다운로드 / 같은 버전 no-op(원격 객체 호출 0회) / 델타(있는 name 건너뜀) / 롤백(포인터 < 로컬) / checksum 불일치 시 중단·임시 파일 제거·manifest 유지 / 크기 불일치 파일 재다운로드 / 산출물 하나 실패 시 manifest 미교체 / 매니페스트 `releaseVersion` ≠ 포인터 오류 / 락 보유 중 `SkippedBecauseLocked` |
| `SupabaseSyncRemote` + fake handler | 단위 | 포인터 응답 파싱, 테이블 없음·행 없음·401 매핑, 객체 404 메시지, 요청 URL·헤더 형식, 실패 메시지에 key 미포함 |
| `DataExtractor` | 단위 | 아카이브·파일 그룹의 zstd/none 해제, 혼합 그룹, 바뀌지 않은 엔트리 건너뜀, 해제 파일 소실 시 재해제, 사라진 엔트리·빈 폴더·미등록 파일 삭제, 파일→폴더 전환, 크기 불일치 시 중단, 두 그룹이 같은 데이터 경로 |
| `BuildCommand` + `DataExtractor` | 통합 | 실제 빌드 산출물을 푼 트리가 원본 데이터 루트와 바이트까지 같다 |
| `Program` | 단위 | `case "sync"` 배선, 세 가지 outcome의 출력·종료 코드 |
| 실제 Supabase | 운영 검증 | 사전 조건 SQL 실행, 배포 스크립트 upsert 후 전량→no-op→증분→롤백, cron 겹침에서 락 동작 |

## 17. 단계별 구현 계획

| 단계 | 내용 | 완료 기준 |
| --- | --- | --- |
| S1 | `SyncArguments`/`ParseSync`, `SyncSettings`/`SyncSettingsResolver`, `Program` 배선(명령 안내 문구 포함) | TestSyncSettings·Program 배선 테스트 통과 |
| S2 | `ISyncRemote`/`SupabaseSyncRemote` (PostgREST 포인터 + 공개 객체 GET, 오류 매핑) | TestSupabaseSyncRemote 통과 |
| S3 | `SyncCommand` (락, no-op, 전량/델타/롤백, 스트리밍 검증, `ManifestStore` bytes 메서드 2개) | TestSyncCommand 통과 + 전체 테스트 회귀 없음 |
| S4 | `docs/cli/sync.md`, distribution PRD 수정, `project-description.md` 갱신, worklog | 문서 링크·앵커 검사 통과 |
| S5 | `DataExtractor`와 `SyncCommand` 배선(해제는 매니페스트 교체 직전), 출력 한 줄 추가, 문서 갱신 | TestDataExtractor 통과 + 전체 테스트 회귀 없음 |

## 18. 완료 정의

다음을 모두 만족하면 sync 구현 완료다.

- 폴링(cron)과 수동 강제 실행이 같은 명령으로 동작하고, 겹치면 한쪽이 조용히 건너뛴다.
- 포인터와 다른 세대(상승·롤백 모두)로의 동기화가 델타 다운로드로 완료되고, 같은 세대면 원격 객체를 받지 않는다.
- 모든 다운로드가 checksum·크기 검증 후에만 최종 이름을 얻고, 실패 시 기존 세대가 온전히 남는다.
- 동기화가 끝난 `<output>/data`가 게시된 세대의 원본 데이터 루트와 같고, 그 세대에 없는 파일이 남지 않는다.
- 새 NuGet 의존성 0, 소비 설정에 secret key 불요, 실패 출력에 key 미포함.
- 테이블·행 없음이 각각 실행 가능한 안내 메시지로 구분된다.
- 16절의 단위 테스트가 모두 통과하고 기존 테스트 회귀가 없다.
- CLI에 DDL, 재시도, 병렬 다운로드, 상주 프로세스, Postgres 쓰기 코드가 들어오지 않는다.

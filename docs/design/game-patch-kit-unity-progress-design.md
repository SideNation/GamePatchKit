# GamePatchKit Unity 진행률 보고 설계 문서

> 기준 문서: [`game-patch-kit-unity-client-design.md`](game-patch-kit-unity-client-design.md)
>
> 상태: 구현 완료 (2026-09-23). 호스트 프로젝트 PlayMode 테스트 19개 통과.

## 1. 기능 요약

`PatchClient.SyncAsync`에 `IProgress<PatchSyncProgress>` 오버로드를 추가해 동기화 진행 상황을 호출자에게 보고한다.
단계(매니페스트 확인 / 다운로드 / 압축 해제)를 구분하고, 다운로드는 **첫 바이트를 받기 전에 정확한 파일 개수와 전송
바이트 총량을 확정**한 뒤 파일 내 바이트 단위로 갱신한다.

기존 설계 문서 `game-patch-kit-unity-client-design.md:104`가 이 확장을 미리 열어 두었다.

```text
- 진행률(`IProgress<T>`) 미적용 — 요청에 없고 별도로 붙일 수 있다.
```

## 2. 현재 상태

`PatchClient`의 공개 표면은 생성자 1개, `DataPath` 1개, `SyncAsync` 1개가 전부다. `IProgress<T>` 인자도, 콜백도,
이벤트도 없다(`event` 키워드가 패키지 전체에 0회). 받은 바이트를 알 수 있는 유일한 경로는 동기화가 완전히 끝난 뒤
반환되는 `PatchSyncResult.DownloadedBytes`다.

`SyncAsync` 실제 흐름(`Runtime/PatchClient.cs`):

```text
L79-81   로컬 manifest.json 읽기 + pending 스캔
L84-88   같은 세대면 미러만 지우고 즉시 반환 (네트워크 0회)
L110     원격 manifests/<version>.json GET
L127     <version>.json 진행 표식 기록
L145-151 아티팩트 열거 + CollectRequiredArtifacts로 필요 집합 산출
L157-173 순차 다운로드 루프 (아티팩트마다 받기 → SHA-256 검증 → rename)
L176-178 전체 해제 (Task.Run, 백그라운드)
L182-185 manifest.json 커밋
L187     미러 삭제
```

병렬 처리는 없다. `Task.WhenAll` · `SemaphoreSlim` · `Parallel.*`이 런타임에 등장하지 않으며, 아티팩트는
`StringComparer.Ordinal` 이름 순으로 결정적으로 처리된다. 따라서 보고는 집계값만으로 충분하고 동시성 필드가 필요 없다.

## 3. grilling 결정 기록

| 번호 | 결정 | 근거 |
| --- | --- | --- |
| Q1 | 총량은 표시용 분모다. 다운로드 전 사용자 동의를 받는 단계를 SDK가 만들지 않는다 | 동의 대기는 UI 흐름이지 동기화 상태가 아니다. 필요한 호출자는 `SyncAsync` 호출 자체를 미루면 된다 |
| Q2 | 노출 지표는 전송 바이트, 파일 개수, 단계별 비율, 단계 구분. 파일명·속도·ETA는 제외 | 객체 이름은 세대와 해시가 섞인 경로라 사람에게 보여줄 값이 아니다. 속도·ETA는 추정 로직과 스무딩이 붙는 별도 기능이다 |
| Q3 | `Ratio`는 **단계별** 0~1. 통합 단일 비율을 제공하지 않는다 | 다운로드의 전송 바이트와 해제의 원본 바이트는 zstd 압축 때문에 단위가 다르다. 합치려면 가중치가 필요한데 그 값은 측정이 아니라 추측이다 |
| Q4 | **오버로드 추가.** 기존 `SyncAsync(long, CancellationToken)`를 그대로 둔다 | 옵셔널 파라미터를 토큰 앞에 끼우면 토큰을 위치 인자로 넘기는 호출부가 전부 깨진다. 저장소 안에만 `TestPatchClient.cs:300,338,423`, `PatchClientSample.cs:122`, `docs/unity/patch-client.md` 샘플이 있고 외부 소비 프로젝트도 같은 형태다. 토큰 뒤에 추가하면 `CancellationToken`을 마지막에 둔다는 .NET 관례를 영구히 어긴다 |
| Q5 | 단계는 `FetchingManifest` / `Downloading` / `Extracting` 3개. `Verifying`은 만들지 않는다 | SHA-256 검증은 독립 구간이 아니라 아티팩트마다의 내부 하위 동작(`PatchClient.cs:386`)이다. 단계로 만들면 아티팩트 수만큼 상태가 왕복 전환되어 호출자가 표시할 수 없다 |
| Q6 | 총량은 **정확값**. 상한값을 허용하지 않는다 | `IsAlreadyStored` 건너뛰기가 루프 안에 있어 사전 합산이 재사용분만큼 부풀려진다. 기존 테스트 `SyncAsync_ResumesInterruptedGeneration`이 `Reused==2, Downloaded==1`로 이 경로를 이미 고정하고 있어, 상한값이면 총 3 중 1만 받고 끝나는 보고가 나간다 |
| Q7 | 다운로드는 **파일 내 바이트 단위 폴링**. 아티팩트 단위 보고로 그치지 않는다 | 사용자 결정. 아티팩트 단위는 새 기계장치가 없어 싸지만 `packing: group` 구성에서 아카이브 하나가 받는 동안 보고가 멈춘다 |
| Q8 | `FetchingManifest`는 총량·완료량 0으로 보고한다 | 매니페스트 크기는 받기 전에 알 수 없고(`PatchClient.cs:110`) 작은 JSON 하나라 보통 수십 ms다. 크기를 알아내려 요청을 하나 더 쏘지 않는다 |
| Q9 | 조기 종료 경로(`PatchClient.cs:84-88`)에서는 **아무것도 보고하지 않는다** | 네트워크를 한 번도 타지 않고 즉시 끝난다. 호출자는 `PatchSyncResult.IsAlreadyUpToDate`로 이미 구분할 수 있고, 기존 테스트 `SyncAsync_SameGeneration_IsAlreadyUpToDateWithoutRequests`가 "요청 0회"를 고정하고 있다 |
| Q10 | 해제 단계 보고는 백그라운드 스레드에서 발생할 수 있다. SDK가 마샬링하지 않고 이 사실을 공개 문서에 명시한다 | 해제는 `Task.Run` 내부에서 실행된다(`PatchClient.cs:176`). 호출자가 `Progress<T>`를 넘기면 캡처된 컨텍스트로 post되므로 선택은 호출자에게 있다. SDK가 강제하면 그 선택지를 뺏는다 |

## 4. 요구사항 정리

| 구분 | 내용 |
| --- | --- |
| 필수 | 다운로드 시작 전 정확한 파일 개수·전송 바이트 총량 확정 |
| 필수 | 다운로드 중 파일 내 바이트 단위 진행 보고 |
| 필수 | 해제 중 엔트리 단위 진행 보고 |
| 필수 | 단계 구분 (`FetchingManifest` / `Downloading` / `Extracting`) |
| 필수 | 기존 2인자 `SyncAsync` 호출부 무수정 |
| 제외 | 속도(B/s), 남은 시간(ETA), 현재 객체 이름 |
| 제외 | 매니페스트 단계의 바이트 진행률 |
| 제외 | 재시도, `Range` 재개 |

## 5. 단순 설계 기준

`AGENTS.md` → `.claude/rules/minimal-implementation.md`의 구현 예산을 지킨다.

| 항목 | 예산 | 이 설계 |
| --- | --- | --- |
| 새 파일 | 1 | 1 (`Runtime/PatchSyncProgress.cs`) |
| 새 타입 | 1~2 | 2 (`PatchPhase`, `PatchSyncProgress`) |
| 새 인터페이스 | 0 | 0 (`IProgress<T>`는 BCL) |
| 새 의존성 | 0 | 0 |
| 새 설정 | 0 | 0 |

세 번째 타입을 만들지 않기 위해 6.3의 내부 반환값은 `ValueTuple`을 쓴다. 같은 저장소의
`ReadStored`(`PatchClient.cs:315`)가 이미 같은 형태다.

"미리 준비하지 않는다" 조항에 따라 **지금 보고하지 않는 단계와 필드를 넣지 않는다.** 특히 `PatchPhase`에 `None`이나
`Completed` 같은, SDK가 절대 보고하지 않는 값을 두지 않는다. "진행률 개념이 적용되지 않는 상태"는 호출자가 nullable
참조로 표현할 수 있고, 그것이 SDK가 거짓 값을 만들어 내는 것보다 정확하다.

## 6. 컴포넌트 · 인터페이스 시그니처

### 6.1 신규 `Runtime/PatchSyncProgress.cs`

```csharp
#nullable enable

namespace GamePatchKit.Unity
{
    public enum PatchPhase
    {
        FetchingManifest,
        Downloading,
        Extracting,
    }

    public sealed class PatchSyncProgress
    {
        internal PatchSyncProgress(PatchPhase phase, int completedCount, int totalCount,
            long completedBytes, long totalBytes)

        public PatchPhase Phase          { get; }
        public int        CompletedCount { get; }
        public int        TotalCount     { get; }
        public long       CompletedBytes { get; }
        public long       TotalBytes     { get; }
        public double     Ratio          => TotalBytes > 0 ? (double)CompletedBytes / TotalBytes : 0d;
    }
}
```

`PatchSyncResult`의 기존 관례를 따른다: `sealed class`, `internal` 생성자, get-only PascalCase 프로퍼티,
파일명 = 타입명, 첫 줄 `#nullable enable`, `Runtime/` 직하(공개) 배치, 자명하지 않은 멤버 위에 한국어 주석.

**참조 타입인 것이 계약상 중요하다.** 해제 단계 보고는 백그라운드 스레드에서 발생할 수 있고, 호출자는 보통 이 값을
필드 하나에 보관한 뒤 다른 스레드에서 읽는다. 참조 대입은 원자적이므로 찢어진 값을 읽을 수 없다. `readonly struct`로
만들면 이 보장이 사라지고 호출자가 직접 잠금을 걸어야 한다.

### 6.2 `PatchClient.SyncAsync` 오버로드

```csharp
// 기존 — 시그니처 불변
public Task<PatchSyncResult> SyncAsync(long releaseVersion, CancellationToken cancellationToken = default)

// 신규
public Task<PatchSyncResult> SyncAsync(long releaseVersion, IProgress<PatchSyncProgress> progress,
    CancellationToken cancellationToken = default)
```

둘 다 `progress`가 nullable인 private 코어로 위임한다.

### 6.3 `DataExtractor.CollectRequiredArtifacts` 반환 확장

`Core/DataExtractor.cs:58-81`은 이미 모든 엔트리를 돌며 `NeedsExtraction` 판정을 수행하고 `entry.Size`를 손에 쥔 채
버린다. 반환을 넓혀 되살린다.

```csharp
// before
public static IReadOnlyCollection<string> CollectRequiredArtifacts(
    string rootPath, PatchManifest target, IReadOnlyList<PatchManifest> known)

// after
public static (IReadOnlyCollection<string> Names, int EntryCount, long PayloadBytes) CollectRequiredArtifacts(
    string rootPath, PatchManifest target, IReadOnlyList<PatchManifest> known)
```

`RemovedCount`는 해제 시점에 기존 트리를 걸으며 발견하므로(`DataExtractor.cs:132-177`) 사전 집계 대상이 아니다.
해제 진행률의 분모에 넣지 않는다.

### 6.4 `DataExtractor.Execute` 보고 전달

`Execute`와 `ExtractGroup`에 `Action<long>? onEntryExtracted`를 전달한다. 인자는 푼 파일의 원본 크기이고 개수는 호출자가 센다. 보고 지점은 기존 취소 검사가 이미 있는
엔트리 경계다: 아카이브 멤버는 `DataExtractor.cs:241`, 파일 엔트리는 `DataExtractor.cs:208`.

## 7. 핵심 처리 흐름

### 7.1 다운로드 계획을 루프 밖으로 hoist

`IsAlreadyStored`(`PatchClient.cs:333-337`)는 `FileInfo` stat만 하는 순수 로컬 판정이라 루프 앞으로 끌어내도 동작이
바뀌지 않는다. 같은 저장소의 `UploadCommand.PlanArtifacts`(`src/GamePatchKit.Cli/UploadCommand.cs:51-68`)가 이미
"계획을 루프 앞에서 확정"하는 같은 형태를 쓴다.

```csharp
// L151(CollectRequiredArtifacts)와 L157(다운로드 루프) 사이에 삽입
ManifestArtifact[] toDownload = artifacts
    .Where(artifact => requiredNames.Contains(artifact.Name) && !IsAlreadyStored(artifact))
    .ToArray();
long totalBytes = toDownload.Sum(artifact => artifact.StoredSize);
```

`reusedCount` 집계는 hoist된 필터에서 세어 기존 `PatchSyncResult.ReusedCount` 값을 그대로 보존한다.

### 7.2 바이트 단위 폴링

`SendAsync`(`PatchClient.cs:255-305`)는 현재 `completed` 콜백만 걸고 단일 `await completion.Task`로 끝난다.

```csharp
private const int ProgressPollIntervalMilliseconds = 100;

// SendAsync에 Action<long>? onBytesReceived 파라미터를 추가한다.
// null이면 기존 경로를 그대로 타 동작과 비용이 바뀌지 않는다.
using (cancellationToken.Register(() => context.Post(_ => AbortIfPending(request, ref isFinished), null)))
{
    if (onBytesReceived is null)
    {
        await completion.Task;
    }
    else
    {
        while (!completion.Task.IsCompleted)
        {
            await Task.WhenAny(completion.Task, Task.Delay(ProgressPollIntervalMilliseconds));
            onBytesReceived((long)request.downloadedBytes);
        }
    }
}
```

전달 경로는 `DownloadArtifactAsync` → `DownloadToFileAsync` → `SendAsync`다. 아티팩트 하나가 진행 중일 때
`CompletedBytes`는 `이미 완료한 아티팩트들의 storedSize 합 + 현재 아티팩트의 수신 바이트`다.

`SendAsync`는 메인 스레드에서 호출되고 `SynchronizationContext`가 캡처되어 있으므로 `Task.Delay` 이후 재개도 메인
스레드다. `UnityWebRequest` 프로퍼티 접근에 문제가 없다.

`Task.Delay`에는 토큰을 넘기지 않는다. 취소는 기존 `cancellationToken.Register` → `Abort` 경로가 담당하며,
`Task.Delay`가 `TaskCanceledException`을 던지면 그 경로와 경쟁해 밖으로 나가는 예외 종류가 흔들린다.

`request.downloadedBytes`는 `ulong`이고 HTTP 전송 인코딩에 따라 `artifact.StoredSize`를 초과할 수 있다. 비율이 1을
넘지 않도록 `StoredSize`로 clamp한다.

### 7.3 보고 지점

| 위치 | 보고 내용 |
| --- | --- |
| `PatchClient.cs:110` 직전 | `FetchingManifest`, 모든 수치 0 |
| 7.1의 계획 확정 직후 | `Downloading`, `CompletedCount=0`, `TotalCount=toDownload.Length`, `CompletedBytes=0`, `TotalBytes` |
| 폴링 틱마다 | `Downloading`, `CompletedBytes` 갱신 |
| 아티팩트 완료마다 | `Downloading`, `CompletedCount` 증가 |
| `PatchClient.cs:176` 직전 | `Extracting`, `TotalCount`/`TotalBytes` = 6.3의 값 |
| 엔트리 해제마다 | `Extracting`, `CompletedCount`/`CompletedBytes` 증가 |
| `PatchClient.cs:84-88` 조기 종료 | **보고 없음** |

## 8. 파일 · 폴더 배치

```text
src/GamePatchKit.Unity/Runtime/
├── PatchClient.cs            # 수정: 오버로드, hoist, 폴링, 보고 지점
├── PatchSyncResult.cs        # 불변
├── PatchSyncProgress.cs      # 신규
└── Core/
    └── DataExtractor.cs      # 수정: CollectRequiredArtifacts 반환, 해제 보고
```

Asmdef 변경 없음. 새 의존성 없음.

## 9. 제외한 구조 · 패턴과 제외 이유

| 제외한 것 | 이유 |
| --- | --- |
| 통합 단일 비율(다운로드 80% / 해제 20% 가중) | 가중치가 측정이 아니라 추측이다. 네트워크가 빠르면 "80%에서 멈춘 값"이 되어 고치려는 문제가 재발한다 |
| `Verifying` 단계 | 아티팩트마다 `Downloading ↔ Verifying`을 왕복한다. 체감되는 정지 구간은 해제 하나이고 `Extracting`이 덮는다 |
| 총량 상한값(hoist 생략) | 중단 세대 재개 시 총량이 실제보다 크다. 기존 테스트가 고정한 정규 경로다 |
| 옵셔널 파라미터(토큰 앞 삽입) | 토큰을 위치 인자로 넘기는 기존 호출부가 전부 깨진다 |
| 옵셔널 파라미터(토큰 뒤 추가) | `CancellationToken`을 마지막에 둔다는 .NET 관례를 영구히 어긴다 |
| `IPatchProgressReporter` 추상화 | 구현 예산의 "새 인터페이스 0"에 걸린다. `IProgress<T>`로 충분하다 |
| 옵션 객체(`PatchSyncOptions`) | 예산 초과이며 지금 필요한 옵션이 없다 |
| `PatchPhase.None` / `Completed` | SDK가 보고하지 않는 값이다. "미리 준비하지 않는다" 위반 |
| 매니페스트 단계 바이트 진행률 | 수십 ms 구간이고 `Content-Length` 도착 전까지는 여전히 미지 구간이 남는다 |
| SDK 내부 마샬링(`SynchronizationContext.Post`) | 호출자가 `Progress<T>`로 고를 수 있다. SDK가 강제하면 선택지를 뺏는다 |
| `PatchSyncResult`에 매니페스트 바이트 합산 | 기존 공개 계약의 의미 변경이고 `TestPatchClient.cs:115`가 깨진다 |

## 10. 성능 · GC 검토 메모

- 보고 1회당 `PatchSyncProgress` 인스턴스 1개가 할당된다. 100ms 폴링이므로 초당 최대 10개 수준이고, 해제는 엔트리당
  1개다. 매니페스트가 다루는 엔트리 규모에서 무시할 수 있다.
- `progress`가 `null`인 기존 경로는 폴링 루프에 진입하지 않으므로 할당도 `Task.Delay`도 발생하지 않는다.
- `toDownload` 배열은 아티팩트 수만큼의 1회 할당이다. 기존에도 `artifacts` 배열을 만들고 있었다.
- `CollectRequiredArtifacts`의 반환을 `ValueTuple`로 넓히는 것은 추가 힙 할당이 아니다.

## 11. 검증 계획

기존 하네스를 그대로 쓴다. `FixtureServer`가 loopback `HttpListener`와 `OnRequest` / `HeldObjectPathPrefix` /
`RequestCount` 훅을 이미 제공한다.

`Progress<T>`는 캡처된 컨텍스트로 비동기 post하므로 결정적 검증에 부적합하다. 테스트에는 `List<T>`에 동기적으로
append하는 작은 `IProgress<T>` recorder를 쓴다.

| 검증 항목 | 기준 |
| --- | --- |
| 총량 정확성 | `SyncAsync_ResumesInterruptedGeneration`(`Reused==2, Downloaded==1`)에서 `Downloading` 첫 보고의 `TotalCount == 1` |
| 단조성 | `CompletedBytes`·`CompletedCount`가 단계 내에서 감소하지 않음 |
| 최종 일치 | `Downloading` 마지막 보고의 `CompletedBytes == result.DownloadedBytes`, `CompletedCount == result.DownloadedCount` |
| 해제 총량 | `Extracting` 마지막 보고의 `CompletedCount == result.ExtractedCount` |
| 조기 종료 | `SyncAsync_SameGeneration_IsAlreadyUpToDate` 경로에서 보고 **0회** |
| 단계 순서 | `FetchingManifest` → `Downloading` → `Extracting` 순, 각 단계 첫 등장 1회 |
| 비율 경계 | `Ratio`가 0 미만이거나 1 초과가 되지 않음(전송 인코딩 clamp 검증) |
| 취소 + 폴링 | 진행률을 받는 중 취소해도 `OperationCanceledException`으로 끝나고 임시 파일이 남지 않음 |
| 기존 회귀 | 기존 15개 테스트 전부 통과. 특히 2인자 `SyncAsync` 호출부 무수정 |

수동 검증은 `unity/GamePatchKit.Unity.Host`의 `PatchClientSample` 씬에서 수행한다.

## 12. 문서 · 버전 갱신

- `docs/unity/patch-client.md:142`의 *"진행률 보고, 재시도, `Range` 재개, Supabase 포인터 조회는 제공하지 않는다"* 를
  수정한다. 그대로 두면 문서가 거짓이 된다. 사용법 절에 오버로드 예시를 추가하고, 해제 단계 보고가 백그라운드
  스레드에서 올 수 있다는 계약도 여기에 적는다.
- `game-patch-kit-unity-client-design.md`의 L49(Q7)·L76·L104를 갱신하고 이 문서를 참조로 건다.
- `src/GamePatchKit.Unity/package.json` 버전을 `0.1.10` → `0.1.11`로 올린다.
- **릴리스 태그는 이번 작업에서 만들지 않는다.** 태그를 끊으면 `Directory.Build.props`의 `<Version>`(현재 `0.1.0`으로
  package.json과 이미 어긋나 있음)이 CLI/NuGet 배포까지 함께 끌고 들어와, 진행률과 무관한 버전 정합성 정리가 이 변경에
  붙는다. 그 정리는 별도 작업으로 분리한다. 브랜치를 직접 참조하는 소비자는 `develop` 푸시만으로 받을 수 있다.
- CHANGELOG 파일은 이 저장소에 없다. 릴리스 노트는 커밋 메시지와 `docs/worklog/`가 담당한다.

## 13. 위험

| 위험 | 대응 |
| --- | --- |
| `downloadedBytes`가 전송 인코딩 때문에 `StoredSize` 초과 | `StoredSize`로 clamp하고 테스트로 고정 |
| 해제 단계 `Report`가 백그라운드 스레드에서 발생 | 참조 타입 + 단일 대입으로 안전. 공개 문서에 명시 |
| `Task.Delay` 취소와 `Abort` 경로 경쟁 | `Task.Delay`에 토큰을 넘기지 않는다 |
| hoist가 `ReusedCount` 집계를 바꿈 | hoist된 필터에서 세어 기존 값 보존. 기존 테스트가 검증 |
| 100ms 폴링이 짧은 다운로드에서 틱을 거의 못 냄 | 알려진 한계. 전송량이 작은 세대에서는 보고가 몇 회에 그친다 |

## 14. 진행 상황

| 단계 | 상태 |
| --- | --- |
| 구현 (6·7절) | 완료 |
| 테스트 추가 (11절) | 완료. PlayMode 19개 통과 (기존 15 + 신규 4) |
| 문서·버전 갱신 (12절) | 완료. `package.json` 0.1.11 |
| `PatchClientSample` 수동 검증 | 미실행 |
| **adversarial 검토** | 진행 중. `AGENTS.md`가 공개 API 변경을 고위험으로 분류해 의무화한다 |
| `worklog-workflow` 기록 | 대기 |
| `agent-memory-workflow`(slug `gamepatchkit`) | 대기 |
| `develop` 푸시 | 대기 (사용자 승인 필요) |

알려진 한계: 픽스처가 수백 바이트라 루프백 다운로드가 폴링 간격보다 훨씬 빨리 끝난다. 폴링 경로는 실행되지만
파일 내 중간 진행 보고가 여러 번 나오는 상황은 테스트로 재현되지 않는다.

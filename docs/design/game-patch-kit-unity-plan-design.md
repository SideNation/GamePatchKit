# GamePatchKit Unity 다운로드 계획 조회 설계 문서

> 기준 문서: [`game-patch-kit-unity-client-design.md`](game-patch-kit-unity-client-design.md)
>
> 관련 문서: [`game-patch-kit-unity-progress-design.md`](game-patch-kit-unity-progress-design.md),
> [`game-patch-kit-unity-local-state-design.md`](game-patch-kit-unity-local-state-design.md)
>
> 상태: 구현 완료 (2026-09-24). 호스트 프로젝트 PlayMode 테스트 39개 통과. adversarial 검토 1회 완료.

## 1. 기능 요약

`PatchClient.PlanAsync(releaseVersion)`를 추가한다. 산출물을 받기 **전에** "받을 것이 있는지, 몇 개이고 몇
바이트인지"를 돌려준다. 소비자는 이 값으로 셀룰러 데이터 사용 전 고지 팝업을 띄우고, 받을 것이 0개면 팝업을
건너뛴다.

디스크를 바꾸지 않는다. 특히 진행 표식을 쓰지 않는다.

## 2. 현재 상태와 문제

`SyncAsync`는 계획을 **내부에서** 확정한다. 진행률 작업에서 `IsAlreadyStored` 판정을 루프 밖으로 끌어냈으므로
정확한 개수와 바이트는 이미 한 지점에 모여 있고, 첫 바이트를 받기 전에 보고까지 나간다(`PatchClient.cs`).

```csharp
Report(progress, PatchPhase.Downloading, 0, toDownload.Length, 0, downloadTotalBytes);
```

그러나 이 지점은 `SyncAsync`를 **시작한 뒤**다. 다운로드를 시작하지 않고 그 숫자만 얻을 방법이 없어서, 소비자는
"받기 전에 알린다"를 구현할 수 없다. 소비자가 매니페스트를 직접 받아 계산하면 `ReadLocalState`로 없앤 파일 포맷
결합이 되살아난다.

## 3. 결정 기록

| 번호 | 결정 | 근거 |
| --- | --- | --- |
| Q1 | `PlanAsync`는 **진행 표식(`<세대>.json`)을 쓰지 않는다** | `SyncAsync`는 산출물보다 먼저 표식을 쓴다. 조회가 같은 일을 하면 사용자가 고지를 거절했을 때 표식이 남아 캐시가 `Pending`이 되고 오프라인 진입이 막힌다. 조회는 디스크를 바꾸지 않아야 한다 |
| Q2 | 루트 폴더를 **만들지 않는다.** `Directory.CreateDirectory`를 호출하지 않는다 | Q1과 같은 이유다. `ReadLocal`·`ScanPending`은 루트가 없어도 동작한다 |
| Q3 | 조기 종료 경로에서 **미러를 지우지 않는다** | `SyncAsync`의 조기 종료는 `DeleteMirror`로 정리까지 한다. 조회는 부작용이 없어야 하므로 `(0, 0)`만 돌려준다 |
| Q4 | `PlanAsync`와 `SyncAsync`는 **독립 실행**한다. 계획을 넘겨받는 핸들을 두지 않는다 | 핸들을 두면 두 호출 사이의 상태 수명을 관리해야 하고, 그 사이 디스크가 바뀌면 낡은 계획으로 받게 된다. 대가는 매니페스트 GET 한 번이 늘어나는 것뿐이고 매니페스트는 작은 JSON 하나다 |
| Q5 | 노출 값은 **받을 개수와 전송 바이트 둘뿐**이다 | 고지 팝업이 필요한 것이 그 둘이고, "받을 것이 있는지"는 개수가 0인지로 판정한다. 해제 대상 수·바이트도 같은 순회에서 나오지만 지금 쓰이지 않으므로 넣지 않는다("미리 준비하지 않는다") |
| Q6 | 매니페스트 획득·known 구성·계획 산출을 private 헬퍼로 **추출해 두 경로가 공유**한다 | 복제하면 매니페스트 재사용·표식 이어받기·`known` 구성 규칙이 두 곳에서 갈라진다. 이 규칙들은 조용히 어긋나면 "받아야 할 것을 안 받는" 오류가 된다 |
| Q7 | 표식 쓰기는 **호출자가 결정**한다. 헬퍼는 받은 바이트를 돌려주고 쓰지 않는다 | `bool writeMarker` 플래그보다 부작용 위치가 분명하다. 조회 경로는 바이트를 버리고 동기화 경로만 기록한다 |
| Q8 | `ValidateRemotePaths`는 조회에서도 실행한다 | 순수 검증이라 부작용이 없고, 잘못 게시된 세대를 고지 팝업 전에 알 수 있다 |

## 4. 요구사항 정리

| 구분 | 내용 |
| --- | --- |
| 필수 | 받을 산출물 개수와 전송 바이트 합계 |
| 필수 | 받을 것이 없으면 개수 0 |
| 필수 | 디스크 변경 없음(루트 생성·표식 기록·미러 삭제 없음) |
| 필수 | `SyncAsync`와 같은 재사용·이어받기 판정을 사용 |
| 제외 | 해제 대상 수·바이트 |
| 제외 | 계획을 `SyncAsync`에 넘기는 핸들 |

## 5. 단순 설계 기준

| 항목 | 예산 | 이 설계 |
| --- | --- | --- |
| 새 파일 | 1 | 1 (`Runtime/PatchSyncPlan.cs`) |
| 새 타입 | 1~2 | 1 (`PatchSyncPlan`) |
| 새 인터페이스 | 0 | 0 |
| 새 의존성 | 0 | 0 |
| 새 설정 | 0 | 0 |

헬퍼의 반환은 `ValueTuple`을 쓴다. 같은 저장소의 `ReadStored`와 `CollectRequiredArtifacts`가 이미 같은 형태다.

## 6. 컴포넌트 · 인터페이스 시그니처

### 6.1 신규 `Runtime/PatchSyncPlan.cs`

```csharp
public sealed class PatchSyncPlan
{
    internal PatchSyncPlan(int downloadCount, long downloadBytes)

    // 받아야 하는 산출물 수. 0이면 받을 것이 없다.
    public int DownloadCount { get; }

    // 받아야 하는 전송 바이트 합계. 이미 받아 둔 산출물은 빠진 정확한 값이다.
    public long DownloadBytes { get; }
}
```

### 6.2 `PatchClient.PlanAsync`

```csharp
// Unity 메인 스레드에서 호출한다. 디스크를 바꾸지 않으므로 SyncAsync 전에 몇 번 불러도 된다.
// 같은 루트에 대한 SyncAsync와 동시에 호출하지 않는다.
public Task<PatchSyncPlan> PlanAsync(long releaseVersion, CancellationToken cancellationToken = default)
```

오류 계약은 `SyncAsync`와 같다. 로컬 I/O 오류와 원격 실패, 매니페스트 오류는 모두 `PatchClientException`이고
취소는 `OperationCanceledException`이다.

### 6.3 추출하는 private 헬퍼

```csharp
// 목표 세대 매니페스트를 정한다. 원격에서 받은 경우에만 FetchedBytes가 채워지고, 그것을 표식으로 쓸지는
// 호출자가 결정한다.
private async Task<(PatchManifest Target, string? PendingPath, byte[]? FetchedBytes)> ResolveTargetManifestAsync(...)

// 트리의 출처를 보증할 수 있는 매니페스트 집합이다.
private static List<PatchManifest> BuildKnownManifests(PatchManifest? localManifest, PendingScan pending)

// 받을 산출물과 해제 대상을 한 번에 산출한다.
private async Task<(ManifestArtifact[] ToDownload, int ReusedCount, long DownloadBytes, int ExtractCount, long ExtractBytes)>
    PlanSyncAsync(PatchManifest target, IReadOnlyList<PatchManifest> known, CancellationToken cancellationToken)
```

`SynchronizeAsync`는 이 셋을 호출하고 표식 기록·다운로드·해제·커밋만 담당하게 짧아진다.

## 7. 소비자 흐름

```csharp
PatchSyncPlan plan = await client.PlanAsync(releaseVersion, token);

if (plan.DownloadCount > 0 && !await ConfirmAsync(plan.DownloadCount, plan.DownloadBytes))
{
    return;   // 사용자가 거절했다. 디스크는 그대로다
}

PatchSyncResult result = await client.SyncAsync(releaseVersion, progress, token);
```

`PlanAsync`가 매니페스트를 받고 `SyncAsync`가 다시 받는다. 작은 JSON 하나이므로 감수한다(Q4).

## 8. 파일 · 폴더 배치

```text
src/GamePatchKit.Unity/Runtime/
├── PatchClient.cs        # 수정: PlanAsync 추가, SynchronizeAsync에서 헬퍼 3개 추출
├── PatchSyncPlan.cs      # 신규
└── Core/                 # 불변
```

## 9. 제외한 구조 · 패턴과 제외 이유

| 제외한 것 | 이유 |
| --- | --- |
| 계획 핸들을 `SyncAsync`에 넘기기 | 두 호출 사이 상태 수명이 생기고 낡은 계획으로 받을 수 있다(Q4) |
| `bool writeMarker` 플래그 | 부작용이 플래그에 숨는다. 바이트를 돌려주고 호출자가 쓰는 편이 분명하다(Q7) |
| 해제 대상 수·바이트 노출 | 지금 쓰이지 않는다(Q5) |
| `PlanAsync`에서 루트 생성 | 조회는 부작용이 없어야 한다(Q2) |
| `SyncAsync` 안에서 고지 콜백 호출 | 동의 대기는 UI 흐름이고 SDK가 UI 수명을 잡으면 취소·재진입 책임이 섞인다 |

## 10. 검증 계획

| 검증 항목 | 기준 |
| --- | --- |
| 첫 설치 | 픽스처 세대 0에서 `DownloadCount == 2`, `DownloadBytes ==` 아카이브 + config 크기 |
| 이미 최신 | 동기화 후 같은 세대를 조회하면 `(0, 0)`, 원격 요청 없음 |
| 다음 세대 | 세대 0 동기화 후 세대 1 조회는 `SyncAsync`의 `DownloadedCount`와 일치 |
| 부작용 없음 | 루트가 없을 때 조회하면 루트가 **생기지 않음** |
| 표식 없음 | 조회 뒤 `<세대>.json`이 **생기지 않고** `ReadLocalState().HasPendingGeneration == false` |
| 미러 유지 | 조기 종료 경로 조회가 `archives/`·`files/`를 지우지 않음 |
| 중단 재개 | 중단된 세대 조회가 남은 산출물만 세고 매니페스트를 다시 받지 않음 |
| 계획 = 실제 | 조회 직후 동기화하면 `DownloadCount == result.DownloadedCount`, `DownloadBytes == result.DownloadedBytes` |
| 기존 회귀 | 기존 29개 테스트 전부 통과 |

`계획 = 실제`가 핵심이다. 고지한 숫자와 실제로 받는 양이 다르면 이 기능의 의미가 없다.

## 11. 문서 · 버전 갱신

- `docs/unity/patch-client.md`에 "다운로드 계획" 절을 추가한다. 고지 팝업 흐름과 부작용 없음을 명시한다.
- `package.json` `0.1.12` → `0.1.13`.
- 릴리스 태그 `v0.2.3`. 태그는 CLI의 NuGet 배포 버전이라 기존 계보(`v0.2.2`)를 잇는다.

## 12. 위임 다음 단계

| 단계 | 상태 |
| --- | --- |
| 구현 (6절) | 완료 |
| 테스트 (10절) | 완료. PlayMode 39개 통과 (기존 29 + 신규 10) |
| 문서·버전 (11절) | 완료. `package.json` 0.1.13 |
| adversarial 검토 | 1회 완료. Apply 3건, Reject 1건 |
| `worklog-workflow` 기록 | 대기 |
| `develop` 푸시 + 태그 | 대기 |

mutation 확인 2건. 각각 해당 테스트 **하나만** 실패한다.

| 변이 | 실패하는 테스트 |
| --- | --- |
| `PlanAsync`가 진행 표식을 쓴다 | `PlanAsync_DoesNotTouchDisk` |
| **루트가 이미 있을 때만** 진행 표식을 쓴다 | `PlanAsync_ExistingRootNewGeneration_ChangesNoFile` |

두 번째는 검토가 "신규 테스트 7개를 모두 통과한다"고 지목한 변이다. 일반 업데이트 경로(루트 존재 + 새 세대
원격 조회)의 파일 단위 스냅샷 비교를 추가해 닫았다.

### adversarial 검토 반영

- **Apply (문서)** — "계획 = 실제"를 무조건 보장으로 적었던 것을 전제 위의 보장으로 정밀화했다. 같은
  `releaseVersion`으로 내용이 다른 매니페스트를 덮어 게시하면 고지량과 실제량이 갈린다. 독점 계약도
  `PlanAsync` → `SyncAsync` 구간까지 확장해 명시했다.
- **Apply (테스트)** — `PlanAsync_ExistingRootNewGeneration_ChangesNoFile`. 기존 무부작용 테스트가 루트 없는 첫
  설치와 early-out 경로만 덮고 있어, 일반 업데이트 경로를 미러까지 심어 두고 파일 단위로 비교한다.
- **Apply (테스트)** — `PlanAsync_NegativeReleaseVersion_Throws`,
  `PlanAsync_CancelledDuringManifestRequest_Throws`. 인자 검증과 취소 계약을 `PlanAsync`에서 직접 고정한다.
- **Reject** — Q4(독립 실행)를 뒤집어 계획 스냅샷을 `SyncAsync`에 넘기라는 제안. **`SyncAsync`의 중단 재개가
  이미 로컬 `<세대>.json`을 그대로 쓰고 원격을 다시 받지 않는다**(`ResolveTargetManifestAsync`의 재사용 분기).
  즉 "같은 세대의 매니페스트는 불변"이라는 전제에 `SyncAsync`가 `PlanAsync`와 무관하게 이미 의존하고 있다.
  핸들을 넘겨도 재개 구간의 같은 노출이 남으므로 근본 원인을 없애지 못하면서 두 호출 사이의 상태 수명만
  생긴다. 전제를 문서에 명시하는 쪽을 택했다.
- **Defer** — 메인 스레드 요구와 잘못된 원격 artifact 경로(Q8)의 `PlanAsync` 전용 테스트. 둘 다 `SyncAsync`와
  같은 검사·헬퍼를 공유하고 그쪽 테스트가 있다. 전용 픽스처가 필요해 비용 대비 이득이 낮다.

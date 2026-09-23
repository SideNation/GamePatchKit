# GamePatchKit Unity 로컬 상태 조회 설계 문서

> 기준 문서: [`game-patch-kit-unity-client-design.md`](game-patch-kit-unity-client-design.md)
>
> 관련 문서: [`game-patch-kit-unity-progress-design.md`](game-patch-kit-unity-progress-design.md)
>
> 상태: 구현 완료 (2026-09-23). 호스트 프로젝트 PlayMode 테스트 28개 통과.

## 1. 기능 요약

`PatchClient`에 저장 폴더의 로컬 상태를 읽는 static 메서드 `ReadLocalState(rootPath)`를 추가한다. 네트워크 없이
"디스크에 완전한 세대가 있는가, 몇 번인가, 중단된 세대가 남아 있는가"를 돌려준다. 오프라인 진입, 복구 필요 판정,
동기화 성공 뒤 정리 확인에 쓰인다.

## 2. 현재 상태와 문제

`SyncAsync`는 시작할 때 이미 이 판정을 한다(`PatchClient.cs:80-88`).

```csharp
PatchManifest? localManifest = await Task.Run(() => ManifestStore.ReadLocal(_rootPath), cancellationToken);
PendingScan pending = await Task.Run(() => ManifestStore.ScanPending(_rootPath), cancellationToken);
```

그러나 `ManifestStore`, `PatchManifest`, `PendingScan`은 전부 `internal`이고 패키지에 `InternalsVisibleTo`가 없다.
소비자가 같은 질문을 하려면 **SDK의 디스크 배치를 계약 삼아 직접 읽는 수밖에 없다.** 실제 소비 프로젝트가 그렇게 했다:

| 소비자가 재구현한 것 | SDK 안의 원본 |
| --- | --- |
| `manifest.json`을 `JObject`로 열어 `schemaVersion`·`releaseVersion`·`sourcePath`·`sourceCommit`·`groups` 검사 | `ManifestStore.ReadLocal` + `PatchManifest` 스키마 검증 |
| 루트의 `<숫자>.json` 스캔 | `ManifestStore.ScanPending` |
| `SUPPORTED_MANIFEST_SCHEMA_VERSION = 1` 하드코딩 | `PatchManifest.CURRENT_SCHEMA_VERSION` |

이 결합은 조용히 깨진다. SDK가 `schemaVersion`을 올리면 SDK는 정상 동작하지만 소비자의 파서는 완전한 완료 캐시를
"손상"으로 분류해 오프라인 진입을 막는다. 그리고 이 필요는 한 소비자의 것이 아니다 — 오프라인 진입을 지원하려는
모든 소비자가 같은 질문을 한다.

## 3. 결정 기록

| 번호 | 결정 | 근거 |
| --- | --- | --- |
| Q1 | **static 메서드** `PatchClient.ReadLocalState(string rootPath)` | 생성자는 절대 http(s) `baseUrl`을 요구한다(`PatchClient.cs:33-37`). 로컬 상태는 서버 주소를 알기 **전에** 필요하고 `baseUrl`에 의존하지 않는다. 인스턴스 메서드로 두면 소비자가 가짜 URL을 넣게 된다 |
| Q2 | **동기** 메서드. `Task`를 돌려주지 않는다 | 작은 JSON 하나 읽기와 디렉터리 열거뿐이다. 소비자는 어차피 판정 직후 분기해야 하고, 백그라운드로 보낼지는 소비자가 `Task.Run`으로 고른다 |
| Q3 | 손상된 `manifest.json`은 **예외가 아니라 상태**(`IsManifestCorrupted`)로 돌려준다 | "손상됨"은 소비자가 복구 흐름으로 보내야 하는 정상적인 결과이지 실패가 아니다. 예외로 던지면 소비자마다 `try/catch`가 필요하다. `ManifestStore.ReadLocal`이 던지는 `PatchClientException`(`로컬 매니페스트가 올바르지 않습니다.`)은 안에서 잡아 flag로 바꾼다 |
| Q4 | 로컬 I/O 오류(`IOException`, `UnauthorizedAccessException`)는 `SyncAsync`와 같이 **`PatchClientException`으로 감싸 던진다** | 기존 오류 계약(`PatchClient.cs:64-71`)과 일치. 권한 문제는 상태가 아니라 실패다 |
| Q5 | 진행 표식은 **읽을 수 있든 없든** 있으면 `HasPendingGeneration = true` | `PendingScan.IsEmpty`는 `Valid`와 `UnreadablePaths`를 모두 본다(`ManifestStore.cs:45`). 해석 못 하는 표식이 있어도 트리가 섞여 있을 수 있어 `SyncAsync`도 그 경우 아무것도 건너뛰지 않는다(`PatchClient.cs:135`) |
| Q6 | 루트 폴더를 **만들지 않는다** | 조회는 부작용이 없어야 한다. `SyncAsync`는 `Directory.CreateDirectory`를 하지만(`PatchClient.cs:79`) 그것은 쓰기 작업의 준비다 |
| Q7 | "루트가 없음"과 "루트는 있는데 `manifest.json`이 없음"을 SDK가 구분하지 않는다 | 둘 다 `CompletedReleaseVersion == null`, 표식 없음, 손상 아님이다. 그 둘을 다르게 다룰지는 소비자 정책이고 `Directory.Exists` 한 줄이다 |
| Q8 | 완료 세대의 **버전만** 돌려주고 매니페스트 내용은 노출하지 않는다 | `PatchManifest`를 공개하면 내부 스키마가 공개 API가 된다. 소비자가 필요한 것은 "완전한가, 몇 번인가"다 |

## 4. 요구사항 정리

| 구분 | 내용 |
| --- | --- |
| 필수 | 완료 세대 버전(없으면 null) |
| 필수 | 중단된 세대 표식 존재 여부 |
| 필수 | `manifest.json` 손상 여부 |
| 필수 | 네트워크 없음, 부작용 없음(폴더 생성·삭제 없음) |
| 필수 | `baseUrl` 없이 호출 가능 |
| 제외 | 매니페스트 내용(그룹·엔트리) 노출 |
| 제외 | `data/` 트리의 무결성 검증 |

## 5. 단순 설계 기준

`AGENTS.md` → `.claude/rules/minimal-implementation.md`의 구현 예산을 지킨다.

| 항목 | 예산 | 이 설계 |
| --- | --- | --- |
| 새 파일 | 1 | 1 (`Runtime/PatchLocalState.cs`) |
| 새 타입 | 1~2 | 1 (`PatchLocalState`) |
| 새 인터페이스 | 0 | 0 |
| 새 의존성 | 0 | 0 |
| 새 설정 | 0 | 0 |

`enum`으로 4상태를 만들지 않는다. "루트 없음/`manifest.json` 없음"의 구분이 소비자 정책이라(Q7) SDK가 상태 집합을
확정할 수 없고, 세 개의 사실만 돌려주면 소비자가 자기 상태를 조합한다.

## 6. 컴포넌트 · 인터페이스 시그니처

### 6.1 신규 `Runtime/PatchLocalState.cs`

```csharp
#nullable enable

namespace GamePatchKit.Unity
{
    public sealed class PatchLocalState
    {
        internal PatchLocalState(long? completedReleaseVersion, bool hasPendingGeneration, bool isManifestCorrupted)

        // manifest.json이 유효하면 그 세대. 없거나 손상됐으면 null.
        public long? CompletedReleaseVersion { get; }

        // 루트 바로 아래에 <세대>.json 진행 표식이 하나라도 있다. 읽을 수 없는 표식도 센다.
        public bool HasPendingGeneration { get; }

        // manifest.json이 있으나 읽을 수 없다. 이때 CompletedReleaseVersion은 null이다.
        public bool IsManifestCorrupted { get; }
    }
}
```

`PatchSyncResult`·`PatchSyncProgress`와 같은 관례: `sealed class`, `internal` 생성자, get-only 프로퍼티, 한국어 주석.

### 6.2 `PatchClient.ReadLocalState`

```csharp
// 네트워크 없이 저장 폴더의 상태를 읽는다. 루트를 만들지 않는다.
// 로컬 I/O 오류는 PatchClientException으로 감싼다. 손상된 manifest.json은 예외가 아니라 IsManifestCorrupted다.
public static PatchLocalState ReadLocalState(string rootPath)
```

내부는 `SyncAsync` 시작부와 같은 두 호출이다.

```csharp
string fullPath = Path.GetFullPath(rootPath);
PendingScan pending = ManifestStore.ScanPending(fullPath);

try
{
    PatchManifest? local = ManifestStore.ReadLocal(fullPath);
    return new PatchLocalState(local?.ReleaseVersion, !pending.IsEmpty, isManifestCorrupted: false);
}
catch (PatchClientException)
{
    // ReadLocal은 파싱 실패만 PatchClientException으로 던진다. I/O 오류는 밖의 래퍼가 감싼다.
    return new PatchLocalState(null, !pending.IsEmpty, isManifestCorrupted: true);
}
```

구현 시 확인한 것: `ScanPending`은 이미 `!Directory.Exists(rootPath)`면 빈 결과를 돌려주므로(`ManifestStore.cs:104`)
루트 부재를 따로 거를 필요가 없었다. `ReadLocal`도 `File.Exists`로 먼저 걸러 루트가 없어도 던지지 않는다.
`ParseManifest`는 `JsonException`과 `OverflowException`을 모두 `PatchClientException`으로 바꾸므로(`ManifestStore.cs`)
"파싱 실패 = `PatchClientException`", "I/O 오류 = `IOException`/`UnauthorizedAccessException`"으로 예외 타입이
깨끗하게 갈린다. 따라서 Q3와 Q4를 catch 절 두 겹으로 구현할 수 있다.

## 7. 소비자 매핑 예시

소비자가 4상태를 쓰는 경우의 조합이다. SDK는 이 enum을 제공하지 않는다.

| 소비자 상태 | 조건 |
| --- | --- |
| 루트 없음(최초 실행) | `Directory.Exists(root) == false` — 소비자가 판정 |
| 진행 중 | `HasPendingGeneration` |
| 완료 | `CompletedReleaseVersion != null && !HasPendingGeneration` |
| 손상 | `IsManifestCorrupted`, 또는 루트는 있는데 `CompletedReleaseVersion == null` |

동기화 성공 뒤 "정리가 실패해 표식이 남았는가"도 같은 호출로 본다. `CleanUp`은 삭제 I/O 오류를 삼키고 성공을
돌려주므로(`PatchClient.cs:199-213`) 이를 확인하려는 소비자는 `SyncAsync` 뒤에 `ReadLocalState`를 부른다.

## 8. 파일 · 폴더 배치

```text
src/GamePatchKit.Unity/Runtime/
├── PatchClient.cs            # 수정: static ReadLocalState 추가
├── PatchLocalState.cs        # 신규
└── Core/
    └── ManifestStore.cs      # 불변 (ScanPending의 루트 부재 동작만 확인)
```

## 9. 제외한 구조 · 패턴과 제외 이유

| 제외한 것 | 이유 |
| --- | --- |
| 인스턴스 메서드 | `baseUrl` 없이는 `PatchClient`를 만들 수 없다(Q1) |
| `Task<PatchLocalState> ReadLocalStateAsync` | 작은 로컬 I/O다. 소비자가 필요하면 `Task.Run`으로 감싼다(Q2) |
| 4상태 `enum PatchLocalStatus` | 루트 부재 처리가 소비자 정책이라 SDK가 상태 집합을 닫을 수 없다(Q5, Q7) |
| `PatchManifest` 공개 | 내부 스키마가 공개 계약이 된다(Q8) |
| `data/` 트리 검증 포함 | 무엇이 "필수 파일"인지는 소비자만 안다. SDK는 매니페스트와 표식만 본다 |
| 손상 시 예외 | 손상은 복구 흐름으로 보낼 정상 결과다(Q3) |

## 10. 검증 계획

`TestPatchClient.cs`에 추가한다. 픽스처 서버 없이 루트 폴더만으로 검증 가능한 항목이 대부분이다.

| 검증 항목 | 기준 |
| --- | --- |
| 루트 없음 | 버전 null, 표식 없음, 손상 아님. 루트가 **생기지 않음** |
| 빈 루트 | 위와 같음 |
| 동기화 성공 후 | `CompletedReleaseVersion == result.ReleaseVersion`, 표식 없음 |
| 중단 후 | 기존 `SyncAsync_ResumesInterruptedGeneration` 시나리오에서 `HasPendingGeneration == true`, 버전은 이전 세대 |
| 손상 | `manifest.json`에 임의 바이트를 쓰면 `IsManifestCorrupted == true`, 버전 null, 예외 없음 |
| 읽을 수 없는 표식 | `7.json`에 임의 바이트를 쓰면 `HasPendingGeneration == true` |
| ~~권한 오류~~ | 미구현. 플랫폼마다 재현 방법이 달라 자동 테스트로 만들지 않았다 |

## 11. 문서 · 버전 갱신

- `docs/unity/patch-client.md`에 "로컬 상태" 절을 추가한다. 오프라인 진입 판정 예시와 함께 `manifest.json`·`<세대>.json`을
  직접 읽지 말고 이 API를 쓰라고 안내한다.
- `package.json` `0.1.11` → `0.1.12`.
- 릴리스 태그는 만들지 않는다(진행률 작업과 같은 이유).

## 12. 위임 다음 단계

| 단계 | 상태 |
| --- | --- |
| 구현 (6절) | 완료 |
| 테스트 (10절) | 완료. PlayMode 28개 통과 (진행률 22 + 로컬 상태 6) |
| 문서·버전 (11절) | 완료. `package.json` 0.1.12 |
| adversarial 검토 | 진행 중 |
| `worklog-workflow` 기록 | 대기 |
| `develop` 푸시 | 대기 (사용자 승인 필요) |

mutation 확인: `!ScanPending(...).IsEmpty`를 `Valid.Count > 0`으로 바꿔 해석 못 하는 표식을 세지 않게 하면
`ReadLocalState_UnreadablePendingMarker_ReportsPending` **하나만** 실패한다(28개 중 27개 통과). Q5 결정이 테스트로
실제 고정되어 있다.

# Unity 리팩터링 체크리스트

> `unity-refactoring-architect` 스킬의 세부 점검용 참조. 리팩터링 중 판단이 흔들릴 때만 읽는다.

## 리팩터링 분류

- 리팩터링: 외부 동작·Inspector 노출·Prefab/Scene 참조·저장 데이터를 바꾸지 않고 내부 구조, 이름, 중복, 책임 배치, 매 프레임 비용을 개선한다.
- 기능 추가: 새 입력, 새 게임 시스템, 새 UI 플로우, 새 ScriptableObject 타입, 새 serialized field, 새 public API가 생긴다.
- 버그 수정: 기존 동작을 의도적으로 바꿔 올바른 동작으로 만든다.

기능 추가나 버그 수정이 섞이면 사용자에게 확인하고 별도 작업으로 전환한다.

## 동작 변경 감지 지점

- Public API: class/struct 이름, 접근 제한자, method signature, parameter default, return type.
- Inspector / Serialization: `[SerializeField]` private field 이름·타입, public field 이름·타입, `[FormerlySerializedAs]` 누락, `SerializeReference` graph, `UnityEvent` listener 모양, custom `ISerializationCallbackReceiver`.
- Prefab / Scene: Prefab override 값, nested Prefab 구조, Scene 안 컴포넌트 GUID·fileID 참조, Component 추가 순서(같은 GameObject의 `GetComponents` 순서에 영향).
- Asset 참조: ScriptableObject asset, Addressables address/label, Resources 폴더 경로, AssetBundle 이름.
- 생명주기: `Awake` / `OnEnable` / `Start` / `Update` / `FixedUpdate` / `LateUpdate` / `OnDisable` / `OnDestroy` / `OnValidate` / `Reset` 실행 순서, 이벤트 구독·해제 쌍, coroutine 시작·중단, `async` / `Task` / `UniTask` 취소.
- 입력 / 메시지: Input System action map, `SendMessage` / `BroadcastMessage` 키, UnityEvent persistent listener.
- 빌드 / 컴파일 경계: asmdef, define symbol, `#if UNITY_EDITOR` / `#if UNITY_*_PLATFORM` 분기, Editor-only assembly 경계.
- 직렬화 호환성: 저장된 save data, JSON·MessagePack DTO, ScriptableObject asset YAML.

이 중 하나라도 바뀌면 리팩터링 범위를 벗어났는지 확인한다.

## 허용되는 대표 리팩터링

- 한 MonoBehaviour 안의 중복을 가까운 private helper 또는 같은 컴포넌트 내부 메서드로 추출.
- 매 프레임 `GetComponent` / `GetComponentInChildren` / `Find` 호출을 `Awake` / `OnEnable` 캐시로 옮김.
- `Update` 안 LINQ, boxing, 익명 delegate를 미리 할당된 컬렉션·메서드 그룹으로 교체.
- 책임이 섞인 MonoBehaviour의 일부 순수 로직을 같은 asmdef 안 일반 C# 클래스로 분리.
- coroutine 누락된 취소 처리(`OnDisable` / `OnDestroy`에서 `StopCoroutine`)와 이벤트 누락된 해제(`OnDisable` 짝) 추가 — 기존 의도를 보존할 때만.
- 내가 만든 unused using, 변수, helper, 임시 SerializeField 제거.

## 금지 또는 사용자 확인 필요

- 직렬화된 필드 이름 변경(이름 변경이 꼭 필요하면 `[FormerlySerializedAs("oldName")]` 마이그레이션 동반 명시).
- public field ↔ `[SerializeField] private field` 변환(Inspector·Prefab override 깨질 수 있음).
- 컴포넌트의 `Awake`/`OnEnable`/`Start` 실행 순서 변경, 이벤트 구독·해제 시점 변경.
- 사용자 요청 없는 `.unity` / `.prefab` / `.asset` YAML 편집, Prefab variant 재구성, Scene 컴포넌트 순서 변경.
- 단일 사용처를 위한 인터페이스, factory, strategy, options 객체, 새 `Manager`/`Service`/`Controller` 추가.
- 사용자 요청 없는 asmdef 추가·분할, DI 컨테이너 도입, Addressables 전환, DOTS/ECS 전환, UniTask·R3 같은 새 외부 의존성 추가.
- 셰이더, 머티리얼, 애니메이션, 아트 에셋 변경.
- 기존 unrelated dead code 삭제.
- 성능 최적화 목적만으로 자료구조·알고리즘 교체.

## Unity 런타임 리스크 빠른 점검

- `Update` / `FixedUpdate` / `LateUpdate` 안에서:
  - `GameObject.Find` / `FindObjectOfType` 호출 → 캐시로 옮길 수 있는가?
  - `GetComponent<T>()` 반복 호출 → `Awake`/`OnEnable`에서 캐시했는가?
  - LINQ(`Where`, `Select`, `OrderBy`), `string.Format`, `+` string concat → 매 프레임 GC 할당.
  - `foreach` 위 컬렉션 enumerator boxing(특히 `IEnumerable<T>`로 받는 경우).
  - 매 프레임 `Instantiate` / `Destroy` → pool로 전환 가능한가? (요청에 포함된 경우에만)
- `Awake` / `OnEnable` 순서 의존성: 같은 GameObject 위 컴포넌트끼리 의존하면 `Awake`에서 안전한가?
- `OnEnable` 구독 ↔ `OnDisable` 해제 쌍이 모두 존재하는가?
- coroutine: 시작한 쪽이 비활성화될 때 `StopCoroutine` 또는 `StopAllCoroutines`로 정리되는가?
- `async` / `Task` / `UniTask`: `CancellationToken`을 받고 있으며, 대상이 destroy될 때 취소되는가? `await` 후 `this`가 destroy됐을 가능성은 확인했는가?
- main thread Unity API: `Task.Run` 내부에서 `transform`, `gameObject`, `Time.*` 같은 main-thread-only API를 호출하지 않는가?

## 테스트·검증 선택 기준

- Unity 의존성 없는 순수 C# 로직: 기존 EditMode 테스트 실행. 부족하면 `csharp-unit-test`로 별도 작업 분리.
- MonoBehaviour 생명주기·코루틴·UI: 기존 PlayMode 테스트 실행. 부족하면 Unity Test Framework PlayMode로 별도 작업 분리.
- Editor 전용 코드: 기존 EditMode 테스트 실행. `#if UNITY_EDITOR` 경계 컴파일 확인.
- 데이터 액세스(Repository / DbContext): `csharp-repository-test`로 별도 작업 분리.
- ASP.NET Core Controller / Endpoint: `csharp-api-test`로 별도 작업 분리.
- 실행 가능한 검증이 없으면: `dotnet build` 또는 IDE 빌드, Unity batchmode(`-batchmode -runTests -testPlatform editmode`) 중 가능한 것을 시도하고, 안 되면 시도 명령과 불가 사유를 보고.

전체 테스트가 과도하면 변경 범위 관련 테스트만 타깃 실행하고, 전체 미실행 위험을 보고한다.

## Dry-Run 예시 ① 단일 MonoBehaviour 중복 제거

요청: `PlayerController.cs`의 입력 처리와 이동 처리가 한 `Update`에 섞여 있고, 같은 클램프 로직이 두 곳에 중복. Unity 리팩터링.

판단:

- 분류: 동작 보존 리팩터링.
- 대상: `PlayerController.cs`.
- 제외: Prefab 인스펙터 값, `[SerializeField]` 이름, 입력 매핑, 새 컴포넌트 분리.
- 동작 보존 기준: public method signature, serialized field 이름·타입, `Update` 동작 결과, 이동 결과값, 입력 응답 타이밍.

최소 계획:

1. 중복된 클램프 식을 `PlayerController` 안 private static method로 추출 → verify: 같은 입력에 같은 출력.
2. `Update`를 `ReadInput()` → `Move()` 두 private 호출로 정리(메서드 분리만, 호출 순서·타이밍 동일) → verify: 컴파일 + 기존 PlayMode 시나리오.
3. 내가 만든 unused using 정리 → verify: `dotnet build` 또는 Unity 컴파일.

검증:

- 변경 전 EditMode 테스트 실행 결과 기록.
- 변경 후 같은 테스트 + PlayMode smoke 시나리오 실행.
- 실행 불가 시 시도 명령과 불가 사유 보고.

보고:

- 변경 파일, 유지한 동작, 제거한 중복, 검증 결과, 미실행 검증과 잔여 위험을 짧게 남긴다.

## Dry-Run 예시 ② 다중 파일 Runtime/Editor 경계 정리

요청: `Assets/Gameplay/Inventory/` 폴더의 Runtime 코드와 Editor 코드가 한 asmdef 안에 섞여 있어 빌드에 Editor 코드가 일부 포함. Unity 리팩터링.

판단:

- 분류: 동작 보존 리팩터링 + 빌드 경계 정리. Runtime 동작 변경 없음.
- 대상: `Assets/Gameplay/Inventory/Runtime/*.cs`, `Assets/Gameplay/Inventory/Editor/*.cs`, 관련 asmdef.
- 제외: ScriptableObject asset 데이터, public Runtime API, Prefab 참조, 신규 기능.
- 동작 보존 기준: Runtime public API, `[SerializeField]` 이름, ScriptableObject asset YAML, Inspector 동작, 에디터 도구 사용성.

최소 계획:

1. Editor 전용 파일을 `Editor/` 하위 폴더로 격리(파일 이동만, 코드 본문 무변경) → verify: Unity 컴파일.
2. `Inventory.asmdef`(Runtime)와 `Inventory.Editor.asmdef`(Editor-only) 분리. Editor asmdef는 `includePlatforms: [Editor]` 명시. namespace는 기존 유지 → verify: Unity 컴파일, Editor 도구 동작.
3. Runtime 코드에서 `#if UNITY_EDITOR`로 가렸던 Editor-only 분기 중 옮긴 쪽은 제거(내 변경의 부산물) → verify: Runtime 빌드에 Editor 심볼이 들어가지 않는지 확인.

직렬화·생명주기 리스크:

- 파일 이동 후 .meta GUID가 유지되는지 확인(Unity Editor에서 이동 시 자동 유지; Git 추적은 `git mv` 또는 동일 GUID 확인).
- ScriptableObject asset의 `m_Script` 참조가 깨지지 않는지 확인.

검증:

- Unity 컴파일(Editor / Runtime 양쪽).
- 가능하면 Unity batchmode `-runTests -testPlatform editmode`.
- Player 빌드에 Editor-only 타입이 포함되지 않는지 빌드 로그 또는 `EditorOnly` 빌드 리포트로 확인.

보고:

- 이동 파일, asmdef 분리 결과, GUID 보존 여부, 실행한 검증, 미실행 검증과 잔여 위험.

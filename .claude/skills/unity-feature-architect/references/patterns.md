# Game Programming Patterns — Unity 적용 카탈로그

[Game Programming Patterns — Robert Nystrom](https://gameprogrammingpatterns.com/contents.html) 목차를 짧게 재서술한 참고용 카탈로그. 각 패턴은 **"한 줄 정의 / Unity 적용 예 / 언제 쓰나 / 언제 쓰지 마라"** 4줄 포맷.

> 기본 원칙: 디폴트는 **패턴 미적용**. 단순 컴포넌트나 일반 C# 클래스로 충분하면 패턴을 쓰지 않는다. 적용 시 "왜 필요한가" 한 줄 정당화가 약하면 다시 빼고 단순 컴포넌트로 둔다.

## 1. Design Patterns Revisited

### Command
- 정의: 동작을 객체로 캡슐화해 실행 · 큐잉 · 취소를 가능하게 한다.
- Unity 적용 예: 입력 매핑 (리바인딩 / 리플레이), Undo 가능한 Editor 툴, 턴제 액션 큐.
- 언제 쓰나: 입력을 리바인딩하거나, 동작을 기록·되감기·네트워크 전송해야 할 때.
- 언제 쓰지 마라: 단일 입력 → 단일 호출처럼 즉시 처리만 필요한 경우.

### Flyweight
- 정의: 동일한 본질 데이터는 공유하고, 인스턴스별 데이터만 분리해 메모리를 절약.
- Unity 적용 예: `ScriptableObject`로 적/아이템 정의 공유, GPU Instancing, `Mesh`/`Material`/`AudioClip` 공유.
- 언제 쓰나: 같은 종류 인스턴스가 수백~수천 개이고 본질 데이터가 동일할 때.
- 언제 쓰지 마라: 인스턴스가 소수거나 인스턴스마다 데이터가 거의 다 다를 때.

### Observer
- 정의: 주체의 상태 변화를 구독자들에게 통지.
- Unity 적용 예: C# `event`/`Action`, `UnityEvent`, `ScriptableObject` 이벤트 채널.
- 언제 쓰나: 약결합 알림(체력 변경 → UI / 사운드 / 도전과제)이 필요할 때.
- 언제 쓰지 마라: 호출 흐름이 1:1로 명확하고 디버깅 추적성이 더 중요한 경우.

### Prototype
- 정의: 인스턴스를 새로 만들지 않고 기존 객체를 복제.
- Unity 적용 예: Prefab `Instantiate` (Unity가 이미 제공), 런타임 상태 복제.
- 언제 쓰나: 복잡한 초기화 비용이 큰 객체를 복제로 빠르게 찍어낼 때.
- 언제 쓰지 마라: Unity에서는 Prefab + `Instantiate`로 보통 충분 — 새 패턴 도입은 불필요한 경우가 많다.

### Singleton
- 정의: 전역 단일 인스턴스 접근.
- Unity 적용 예: `MonoBehaviour` 싱글톤 (`DontDestroyOnLoad`) 또는 `ScriptableObject` 싱글톤. **두 변형을 명확히 구분**한다.
- 언제 쓰나: 진짜로 게임 전역에 하나만 존재해야 하는 시스템(`AudioManager`, `SaveSystem`).
- 언제 쓰지 마라: 테스트 격리가 필요하거나, DI 컨테이너(Zenject/VContainer)가 있거나, 의존 추적이 어렵게 만드는 경우. 단일 사용 편의 목적은 거절.

### State
- 정의: 객체 상태별 동작을 별도 클래스로 분리하고 전이.
- Unity 적용 예: 캐릭터 행동(`Idle`/`Run`/`Attack`/`Stun`), AI FSM, UI 화면 전이.
- 언제 쓰나: 상태 수가 4개 이상이고 상태별 동작이 명확히 분기될 때.
- 언제 쓰지 마라: `bool` 2~3개 / `enum` switch 한 곳으로 충분한 경우.

## 2. Sequencing Patterns

### Double Buffer
- 정의: 한 버퍼를 읽는 동안 다른 버퍼에 쓰고 교체.
- Unity 적용 예: 커스텀 시뮬레이션(파티클 · 자동화 격자 · GPU 더블 버퍼), 프레임 간 일관성이 중요한 데이터.
- 언제 쓰나: 같은 프레임에서 읽기/쓰기 순서가 결과를 흔드는 시뮬레이션.
- 언제 쓰지 마라: 그래픽스 백버퍼는 이미 Unity/GPU가 처리 — 일반 게임 로직에 끌어들이지 않는다.

### Game Loop
- 정의: 입력 처리 → 업데이트 → 렌더링을 정해진 타이밍으로 반복.
- Unity 적용 예: **Unity가 이미 제공** — `Update`/`FixedUpdate`/`LateUpdate`/`PlayerLoop`/`UpdateManager`.
- 언제 쓰나: Unity 외부에서 자체 시뮬레이션 루프를 돌릴 때만.
- 언제 쓰지 마라: Unity 안에서 새 게임 루프를 직접 구현하지 않는다. 라이프사이클을 활용한다.

### Update Method
- 정의: 모든 액티브 객체에 매 프레임 `Update`를 호출.
- Unity 적용 예: **Unity가 이미 제공** — `MonoBehaviour.Update` 자체.
- 언제 쓰나: 매우 많은(수천+) 오브젝트가 모두 매 프레임 호출되어 `Update` 오버헤드가 병목일 때, **자체 매니저로 묶어 일괄 처리**.
- 언제 쓰지 마라: 일반 규모에서는 `MonoBehaviour.Update`로 충분 — 새 매니저로 묶지 않는다.

## 3. Behavioral Patterns

### Bytecode
- 정의: 도메인 동작을 작은 가상 머신용 명령어 집합으로 표현.
- Unity 적용 예: 스킬/스크립팅 시스템, 모드 지원, 데이터 주도 컷씬 / 다이얼로그.
- 언제 쓰나: 비프로그래머가 게임 동작을 데이터로 정의해야 하고, 실행 안전성이 중요할 때.
- 언제 쓰지 마라: 단순 컨피그 / SO / Timeline / Visual Scripting으로 충분한 경우.

### Subclass Sandbox
- 정의: 기본 클래스가 제공하는 **고수준 연산 도구**를 자식 클래스가 조합해 동작을 정의.
- Unity 적용 예: `Enemy` 베이스가 `MoveTo` / `PlayAnim` / `SpawnFx`만 노출하고, `EnemyOrc` / `EnemyGoblin`이 조합.
- 언제 쓰나: 다양한 자식이 같은 저수준 API(물리/애니/사운드)에 의존하지만 다른 시나리오로 조합할 때.
- 언제 쓰지 마라: 컴포지션(컴포넌트 추가) 또는 ScriptableObject 데이터로 더 잘 표현되는 경우.

### Type Object
- 정의: "종류"를 인스턴스 객체로 표현해 코드 변경 없이 종류를 늘린다.
- Unity 적용 예: `EnemyTypeSO` / `WeaponTypeSO` / `ItemTypeSO` — `ScriptableObject`가 자연스럽게 이 역할.
- 언제 쓰나: 새 적/아이템 추가를 데이터(SO)만 만들어서 처리하고 싶을 때.
- 언제 쓰지 마라: 종류가 고정 소수이고 코드 분기가 더 읽기 쉬운 경우.

## 4. Decoupling Patterns

### Component
- 정의: 객체를 책임 단위 컴포넌트로 쪼개 조합으로 동작을 구성.
- Unity 적용 예: **Unity GameObject-Component 자체** — `Rigidbody` + `Collider` + `Health` + `Renderer`.
- 언제 쓰나: 항상. **분해 원칙**으로 사용하지, "패턴 도입"으로 다시 끌어들이지 않는다.
- 언제 쓰지 마라: 1책임을 인위적으로 여러 컴포넌트로 쪼개 결합도를 키우는 경우.

### Event Queue
- 정의: 이벤트를 즉시 처리하지 않고 큐에 쌓아 정해진 시점에 일괄 처리.
- Unity 적용 예: 입력 버퍼링, 네트워크 메시지 큐, 사운드 요청 큐(우선순위 / 디바운스).
- 언제 쓰나: 발행 시점과 처리 시점을 분리해야 하거나, 순서/디바운스/우선순위가 필요할 때.
- 언제 쓰지 마라: 단순 통지이고 즉시 처리해도 무방한 경우 — `event`/`Action`이면 충분.

### Service Locator
- 정의: 전역 레지스트리에서 서비스 객체를 조회.
- Unity 적용 예: 작은 프로젝트의 `ServiceLocator.Get<IAudio>()`. DI 컨테이너(Zenject / VContainer) 사용 시 그쪽으로 대체.
- 언제 쓰나: 싱글톤보다 테스트 가능하게 만들고 싶지만 DI 도입은 부담스러운 중간 단계.
- 언제 쓰지 마라: 이미 DI 컨테이너를 쓰는 프로젝트, 의존 추적이 중요한 코어 시스템. 단일 호출자만 있는 경우.

## 5. Optimization Patterns

### Data Locality
- 정의: 캐시 친화적으로 데이터를 묶어 메모리 접근 비용을 줄인다.
- Unity 적용 예: SoA(Struct of Arrays), `NativeArray`/`Burst`/Jobs, DOTS/ECS (별도 도입 필요).
- 언제 쓰나: 프로파일러로 캐시 미스가 핫스팟임이 확인됐고, 대규모 동질 데이터를 매 프레임 순회할 때.
- 언제 쓰지 마라: 측정 없이 추측만으로. 그리고 DOTS/ECS 도입은 사용자가 명시 요청 시에만.

### Dirty Flag
- 정의: 변경된 객체만 표시했다가 필요한 시점에 일괄 재계산.
- Unity 적용 예: UI 갱신 (`SetDirty`), 메시 생성, 절차적 텍스처, 저장 시스템.
- 언제 쓰나: 매 프레임 재계산 비용이 크고, 실제 변경 빈도가 낮을 때.
- 언제 쓰지 마라: 재계산 비용이 작거나, 더티 추적 로직 자체가 더 복잡해지는 경우.

### Object Pool
- 정의: 생성/파괴 대신 재사용 가능한 객체 풀에서 빌려 쓴다.
- Unity 적용 예: 총알, 파티클, 적, 데미지 텍스트. **Unity 2021+ `UnityEngine.Pool` 활용** (직접 구현 X).
- 언제 쓰나: 짧은 수명 객체가 초당 수십~수백 개 생성/파괴되어 GC 스파이크가 측정될 때.
- 언제 쓰지 마라: 동시 인스턴스가 적거나 생성 빈도가 낮을 때. "혹시 모르니까" 도입은 거절.

### Spatial Partition
- 정의: 공간을 분할해 근접 객체만 빠르게 찾는다.
- Unity 적용 예: Quadtree / Octree / Grid, `Physics.OverlapSphere` 대용, AI 시야 범위 검색.
- 언제 쓰나: 객체 N개의 N×N 거리/충돌 검사가 병목으로 측정됐고 Unity 물리만으로 부족할 때.
- 언제 쓰지 마라: Unity `Physics`/`Collider` 트리거로 충분한 경우. N이 작은 경우.

## Unity 특화 주석 요약

- **Singleton**: `MonoBehaviour` 싱글톤 vs `ScriptableObject` 싱글톤 — 라이프사이클과 직렬화 동작이 다르다. 어느 변형인지 설계 문서에 명시.
- **Game Loop / Update Method**: Unity가 이미 제공 → 새로 구현하지 말고 `Awake`/`Start`/`OnEnable`/`Update`/`FixedUpdate`/`LateUpdate`/`OnDestroy`/`PlayerLoop`를 활용.
- **Component**: Unity의 GameObject-Component 자체이므로 "패턴 도입"이 아니라 **분해 원칙**으로 사용.
- **Service Locator**: Zenject / VContainer 등 DI 컨테이너 사용 시 그쪽으로 대체.
- **Object Pool**: Unity 2021+ `UnityEngine.Pool` 활용 (직접 구현 금지). 측정 없이 도입하지 않는다.
- **Addressables**: `com.unity.addressables`가 설치되어 있거나 요구사항에서 원격 / 지연 로딩을 요구할 때만 설계에 포함.
- **DOTS / ECS**: 사용자가 명시 요청 시에만. 일반 OOP 컴포넌트 모델과 패턴 가이드가 다르므로 별도 안내가 필요함.

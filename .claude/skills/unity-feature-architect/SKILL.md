---
name: unity-feature-architect
description: Unity 3D 신규 기능을 **구현하기 전에** 요구사항 → 최소 구조 설계 → Unity 객체 분해(MonoBehaviour / ScriptableObject / 일반 C# / Editor) → Game Programming Patterns 필요성 검토 → 컴포넌트·인터페이스 시그니처 초안 → 폴더·Asmdef·Prefab/Scene/Addressable 배치까지 한 사이클로 합의하기 위한 설계 스킬. 산출물은 대상 Unity 프로젝트의 `docs/design/<feature>-design.md` 설계 합의 문서이며, 구현·리팩터링·테스트·아트는 다른 작업으로 위임한다. 트리거 어구는 "Unity 신규 기능 설계", "Unity 기능 설계", "Unity 시스템 설계", "Unity feature design", "Unity design doc"이며, 자연어 게임 요구사항이나 `prd/*-prd.md` 참조 문서 + "코드 작성 전 설계" 요청에도 트리거한다. 단순 스크립트 구현/리팩터링/테스트/사용법 문서/셰이더·아트/빌드 설정은 트리거 아님.
---

# Unity Feature Architect

Unity 3D / C# 신규 기능을 **구현 전에** 합의하기 위한 설계 스킬. 메서드 본문·게임 로직·셰이더·아트·테스트 코드는 작성하지 않고, `docs/design/<feature>-design.md` 형식의 **설계 합의 문서**만 산출한다.

스킬 자산:

- 문서 스켈레톤: [template.md](template.md)
- Game Programming Patterns 카탈로그: [references/patterns.md](references/patterns.md)
- 선택형 검토 에이전트: [../../agents/unity-design-reviewer.md](../../agents/unity-design-reviewer.md)

## When to invoke

| 요청 유형                                          | 동작                                                                 |
| -------------------------------------------------- | -------------------------------------------------------------------- |
| "Unity 신규 기능 설계" / "Unity feature design" 등 | 설계 합의 문서 신규 작성                                             |
| 자연어 게임 요구사항 + "코드 작성 전 설계"         | 설계 합의 문서 신규 작성                                             |
| `prd/*-prd.md` 참조 + "설계 문서로 만들어"         | 설계 합의 문서 신규 작성                                             |
| 단순 스크립트 구현 요청                            | 사용 안 함 → 일반 코딩 작업                                          |
| 기존 코드 리팩터링                                 | 사용 안 함                                                           |
| 테스트 작성                                        | 사용 안 함 → `csharp-unit-test` / `csharp-api-test` 등               |
| 사용법 문서                                        | 사용 안 함 → `feature-docs`                                          |
| 셰이더 · 머티리얼 · 애니메이션 · 아트              | 사용 안 함                                                           |
| 빌드 · 배포 · CI 설정                              | 사용 안 함                                                           |

"MonoBehaviour 설계", "Unity 컴포넌트 설계" 같은 표현은 **신규 기능 설계 문서 산출 의도가 함께 있을 때만** 트리거한다. 모호하면 사용자에게 "구현 전 설계 문서가 필요한가, 아니면 바로 구현인가"를 확인한다.

## Procedure

1. **프로젝트 컨텍스트 확인** — Unity 프로젝트가 제공되면 다음을 우선 확인하고 결과를 설계 문서에 인용한다.
   - `Assets/` 구조와 폴더 컨벤션 (`Assets/_Project/`, `Assets/Scripts/<Feature>/` 등)
   - `ProjectSettings/ProjectVersion.txt` — Unity 버전
   - `Packages/manifest.json` — URP/HDRP, Addressables, Input System, DI(Zenject/VContainer) 패키지 설치 여부
   - `ProjectSettings/GraphicsSettings.asset` + `ProjectSettings/QualitySettings.asset` — 렌더 파이프라인
   - `ProjectSettings/ProjectSettings.asset` — Active Input Handling (Old/New/Both)
   - 기존 asmdef 경계, UI 시스템(UGUI / UI Toolkit)
   - 프로젝트가 없으면 일반 Unity 기준으로 가정하고, 그 가정을 설계 문서의 "요구사항 정리"·"파일·폴더·Asmdef 배치" 섹션에 명시한다.
2. **요구사항 수집** — 게임 기능 / 입력(키·터치·이벤트) / 출력(화면·사운드·상태 변화) / 제약(타깃 플랫폼·프레임·메모리)을 추출한다. 모호하면 사용자에게 확인한다.
3. **최소 설계안 우선** — 가장 적은 GameObject, Component, ScriptableObject, 일반 C# 클래스, Prefab, Scene 변경으로 동작 가능한 설계를 먼저 제시한다. 추가 객체·패턴·인터페이스·Asmdef·Addressables는 필요성이 **한 줄로 설명될 때만** 추가한다. 더 단순한 접근이 있으면 그쪽을 기본안으로 삼고, 복잡한 대안은 제외하거나 "제외한 구조·패턴과 제외 이유"에 짧게 적는다.
4. **Unity 객체 분해** — 요구사항을 다음 단위로 분해한다. 억지로 모든 것을 MonoBehaviour로 만들지 않는다. 단일 책임이 실제 게임플레이 요구사항에 직접 연결되지 않으면 새 객체로 분리하지 않는다.
   - **MonoBehaviour**: Scene · GameObject 생명주기 · 물리 · 입력이 필요한 경우
   - **ScriptableObject**: 데이터 · 설정 · 이벤트 채널 · 정적 카탈로그
   - **일반 C# 클래스 / static**: 순수 로직 · 계산 · 상태 머신 본체
   - **Editor 확장**: 인스펙터 / 툴이 필요한 경우 (`Assets/.../Editor/`)
5. **패턴 필요성 검토** — 디폴트는 **패턴 미적용**. 필요 시에만 [references/patterns.md](references/patterns.md)를 읽어 적용 가능한 패턴을 찾는다. 단순 컴포넌트나 일반 C# 클래스로 충분하면 패턴을 쓰지 않는다. 적용 시 "왜 필요한가" **한 줄 정당화**를 같이 적는다. 정당화가 약하면 패턴을 빼고 단순 컴포넌트로 둔다.
6. **인터페이스 생성 기준** — 외부 의존성, 대체 구현, DI 경계, Editor↔Runtime 경계, 이미 프로젝트에서 관례화된 경계가 있을 때만 인터페이스를 만든다. **단일 구현 MonoBehaviour에는 인터페이스를 만들지 않는다.** 테스트만을 위한 단일 구현 인터페이스도 만들지 않는다.
7. **구조 초안** — 컴포넌트 · 클래스의 시그니처(이름 · `[SerializeField]` 필드 · 메서드 시그니처)만 작성한다. Unity 생명주기 메서드(`Awake` / `Start` / `OnEnable` / `Update` / `FixedUpdate` / `OnDestroy` 등) 중 **사용할 것만** 표시한다. 메서드 본문은 작성하지 않고, 코드블록을 쓰더라도 `// Signature sketch, not implementation`임을 명시한다. `{ }` 안에 실제 로직을 넣지 않는다. 명명 · 정렬은 `csharp-coding-standards` 룰을 따른다.
8. **파일 · 폴더 · Asmdef 배치** — 기존 프로젝트 관례를 우선한다. Feature 단위(`Assets/Scripts/<Feature>/Runtime/`, `Assets/Scripts/<Feature>/Editor/`) 구조는 기존 관례가 없거나 기능 규모가 명확히 분리될 때만 제안한다. **새 asmdef는 기본적으로 만들지 않는다.** Runtime/Editor 분리, 명확한 모듈 경계, 컴파일 의존성 단방향 유지가 필요할 때만 신설하고, 필요 없으면 "새 asmdef 없음"으로 적는다.
9. **Prefab · Scene · Addressable 배치** — 새 Prefab/Scene/Addressable이 필요하면 경로와 의존 관계, 로딩 시점을 명시한다. 기존 Scene 오브젝트나 Prefab 수정으로 충분하면 새 에셋을 만들지 않는다. Addressables는 `com.unity.addressables`가 설치되어 있거나 요구사항에서 명시된 경우에만 제안한다.
10. **성능 · GC 검토** — 매 프레임 `Update`에서 발생할 수 있는 할당, 빈번한 `GetComponent` / `Find` 호출, `Instantiate` / `Destroy` 빈도, 코루틴 vs `UniTask` / `Task` 선택, 캐싱 대상에 대해 짧은 코멘트를 남긴다. `UnityEngine.Pool` 기반 Object Pool은 대량 생성·파괴가 명확히 예측될 때만 권장하고, 그렇지 않으면 적용하지 않는다.
11. **단순화 게이트** — 다음 중 하나라도 해당되면 최종 문서 전에 구조를 줄일 수 있는지 먼저 재검토하고, 자가 검토 결과를 문서의 "단순화 자가 검토 결과" 섹션에 남긴다.
    - 패턴 2개 이상 사용
    - 인터페이스 3개 이상
    - 단일 구현 인터페이스가 여럿
    - 새 asmdef를 1개 이상 제안
    - `Manager` / `Service` / `Controller` 이름의 객체가 실제 GameObject나 유스케이스에 직접 연결되지 않음
    - 도메인 객체가 실제 게임플레이에서 직접 나오지 않음
    - 모든 것을 MonoBehaviour로 만든 경우 (반대로 모든 것을 ScriptableObject로만 만든 경우 포함)
12. **선택형 에이전트 검토** — 사용자가 독립 검토를 요청했거나 단순화 게이트에 걸린 경우 [unity-design-reviewer](../../agents/unity-design-reviewer.md) 에이전트에게 초안을 검토시킨다. 입력은 요구사항 요약 · 프로젝트 컨텍스트 요약 · 설계 초안 경로 또는 본문으로 제한한다. 에이전트에게 구현 · 리팩터링 · 테스트 작성을 맡기지 않는다.
13. **검토 반영** — 에이전트 의견 중 **단순화에 직접 도움이 되는 내용만** 최종 설계 문서에 반영한다. 반영하지 않은 의견은 "제외한 구조·패턴과 제외 이유" 섹션에 짧게 남긴다.
14. **위임 안내** — 산출 문서 말미에 "구현은 일반 코딩 작업, 테스트는 `csharp-unit-test` 등, 셰이더 · 아트는 별도 작업으로 위임"을 명시한다.
15. **출력 및 보고** — 설계 문서를 `docs/design/<feature>-design.md`(또는 사용자 지정 경로)에 저장하고, 만든 경로 + 핵심 결정(분리 단위 · 적용 패턴 수 · Asmdef 경계 유무 · 에이전트 검토 여부)을 1~3줄로 보고한다.

## 설계 문서 섹션 구성

[template.md](template.md)는 다음 13개 섹션을 포함한다. 작성 시 비어 있는 섹션은 "없음" 한 단어로 남기되 섹션 자체는 유지한다.

1. 기능 요약 (1~3문장)
2. 요구사항 정리 (입력 / 출력 / 제약 / 플랫폼 · 성능 목표)
3. 단순 설계 기준
4. Unity 객체 분해 (MonoBehaviour / ScriptableObject / 일반 C# / Editor — 각 책임 1줄)
5. 적용 패턴 (Game Programming Patterns 패턴명 + 정당화 1줄, 없으면 "없음")
6. 제외한 구조 · 패턴과 제외 이유
7. 컴포넌트 · 인터페이스 시그니처 (Unity 생명주기 메서드 사용 표시 포함, 구현 없음, `// Signature sketch, not implementation`)
8. 파일 · 폴더 · Asmdef 배치 제안
9. Prefab · Scene · Addressable 배치 (해당 시)
10. 성능 · GC 검토 메모
11. 단순화 자가 검토 결과
12. 에이전트 검토 결과 (사용한 경우에만)
13. 위임 다음 단계 (구현 / 테스트 / 아트 등)

## Writing principles

1. **구현 전 합의 문서** — 메서드 본문 · 게임 로직 · 알고리즘은 적지 않는다. 시그니처와 책임 기술까지만.
2. **MonoBehaviour 디폴트 OFF** — Scene / GameObject / 물리 / 입력에 진짜 묶여 있을 때만 MonoBehaviour를 쓴다. 순수 로직은 일반 C# 클래스가 우선.
3. **패턴 · 인터페이스 · Asmdef · Addressables 그룹 · 새 `Manager`/`Service`/`Controller`는 기본 미적용** — 요구사항이나 기존 프로젝트 관례로 필요성이 설명될 때만 제안한다.
4. **OOP 컴포넌트 모델 기본**, DOTS/ECS는 사용자가 **명시 요청 시에만** 다루고 별도 패턴 가이드가 필요하다고 안내한다.
5. **다른 엔진 요청은 거절** — Unity 3D / C# 전용.
6. **`csharp-coding-standards` 룰을 따른다** — 명명 · 정렬 · 시그니처 스타일.
7. **에이전트 결과는 의견** — 최종 산출 책임은 스킬 실행자가 진다. 에이전트가 제안한 추가 구조도 단순화 게이트를 통과해야 한다.

## Anti-patterns

- 시그니처 자리에 메서드 본문 · 게임 로직을 적는다.
- 모든 책임을 MonoBehaviour로 만든다 (또는 반대로 모든 것을 ScriptableObject로만 만든다).
- 단일 구현 MonoBehaviour에 인터페이스를 추출한다.
- "테스트를 위해" 단일 구현 인터페이스를 만든다.
- `Manager` / `Service` / `Controller` 이름의 객체가 실제 GameObject나 유스케이스 없이 등장한다.
- 새 asmdef · Addressables 그룹을 정당화 없이 신설한다.
- 매 프레임 `Update`에서 `GetComponent` / `Find` / `Instantiate`를 반복하도록 설계해 두고 성능 · GC 메모를 비운다.
- Game Loop / Update Method를 Unity가 이미 제공하는데 새로 패턴으로 구현한다.
- DOTS/ECS를 사용자가 요청하지 않았는데 끌어들인다.
- 동작 변화 없는 리팩터링이나 테스트 코드 작성을 이 스킬에서 처리한다.

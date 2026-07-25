---
name: unity-refactoring-architect
description: Unity 3D / C# 기존 코드를 동작 보존 리팩터링하는 스킬. 사용자가 "Unity 리팩터링", "Unity 코드 리팩터링", "Unity refactoring", "MonoBehaviour 리팩터링", "ScriptableObject 리팩터링", "Unity GC 개선 리팩터링"을 요청하거나, Unity 프로젝트의 `.cs` 파일·`Assets/` 하위 폴더·MonoBehaviour·ScriptableObject·Editor script·asmdef 경계를 주고 "동작은 유지하고 정리", "중복 제거", "Update 최적화", "컴포넌트 책임 분리", "인스펙터 필드 정리" 같은 기존 동작 보존 리팩터링을 요청할 때 사용한다. 신규 기능 설계, 새 게임 시스템 구현, 테스트 코드만 생성, 사용법 문서, 셰이더·머티리얼·아트 작업, Unity 버전 업그레이드, DOTS/ECS 전환에는 사용하지 않는다.
---

# Unity Refactoring Architect

Unity 3D / C# 기존 코드를 **동작 보존**으로 리팩터링한다. 새 기능을 설계하거나 구현하지 않고, 변경 전 가정·성공 기준·검증 방법을 먼저 정한 뒤 필요한 파일만 고친다.

스킬 자산:

- 리팩터링 계획 템플릿: [template.md](template.md)
- Unity 리팩터링 체크리스트와 dry-run 예시: [references/checklist.md](references/checklist.md)
- 선택형 작업 에이전트: [../../agents/unity-refactoring-architect.md](../../agents/unity-refactoring-architect.md)

## When to invoke

| 요청 유형 | 동작 |
|-----------|------|
| "Unity 리팩터링" / "Unity refactoring" | 기존 Unity C# 코드 리팩터링 |
| "MonoBehaviour 리팩터링" / "ScriptableObject 리팩터링" | Unity 직렬화·생명주기 보존 기준으로 리팩터링 |
| "동작은 유지하고 정리" / "중복 제거" | 최소 변경으로 중복·책임 혼재 제거 |
| "Update 최적화" / "Unity GC 개선 리팩터링" | 매 프레임 할당·반복 조회·생성/파괴 리스크 개선 |
| 신규 기능 설계 | 사용 안 함 → `unity-feature-architect` |
| 테스트 코드만 작성 | 사용 안 함 → `csharp-unit-test` 등 |
| 셰이더·아트·빌드 설정 | 사용 안 함 |

기능 추가나 동작 변경 요청이 섞여 있으면 리팩터링 범위와 기능 변경 범위를 분리한다. 모호하면 구현 전에 사용자에게 확인한다.

## Procedure

1. **요청 분류** — 리팩터링인지, 버그 수정인지, 신규 기능인지, 설계 문서 작성인지 먼저 구분한다.
2. **가정 명시** — 유지해야 할 동작, public API, Prefab/Scene 참조, 테스트 가능 여부가 불명확하면 먼저 질문한다.
3. **프로젝트 컨텍스트 확인** — Unity 프로젝트가 제공되면 다음을 확인한다.
   - `Assets/` 구조와 기존 폴더 컨벤션
   - `ProjectSettings/ProjectVersion.txt`
   - `Packages/manifest.json`
   - asmdef 경계와 테스트 폴더
   - UGUI / UI Toolkit, DI 컨테이너 사용 여부
   - 프로젝트가 없으면 "코드 조각 기준"으로 가정하고 보고한다.
4. **성공 기준 정의** — "동작 동일", "기존 테스트 통과", "컴파일 오류 없음", "할당 제거", "중복 제거"처럼 검증 가능한 기준으로 바꾼다.
5. **변경 범위 고정** — 작업 전 대상 파일과 제외 파일을 정한다. 관련 없는 스타일 정리, 네이밍 변경, 폴더 이동, 포맷팅은 하지 않는다.
6. **계획 문서화 판단** — 다음 중 하나라도 해당되면 [template.md](template.md)로 `docs/refactoring/<scope>-refactor-plan.md`를 작성하거나 대화 내 짧은 계획을 먼저 남긴다.
   - 3개 이상 파일 수정
   - public API 변경 가능성
   - serialized field 변경 가능성
   - asmdef 변경 가능성
   - Scene/Prefab 참조 리스크
7. **Unity 직렬화 보존** — `[SerializeField]`, public field, `[FormerlySerializedAs]`, `SerializeReference`, `UnityEvent`, Prefab/Scene 참조, ScriptableObject asset 참조를 확인한다. 직렬화된 필드명 변경은 기본 금지다.
8. **생명주기 보존** — `Awake`, `OnEnable`, `Start`, `Update`, `FixedUpdate`, `LateUpdate`, `OnDisable`, `OnDestroy` 호출 순서와 이벤트 구독·해제 타이밍을 바꾸지 않는다. 바꿔야 하면 동작 변경으로 분류해 사용자 확인을 받는다.
9. **단순성 우선** — 단일 사용처를 위한 인터페이스·팩토리·전략 패턴·서비스 계층을 만들지 않는다. 중복 제거는 가까운 private helper나 작은 일반 C# 클래스로 충분한지 먼저 본다.
10. **수술적 패치** — 필요한 코드만 수정하고 기존 스타일을 따른다. 자신의 변경으로 생긴 unused import, dead variable, orphan helper는 제거한다. 기존 죽은 코드는 보고만 한다.
11. **Unity 런타임 리스크 점검** — 매 프레임 할당, LINQ, boxing, 반복 `GetComponent` / `Find`, 빈번한 `Instantiate` / `Destroy`, coroutine 취소, async / `Task` / `UniTask` 생명주기, main thread API 호출 여부를 확인한다.
12. **에이전트 사용 판단** — 파일 1~2개와 검증이 명확한 작업은 스킬 단독으로 처리한다. 여러 asmdef, Runtime/Editor 분리, Scene/Prefab 참조 리스크, 성능 리팩터링이 얽히면 [unity-refactoring-architect](../../agents/unity-refactoring-architect.md) 에이전트를 사용할 수 있다.
13. **검증** — 기존 EditMode/PlayMode 테스트, asmdef 컴파일, `dotnet`/IDE 빌드, Unity batchmode 테스트 중 프로젝트가 제공하는 방법을 사용한다. 실행 환경이 없으면 시도한 명령과 불가 사유를 보고한다.
14. **최종 보고** — 변경 파일, 유지한 동작, 제거한 중복·위험, 실행한 검증, 실행하지 못한 검증, 남은 리스크를 1~5줄로 보고한다.

## Refactoring Plan Sections

[template.md](template.md)는 다음 11개 섹션을 포함한다. 작은 리팩터링은 별도 파일을 만들지 않고 같은 항목을 대화 내 계획으로 짧게 남겨도 된다.

1. 대상 범위
2. 현재 문제
3. 유지해야 할 동작
4. 명시한 가정과 확인 질문
5. 변경 파일 / 제외 파일
6. 최소 변경안
7. Unity 직렬화·Prefab·Scene 참조 리스크
8. 생명주기·성능·GC 리스크
9. 검증 계획
10. 검증 결과
11. 보류한 개선 사항

## Writing Principles

1. **동작 보존이 기본값** — 사용자 요청 없이 기능을 추가하거나 결과를 바꾸지 않는다.
2. **생각 후 구현** — 모호한 요구사항, 직렬화 필드 변경, 생명주기 변경, public API 변경은 먼저 확인한다.
3. **단순성 우선** — 추상화·패턴·계층은 단순한 변경으로 해결되지 않을 때만 쓴다.
4. **수술적 변경** — 변경 이유가 사용자 요청과 직접 연결되지 않으면 수정하지 않는다.
5. **Unity 직렬화 보존** — serialized field 이름과 asset 참조를 보존한다. 이름 변경이 필요하면 `[FormerlySerializedAs]` 등 마이그레이션 방안을 명시한다.
6. **Unity 생명주기 보존** — 이벤트 구독·해제, coroutine, async 생명주기를 임의로 바꾸지 않는다.
7. **기존 검증 우선** — 새 테스트 프레임워크를 도입하지 않고 프로젝트가 이미 제공하는 검증 방법을 따른다.
8. **`csharp-coding-standards` 룰을 따른다** — C# 명명·정렬·코드 요소 순서는 기존 팀 규칙에 맞춘다.

## Detailed Checklist

세부 점검이 필요할 때만 [references/checklist.md](references/checklist.md)를 참조한다. 동작 변경 감지 지점, 허용/금지 리팩터링, Unity 런타임 리스크 빠른 점검, 테스트·검증 선택 기준, Unity-specific dry-run 예시 2개(단일 MonoBehaviour 중복 제거 / 다중 파일 Runtime·Editor 경계 정리)를 포함한다.

## Anti-patterns

- "리팩터링" 요청에 새 기능·새 UI·새 저장 포맷을 섞는다.
- 단일 구현 MonoBehaviour를 위해 인터페이스·팩토리·전략 패턴을 만든다.
- `[SerializeField]` private field 이름을 바꾸고 Prefab/Scene 마이그레이션을 언급하지 않는다.
- `OnEnable` 구독 / `OnDisable` 해제 쌍을 깨뜨린다.
- `Update` 최적화 중 동작 타이밍을 바꾼다.
- 사용자 요청 없이 asmdef, DI 컨테이너, Addressables, DOTS/ECS를 도입한다.
- 관련 없는 파일을 포맷하거나 네이밍을 고친다.
- 검증을 실행하지 못했는데 "완료"만 보고한다.

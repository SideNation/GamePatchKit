# <FeatureName> 설계 문서

## 1. 기능 요약
이 기능이 무엇이고 게임에서 언제 동작하는지 1~3문장.

## 2. 요구사항 정리
- **입력**: 키 / 터치 / 이벤트 / 외부 호출
- **출력**: 화면 / 사운드 / 상태 변화 / 저장
- **제약**: 타깃 플랫폼 (PC / Mobile / Console), 프레임 예산, 메모리 예산, 네트워크 조건
- **성능 목표**: 예) 60fps 유지, 프레임 당 GC 0 byte, 동시 인스턴스 N개

## 3. 단순 설계 기준
가장 적은 GameObject · Component · ScriptableObject · 일반 C# 클래스 · Prefab · Scene 변경으로 동작 가능한 설계를 적는다.

- 새 객체 / 에셋을 만들기 전에 기존 GameObject · Prefab · Scene에 붙일 수 있는지 검토.
- 추가 객체 · 패턴 · 인터페이스 · Asmdef · Addressables는 "왜 필요한가"가 한 줄로 설명될 때만 도입.

## 4. Unity 객체 분해
각 책임 1줄. 단일 책임이 실제 게임플레이 요구사항에 직접 연결되지 않으면 분리하지 않는다.

| 분류              | 이름                  | 책임                           |
| ----------------- | --------------------- | ------------------------------ |
| MonoBehaviour     | `<Name>`              | 책임 한 줄                     |
| ScriptableObject  | `<Name>`              | 데이터 / 설정 / 이벤트 채널    |
| 일반 C# 클래스    | `<Name>`              | 순수 로직 / 상태 머신          |
| Editor 확장       | `<Name>`              | 인스펙터 / 툴 (`Editor/` 아래) |

## 5. 적용 패턴
[references/patterns.md](references/patterns.md) 중 필요한 것만. 없으면 "없음".

| 패턴 | 적용 대상 | 정당화 (한 줄) |
| ---- | --------- | -------------- |
|      |           |                |

## 6. 제외한 구조 · 패턴과 제외 이유
복잡도 대비 이득이 약해서 도입하지 않은 대안.

- 예: `Service Locator` 미적용 — DI 컨테이너 미사용 프로젝트라 단순 직참조로 충분
- 예: `Object Pool` 미적용 — 동시 인스턴스 ≤ 10개로 예측

## 7. 컴포넌트 · 인터페이스 시그니처
Unity 생명주기 메서드 중 **사용할 것만** 표시. 구현은 적지 않는다.

```csharp
// Signature sketch, not implementation.

public class <Name> : MonoBehaviour
{
    [SerializeField] private <Type> _<fieldName>;

    private void Awake();
    private void OnEnable();
    private void Update();
    private void OnDestroy();

    public <ReturnType> <PublicMethod>(<Params>);
}
```

```csharp
// Signature sketch, not implementation.

[CreateAssetMenu(menuName = "...")]
public class <Name>SO : ScriptableObject
{
    public <Type> <FieldName>;
}
```

```csharp
// Signature sketch, not implementation. Add only if a real boundary exists.

public interface I<Name>
{
    <ReturnType> <Method>(<Params>);
}
```

## 8. 파일 · 폴더 · Asmdef 배치 제안
기존 프로젝트 관례 우선. 새 asmdef는 기본적으로 만들지 않는다.

```
Assets/Scripts/<Feature>/
├── Runtime/
│   ├── <Name>.cs
│   └── <Name>SO.cs
└── Editor/                 # 인스펙터 / 툴이 필요할 때만
    └── <Name>Editor.cs
```

- 새 asmdef: 없음 / 또는 `<Feature>.Runtime.asmdef` (이유: Runtime ↔ Editor 분리 / 컴파일 의존성 단방향)

## 9. Prefab · Scene · Addressable 배치
해당 사항이 없으면 "없음".

- Prefab: `Assets/Prefabs/<Feature>/<Name>.prefab` — 의존: `<NameSO>`
- Scene: 기존 `MainGame.unity`에 GameObject 추가 / 또는 새 `Assets/Scenes/<Feature>.unity`
- Addressable: 그룹 `<Feature>` (라벨 `<label>`) — 로딩 시점: 게임 시작 시 / 씬 진입 시 / 사용자 액션 시

## 10. 성능 · GC 검토 메모
- `Update` 매 프레임 할당 여부와 회피 방법 (캐싱 / 풀링 / 이벤트)
- `GetComponent` / `Find` 호출 위치 (가능하면 `Awake`에서 1회 캐싱)
- `Instantiate` / `Destroy` 빈도 → `UnityEngine.Pool` 적용 여부
- 코루틴 vs `UniTask` / `Task` 선택과 이유

## 11. 단순화 자가 검토 결과
다음 항목을 점검하고 결과를 적는다.

- 패턴 2개 이상 사용? → 줄였는가
- 인터페이스 3개 이상? → 단일 구현 인터페이스 제거했는가
- 새 asmdef 1개 이상 제안? → 정말 필요한가
- `Manager` / `Service` / `Controller` 객체가 실제 GameObject / 유스케이스에 연결됐는가
- 도메인 객체가 실제 게임플레이에서 직접 나오는가
- MonoBehaviour 일변도 또는 ScriptableObject 일변도가 아닌가

## 12. 에이전트 검토 결과
[unity-design-reviewer](../../agents/unity-design-reviewer.md)를 사용한 경우에만 작성. `Blocker` / `Simplify` / `Unity Fit` / `Residual Risk` 항목 요약 + 반영 / 미반영 표시.

- 사용하지 않았다면 이 섹션을 통째로 삭제하거나 "사용 안 함"으로 남긴다.

## 13. 위임 다음 단계
- 구현: 일반 코딩 작업
- 단위 테스트: [csharp-unit-test](../../skills/csharp-unit-test/SKILL.md)
- API / 통합 테스트: [csharp-api-test](../../skills/csharp-api-test/SKILL.md) (필요 시)
- Repository / EF Core 테스트: [csharp-repository-test](../../skills/csharp-repository-test/SKILL.md) (필요 시)
- 셰이더 / 머티리얼 / 애니메이션 / 아트 에셋: 별도 작업
- 사용법 문서: [feature-docs](../../skills/feature-docs/SKILL.md)

# <ScopeName> 리팩터링 계획

## 1. 대상 범위
- 대상 파일 / 폴더:
- 관련 Unity 객체:
- 실행 방식: skill 단독 / agent + skill

## 2. 현재 문제
- 중복:
- 책임 혼재:
- 성능 / GC:
- 생명주기:
- 기타:

## 3. 유지해야 할 동작
- public API:
- Inspector / serialized field:
- Prefab / Scene 참조:
- 이벤트 / 입력 / 출력:
- 저장 데이터 / ScriptableObject asset:

## 4. 명시한 가정과 확인 질문
- 가정:
- 확인 질문:

## 5. 변경 파일 / 제외 파일
| 구분 | 경로 | 이유 |
|------|------|------|
| 변경 | `Assets/...` | |
| 제외 | `Assets/...` | |

## 6. 최소 변경안
가장 적은 파일과 가장 작은 구조 변경으로 해결하는 안을 적는다.

1. ...
2. ...
3. ...

## 7. Unity 직렬화·Prefab·Scene 참조 리스크
- `[SerializeField]` / public field 이름 변경 여부:
- `[FormerlySerializedAs]` 필요 여부:
- `SerializeReference` / `UnityEvent` 영향:
- Prefab / Scene override 영향:
- ScriptableObject asset 영향:

## 8. 생명주기·성능·GC 리스크
- `Awake` / `OnEnable` / `Start` 순서:
- `Update` / `FixedUpdate` / `LateUpdate` 타이밍:
- 이벤트 구독·해제:
- coroutine / async / `Task` / `UniTask` 생명주기:
- 매 프레임 할당 / LINQ / boxing:
- `GetComponent` / `Find` / `Instantiate` / `Destroy`:

## 9. 검증 계획
- 컴파일:
- EditMode 테스트:
- PlayMode 테스트:
- Unity batchmode:
- 수동 확인:

## 10. 검증 결과
- 실행한 검증:
- 결과:
- 실행하지 못한 검증:
- 불가 사유:

## 11. 보류한 개선 사항
리팩터링 목표와 직접 연결되지 않아 이번 변경에서 제외한 항목.

- ...

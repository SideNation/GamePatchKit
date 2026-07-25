---
name: feature-docs
description: Writes or updates feature usage documentation under docs/<category>/<feature>.md whenever code adds or changes user-facing behavior. Triggers when the user adds a new feature, module, public API, scene/screen, or changes API signatures, flows, options, or error handling. Also use on requests like "사용법 문서 작성/갱신", "기능 문서", "feature doc", "how-to doc". Skips internal refactors and bug fixes that do not change behavior.
---

# Feature Usage Docs

코드 변경에 맞춰 `docs/` 아래 기능별 사용법 문서를 작성·갱신한다. "이 기능을 어떻게 호출하고, 어떤 결과·콜백·에러를 받는가"를 개발자가 한 번에 알 수 있도록 한다.

스킬 자산:
- 문서 스켈레톤: [template.md](template.md)
- 좋은 예시: [examples/login-flow.md](examples/login-flow.md), [examples/coupon-redeem.md](examples/coupon-redeem.md)

## When to invoke

| 변경 종류 | 문서 작업 |
|-----------|-----------|
| 새 기능/모듈/공개 API 추가 | 신규 문서 생성 |
| 공개 API 시그니처·동작 변경 | 해당 문서 갱신 |
| 에러 처리·플로우·옵션 추가 | 해당 섹션 갱신 |
| 새 화면/씬/엔트리 포인트 추가 | 신규 문서 생성 |
| 내부 리팩터(동작 동일) | 갱신 불필요 — 작성하지 않는다 |
| 버그 수정(동작 변경 없음) | 갱신 불필요 — 작성하지 않는다 |

동작 변화 여부가 모호하면 사용자에게 확인한다.

## Procedure

새 기능·변경을 받으면 순서대로 처리한다.

1. **변경 분류** — 위 표로 문서 작업 필요 여부를 판단. 불필요하면 그 사실만 보고하고 종료.
2. **기존 문서 탐색** — `docs/` 아래에서 같은 기능 문서가 있는지 확인. 있으면 갱신, 없으면 카테고리를 정해 신규 생성.
3. **카테고리 결정** — `backend`, `ui`, `gameplay`, `editor`, `prd` 등 **기존 폴더 우선 재사용**. 새 폴더는 명백히 맞는 게 없을 때만.
4. **파일 경로 확정** — `docs/<카테고리>/<기능명>.md`, 파일명은 `kebab-case` 영문. 파일당 1기능 원칙.
5. **문서 작성/갱신** — [template.md](template.md)를 기반으로, 필요한 섹션만 채운다. 동작이 바뀌지 않은 섹션은 건드리지 않는다.
6. **링크 검증** — 본문에 적은 `[경로](경로)` 링크가 실제 존재하는지 확인.
7. **보고** — 만든/갱신한 문서 경로와 핵심 변경 요약을 1~3줄로 보고.

작성 톤·구성이 헷갈리면 [examples/](examples/)의 예시를 모방한다.

## Writing principles

1. **개발자 관점의 사용법**을 적는다 — 호출 방법, 결과, 콜백, 에러. 내부 구현 설명은 적지 않는다.
2. **코드 자체로 알 수 있는 내용은 생략**한다. 파라미터 타입을 그대로 옮기는 표 같은 건 만들지 않는다. 비자명한 제약·전제·부작용 위주로 적는다.
3. **변경 이력/PR 번호/날짜는 본문에 적지 않는다** — 그건 git이 한다.
4. **한국어로 작성**한다. 코드·식별자·에러 코드(`statusCode`, `Pending` 등)는 영어 그대로.
5. **코드 예제는 실제 컴파일·실행되는 형태**로 작성한다. 의사코드는 `// pseudocode` 같은 표시로 명시.
6. **다른 파일/문서 참조는 마크다운 링크**로 — `[name](relative/path.cs)`. 경로는 문서 위치 기준 상대 경로.

## Anti-patterns

- 내부 리팩터에 대해 새 문서를 만든다.
- 한 기능을 여러 파일로 쪼갠다.
- "이 함수는 다음 단계로 동작합니다…" 같이 구현을 풀어 쓴다.
- 파라미터 타입만 나열한 표를 채운다.
- 변경 이력·작성자·날짜 메타를 본문에 박는다.
- 사용 예시 없이 산문만으로 사용법을 설명한다.

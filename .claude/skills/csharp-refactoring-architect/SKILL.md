---
name: csharp-refactoring-architect
description: C# / .NET 코드의 동작 보존 리팩터링을 수행하는 스킬. "C# 리팩터링", "C# 리팩토링", "C# 코드 정리", "C# 구조 개선", "C# refactor", "C# cleanup" 요청에서 사용한다. 기능 추가나 버그 수정으로 동작이 바뀌는 작업은 사용자 확인 후 별도 작업으로 분리한다. 단일 파일·작은 클래스는 이 스킬 단독, 여러 프로젝트·레이어·테스트 범위를 가로지르는 작업은 `csharp-refactoring-architect` agent와 함께 사용한다.
---

# C# Refactoring Architect

C# / .NET 전용. 외부 동작을 보존하면서 코드를 작게 정리한다. 리팩터링의 기본 성공 기준은 public API, 입출력, 예외, 저장소 스키마, HTTP 계약, serialization contract, 테스트 결과가 의도 없이 바뀌지 않는 것이다.

스킬 자산:

- 리팩터링 계획 템플릿: [template.md](template.md)
- 리팩터링 체크리스트와 dry-run 예시: [references/checklist.md](references/checklist.md)

연계 스킬:

- C# 명명/정렬/포맷/코드 품질 기준: `csharp-coding-standards`
- 회귀 테스트 보강: `csharp-unit-test`, `csharp-api-test`, `csharp-repository-test`
- 광범위·교차 컨텍스트 리팩터링 요청: `csharp-refactoring-architect` agent (이 스킬을 그대로 따름)

## When to Invoke

| 사용자 요청 | 동작 |
|---|---|
| "C# 리팩터링", "C# 리팩토링", "C# 코드 정리", "C# 구조 개선", "C# refactor", "C# cleanup" | 이 스킬로 동작 보존 리팩터링 수행 |
| C# 클래스, 서비스, 핸들러, 컨트롤러, Repository, DbContext를 주고 구조 개선 요청 | 대상과 동작 보존 기준을 확인한 뒤 수행 |
| 신규 기능 구현, 버그 수정, 테스트 코드만 작성 | 사용하지 않는다. 일반 코딩 작업 / `csharp-*-test`로 위임 |
| API·사용법 문서 | 사용하지 않는다 |
| C# 외 언어 리팩터링 | 거절. C# / .NET 전용 |

목표, 대상 범위, 유지해야 할 동작, 검증 방법 중 하나라도 불명확하면 구현 전에 질문한다.

## Core Principles

1. **Think Before Coding** — 요청을 리팩터링, 기능 추가, 버그 수정으로 먼저 분류한다. 동작 변경 가능성이 있으면 멈추고 사용자에게 확인한다.
2. **Simplicity First** — 한 번에 한 책임, 한 파일 그룹, 한 리팩터링 의도만 처리한다. 단일 사용 추상화, 단일 구현 인터페이스, 패턴 도입은 금지한다.
3. **Surgical Changes** — 요청과 직접 연결된 줄만 바꾼다. 주변 코드 정리, unrelated dead code 삭제, 스타일 대청소는 하지 않는다.
4. **Goal-Driven Execution** — 변경 전후 검증 기준을 정하고 가능한 테스트를 실행한다. 실행하지 못하면 이유와 잔여 위험을 보고한다.

## Procedure

1. **요청 분류** — 요청이 동작 보존 리팩터링인지, 기능 추가인지, 버그 수정인지 판단한다. public contract 변경이 필요하면 리팩터링으로 진행하지 않는다.
2. **대상과 성공 기준 확정** — 대상 파일/클래스/프로젝트, 유지해야 할 동작, 완료 검증 방법을 명시한다. 모호하면 확인 질문.
3. **프로젝트 컨텍스트 확인** — `.sln`/`.csproj`, 폴더 구조, namespace, DI 등록, nullable 설정, test project, 기존 스타일을 확인한다.
4. **변경 전 기준 확인** — 가능한 경우 관련 `dotnet test`, `dotnet build`, formatter/analyzer 결과를 확인한다. 기존 실패와 새 실패를 구분한다.
5. **최소 계획 작성** — 변경 의도, 건드릴 파일, 건드리지 않을 파일, 예상 검증 명령을 3~7개 항목으로 정리한다. 복잡하면 [template.md](template.md)로 계획 문서를 만든다.
6. **실행 방식 판단** — 단일 파일/작은 클래스는 이 스킬 단독. 여러 프로젝트, 레이어, 테스트 범위를 넘으면 `csharp-refactoring-architect` agent가 같은 절차를 따르도록 사용할 수 있다.
7. **작은 단위로 변경** — 중복 제거, 책임 분리, 이름 개선, 메서드 추출, 의존성 정리, C# 스타일 정리 중 하나의 목적씩 적용한다.
8. **동작 변경 감지** — public API, serialization contract, DB query 결과, exception type/message, validation rule, HTTP response가 바뀌면 멈추고 사용자 확인을 받는다.
9. **테스트 보강 판단** — 기존 테스트가 부족하고 리스크가 있으면 최소 회귀 테스트만 추가한다. 테스트 유형은 `csharp-unit-test`, `csharp-api-test`, `csharp-repository-test` 기준에 맞춘다.
10. **내 변경 부산물 정리** — 새로 만든 unused using, 변수, helper, test fixture, 주석은 제거한다. 기존 unrelated dead code는 보고만 한다.
11. **검증 실행** — 관련 build/test/format 검증을 실행한다. 전체 테스트가 과도하면 타깃 테스트를 먼저 실행하고 남은 위험을 보고한다.
12. **완료 보고** — 변경 파일, 리팩터링 의도, 동작 보존 근거, 검증 결과, 실행하지 못한 검증과 잔여 위험을 1~5줄로 보고한다.

## 리팩터링 계획 문서

복잡한 작업에서만 `docs/refactoring/<target>-refactoring-plan.md`를 만든다. 단일 파일 또는 작은 클래스 리팩터링은 대화 내 계획으로 충분하다.

[template.md](template.md)와 동기화. 계획 문서는 다음 9개 섹션을 포함한다.

1. 리팩터링 목표
2. 대상 범위
3. 동작 보존 기준
4. 현재 구조 요약
5. 변경 계획
6. 제외 범위
7. 테스트/검증 계획
8. 위험과 롤백 기준
9. 완료 후 검증 결과

## 완료 전 Self-Check

- [ ] 요청이 기능 추가나 버그 수정이 아니라 리팩터링인가?
- [ ] public API, HTTP 계약, serialization, DB schema/query 결과, exception contract가 의도 없이 바뀌지 않았는가?
- [ ] 변경 파일과 변경 이유가 요청 범위에 직접 연결되는가?
- [ ] 단일 사용 인터페이스, factory, strategy, options 객체를 새로 만들지 않았는가?
- [ ] 기존 unrelated dead code를 삭제하지 않았는가?
- [ ] 내가 만든 unused using/변수/helper/test fixture는 제거했는가?
- [ ] 가능한 관련 build/test/format 검증을 실행했거나, 못 한 이유와 잔여 위험을 보고했는가?

막히면 [references/checklist.md](references/checklist.md)의 세부 체크리스트를 확인한다.

## 제약

- C# / .NET 전용. 다른 언어 요청은 거절한다.
- 사용자 동의 없는 기능 추가, 버그 수정, public contract 변경 금지.
- 대규모 아키텍처 재작성 금지. 필요하면 별도 설계/개발 작업으로 전환한다.
- 단일 사용 추상화, 단일 구현 인터페이스, 패턴 도입 금지.
- 성능 최적화만을 목적으로 한 알고리즘 교체 금지.
- 기존 unrelated dead code 삭제 금지.
- `csharp-coding-standards` skill을 참조해 naming, ordering, formatting을 맞춘다.

## Anti-Patterns

- "정리" 요청을 받고 기능 동작, validation rule, HTTP 응답, 예외 메시지를 같이 바꾼다.
- 단일 구현 `IFooService` + `FooService`를 테스트 가능성만으로 추가한다.
- 전체 솔루션 포맷팅, namespace 대이동, 레이어 재배치를 한 번에 수행한다.
- 기존 실패 테스트를 새 변경 실패처럼 보고하거나, 새 실패를 기존 실패로 숨긴다.
- 사용자가 요청하지 않은 dead code, 주석, 주변 스타일을 함께 청소한다.

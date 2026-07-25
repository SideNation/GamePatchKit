# C# 리팩터링 체크리스트

> `csharp-refactoring-architect` 스킬의 세부 점검용 참조. 리팩터링 중 판단이 흔들릴 때만 읽는다.

## 리팩터링 분류

- 리팩터링: 외부 동작을 바꾸지 않고 내부 구조, 이름, 중복, 책임 배치를 개선한다.
- 기능 추가: 새 입력, 출력, 옵션, validation rule, 상태, endpoint, public API가 생긴다.
- 버그 수정: 기존 동작을 의도적으로 바꿔 올바른 동작으로 만든다.

기능 추가나 버그 수정이 섞이면 사용자에게 확인하고 별도 작업으로 전환한다.

## 동작 변경 감지 지점

- Public API: class/interface/record 이름, 접근 제한자, method signature, parameter default, return type.
- Serialization: JSON property 이름, nullability, enum string, DTO shape, `[JsonPropertyName]`.
- HTTP: route, method, status code, response body, error body, auth/permission behavior.
- Database: migration, table/column, query filter/order, include graph, transaction boundary.
- Exception: type, message, throw timing, swallowed exception, retry behavior.
- Async: `Task`/`ValueTask`, cancellation token propagation, fire-and-forget behavior.
- DI: service lifetime, registration key, implementation replacement.
- Tests: fixture setup, mock expectations, test naming convention.

이 중 하나라도 바뀌면 리팩터링 범위를 벗어났는지 확인한다.

## 허용되는 대표 리팩터링

- 중복 코드를 같은 파일 또는 가까운 책임 안의 private helper로 추출.
- 긴 메서드를 의미 있는 private 메서드로 분리.
- 역할이 섞인 클래스를 기존 구조 안에서 작은 클래스로 분리.
- 이름이 오해를 부르면 프로젝트 naming 관례에 맞게 개선.
- 불필요한 의존성을 제거하거나 생성자 주입 순서를 기존 관례에 맞춤.
- 내가 만든 unused using, 변수, helper, test fixture 제거.

## 금지 또는 사용자 확인 필요

- public contract 변경.
- 단일 구현 인터페이스, factory, strategy, options 객체 추가.
- 전체 솔루션 포맷팅.
- 레이어/폴더 대이동.
- 기존 unrelated dead code 삭제.
- 성능 최적화 목적으로 자료구조나 알고리즘 교체.
- 테스트 기대값 변경으로 리팩터링 실패를 숨김.

## 테스트 선택 기준

- Service/Handler/Validator 리팩터링: 기존 unit test 실행. 부족하면 `csharp-unit-test`.
- Controller/Endpoint 리팩터링: route/status/body 계약 확인. 부족하면 `csharp-api-test`.
- Repository/DbContext 리팩터링: query 결과, include, ordering, transaction 확인. 부족하면 `csharp-repository-test`.
- 공용 helper 리팩터링: 호출자 중 대표 경로 테스트와 build 실행.

전체 테스트가 과도하면 타깃 테스트를 먼저 실행하고, 전체 미실행 위험을 보고한다.

## Dry-Run 예시

요청: `OrderService.cs`가 길고 중복이 많으니 C# 리팩터링.

판단:

- 분류: 동작 보존 리팩터링.
- 대상: `OrderService.cs`, 관련 unit test.
- 제외: 새 할인 정책 추가, Repository query 변경, DTO 변경.
- 동작 보존 기준: public method signature, exception type/message, order status transition, repository 호출 순서 유지.

최소 계획:

1. 중복된 status guard를 private method로 추출 → verify: 기존 unit test.
2. 결제 가능 여부 계산을 private method로 분리 → verify: public method output 변경 없음.
3. 내가 만든 unused using/helper 정리 → verify: `dotnet build`.

검증:

- 변경 전 `dotnet test <test-project> --filter OrderService` 실행.
- 변경 후 같은 테스트와 관련 프로젝트 `dotnet build` 실행.
- 실패가 기존 상태면 기록하고, 새 실패면 해당 단계 재검토.

보고:

- 변경 파일, 리팩터링 의도, 동작 보존 근거, 검증 결과, 미실행 검증과 잔여 위험을 짧게 남긴다.

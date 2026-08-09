# <FeatureName> 설계

> `csharp-feature-architect` 스킬이 생성하는 합의용 설계 문서. 구현 전 합의용이며 메서드 본문은 적지 않는다.

## 1. 기능 요약

<무엇을 만들 것인지 1~3문장. "왜"는 가정/요구사항 섹션에서 다룬다.>

## 2. 가정과 확인 필요 사항

- 가정:
  - <예: ASP.NET Core 8 Web API 프로젝트라고 가정>
  - <예: DI는 `Microsoft.Extensions.DependencyInjection` 사용>
- 확인 필요:
  - <예: 인증 토큰 만료 정책 — 24h vs 7d 중 어느 쪽?>
  - <예: 멱등성 보장 필요 여부>

## 3. 요구사항 정리

- 입력:
  - <필드명: 타입 — 설명>
- 출력:
  - <필드명: 타입 — 설명>
- 제약:
  - <예: 동시 요청 100 QPS까지 처리>
  - <예: 영속 저장소는 PostgreSQL>

## 4. 실행 방식

- [ ] skill 단독 (`csharp-feature-architect` skill만 사용)
- [ ] agent + skill (`csharp-feature-architect` agent가 같은 skill 절차를 따라 진행)

선택 사유: <예: 단일 bounded context, 단일 프로젝트 내 변경이므로 skill 단독으로 충분>

## 5. 가장 단순한 구조 후보

<단일 클래스/단일 서비스/기존 폴더 배치로 해결 가능한 1차 안. 이 안을 우선 검토한다.>

예시:

- `OrderConfirmationService` 한 클래스 추가, `Application/Services/` 기존 폴더에 배치.
- 컨트롤러 메서드 1개 추가 (`POST /orders/{id}/confirm`).
- 기존 `IOrderRepository` 그대로 사용.

## 6. 단순 구조 실패 조건

<단순 구조가 요구사항을 만족하지 못하는 구체적 조건. 없으면 "없음"이라고 명시한다.>

- 예: 결제·재고 차감·알림 발송이 트랜잭션 경계를 가로질러 보상 트랜잭션이 필요 → Saga 패턴 도입.
- 예: 입력 변환 규칙이 5종 이상으로 분기 → Strategy 도입 검토.

없으면: `없음 — 단순 구조로 확정`

## 7. 책임/도메인 분해

> 단순 구조로 확정된 경우 이 섹션을 "단순 구조로 확정, 분해 없음"으로 둔다.

| 책임 단위 | 종류 | 설명 |
|---|---|---|
| `<Name>` | <Service/Validator/Handler/DTO/Entity/VO/...> | <한 줄> |

## 8. 적용 패턴과 정당화

> 기본은 패턴 0개. 도입했다면 항목별로 단순 구조 실패 조건과 연결한다.

| 패턴 | 적용 위치 | 정당화 (요구사항 근거) |
|---|---|---|
| <Strategy/Factory/Mediator/...> | <클래스/경계> | <한 줄> |

없으면: `없음`

## 9. 인터페이스 생성 근거

> 단일 구현·단일 호출자만을 위한 인터페이스는 금지. 외부 의존성/DI 경계/테스트 대역/관례화된 경계가 있을 때만 작성.

| 인터페이스 | 근거 |
|---|---|
| `I<Name>` | <외부 SDK 추상화 / 테스트 대역 / 기존 관례 ...> |

없으면: `없음`

## 10. 인터페이스·클래스 시그니처

> 메서드 본문 금지. 이름·파라미터·반환 타입·예외만 작성. `csharp-coding-standards` 규칙(명명·정렬)을 따른다.

```csharp
namespace MyApp.Application.Orders;

public interface IOrderConfirmationService
{
    Task<OrderConfirmationResult> ConfirmAsync(long orderId, CancellationToken cancellationToken);
}

public sealed class OrderConfirmationService : IOrderConfirmationService
{
    public OrderConfirmationService(IOrderRepository orderRepository, ILogger<OrderConfirmationService> logger);

    public Task<OrderConfirmationResult> ConfirmAsync(long orderId, CancellationToken cancellationToken);
}

public sealed record OrderConfirmationResult(long OrderId, OrderStatus Status, DateTimeOffset ConfirmedAt);
```

## 11. 파일·폴더 배치 제안

- 기존 구조 재사용 우선.
- 새 구조가 필요하면 Feature 단위(`Features/<FeatureName>/`) 또는 레이어 단위(`Domain/` / `Application/` / `Infrastructure/`) 중 프로젝트에 맞는 한 가지만 선택.

```
src/MyApp/
├── Application/
│   └── Orders/
│       ├── IOrderConfirmationService.cs
│       ├── OrderConfirmationService.cs
│       └── OrderConfirmationResult.cs
└── Api/
    └── Controllers/
        └── OrdersController.cs   (Confirm 액션 추가)
```

DI 등록 위치: `Program.cs` (`builder.Services.AddScoped<IOrderConfirmationService, OrderConfirmationService>();`)

## 12. 도입하지 않은 구조

> 검토했지만 도입하지 않은 패턴/인터페이스/레이어와 제외 이유.

- `IOrderConfirmationPolicy` 인터페이스: 정책 분기가 1종 → 단일 구현·단일 호출자가 되므로 제외.
- Mediator 패턴(MediatR): 호출 경로가 컨트롤러 → 서비스 단일 단계 → 도입 이득 없음.
- 별도 `Confirmation/` Feature 폴더: 기존 `Orders` 폴더로 충분.

## 13. 단순화 자가 검토 결과

- 새 인터페이스 수: <N> (예산 0~2)
- 새 폴더 계층 증가: <N> (예산 0~1)
- 적용 패턴 수: <N> (예산 0)
- 단일 구현 인터페이스: 없음 / <목록>
- 요구사항에서 직접 도출되지 않은 도메인 객체: 없음 / <목록>
- 종합 판정: 단순 구조 유지 / 정당화된 추가만 도입 / 재검토 필요

## 14. 위임 다음 단계

- 구현: 일반 코딩 작업으로 진행 (`csharp-coding-standards` 적용)
- 단위 테스트(Service/Handler/Validator): `csharp-unit-test` 스킬
- API/Controller 테스트: `csharp-api-test` 스킬
- Repository/DbContext 테스트: `csharp-repository-test` 스킬

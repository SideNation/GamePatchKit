# Coupon Redeem

## 개요
사용자가 입력한 쿠폰 코드를 검증하고 인벤토리에 보상을 지급한다. 상점 화면의 "쿠폰 등록" 버튼과 푸시 알림 딥링크에서 호출된다.

## 사용법

진입점은 `CouponService.RedeemAsync`.

```csharp
var result = await _couponService.RedeemAsync(
    userId: currentUser.Id,
    code: "SUMMER2026",
    cancellationToken);

switch (result.Status)
{
    case RedeemStatus.Granted:
        _inventoryUi.Refresh(result.GrantedItems);
        break;
    case RedeemStatus.AlreadyUsed:
        _toast.Show("이미 사용한 쿠폰입니다.");
        break;
    // ...
}
```

`OnCouponRedeemed` 이벤트는 성공 시에만 발생한다.

```csharp
public event Action<RedeemResult> OnCouponRedeemed;
// RedeemResult { string Code, IReadOnlyList<ItemGrant> GrantedItems, DateTime RedeemedAt }
```

- `code`는 대소문자를 구분하지 않으며, 앞뒤 공백은 자동 제거된다.
- 보상 지급은 단일 트랜잭션이다. 인벤토리 슬롯 부족이면 **아무것도 지급되지 않는다**.

## 흐름

1. 클라이언트가 `RedeemAsync` 호출.
2. 서버가 코드 유효성·만료·사용 한도 검사.
3. 통과 시 `inventory_grants`에 일괄 삽입 + `coupon_redemptions`에 사용 기록.
4. 둘 중 하나라도 실패하면 트랜잭션 롤백 후 `RedeemStatus.Failed`.

## 에러 처리

| `RedeemStatus` | 의미 | 권장 처리 |
|----------------|------|-----------|
| `Granted` | 정상 지급 | 인벤토리 갱신, 성공 토스트 |
| `NotFound` | 존재하지 않는 코드 | "유효하지 않은 코드입니다" |
| `Expired` | 만료됨 | 만료 시각 안내 |
| `AlreadyUsed` | 동일 사용자가 이미 사용 | 이력 화면으로 이동 유도 |
| `LimitExceeded` | 쿠폰 전체 사용 한도 초과 | "조기 마감되었습니다" |
| `InventoryFull` | 인벤토리 슬롯 부족 | 정리 안내 후 재시도 |
| `Failed` | 그 외 서버 오류 | 일반 재시도 흐름 |

## 제약/주의사항

- 동일 사용자가 같은 코드를 동시에 여러 번 호출해도 1회만 지급된다 — `(user_id, code)` UNIQUE 제약으로 보장.
- 운영툴에서 쿠폰을 비활성화하면 진행 중인 호출은 즉시 `Expired`로 응답한다.

## 관련 파일
- [src/Coupons/CouponService.cs](../../src/Coupons/CouponService.cs)
- [src/Coupons/RedeemResult.cs](../../src/Coupons/RedeemResult.cs)
- [db/migrations/2026_03_01_coupon_redemptions.sql](../../db/migrations/2026_03_01_coupon_redemptions.sql)

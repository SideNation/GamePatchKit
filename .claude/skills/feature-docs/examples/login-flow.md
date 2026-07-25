# Login Flow

## 개요
이메일·비밀번호로 로그인해 access/refresh 토큰 쌍을 발급받는다. 모바일 클라이언트가 앱 시작 시 호출하며, 결과 토큰은 `SecureStorage`에 보관해 이후 API 호출 시 `Authorization` 헤더에 사용한다.

## 사용법

진입점은 `AuthService.LoginAsync`.

```csharp
var service = serviceProvider.GetRequiredService<IAuthService>();

var result = await service.LoginAsync(
    new LoginRequest("alice@example.com", "p@ssw0rd"),
    cancellationToken);

if (result.IsSuccess)
{
    await SecureStorage.SaveAsync("access_token", result.AccessToken);
    await SecureStorage.SaveAsync("refresh_token", result.RefreshToken);
}
```

- `email`은 RFC 5322 기본 형식만 통과시키며, 대소문자는 서버에서 lower-case로 정규화된다.
- `password`는 클라이언트에서 길이 검증을 하지 않는다 — 서버가 단일 진실원이다.
- `LoginAsync`는 5초 타임아웃이 기본값이고, `CancellationToken`으로만 중단 가능하다.

## 흐름

1. 클라이언트가 `LoginAsync` 호출.
2. 서버가 `users` 테이블에서 정규화된 이메일로 사용자 조회.
3. `Argon2id` 해시 비교 후 access(15m) / refresh(14d) 토큰 발급.
4. 같은 디바이스의 기존 refresh 토큰은 자동 회수된다.

## 에러 처리

| `errorCode` | 의미 | 권장 처리 |
|-------------|------|-----------|
| `INVALID_CREDENTIALS` | 이메일/비밀번호 불일치 | 일반화된 메시지 표시("이메일 또는 비밀번호가 올바르지 않습니다") |
| `ACCOUNT_LOCKED` | 5회 연속 실패로 잠김 | 10분 후 재시도 안내 + 비밀번호 재설정 링크 노출 |
| `EMAIL_NOT_VERIFIED` | 이메일 미인증 | 인증 메일 재전송 화면으로 이동 |
| `RATE_LIMITED` | IP 단위 분당 10회 초과 | `Retry-After` 헤더 값만큼 대기 후 재시도 |

`IsSuccess == false`이면 `result.ErrorCode`로 분기하고, 본문 메시지를 그대로 사용자에게 노출하지 않는다.

## 제약/주의사항

- access 토큰은 갱신할 수 없다. 만료 시 refresh 토큰으로 재발급받거나 다시 로그인한다.
- refresh 토큰은 1회용이다. 사용 즉시 새 refresh 토큰으로 교체된다 — 동시 요청은 한쪽이 무효화될 수 있다.

## 관련 파일
- [src/Auth/AuthService.cs](../../src/Auth/AuthService.cs)
- [src/Auth/LoginRequest.cs](../../src/Auth/LoginRequest.cs)
- [src/Auth/Tokens/RefreshTokenRotator.cs](../../src/Auth/Tokens/RefreshTokenRotator.cs)

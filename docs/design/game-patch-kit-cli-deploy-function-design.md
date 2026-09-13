---
_meta:
  meta:
    version: 0.1.0
    updated: 2026-09-13
    changelog:
      - 함수 오류의 상태와 코드만 기록하는 안전한 로그 추가
      - 호출자 API key 대신 함수에 기본 제공되는 프로젝트 publishable key를 사용
      - 함수 조회를 고정 버전 SupabaseClient로 단순화하고 오류 계약을 축소
      - 프로젝트 ID와 Access Token으로 패치 버전 조회 함수를 배포하는 CLI 설계 초안 작성
---

# GamePatchKit CLI `deploy-function` 개발 계획

> 상태: 사용자 확정 요구사항을 반영한 구현 전 설계. 함수·CLI 구현과 실제 배포는 아직 수행하지 않았다.
>
> 관련 문서: [sync](../cli/sync.md), [upload](../cli/upload.md)

## 1. 기능 요약

`gpk deploy-function --project-id <project-ref> --access-token <access-token>` 명령으로 지정한 Supabase 프로젝트에 `get-patch-version` Edge Function을 생성하거나 갱신한다. 배포된 함수는 자신이 속한 프로젝트의 `public.gamepatch_pointer`에서 현재 배포된 패치 버전을 읽는다.

여러 프로젝트에 같은 CLI를 사용하고 인자만 바꿔 배포한다. 함수 소스에 특정 프로젝트 ID나 인증 정보를 넣지 않는다.

## 2. 가정과 확인 필요 사항

### 사용자가 확정한 사항

- 배포 대상은 인자로 받은 Supabase 프로젝트 ID다. 여기서 ID는 URL의 `<project-ref>.supabase.co`에 쓰이는 project ref를 뜻한다.
- 배포 인증은 `secret key`나 `publishable key` 대신 Supabase Access Token을 사용한다.
- 별도 배포 스크립트 대신 기존 CLI에 `gpk deploy-function` 명령을 추가한다.
- 배포된 함수의 데이터 원본은 대상 프로젝트의 기존 `gamepatch_pointer` 테이블이다.

### 이 문서의 최소 범위 기본안

- Supabase 호스팅 프로젝트를 대상으로 하며 하나의 실행은 하나의 프로젝트에 배포한다.
- 최초 설치 범위는 함수 생성이다. 테이블·버킷·패치 데이터는 기존 게시 환경에 준비되어 있어야 한다. DB 초기화와 마이그레이션 자동 실행은 이 명령에 포함하지 않는다.
- 테이블 생성이 필요한 프로젝트는 기존 [sync 사전 조건](../cli/sync.md#사전-조건)의 계약을 프로젝트의 버전 관리되는 마이그레이션으로 적용한다. 콘솔에서만 수동 생성하는 절차를 새로 도입하지 않는다.
- 기존 함수가 같은 이름이면 이 명령이 관리하는 대상으로 보고 갱신한다. 함수 이름은 고정하며 임의 함수 배포 명령으로 확장하지 않는다.
- 함수 호출은 호스팅 환경에 기본 제공되는 프로젝트 publishable key를 사용하는 공개 패치 정보 조회를 기준으로 한다. 비공개 프로젝트별 사용자 권한 모델은 가정하지 않는다.
- 기존 `gpk sync`는 현재 PostgREST 직접 조회 계약을 유지한다. 새 함수로의 전환은 이 기능의 필수 변경이 아니다.

위 기본안은 추가 인터뷰 없이 문서를 완성하기 위한 설계 선택이다. 사용자가 직접 확정한 네 가지 요구와 구분한다.

### 구현 시 기술 검증 사항

- Management API의 multipart 파일명·metadata·entrypoint 조합을 실제 배포로 검증한다.
- 대상 Edge Runtime에서 기본 제공 환경 변수와 publishable key를 사용한 SupabaseClient 조회가 동작하는지 확인한다.
- 아래의 문자열 캐스팅 조회가 대상 PostgREST 버전에서 `bigint` 정밀도를 보존하는지 확인한다.

## 3. 요구사항 정리

### 배포 명령 입력·출력

| 항목 | 계약 |
| --- | --- |
| 명령 | `gpk deploy-function` |
| `--project-id` | 필수 문자열. 프로젝트 이름이나 URL이 아닌 project ref |
| `--access-token` | 필수 문자열. 해당 프로젝트의 함수 배포 권한을 가진 Access Token |
| 성공 출력 | 대상 project ref, 함수 이름, 배포 결과의 함수 버전, 호출 URL |
| 종료 코드 | 배포 성공은 `0`, 인자·인증·원격 배포 실패는 `1` |

인자는 누락·빈 값·중복·알 수 없는 옵션을 거부한다. 기존 build/upload/sync용 옵션의 허용 범위를 바꾸지 않는다. 토큰 원문이나 전체 명령행은 출력·파일 저장·함수 소스 삽입 대상이 아니다. 오류 출력에도 토큰이 섞이지 않도록 인자 값과 원격 응답 원문을 그대로 출력하지 않는다.

Access Token은 Management API 요청의 `Authorization: Bearer` 헤더에만 사용한다. 프로젝트 API 키는 배포 인증을 대체하지 않는다. Supabase가 제공하는 배포 API와 토큰 인증을 이용한다. [Management API 인증](https://supabase.com/docs/reference/api/introduction), [함수 배포 API](https://supabase.com/docs/reference/api/v1-deploy-a-function)

### 배포 동작

1. 인자를 해석하고 CLI에 포함된 함수 소스를 읽는다.
2. `https://api.supabase.com/v1/projects/{ref}/functions/deploy`에 고정 함수 slug `get-patch-version`과 소스·배포 metadata를 전송한다. 이 API는 함수가 없으면 생성한다.
3. 정상 응답의 slug·상태·버전을 확인하고 지정 프로젝트의 호출 URL을 출력한다. 정상 배포로 확인되지 않은 상태를 성공으로 출력하지 않는다.

배포 API는 `edge_functions:write` OAuth scope 또는 fine-grained token의 `edge_functions_write` 권한을 명시한다. 실제 토큰은 대상 프로젝트에 접근할 수 있어야 한다. [배포 API 계약](https://supabase.com/docs/reference/api/v1-deploy-a-function)

명령의 성공은 함수 배포 성공을 뜻한다. 조회할 bucket을 입력받지 않으므로 실제 패치 조회 성공까지 자동으로 보증하지 않는다. 런타임 호출은 구현 완료 검증에서 별도로 확인한다.

네트워크 오류로 응답을 받지 못한 경우 배포 적용 여부가 불명확할 수 있다. 성공으로 단정하지 않고 재실행할 수 있도록 안내한다. 재실행은 같은 함수 이름을 갱신하며 포인터나 패치 파일에 영향을 주지 않는다. 함수 배포 버전은 재실행할 때 증가할 수 있다.

### 배포된 함수의 HTTP 계약

| 항목 | 계약 |
| --- | --- |
| 메서드·경로 | `GET /functions/v1/get-patch-version?bucket=<bucket>` |
| 인증 헤더 | 없음. `verify_jwt=false`인 공개 GET |
| 조회 대상 | 함수 실행 환경의 `SUPABASE_URL`에 속한 `public.gamepatch_pointer` |
| `bucket` | 필수. 기존 CLI와 같은 영숫자·`.`·`_`·`-` 범위. 정확히 해당 행만 조회 |
| 성공 응답 | `bucket: string`, `releaseVersion: string`, `manifestPath: string` |
| 응답 예 | bucket=`game-a`, releaseVersion=`42`, manifestPath=`manifests/42.json` |

배포 인자의 project ref는 함수 설치 위치를 결정한다. 조회 요청의 bucket은 설치된 프로젝트 안에서 패치 데이터를 선택한다. 함수 호출자가 다른 프로젝트 URL이나 테이블 이름을 지정할 수 없다.

`releaseVersion`은 부호 없는 10진수 표기의 문자열이며 값의 범위는 기존 계약과 같은 `0`부터 `long.MaxValue`까지다. 매니페스트 파일 자체의 정수 필드나 기존 sync 응답 파싱 계약은 변경하지 않는다.

함수는 PostgREST의 `select=bucket,release_version::text`로 조회하여 JSON 파싱 전에 DB에서 문자열로 변환한다. JavaScript `Number`로 변환한 뒤 문자열로 되돌리는 방식은 쓰지 않는다. `manifestPath`는 이 문자열에서 계산하며 별도로 저장하지 않는다. [PostgREST 컬럼 캐스팅](https://docs.postgrest.org/en/stable/references/api/tables_views.html#casting-columns)

### 호출 인증·DB 권한

- 공개 호출을 위해 함수 배포 metadata에 `verify_jwt=false`를 명시한다. [Edge Function 인증 헤더](https://supabase.com/docs/guides/functions/auth-headers)
- 함수는 호스팅 환경에 기본 제공되는 `SUPABASE_PUBLISHABLE_KEYS.default`를 고정된 대상 프로젝트의 PostgREST에 전달한다. 별도 배포 인자나 secret 등록은 추가하지 않는다. [Edge Function 환경 변수](https://supabase.com/docs/guides/functions/secrets)
- 호출자의 `apikey`와 `Authorization` 헤더는 전달하지 않고 기본 publishable key에 해당하는 `anon` 읽기 권한으로만 조회한다. Edge Function에서도 service role로 권한을 높이지 않는다.
- 기존 `anon` SELECT GRANT와 읽기 RLS 정책을 사용한다. 기존 공개 패치 정보 접근 모델을 유지하며 publishable key를 게임별 비밀 접근권으로 간주하지 않는다.
- Access Token은 배포 시에만 사용하고 함수 환경 변수에 등록하지 않는다. 별도의 DB 비밀번호나 프로젝트 secret key를 입력받지 않는다.

### HTTP 실패 계약

오류 본문은 `error.code`와 `error.message`를 가지며 내부 응답이나 인증 정보를 노출하지 않는다.
모든 오류 응답은 `function`, HTTP `status`, `error.code`만 포함한 JSON 한 줄을 `console.error`로 기록한다. API key, Access Token, 요청 헤더와 상류 응답 본문은 로그에 넣지 않는다.

| 상태 | 의미 |
| --- | --- |
| `400` | bucket 누락·빈 값·중복·잘못된 형식 |
| `401` | 함수에 기본 제공된 publishable key가 원격 API에서 거부됨 |
| `403` | 인증된 조회 요청이 원격 API에서 권한 부족으로 거부됨 |
| `404` | 조회 성공 후 해당 bucket의 포인터 행이 없음 |
| `405` | GET 이외의 지원하지 않는 메서드 |
| `500` | 테이블 누락·조회 실패·기본 환경 변수 누락 또는 형식 오류 |

테이블이 없는 상태와 정상 조회 결과가 빈 상태를 구별한다. 첫 게시 전에는 `0`을 만들어 반환하지 않는다. 실제 첫 게시 버전 `0`은 정상 값이다.

### 배포 버전 의미와 기존 흐름

- 현재 포인터 값을 그대로 반환한다. 저장소에 있는 가장 큰 버전을 검색하지 않는다.
- 클라이언트는 현재 버전과 다르면 반영한다. `42 → 41` 롤백도 같은 계약이다.
- 기존 `산출물·매니페스트 업로드 → 상태 Git push → 포인터 갱신` 순서를 유지한다.
- 함수 배포 버전과 패치 `releaseVersion`은 서로 다른 값이다. 함수 재배포로 패치 버전을 증가시키지 않는다.

## 4. 실행 방식

`csharp-feature-architect` 스킬 단독으로 설계한다. 기존 C# CLI의 명령 하나와 그 명령에 동봉할 TypeScript 함수의 배포·HTTP 계약이 범위다. 별도 에이전트, C# 프로젝트, DI 컨테이너는 필요하지 않다. TypeScript 구현 코드는 이 문서에 작성하지 않는다.

## 5. 가장 단순한 구조 후보

기존 `GamePatchKit.Cli` namespace에 `DeployFunctionCommand` 클래스 하나를 추가한다. 인자 파싱은 기존 `ArgumentsParser`, 명령 분기와 출력은 기존 `Program`에 연결한다.

C#은 기존 런타임의 `HttpClient`와 multipart 전송, 기존 Newtonsoft.Json으로 Management API를 호출한다. Supabase CLI·Docker·Deno가 사용자 머신에 설치되어 있어야 하는 구조를 만들지 않는다.

함수 소스와 `deno.json`·`deno.lock`은 CLI assembly의 embedded resource로 포함한다. NuGet tool과 self-contained 배포물 모두 같은 파일을 포함하고 현재 작업 디렉터리나 저장소 checkout에 의존하지 않는다. 함수는 고정 버전 SupabaseClient로 포인터를 조회한다.

## 6. 단순 구조 실패 조건

없음 — 현재 요구는 단일 Management API 배포와 단일 테이블 읽기로 충족하므로 단순 구조로 확정한다. multipart 업로드의 실제 동작은 구현 초기에 검증하되, 검증 전부터 별도 배포 서비스나 프레임워크를 추가하지 않는다.

## 7. 책임/도메인 분해

단순 구조로 확정하며 별도 도메인 분해는 없다.

| 단위 | 책임 |
| --- | --- |
| 기존 `ArgumentsParser` | 배포 인자 파싱·검증 |
| `DeployFunctionCommand` | 동봉 소스 읽기, 지정 프로젝트로 배포, 결과 확인 |
| 기존 `Program` | 명령 연결, 성공 출력, 기존 실패 종료 처리 |
| `get-patch-version.ts` | 호출 검증, 자신의 프로젝트에서 포인터 조회, 응답 구성 |

## 8. 적용 패턴과 정당화

없음. 기존 명령 클래스 호출 방식으로 충분하다.

## 9. 인터페이스 생성 근거

없음. HTTP 검증에는 .NET의 기존 `HttpMessageHandler` 경계를 사용하며 배포용 인터페이스를 새로 만들지 않는다.

## 10. 인터페이스·클래스 시그니처

아래는 구현 본문이 없는 C# 계약 초안이다. 이름·접근 범위는 기존 CLI 관례를 따른다.

| 선언 위치 | 시그니처 |
| --- | --- |
| `CommandArguments.cs` | `internal sealed record DeployFunctionArguments(string ProjectId, string AccessToken)` |
| 기존 `ArgumentsParser` | `public static DeployFunctionArguments ParseDeployFunction(string[] arguments)` |
| 신규 클래스 | `internal sealed class DeployFunctionCommand` |
| 생성자 | `public DeployFunctionCommand(HttpClient httpClient)` |
| 실행 | `public Task<string> ExecuteAsync(DeployFunctionArguments arguments)` |

실행 메서드는 검증된 배포 결과의 출력용 요약 문자열을 반환한다. 실패는 기존 `BuildException`으로 전달한다. 새 결과 DTO는 만들지 않는다. `HttpClient`의 수명은 호출하는 `Program`이 소유한다.

## 11. 파일·폴더 배치 제안

아래는 향후 구현 시 변경 범위이며 이번 문서 작성으로 생성한 구현 파일 목록이 아니다.

| 경로 | 변경 목적 |
| --- | --- |
| `src/GamePatchKit.Cli/DeployFunctionCommand.cs` | 배포 명령 구현 |
| `src/GamePatchKit.Cli/Functions/get-patch-version.ts` | 동봉할 Edge Function의 유일한 원본 |
| `src/GamePatchKit.Cli/Functions/deno.json` | SupabaseClient 버전 고정 |
| `src/GamePatchKit.Cli/Functions/deno.lock` | 함수 의존성 무결성 고정 |
| `src/GamePatchKit.Cli/CommandArguments.cs` | 배포 인자 타입·파서 추가 |
| `src/GamePatchKit.Cli/Program.cs` | 명령 분기·출력 연결 |
| `src/GamePatchKit.Cli/GamePatchKit.Cli.csproj` | 함수 소스를 embedded resource로 포함 |
| `docs/cli/deploy-function.md` | 사용법·사전 조건·실패 및 재배포 설명 |
| `README.md` | 새 명령의 짧은 안내와 사용법 링크 |
| 기존 테스트·패키징 검증 범위 | 배포 인자·HTTP 계약·리소스 포함 검증 |

새 구현 파일이 기본 예산 1개를 넘는 이유는 로컬 C# 배포 명령과 원격 TypeScript 함수가 서로 다른 실행 환경의 필수 산출물이기 때문이다. 소스를 C# 문자열과 별도 `.ts` 파일에 중복 보관하지 않는다.

## 12. 도입하지 않은 구조

- 별도 설치 프로그램·배포 스크립트·외부 CLI 실행: 기존 gpk 하나로 사용하려는 요구와 맞지 않음.
- 범용 Management API SDK·배포 인터페이스·별도 레이어: 고정 함수 하나를 배포하는 현재 요구에 불필요함.
- 새 버전 테이블·RPC·프로젝트 ID 매핑 테이블: 기존 포인터와 배포 대상 project ref로 충분함.
- DB·버킷 자동 생성 및 임의 마이그레이션 실행: 함수 배포와 독립된 변경이며 이번 최소 범위에는 포함하지 않음.
- 자동 재시도·캐시·배치 프로젝트 배포·플랫폼 및 채널 정책: 요청에서 요구하지 않음.
- 함수명·테이블명·원격 소스 경로 설정: 고정된 패치 버전 함수를 배포하는 목적에 불필요함.
- 자동 공개 API 키 조회·배포 명령의 실데이터 호출 검사: 두 배포 인자만 받는 계약에 추가 관리 권한과 동작을 요구함.
- custom secret 등록과 publishable key 배포 인자: 호스팅 환경의 기본 publishable key로 충분하며 추가 Management API 권한과 설정이 불필요함.

## 13. 단순화 자가 검토 결과

- 새 C# 타입 2개, 새 인터페이스 0개, 패턴 0개, 새 구현 폴더 계층 1개다.
- 런타임 의존성은 `@supabase/supabase-js` 하나이며 정확한 버전과 lockfile을 함께 관리한다.
- 단일 구현 인터페이스, 새 도메인 객체, 기존 CLI 파서의 광범위한 리팩터링은 없다.
- 사용자 확정 요구와 문서 작성 시의 기본안을 구분했다.
- 배포 성공과 패치 조회 성공, 함수 버전과 패치 버전, 배포 인증과 조회 인증을 구분했다.
- 호출자에게 프로젝트 publishable key를 요구하지 않고 함수의 기본 환경 변수를 사용한다.
- 종합 판정: 단순 구조 유지. 실제 패키징과 원격 배포 검증은 구현 단계에 남아 있다.

## 14. 위임 다음 단계

### 구현 순서와 완료 조건

| 단계 | 작업 | 검증 기준 |
| --- | --- | --- |
| 1 | Management API 배포 형식과 단일 함수 소스 준비 | 대상 테스트 프로젝트에 함수가 생성되고 같은 이름으로 갱신됨 |
| 2 | C# 명령·인자 파서 연결 | project ref가 요청 경로에, 토큰이 인증 헤더에 전달됨. 잘못된 인자는 원격 호출 전에 실패 |
| 3 | 조회 HTTP 계약 구현 | 두 bucket의 각 버전, 미게시 404, 실제 버전 0, 롤백을 정확하게 반환 |
| 4 | 인증·오류·숫자 계약 확인 | 기본 publishable key를 사용하고 호출자 인증 헤더를 전달하지 않음. 인증·권한 오류는 401·403, 나머지 조회 실패는 500으로 반환하고 상태·코드만 로그에 기록. `9007199254740993` 및 `9223372036854775807`이 문자열로 보존됨 |
| 5 | 기존 배포물에 함수 리소스 동봉 | 저장소 밖에서 실행한 NuGet tool 및 standalone 산출물에 동일 소스·의존성 설정·lockfile이 포함되고 읽힘 |
| 6 | 프로젝트 간 재사용 확인 | 동일 CLI로 A와 B에 배포하고 각 함수가 자신의 프로젝트 포인터만 반환함 |
| 7 | 사용법 문서와 자체 검토 | 실제 명령·제약·검증 결과가 문서와 일치하며 기존 CLI 검사 통과 |

배포 실패는 401·403·429·서버 오류·네트워크 실패를 구분해 종료 코드 1로 보고하고, 어느 경로에서도 토큰이 출력되지 않는지 확인한다. 함수 재배포 전후 포인터 값과 Storage 산출물이 바뀌지 않는지도 확인한다.

검증은 기존 .NET 테스트 환경과 HTTP handler 대역을 우선 활용한다. 새 테스트 프레임워크를 도입하지 않는다. 원격 호출 검증의 포인터 fixture도 재현 가능한 코드로 준비하며 운영 데이터를 사용하지 않는다. 실제 배포 API를 호출하지 않은 로컬 검사만으로 원격 배포 성공을 보고하지 않는다.

구현은 일반 코딩 작업으로 진행하며 `csharp-coding-standards`를 적용한다. C# 단위 테스트 작성 시 `csharp-unit-test`를 사용하고, 인증·공개 HTTP 계약·다중 파일 변경의 자체 검토에는 `codex-solo-workflow`의 adversarial review를 적용한다. 현재 요청의 산출물은 이 계획 문서이며 구현·테스트 코드 작성·실제 배포는 다음 작업이다.

# `gpk deploy-function`

## 개요

지정한 Supabase 호스팅 프로젝트에 `get-patch-version` Edge Function을 생성하거나 갱신한다. 함수는 자신이 설치된 프로젝트의 `public.gamepatch_pointer`에서 bucket의 현재 패치 버전을 읽는다.

## 사전 조건

- 대상 프로젝트의 project ref와 함수 배포 권한이 있는 Supabase Access Token이 필요하다. project ref는 프로젝트 이름이나 URL이 아닌 **소문자 영문 20자**다.
- Access Token에는 프로젝트 접근 권한과 `edge_functions_write` 권한(OAuth는 `edge_functions:write`)이 필요하다. publishable key나 secret key는 배포 인증을 대체하지 않는다.
- 조회를 위해 [sync 사전 조건](sync.md#사전-조건)의 테이블·SELECT GRANT·읽기 RLS 계약을 프로젝트의 **버전 관리되는 마이그레이션**으로 먼저 적용한다. 이 명령은 DB·버킷·포인터 데이터를 생성하거나 수정하지 않는다.
- 함수는 호스팅 환경에 기본 제공되는 `SUPABASE_PUBLISHABLE_KEYS.default`를 사용한다. 호출자는 API key를 전달하지 않는다. 공개 패치 정보 조회이며 별도 사용자별 접근 제어를 제공하지 않는다.

배포 머신에 Supabase CLI, Docker, Deno, Node.js를 설치할 필요는 없다. NuGet tool과 standalone 모두 함수 소스와 고정된 SupabaseClient 의존성 설정을 동봉하므로 저장소 밖에서도 실행된다. [.NET 런타임 요구사항](distribution.md)은 기존 배포물과 같다.

## 배포

아래 변수에는 준비된 project ref와 Access Token을 넣는다. CLI 자체의 환경 변수 옵션이 아니라 셸에서 두 필수 인자를 전달하는 예시다.

```sh
gpk deploy-function --project-id "$PROJECT_REF" --access-token "$SUPABASE_ACCESS_TOKEN"
```

두 인자는 순서와 무관하다. 누락·공백 값·중복·알 수 없는 옵션과 잘못된 project ref를 원격 요청 전에 거부한다. `--env-file`, `--output`, 임의 함수명·소스 경로 옵션은 지원하지 않는다.

성공 출력 예시:

```text
함수 배포 완료: project=abcdefghijklmnopqrst, function=get-patch-version, functionVersion=1
호출 URL: https://abcdefghijklmnopqrst.supabase.co/functions/v1/get-patch-version
```

같은 명령을 다시 실행하면 같은 이름의 함수를 갱신한다. 다른 프로젝트에는 `--project-id`와 해당 프로젝트에 접근 가능한 토큰을 바꿔 같은 CLI를 실행한다. `functionVersion`은 **함수 배포 버전**이며 패치 `releaseVersion`과 다르다. 함수 재배포는 포인터나 Storage 산출물을 바꾸지 않는다.

성공은 API의 배포 완료 응답(`201`, 지정한 slug, `ACTIVE`, 양의 정수 버전)을 확인했다는 뜻이다. 실제 조회 성공 여부는 아래 호출로 따로 확인한다. 자동 재시도는 없으며, 네트워크 오류가 나면 배포가 적용됐는지 불명확할 수 있다. 대상 프로젝트를 확인한 뒤 재실행할 수 있고 함수 배포 버전은 증가할 수 있다.

토큰은 Management API의 `Authorization: Bearer` 헤더로만 전송한다. 함수 소스·환경 변수·출력·파일에 저장하지 않는다. CLI 오류에 인자 값·원격 응답 원문을 출력하지 않는다. 토큰이 포함된 전체 명령행을 작업일지나 CI 로그에 남기지 않는다.

## 함수 호출

```sh
curl --get "https://${PROJECT_REF}.supabase.co/functions/v1/get-patch-version" \
  --data-urlencode 'bucket=game-a'
```

```json
{"bucket":"game-a","releaseVersion":"42","manifestPath":"manifests/42.json"}
```

- GET만 지원한다. `bucket`은 하나만 지정하고 영숫자·`.`·`_`·`-`를 사용한다. 빈 문자열, `.`과 `..`는 거부한다.
- 요청의 `apikey`와 `Authorization`은 SupabaseClient에 전달하지 않는다. 함수 실행 환경의 `SUPABASE_PUBLISHABLE_KEYS.default`로 클라이언트를 만들고 `anon`의 기존 읽기 권한으로 조회한다. service role을 사용하지 않는다.
- 공개 호출을 위해 함수의 `verify_jwt=false`로 배포한다. publishable key를 배포 인자나 별도 secret으로 등록하지 않는다.
- `releaseVersion`은 `0`부터 `9223372036854775807`까지의 10진수 **문자열**이다. `9007199254740993`도 정확하게 보존한다. 클라이언트에서 JavaScript `Number`로 바꾸지 않는다.
- 첫 게시 전 포인터가 없으면 404다. 실제 게시 버전 `0`은 정상이다. 가장 큰 버전을 검색하지 않고 현재 포인터를 반환하므로 `42 → 41` 롤백도 반영한다.
- 함수 호출자가 프로젝트 URL이나 테이블을 바꿀 수 없다. 기존 `gpk sync`는 계속 PostgREST를 직접 조회한다.

## 오류 처리

CLI 종료 코드는 성공 `0`, 인자·인증·배포 실패 `1`이다. 배포 API의 401(토큰 인증), 403(권한), 429(요청 한도), 5xx(서버 오류), 네트워크 오류를 구분해 안내한다.

함수 오류 본문은 다음 형태다.

```json
{"error":{"code":"pointer_not_found","message":"Request failed."}}
```

| HTTP | `error.code` | 의미·조치 |
| --- | --- | --- |
| 400 | `invalid_bucket` | bucket 개수·값·형식 확인 |
| 401 | `query_failed` | 함수에 기본 제공된 publishable key로 SupabaseClient 인증 실패 |
| 403 | `query_failed` | 읽기 권한 거부. SELECT GRANT와 RLS 확인 |
| 404 | `pointer_not_found` | 정상 조회 결과 행이 없음. 게시 여부와 읽기 RLS 정책 확인 |
| 405 | `method_not_allowed` | GET 사용 |
| 500 | `configuration_error` | 함수의 기본 제공 `SUPABASE_URL`·`SUPABASE_PUBLISHABLE_KEYS` 확인 |
| 500 | `query_failed` | 테이블·컬럼·PostgREST 연결 등 조회 실패 확인 |

RLS가 행을 숨기면 정상 빈 조회(404)다. 테이블 누락이나 연결 실패는 `query_failed` 500이다. OPTIONS/CORS 처리는 이 GET 계약에 포함하지 않는다.

모든 함수 오류는 Supabase Functions의 Logs에 JSON 한 줄로 기록한다. 로그에는 `function`, HTTP `status`, `error.code`만 포함하며 API key, Access Token, 요청 헤더와 상류 응답 본문은 기록하지 않는다.

## 검증

로컬 개발 검사는 .NET 10 SDK, Deno 2.1.14, Node.js 24로 실행한다. 함수는 `@supabase/supabase-js` 2.116.0과 lockfile을 사용한다.

```sh
dotnet test GamePatchKit.sln --configuration Release
deno test --frozen --allow-env=SUPABASE_URL,SUPABASE_PUBLISHABLE_KEYS --allow-net=127.0.0.1 \
  --config src/GamePatchKit.Cli/Functions/deno.json \
  --lock src/GamePatchKit.Cli/Functions/deno.lock \
  tests/Functions/test-get-patch-version.ts
node tests/Functions/test-deploy-packaging.mjs /absolute/path/to/gpk
```

마지막 명령은 NuGet tool 설치 폴더의 `gpk` 또는 standalone 실행 파일을 받는다. 동봉 소스·`deno.json`·`deno.lock`의 일치 여부를 검사하고 임시 폴더에서 실행한다. 로컬 프록시가 Supabase 연결을 차단하므로 실제 배포는 하지 않는다. CI는 기존 Windows/Linux/macOS 배포물 검사에 이 검사를 포함한다.

| 설계 14장 | 구현·로컬 검증 | 실제 Supabase 검증 상태 |
| --- | --- | --- |
| 1. 배포 형식·소스 | 공식 multipart 명세, 파일명/entrypoint/metadata 일치 검사 | 테스트 프로젝트 정보 미제공으로 생성·갱신 미검증 |
| 2. CLI 연결 | project ref 경로·Bearer 헤더·인자 거부·안전한 오류 검사 통과 | 실제 인증·권한 응답 미검증 |
| 3. 조회 계약 | 두 bucket, 미게시, 0, 롤백의 원본 함수 실행 검사 통과 | 실제 포인터 조회 미검증 |
| 4. 인증·오류·정수 | 기본 제공 키 사용, 호출자 헤더 미전달, 401/403/500 구분, 큰 정수 문자열 보존 검사 통과 | 플랫폼 기본 환경 변수·PostgREST 캐스팅 미검증 |
| 5. 패키징 | macOS arm64 NuGet tool·standalone의 저장소 밖 리소스 읽기 검사 | Windows/Linux는 CI 실행 필요 |
| 6. 프로젝트 재사용 | 동일 소스·서로 다른 배포 경로와 런타임 URL 대역 검사 통과 | A/B 실제 배포·프로젝트 분리 미검증 |
| 7. 문서·검토 | 기존 CLI 테스트 및 자체 검토 수행 | 실제 배포 후 결과 갱신 필요 |

원격 검증은 운영 데이터 대신 [포인터 fixture](../../tests/Functions/remote-fixture.sql)를 전용 테스트 프로젝트에서 사용한다. 사전 조건 마이그레이션을 적용한 후 기존 DB 연결 설정으로 실행한다.

```sh
# PGHOST/PGPORT/PGDATABASE/PGUSER 등의 접속 설정은 테스트 프로젝트 A를 가리킨다.
psql -v ON_ERROR_STOP=1 -v game_a_version=42 -f tests/Functions/remote-fixture.sql
# A의 롤백 검증 시 같은 fixture를 41로 다시 적용한다.
psql -v ON_ERROR_STOP=1 -v game_a_version=41 -f tests/Functions/remote-fixture.sql
# 프로젝트 B 접속 설정으로 전환한 후:
psql -v ON_ERROR_STOP=1 -v game_a_version=7 -f tests/Functions/remote-fixture.sql
```

A의 최초 값 42를 확인한 뒤 41로 바꾸고 다시 조회한다. B에서는 같은 `gpk-test-game-a`가 7이어야 한다. `gpk-test-game-b`는 13, `gpk-test-zero`는 0, `gpk-test-large`와 `gpk-test-max`는 큰 정수 문자열, `gpk-test-unpublished`는 404를 기대한다. 호출자 API key 없는 공개 호출, 프로젝트별 기본 publishable key, 권한 거부·테이블 누락도 전용 프로젝트에서 확인해야 한다.

함수 재배포 전후에는 fixture를 다시 적용하지 않고 다음 읽기 결과를 비교한다. 포인터와 기존 Storage 산출물 목록·metadata가 같아야 한다.

```sql
select bucket, release_version, updated_at from public.gamepatch_pointer order by bucket;
select bucket_id, name, id, updated_at, metadata from storage.objects order by bucket_id, name;
```

실제 배포 API를 호출하지 않은 로컬 결과는 원격 배포 성공을 보증하지 않는다.

## 관련 문서

- [설계와 범위](../design/game-patch-kit-cli-deploy-function-design.md)
- [Supabase 함수 배포 API](https://supabase.com/docs/reference/api/v1-deploy-a-function)
- [Supabase 함수 인증 헤더](https://supabase.com/docs/guides/functions/auth-headers)
- [PostgREST 컬럼 캐스팅](https://docs.postgrest.org/en/stable/references/api/tables_views.html#casting-columns)

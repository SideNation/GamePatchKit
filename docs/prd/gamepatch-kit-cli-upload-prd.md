# GamePatchKit CLI Supabase 업로드

## 목적

`gpk build`가 만든 패치 데이터를 Supabase Storage 버킷에 올리는 `gpk upload` 명령을 추가한다.

업로드 대상과 인증정보는 패치 데이터 프로젝트가 관리한다. CLI는 로컬의 마지막 업로드 성공 매니페스트를 기준으로 새 산출물만 고른다. 산출물은 upsert하고 세대 매니페스트는 불변 객체로 생성해 중단 후에도 같은 소스 commit에서 안전하게 재실행할 수 있게 한다.

## 개발 환경

| 항목 | 버전·조건 |
| --- | --- |
| Supabase 요금제 | Pro 또는 Team 필수 |
| Supabase.Storage | 2.7.0 |

- Free 플랜의 전역 파일 제한은 50 MB이므로 지원 대상에서 제외한다. Pro·Team은 전역 제한을 최대 500 GB까지 설정할 수 있다. 대상 프로젝트의 전역 제한과 버킷 제한을 가장 큰 `storedSize` 이상으로 미리 설정해야 한다. [`Supabase Storage 파일 제한`](https://supabase.com/docs/guides/storage/uploads/file-limits)
- `UploadOrResume`의 6 MiB TUS 청크는 요청을 나누는 방식이며 요금제·전역·버킷의 객체 크기 제한을 우회하지 않는다. Supabase resumable upload는 더 큰 객체를 지원하지만, `gpk upload`는 전송 성능을 위해 산출물 하나의 일반 도구 상한을 **1 GiB(1,073,741,824바이트)**로 제한한다. [`Supabase resumable upload`](https://supabase.com/docs/guides/storage/uploads/resumable-uploads)
- `Supabase.Storage`는 `netstandard2.0`으로 배포되어 `net10.0`에서 참조할 수 있다. 전이 의존성은 `BirdMessenger` 4.0.0, `MimeMapping` 4.0.0, `Newtonsoft.Json` 13.0.2, `Supabase.Core` 1.2.0, `System.Diagnostics.DiagnosticSource` 8.0.1이며 모두 `netstandard2.0` 이상이다.
- 메타 패키지 `Supabase`는 참조하지 않는다. Gotrue·Realtime·Postgrest·Functions까지 끌어오는데 이번 범위에서 쓰는 것은 Storage뿐이다.
- `Supabase.Storage`가 요구하는 `Newtonsoft.Json` 13.0.2는 CLI가 이미 고정한 버전과 같아 버전 충돌이 없다.
- `Supabase.Storage`의 업로드 API는 `Task`만 제공한다. `upload` 경로는 async로 만들고 `Program.Main`을 `async Task<int>`로 바꾼다. `build`와 `verify`의 동기 경로는 그대로 둔다.

## 요구사항

### 운영 전제

- 한 패치 데이터 프로젝트의 `build` → `verify` → `upload` → 상태 Git 저장소 갱신 → 버전 포인터 갱신은 지정된 수동 배포 환경 한 곳에서 순차 실행한다. 같은 `--output`을 사용하는 다른 `build`·`verify`·`upload`와 겹치면 안 된다. CLI에는 락이나 동시성 제어를 넣지 않는다.
- 패치 데이터 배포에는 GitHub Actions를 사용하지 않는다. 운영자가 코드로 관리하는 배포 스크립트가 전체 순서를 직렬화한다. CLI NuGet 패키지 배포용 GitHub Actions는 별도이며 이 전제의 대상이 아니다.
- 지정된 배포 환경은 소스 저장소의 전체 Git 이력을 유지한다. 이전 성공 매니페스트의 `sourceCommit`과 다시 게시할 대상 SHA를 모두 checkout할 수 있어야 한다.
- `gpk upload`가 로컬 선검증을 시작한 뒤 `.gpk-upload-state.json`을 교체할 때까지 다른 프로세스가 같은 `--output`의 `manifest.json`이나 산출물을 수정하지 않는다.
- 하나의 `--output`과 그 로컬 업로드 성공 상태는 하나의 고정된 Supabase 프로젝트·버킷에만 사용한다.
- 원격 객체를 다른 프로그램이 수정하거나 삭제하지 않는다. 산출물은 원격 존재 여부와 바이트를 조회하지 않는다. 세대 매니페스트는 create-only 게시가 중복 객체로 거부된 경우에만 기존 원격 바이트를 내려받아 현재 `manifest.json`과 비교한다.
- 버킷 생성, 공개 설정, 전역·버킷 파일 제한, API 키 발급은 대상 패치 데이터 프로젝트가 코드로 관리하는 배포 사전 조건이다. CLI가 이 리소스를 만들거나 설정하지 않는다.

> **중요 운영 전제:** 상태 복원, 정확한 `sourceCommit` 기록, `gpk`와 압축 구현 버전 고정, 동시 실행 금지, 상태 Git push와 포인터 갱신은 별도의 코드 관리 배포 스크립트가 책임진다. 이 스크립트는 현재 CLI 구현 범위에 포함하지 않지만, 별도 작업으로 완료하기 전에는 운영 배포를 시작하지 않는다.

### 명령

```text
gpk upload --output <패치 데이터 폴더> [--env-file <환경 변수 파일>]
```

- `--output`은 필수다.
- `--source`를 받지 않는다. 매니페스트가 올릴 대상을 모두 담고 있어 자기서술적이고, 업로드가 필요한 자리에는 소스 체크아웃이 없기 때문이다. `verify`와 같은 이유다.
- Git 저장소와 `gamepatchkit.yml`을 요구하지 않는다.

### 설정 값과 Storage client 초기화

| 이름 | 의미 |
| --- | --- |
| `GPK_SUPABASE_STORAGE_URL` | `https://<project-ref>.storage.supabase.co/storage/v1` 형식의 직접 Storage API URL |
| `GPK_SUPABASE_SECRET_KEY` | 대상 프로젝트의 `sb_secret_...` API key |
| `GPK_SUPABASE_BUCKET` | 업로드할 기존 버킷 이름 |

- `GPK_SUPABASE_STORAGE_URL`은 일반 프로젝트 URL이 아니라 `/storage/v1`까지 포함한 직접 Storage API URL이다. 큰 파일에는 직접 Storage hostname 사용을 권장하는 Supabase 지침을 따른다.
- `Supabase.Storage.Client`에는 URL 끝의 `/`를 제거한 값을 넘긴다. 라이브러리가 여기에 `/object/...`와 `/upload/resumable`을 붙인다.
- `GPK_SUPABASE_SECRET_KEY`는 `sb_secret_`로 시작해야 하며 다른 key 형식은 네트워크 요청 전에 거부한다.
- API key는 `apikey` 요청 헤더로만 전달한다. `sb_secret_...` key는 JWT가 아니므로 `Authorization: Bearer`에 넣지 않는다. 이 키는 `service_role`로 동작하고 RLS를 우회하므로 Storage policy가 필요하지 않지만 프로젝트 전체에 강한 권한을 가진다. [`Supabase API key`](https://supabase.com/docs/guides/getting-started/api-keys)
- 키는 지정된 수동 배포 환경에서만 사용한다. 저장소·패키지·로그에 절대 포함하지 않고 운영 환경의 secret 저장소나 프로세스 환경 변수로 전달한다.
- 세 값을 모두 요구한다. 하나라도 비어 있으면 빠진 이름을 모두 나열하고 아무것도 올리지 않은 채 중단한다.
- 프로세스 환경 변수를 먼저 읽고, `--env-file`을 지정했으면 그 파일의 값으로 덮어쓴다. 명령에 적은 것이 실제로 쓰이는 값이어야 하며, 로컬 셸이나 사용자 프로필에 남은 오래된 변수가 방금 지정한 파일을 조용히 이기는 상태를 만들지 않는다.
- `--env-file`의 경로가 없으면 중단한다. 지정했는데 읽히지 않은 것을 성공으로 넘기지 않는다.
- 환경 변수 파일은 줄마다 `KEY=VALUE` 하나를 적는다. 빈 줄과 `#`으로 시작하는 줄은 건너뛰고, 키와 값의 앞뒤 공백은 제거하며, 첫 `=`까지가 키다. 위 세 이름 외의 키는 무시한다.
- 키 값을 표준 출력·표준 에러 어디에도 출력하지 않는다. 실패 메시지에도 넣지 않는다.
- 환경 변수 파일은 키를 담으므로 저장소에 커밋하지 않는다.

### 업로드 대상의 로컬 검증

1. `--output`의 `manifest.json`을 읽고 `gpk verify`와 같은 관계 검증을 수행한다. 위반이 있으면 아무것도 올리지 않고 중단한다.
2. 올릴 대상은 매니페스트가 참조하는 산출물이다. `archive`와 `source: file` 엔트리의 `name`을 올리고, `source: archive` 엔트리는 아카이브 안에 있으므로 따로 올리지 않는다.
3. 각 산출물의 로컬 존재 여부, 실제 크기 대 `storedSize`, `gpk upload`의 산출물당 1 GiB 상한을 먼저 확인한다. 하나라도 없거나 크기가 다르거나 `storedSize`가 1,073,741,824바이트를 넘으면 아무것도 올리지 않고 중단한다.
4. SHA-256을 다시 계산하지 않는다. 전량 해시는 `gpk verify`가 담당하며, 배포 직전 검증이 필요하면 `gpk verify`를 먼저 실행한다.
5. 네트워크 호출 전에 `GPK_SUPABASE_BUCKET`, 현재 매니페스트가 참조하는 **모든** 산출물 `name`과 `manifests/<releaseVersion>.json`을 한 원격 경로 선검증 단계에서 검사한다. 이번 실행에서 건너뛸 산출물도 새 세대 매니페스트가 참조하므로 검사 대상이다.
6. 원격 객체 경로는 `/`로 구분한 비어 있지 않은 세그먼트로 구성하고 각 세그먼트에는 ASCII 영문 대소문자, 숫자, `.`, `_`, `-`만 허용한다. 세그먼트 `.`과 `..`, 앞뒤 `/`, `//`, 공백, 한글을 포함한 비 ASCII 문자, URL 예약문자와 역슬래시는 거부한다. Supabase가 허용하는 파일 이름보다 의도적으로 좁은 규칙을 사용해 소비 측 URL 조합을 단순하게 유지한다. [`Supabase Storage 파일 이름 제한`](https://supabase.com/docs/guides/storage/uploads/file-limits#file-name-restrictions)
7. 버킷은 `/`가 없는 단일 세그먼트로 같은 문자 규칙을 적용한다. 버킷과 객체 경로 중 잘못된 항목이 하나라도 있으면 모든 오류를 한 번에 나열하고 Storage client를 호출하지 않은 채 중단한다. 검증을 통과한 버킷 이름과 `name`은 URI escaping 없이 원격 경로로 그대로 사용한다.

이 허용 문자 제한은 `gpk upload`의 원격 경로 선검증에만 적용한다. `gpk build`의 기존 Git 추적 파일·정규화 상대 경로 규칙은 바꾸지 않으므로, 허용 범위 밖의 이름이 있으면 빌드는 성공할 수 있지만 업로드는 네트워크 호출 전에 실패한다.

### 로컬 업로드 성공 상태

`<output>/.gpk-upload-state.json`에 마지막으로 업로드를 완료한 `manifest.json` 전체를 보관한다. 별도 상태 스키마를 만들지 않고 기존 매니페스트 형식을 그대로 사용한다.

- 성공 상태가 없거나 읽을 수 없으면 현재 매니페스트가 참조하는 산출물을 모두 업로드 대상으로 고른다. 이 상태만으로 실제 첫 배포라고 추론하거나 기존 세대 매니페스트의 덮어쓰기를 허용하지 않는다.
- 성공 상태가 있으면 현재 매니페스트의 산출물 중 성공 상태에 같은 `name`이 없는 것만 올린다. 같은 이름의 원격 객체는 같은 바이트라는 `build` 산출물 계약과 단일 게시자 전제를 신뢰하고 추가 검사하지 않는다.
- 로컬 상태는 산출물 업로드 대상을 계산하는 유일한 기준이다. 원격 산출물은 조회하지 않으며, 원격 세대 매니페스트는 create-only 중복 충돌을 판정할 때만 조회한다.
- 모든 원격 업로드가 성공한 뒤 현재 `manifest.json`을 같은 디렉터리의 임시 파일에 완전히 쓰고 `.gpk-upload-state.json`으로 원자적으로 교체한다.
- 원격 업로드나 로컬 상태 교체가 실패하면 기존 성공 상태를 유지하고 0이 아닌 종료 코드로 끝낸다. 다음 실행은 이전 성공 상태에서 대상을 다시 계산하고 이미 올라간 산출물도 upsert하며, 동일한 세대 매니페스트는 바이트 비교 후 재사용한다.
- 성공 상태 파일이 삭제되면 다음 실행에서 산출물을 전량 다시 올린다. 같은 세대 경로의 원격 매니페스트 바이트가 같으면 재사용하고 다르면 버전 충돌로 중단하므로 기존 세대가 바뀌지 않는다.
- 성공 상태에는 API key, URL, 버킷 이름을 기록하지 않는다. 하나의 output이 하나의 고정 대상에만 연결된다는 운영 전제를 따른다.

### 상태 Git 저장소

릴리스 계보를 지정된 수동 배포 환경에서 이어가기 위해 소스 저장소와 분리된 private Git 저장소를 둔다. 패치 상태 데이터로 추적하는 파일은 `manifest.json`과 `.gpk-upload-state.json` 두 개뿐이며, `archives/`·`files/` 산출물은 Git에 넣지 않는다. 같은 소스 저장소에 상태를 커밋하면 그 커밋이 다음 `sourceCommit`을 바꾸므로 사용하지 않는다.

- 첫 배포가 아니면 배포 시작 시 상태 Git 저장소의 두 파일을 `--output`에 복원한다. 상태 저장소가 비어 있는 경우는 실제 첫 배포에만 허용하며, 기존 버킷이 있는데 상태를 잃은 상황을 새 `releaseVersion: 0`으로 시작하지 않는다.
- `gpk build`는 이전 매니페스트가 승계하는 산출물을 로컬에서 요구한다. 영속 `--output`이 없는 깨끗한 실행 환경은 현재 Supabase 버킷에서 이전 매니페스트가 참조하는 산출물을 복원한 뒤 `gpk verify`를 실행한다. 이 다운로드 자동화와 별도 산출물 백업은 이번 CLI 범위에 포함하지 않는다.
- `gpk upload`와 로컬 성공 상태 기록이 모두 성공한 뒤 두 상태 파일을 **한 Git commit**으로 저장하고 push한다. 그 뒤에만 Postgres 버전 포인터를 갱신한다.
- 첫 Storage 호출 전 실패는 원격 상태를 바꾸지 않았으므로 수정한 새 commit으로 다시 시작할 수 있다. 첫 Storage 호출이 시작된 뒤 실패하면 `manifest.json.sourceCommit`에 기록된 정확한 SHA를 checkout해 같은 CLI·압축 구현 버전으로 `build` → `verify` → `upload`부터 다시 실행한다. 이 배포가 상태 Git push와 포인터 갱신까지 끝나기 전에는 더 새로운 source commit을 게시하지 않는다.
- 배포 스크립트는 첫 Storage 호출 전에 대상 `manifest.json.sourceCommit`을 기록해 실패한 SHA를 식별할 수 있게 한다. 별도 상태 파일이나 CLI `--commit`·`--rebuild` 옵션은 추가하지 않고 Git checkout으로 재현한다.
- 상태 commit 또는 push가 실패하면 포인터를 갱신하지 않고 같은 source commit의 배포를 다시 완료한다.
- CLI는 소스 checkout이나 상태 저장소 clone·commit·push를 수행하지 않는다. 전체 Git 이력을 보유한 지정 배포 환경의 운영 스크립트가 이 순서를 담당한다.

### 업로드 순서와 세대 불변성

1. 로컬 성공 상태와 비교해 선택한 산출물을 먼저 순서대로 올린다.
2. `manifests/<releaseVersion>.json`을 불변 객체로 생성한다.
3. 모두 성공한 뒤 로컬 `.gpk-upload-state.json`을 교체한다.
4. 산출물은 `UploadOrResume`과 `FileOptions.Upsert=true`를 사용한다. 같은 산출물 `name`은 같은 저장 바이트라는 빌드 계약을 전제로 중단 후 재실행에서 이미 전송된 객체를 다시 쓸 수 있게 한다.
5. 세대 매니페스트는 일반 파일 업로드와 `FileOptions.Upsert=false`를 사용한다. 대상 경로가 없으면 새로 만들고, 중복 객체 오류가 나면 기존 원격 파일을 다운로드해 현재 `manifest.json`과 바이트 단위로 비교한다. 같으면 이미 게시된 세대로 재사용하고, 다르면 기존 객체를 바꾸지 않은 채 버전 충돌로 중단한다. 중복 이외의 업로드·다운로드 오류는 그대로 실패 처리한다.
6. 원격 세대 매니페스트 비교는 중복 객체 오류를 해결하기 위한 절차일 뿐 업로드 대상 계산이나 릴리스 버전 결정에 사용하지 않는다.
7. 게시된 세대 경로의 바이트를 바꾸지 않으므로 CDN 캐시 무효화나 overwrite 전파 시간에 의존하지 않는다. 새 내용은 항상 증가한 `releaseVersion`의 새 경로로 게시한다. [`Supabase Smart CDN`](https://supabase.com/docs/guides/storage/cdn/smart-cdn)
8. `UploadOrResume`은 산출물을 6 MiB TUS 청크로 전송한다. 이 방식은 요청 크기를 나눌 뿐 `gpk upload`의 산출물당 1 GiB 상한과 사전에 설정한 전역·버킷 파일 제한을 바꾸지 않는다.
9. `FileOptions.ContentType`은 아카이브·파일 객체에 `application/octet-stream`, 세대 매니페스트에 `application/json`을 준다.

중복 객체 오류는 다음 응답으로 한정한다.

- HTTP 409이며 `Supabase.Storage`가 `FailureHint.Reason.AlreadyExists`로 분류한 응답
- HTTP 409이며 응답 JSON의 `code`가 `ResourceAlreadyExists` 또는 `KeyAlreadyExists`인 응답
- legacy HTTP 400이며 응답 JSON의 `message` 또는 공백을 제거한 원문이 `Asset Already Exists`와 정확히 일치하는 응답

일반적인 `exists` 부분 문자열만으로 중복을 추정하지 않는다. 위 조건에 해당하지 않는 HTTP 400·409 응답은 원격 파일을 다운로드하지 않고 원래 오류로 실패한다.

원격 루트 `manifest.json`은 올리지 않는다. 소비자는 Postgres 버전 포인터로 선택한 `manifests/<releaseVersion>.json`만 읽고, 다음 업로드의 델타 기준은 로컬 `.gpk-upload-state.json`만 사용한다.

### 업로드 요약 출력

올린 산출물 수와 총 바이트, 건너뛴 산출물 수를 출력한다. 세대 매니페스트는 산출물 집계와 분리해 표시한다. 모두 성공하면 종료 코드 0, 중단하면 0이 아닌 값으로 끝낸다.

게시한 `releaseVersion`을 출력한다. 지정된 운영 스크립트가 업로드 성공 후 버전 포인터에 쓸 값이며, 이 값 없이는 다음 단계를 자동화할 수 없다. 재실행할 SHA는 업로드 전에 기록한 로컬 `manifest.json.sourceCommit`을 사용한다.

Storage 호출이 실패하면 표준 에러에 실패 단계(`artifact-upsert`, `manifest-create`, `manifest-download`), 원격 객체 경로, SDK 예외 타입과 확인 가능한 HTTP 상태·Supabase 오류 코드·메시지를 출력한다. 원시 요청·응답 헤더, API key, 환경 변수 파일 내용과 전체 원시 응답 본문은 출력하지 않는다.

## 완료 조건

1. `gpk upload --output <폴더>`가 선택한 산출물을 upsert한 뒤 `manifests/<releaseVersion>.json`을 create-only로 게시하고 마지막에 로컬 성공 상태를 교체한 뒤 종료 코드 0으로 끝난다.
2. 같은 명령을 다시 실행하면 산출물은 모두 건너뛰고, 이미 존재하는 세대 매니페스트가 같은 바이트임을 확인해 재사용한다. 요약의 건너뛴 산출물 수가 참조 산출물 수와 같다.
3. 파일 하나를 고쳐 증분 빌드한 뒤 업로드하면 새로 생긴 파일 객체와 세대 매니페스트만 올라가고 기존 아카이브는 건너뛴다.
4. `.gpk-upload-state.json`이 없거나 읽을 수 없으면 참조 산출물을 모두 upsert한다. 기존 `manifests/<releaseVersion>.json`이 같으면 재사용하고 다르면 덮어쓰지 않고 중단하며, 성공 시 새 상태를 기록한다.
5. Pro 또는 Team 프로젝트에서 전역·버킷 제한을 가장 큰 `storedSize` 이상으로 설정하면 50 MB를 넘고 1 GiB 이하인 아카이브도 `UploadOrResume`으로 올라간다. `storedSize`가 1,073,741,824바이트를 넘는 산출물은 첫 Storage 호출 전에 거부한다.
6. `GPK_SUPABASE_STORAGE_URL`·`GPK_SUPABASE_SECRET_KEY`·`GPK_SUPABASE_BUCKET` 중 하나라도 없으면 빠진 이름이 모두 나열되고 아무것도 올라가지 않은 채 0이 아닌 종료 코드로 끝난다.
7. 같은 이름이 프로세스 환경 변수와 `--env-file`에 모두 있으면 `--env-file`의 값이 쓰인다. `--env-file`을 주지 않으면 프로세스 환경 변수만 쓴다.
8. `--env-file`의 경로가 없으면 아무것도 올리지 않고 중단한다.
9. `GPK_SUPABASE_SECRET_KEY`가 `sb_secret_`로 시작하지 않으면 아무것도 올리지 않고 중단한다. 유효한 값은 성공·실패 어느 경우에도 표준 출력과 표준 에러에 나타나지 않고 API 요청의 `apikey` 헤더로만 전달된다.
10. 매니페스트가 참조하는 산출물이 로컬에 없거나 실제 크기가 `storedSize`와 다르면 아무것도 올리지 않고 중단한다.
11. 로컬 `manifest.json`이 관계 검증을 통과하지 못하면 아무것도 올리지 않고 중단한다.
12. `gpk upload`에 `--source`를 주면 거부하고, Git 저장소와 `gamepatchkit.yml` 없이 실행된다.
13. `Supabase` 메타 패키지 없이 `Supabase.Storage`만 참조한 상태로 `net10.0`에서 복원·빌드·테스트가 통과한다.
14. 서로 다른 `releaseVersion`으로 두 번 업로드하면 `manifests/` 아래에 두 세대가 모두 남고 이전 세대의 바이트가 바뀌지 않는다. 같은 세대 경로에 다른 바이트를 게시하려 하면 버전 충돌로 중단한다.
15. 게시한 `releaseVersion`이 표준 출력에 나타나 운영 스크립트가 버전 포인터에 쓸 값을 읽을 수 있다.
16. 업로드가 어느 단계에서 실패해도 그 뒤의 원격 객체를 올리지 않고 로컬 성공 상태를 바꾸지 않는다. 첫 Storage 호출 뒤의 실패는 로컬 `manifest.json.sourceCommit`의 정확한 SHA에서 재실행해 완료하며, 그전에는 더 새로운 SHA를 게시하지 않는다.
17. Storage 대역을 사용한 자동 테스트가 산출물 → 세대 매니페스트 순서, 동일 세대 재사용, 다른 바이트 충돌과 단계별 실패 중단을 검증한다. adapter 단위 테스트는 현재 HTTP 409 중복 코드와 legacy HTTP 400 중복 메시지만 재사용 경로로 분류하고 그 밖의 400·409를 실패 처리하는지 검증한다.
18. 직접 Storage API URL과 `sb_secret_...` key의 `apikey` 헤더를 사용해 실제 Pro 또는 Team 프로젝트에 업로드할 수 있고, 권한이 부족하면 키 값을 노출하지 않은 채 실패한다.
19. 버킷 이름, 현재 매니페스트가 참조하는 모든 산출물과 세대 매니페스트의 원격 경로를 한 선검증 단계에서 첫 Storage 호출 전에 검사한다. 하나라도 허용 문자 규칙을 위반하면 버킷과 잘못된 이름·경로를 모두 출력하고 Storage 호출은 0회다.
20. 별도 private 상태 Git 저장소가 성공적으로 게시된 `manifest.json`과 `.gpk-upload-state.json`만 같은 commit으로 보존하고, 실제 첫 배포가 아닌 지정 배포 환경은 이 상태를 복원해 이전 `releaseVersion`과 업로드 델타를 이어간다.
21. 같은 패치 데이터 프로젝트의 배포는 지정된 수동 환경 한 곳에서 동시에 실행되지 않는다. 첫 Storage 호출 뒤 실패하거나 상태 저장소 push가 실패하면 기록한 `sourceCommit` SHA의 재실행이 완료되기 전까지 더 새로운 릴리스를 게시하지 않는다.
22. `Program.Main`과 기존 `Program.Run`의 async 전환으로 영향을 받는 `TestBuildCommand`, `TestVerifyCommand`, `TestCommandArguments`의 모든 호출부를 `await Program.RunAsync` 경로로 바꾼 뒤 기존 명령 테스트가 통과한다.
23. Storage 오류는 표준 에러에 실패 단계·원격 경로·예외 타입과 확인 가능한 HTTP 상태·Supabase 오류 코드·메시지를 남기며 API key, 요청·응답 헤더, 환경 변수 파일 내용과 전체 원시 응답 본문을 노출하지 않는다.

## 제외 범위

이번 범위를 업로드 한 방향으로 한정한다. 다음은 넣지 않는다.

- 다운로드, 삭제, 버킷 생성·공개 설정, 요금제·전역·버킷 제한 설정, API key 발급 — 대상 패치 데이터 프로젝트의 재현 가능한 인프라 설정으로 관리한다
- **버전 포인터 갱신** — `gpk upload`는 Postgres를 건드리지 않는다. 게시한 `releaseVersion`을 출력하는 데까지가 이 명령의 범위이고, 포인터 갱신은 업로드와 두 상태 파일의 Git push가 모두 성공한 뒤 지정된 수동 배포 스크립트가 한다. 포인터의 역할은 [gamepatch-kit-distribution-prd.md](gamepatch-kit-distribution-prd.md)에서 다룬다
- 원격에 남은 미참조 과거 세대 정리와 보존 정책
- 캐시 정책 — `FileOptions.CacheControl`을 지정하지 않고 라이브러리 기본값을 그대로 쓴다
- 서명 URL, 공개·비공개 전환, CDN 캐시 무효화
- publishable·legacy `anon`·사용자 JWT를 이용한 제한 권한 업로드와 Storage RLS policy — 업로드 인증은 `sb_secret_...`만 지원한다
- 업로드 재시도와 백오프 — CLI가 별도 재시도를 걸지 않는다. `UploadOrResume`의 세션 URL은 프로세스 메모리에만 남으며, 프로세스를 다시 실행하면 로컬 성공 상태와 산출물 upsert로 처음부터 재전송한다
- `Supabase.Storage` 내부 stream·file handle 수명 우회 — 현재는 SDK 책임으로 두고, 실제 운영 오류가 확인되면 SDK 버전 변경이나 별도 수정으로 대응한다
- 병렬 업로드와 CLI 내부 동시성 제어 — 객체를 하나씩 순서대로 올리고 전체 배포 흐름을 지정된 수동 운영 스크립트가 직렬화한다
- 업로드 대상 계산을 위한 원격 매니페스트·버전 조회와 개별 원격 산출물 존재·무결성 확인 — create-only 세대 매니페스트의 중복 오류에서 수행하는 바이트 비교만 예외다
- 하나의 로컬 성공 상태로 여러 프로젝트나 버킷에 게시하는 동작
- 실제 산출물의 Git·Git LFS 저장과 별도 object storage 백업 — Git에는 두 상태 파일만 보존한다
- 소스 checkout, 상태 Git 저장소의 clone·commit·push와 산출물 복원 자동화 — 지정된 수동 배포 환경의 운영 스크립트 책임이다
- CLI `--commit`·`--rebuild` 옵션 — 재실행 대상은 `manifest.json.sourceCommit`이며 Git checkout과 기존 `build` → `verify` → `upload` 조합으로 재현한다
- 패치 데이터 배포용 GitHub Actions — 수동 배포로 운영한다. CLI NuGet 패키지 배포 자동화는 이 범위와 무관하다
- `gpk build` 완료 후 자동 업로드 — `upload`는 독립 명령이다
- Supabase 외 스토리지 백엔드

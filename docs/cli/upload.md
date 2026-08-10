# `gpk upload`

## 개요

`gpk upload`는 `gpk build`가 만든 패치 데이터를 Supabase Storage 버킷에 올리는 명령이다. 업로드 대상과 인증정보는 패치 데이터 프로젝트가 관리한다.

- 로컬의 마지막 업로드 성공 매니페스트(`.gpk-upload-state.json`)를 기준으로 새로 생긴 산출물만 고른다.
- 산출물(`archive`, `source: file` 엔트리)은 `Upsert=true`로 올려 중단 후 재실행에서 이미 올라간 객체도 안전하게 다시 upsert한다.
- 세대 매니페스트(`manifests/<releaseVersion>.json`)는 `Upsert=false`로 생성해 불변 객체로 남기고, 같은 세대 경로의 바이트를 절대 덮어쓰지 않는다.

## 사전 조건

| 항목 | 조건 |
| --- | --- |
| Supabase 요금제 | Pro 또는 Team 필수. Free 플랜의 전역 파일 제한(50 MB)은 지원 대상이 아니다. |
| 전역·버킷 파일 제한 | 대상 프로젝트에서 가장 큰 산출물의 `storedSize` 이상으로 미리 설정해야 한다. `gpk upload`는 이 설정을 만들거나 조회하지 않는다. |
| 참조 패키지 | `Supabase.Storage` 2.7.0만 참조한다. 메타 패키지 `Supabase`는 참조하지 않는다. |
| 버킷·API key | 대상 버킷 생성과 `sb_secret_...` API key 발급은 패치 데이터 프로젝트가 코드로 관리하는 배포 사전 조건이다. |

`UploadOrResume`이 산출물을 6 MiB TUS 청크로 나눠 전송하는 것과 별개로, `gpk upload`는 전송 성능을 위해 산출물 하나의 크기를 **1 GiB(1,073,741,824바이트)** 이하로 제한한다. 이 상한은 Supabase 요금제·전역·버킷 제한과 무관하게 CLI가 항상 적용한다.

## 사용법

```shell
gpk upload --output <패치 데이터 폴더> [--env-file <환경 변수 파일>]
```

- `--output`은 필수다.
- `--source`는 받지 않는다. `gpk verify`와 같은 이유로, `manifest.json`이 올릴 대상을 모두 담고 있어 소스 체크아웃이 필요하지 않다.
- Git 저장소와 `gamepatchkit.yml`을 요구하지 않는다.
- 알 수 없는 옵션은 거부한다.

## 설정 값

다음 세 값을 모두 요구한다. 하나라도 비어 있으면 빠진 이름을 모두 나열하고 아무것도 올리지 않은 채 중단한다.

| 이름 | 의미 |
| --- | --- |
| `GPK_SUPABASE_URL` | `https://<project-ref>.storage.supabase.co/storage/v1` 형식의 직접 Storage API URL |
| `GPK_SUPABASE_KEY` | 대상 프로젝트의 `sb_secret_...` API key |
| `GPK_SUPABASE_BUCKET` | 업로드할 기존 버킷 이름 |

- `GPK_SUPABASE_URL`은 HTTPS이고 `/storage/v1`로 끝나는 직접 Storage API URL이어야 한다. 일반 프로젝트 URL, HTTP URL은 거부한다. 큰 파일에는 직접 Storage hostname 사용을 권장하는 Supabase 지침을 따른다.
- `Supabase.Storage.Client`에는 URL 끝의 `/`를 제거한 값을 넘긴다. 라이브러리가 여기에 `/object/...`와 `/upload/resumable`을 붙인다.
- `GPK_SUPABASE_KEY`는 `sb_secret_`로 시작해야 한다. 다른 형식의 key(publishable, legacy `anon`, 사용자 JWT 등)는 네트워크 요청 전에 거부한다. `sb_secret_...` key는 `service_role`로 동작해 RLS를 우회하므로 Storage policy가 필요 없지만 프로젝트 전체에 강한 권한을 가진다.
- API key는 `apikey` 요청 헤더로만 전달한다. `sb_secret_...` key는 JWT가 아니므로 `Authorization: Bearer`에는 넣지 않는다.
- `GPK_SUPABASE_BUCKET`은 이 단계에서는 비어 있지 않은지만 확인한다. 문자·세그먼트 규칙은 [원격 경로 허용 문자 규칙](#원격-경로-허용-문자-규칙)에서 다른 모든 원격 경로와 함께 검사한다.

## `--env-file` 우선순위

- 프로세스 환경 변수를 먼저 읽는다.
- `--env-file`을 지정하면 그 파일에 있는 같은 이름의 값이 프로세스 환경 변수를 덮어쓴다. 파일에 없는 이름은 프로세스 환경 변수 값을 그대로 쓴다.
- `--env-file` 경로가 없으면 아무것도 하지 않고 중단한다.
- 파일은 줄마다 `KEY=VALUE` 하나를 적는다. 빈 줄과 `#`으로 시작하는 줄은 건너뛰고, 키와 값의 앞뒤 공백은 제거하며, 첫 `=`까지가 키다. 위 세 이름 외의 키는 무시한다.
- 환경 변수 파일은 key를 담으므로 저장소에 커밋하지 않는다.

## key 노출 금지

`GPK_SUPABASE_KEY` 값은 표준 출력·표준 에러·예외 메시지 어디에도 나타나지 않는다. 실패 메시지는 값이 아니라 이름만 가리킨다. Storage 실패 진단에도 API key, 원시 요청·응답 헤더는 포함하지 않는다.

## 원격 경로 허용 문자 규칙

첫 Storage 호출 전에 버킷 이름, 현재 매니페스트가 참조하는 **모든** 산출물 `name`, `manifests/<releaseVersion>.json` 경로를 한 번에 검사한다. 이번 실행에서 건너뛸 산출물도 새 세대 매니페스트가 참조하므로 검사 대상이다.

- 원격 객체 경로는 `/`로 구분한 비어 있지 않은 세그먼트로 구성한다.
- 각 세그먼트에는 ASCII 영문 대소문자, 숫자, `.`, `_`, `-`만 허용한다.
- 세그먼트 `.`과 `..`, 빈 세그먼트, 공백, 한글을 포함한 비 ASCII 문자, URL 예약문자, 역슬래시는 거부한다.
- 버킷은 `/`가 없는 단일 세그먼트로 같은 문자 규칙을 적용한다.
- Supabase가 허용하는 파일 이름보다 의도적으로 좁은 규칙을 사용해 소비 측 URL 조합을 단순하게 유지한다.

버킷과 원격 경로 중 잘못된 항목이 하나라도 있으면 모든 오류를 한 번에 나열하고 Storage client를 호출하지 않은 채 중단한다.

```text
원격 경로가 올바르지 않습니다.
버킷 이름이 올바르지 않습니다: bad bucket
산출물 경로가 올바르지 않습니다: files/그룹 이름/1/a.v1.0
```

이 허용 문자 제한은 `gpk upload`의 원격 경로 선검증에만 적용한다. `gpk build`의 기존 Git 추적 파일·정규화 상대 경로 규칙은 바꾸지 않으므로, 허용 범위 밖의 이름이 있으면 빌드는 성공할 수 있지만 업로드는 네트워크 호출 전에 실패한다.

## 로컬 업로드 대상 검증

1. `--output`의 `manifest.json`을 읽고 `gpk verify`와 같은 관계 검증을 수행한다. 매니페스트가 없거나 위반이 있으면 아무것도 올리지 않고 중단한다.
2. 올릴 대상은 매니페스트가 참조하는 산출물이다. `archive`와 `source: file` 엔트리의 `name`을 올리고, `source: archive` 엔트리는 아카이브 안에 있으므로 따로 올리지 않는다.
3. 각 산출물의 로컬 존재 여부, 실제 크기 대 `storedSize`, 산출물당 1 GiB 상한을 먼저 확인한다. 하나라도 없거나 크기가 다르거나 `storedSize`가 1,073,741,824바이트를 넘으면 아무것도 올리지 않고 중단한다.
4. SHA-256을 다시 계산하지 않는다. 전량 해시는 `gpk verify`가 담당하며, 배포 직전 검증이 필요하면 `gpk verify`를 먼저 실행한다.

## 로컬 업로드 성공 상태

`<output>/.gpk-upload-state.json`에 마지막으로 업로드를 완료한 `manifest.json` 전체를 그대로 보관한다. 별도 상태 스키마는 두지 않는다.

- 성공 상태가 없거나 읽을 수 없으면(JSON 형식 오류, 스키마 위반 등) 현재 매니페스트가 참조하는 산출물을 모두 업로드 대상으로 고른다. 이 상태만으로 실제 첫 배포라고 추론하거나 기존 세대 매니페스트의 덮어쓰기를 허용하지 않는다.
- 성공 상태가 있으면 현재 매니페스트의 산출물 중 성공 상태에 같은 `name`이 없는 것만 올린다. 같은 이름의 원격 객체는 같은 바이트라는 `build` 산출물 계약과 단일 게시자 전제를 신뢰하고 원격 바이트를 추가로 조회하지 않는다.
- 로컬 상태는 산출물 업로드 대상을 계산하는 유일한 기준이다. 원격 산출물은 조회하지 않으며, 원격 세대 매니페스트는 create-only 중복 충돌을 판정할 때만 조회한다.
- 모든 원격 업로드(선택 산출물 upsert + 세대 매니페스트 create-or-verify)가 성공한 뒤에만 현재 `manifest.json`을 같은 디렉터리의 임시 파일에 완전히 쓰고 `.gpk-upload-state.json`으로 원자적으로 교체한다.
- 원격 업로드나 로컬 상태 교체가 실패하면 기존 성공 상태를 그대로 유지하고 0이 아닌 종료 코드로 끝난다. 다음 실행은 이전 성공 상태에서 대상을 다시 계산하고 이미 올라간 산출물도 upsert하며, 동일한 세대 매니페스트는 바이트 비교 후 재사용한다.
- 성공 상태 파일에는 현재 매니페스트만 있고 API key, URL, 버킷 이름은 기록하지 않는다. 하나의 `--output`은 하나의 고정된 Supabase 프로젝트·버킷에만 연결된다는 운영 전제를 따른다.

## 업로드 순서와 세대 불변성

1. 로컬 성공 상태와 비교해 선택한 산출물을 먼저 이름 순서대로 `UploadOrResume` + `FileOptions.Upsert=true`로 올린다.
2. 모든 산출물이 성공한 뒤 `manifests/<releaseVersion>.json`을 일반 업로드 + `FileOptions.Upsert=false`로 생성한다.
3. 두 단계가 모두 성공한 뒤에만 로컬 `.gpk-upload-state.json`을 교체한다.

세대 매니페스트가 이미 존재해 중복 객체 오류가 나면 기존 원격 파일을 다운로드해 현재 `manifest.json`과 바이트 단위로 비교한다.

- 같으면 이미 게시된 세대로 재사용하고 정상 종료한다.
- 다르면 기존 객체를 바꾸지 않은 채 버전 충돌로 중단한다(`세대 매니페스트가 이미 다른 내용으로 존재합니다: <경로>`).
- 중복 이외의 업로드·다운로드 오류는 원격 파일을 조회하지 않고 그대로 실패 처리한다.

중복 객체 오류는 다음 응답으로만 판정하며, 일반적인 `exists` 부분 문자열 매칭은 사용하지 않는다.

- HTTP 409이며 `Supabase.Storage`가 `FailureHint.Reason.AlreadyExists`로 분류한 응답
- HTTP 409이며 응답 JSON의 `code`가 `ResourceAlreadyExists` 또는 `KeyAlreadyExists`인 응답
- legacy HTTP 400이며 응답 JSON의 `message` 또는 공백을 제거한 원문이 `Asset Already Exists`와 정확히 일치하는 응답

게시된 세대 경로의 바이트는 절대 바뀌지 않는다. 새 내용은 항상 증가한 `releaseVersion`의 새 경로로 게시하므로 `manifests/` 아래에 이전 세대와 새 세대가 함께 누적된다. 원격 루트 `manifest.json`은 올리지 않는다. `FileOptions.ContentType`은 아카이브·파일 객체에 `application/octet-stream`, 세대 매니페스트에 `application/json`을 사용한다.

## 성공 출력

모두 성공하면 산출물 집계와 세대 매니페스트를 분리해 출력하고 종료 코드 `0`으로 끝난다.

```text
업로드 산출물: uploaded=2, uploadedBytes=12, skipped=0
세대 매니페스트: releaseVersion=0
```

- `uploaded`/`uploadedBytes`: 이번 실행에서 올린 산출물 수와 총 바이트
- `skipped`: 로컬 성공 상태에 이미 있어 건너뛴 산출물 수
- `releaseVersion`: 게시한 세대 매니페스트 번호. 지정된 운영 스크립트가 업로드 성공 후 Postgres 버전 포인터에 쓸 값이다.

재실행할 SHA가 필요하면 업로드 전에 기록한 로컬 `manifest.json.sourceCommit`을 사용한다.

## 실패와 진단 메시지

어느 단계든 실패하면 그 뒤의 원격 호출과 로컬 상태 교체를 수행하지 않고 종료 코드 `1`로 끝난다. 이미 올라간 산출물은 다음 실행에서 같은 경로로 다시 upsert된다.

Storage 호출이 실패하면 표준 에러에 실패 단계, 원격 객체 경로, SDK 예외 타입과 확인 가능한 HTTP 상태·Supabase 오류 코드·메시지를 출력한다.

```text
Storage 요청이 실패했습니다. stage=artifact-upsert, remotePath=files/group/1/a.v1.0, exceptionType=SupabaseStorageException, statusCode=500, errorMessage=internal error
```

- `stage`는 `artifact-upsert`, `manifest-create`, `manifest-download` 중 하나다.
- 원시 요청·응답 헤더, API key, 환경 변수 파일 내용과 전체 원시 응답 본문은 출력하지 않는다.
- `SupabaseStorageException` 본문이 예상 JSON이 아니면 원문 대신 예외 타입·HTTP 상태까지만 남긴다.

로컬 선검증(관계 검증, 산출물 존재·크기·1 GiB 상한, 원격 경로 허용 문자) 실패는 Storage를 한 번도 호출하지 않고 해당 사유만 출력한다.

## 상태 Git 저장소와 동시 실행 금지

릴리스 계보를 지정된 수동 배포 환경에서 이어가려면 소스 저장소와 분리된 private Git 저장소가 필요하다. `gpk upload` 자체는 Git을 호출하지 않으므로, 지정된 배포 스크립트가 다음 운영 전제를 지켜야 한다.

- 상태 Git 저장소는 성공적으로 게시된 `manifest.json`과 `.gpk-upload-state.json` 두 파일만 추적한다. `archives/`·`files/` 산출물은 Git에 넣지 않는다.
- 첫 배포가 아니면 배포 시작 시 상태 저장소의 두 파일을 `--output`에 복원한다. 저장소가 비어 있는 경우는 실제 첫 배포에만 허용한다.
- `gpk upload`와 로컬 성공 상태 기록이 모두 성공한 뒤 두 상태 파일을 **한 Git commit**으로 저장하고 push한다. 그 뒤에만 Postgres 버전 포인터를 갱신한다.
- 같은 `--output`을 사용하는 `build`·`verify`·`upload`는 동시에 실행하지 않는다. CLI는 락이나 동시성 제어를 넣지 않으므로, 지정된 수동 배포 환경 한 곳이 전체 흐름을 직렬화해야 한다.
- 원격 객체를 다른 프로그램이 수정하거나 삭제하지 않는다는 전제를 사용한다.

이 배포 스크립트와 상태 저장소 자체는 `gpk upload` 구현 범위 밖이며, 별도의 코드 관리 배포 스크립트 작업으로 완료해야 한다.

## `sourceCommit` 기반 실패 복구

첫 Storage 호출 전에 실패하면 원격 상태가 바뀌지 않았으므로 수정한 새 commit으로 다시 시작할 수 있다.

첫 Storage 호출이 시작된 뒤 실패하면(산출물 upsert 도중, 세대 매니페스트 생성 도중, 로컬 상태 교체 실패 등) 다음 순서로 복구한다.

1. 실패 시점의 로컬 `manifest.json.sourceCommit`에 기록된 정확한 SHA를 확인한다(배포 스크립트가 첫 Storage 호출 전에 이 값을 기록해 둔다).
2. 그 SHA를 checkout하고 같은 `gpk`·압축 구현 버전으로 `gpk build` → `gpk verify` → `gpk upload`를 다시 실행한다.
3. 이미 올라간 산출물은 같은 이름으로 다시 upsert되고, 이미 생성된 세대 매니페스트는 바이트가 같으면 재사용되므로 안전하게 완료된다.
4. 이 재실행이 상태 Git push와 버전 포인터 갱신까지 끝나기 전에는 더 새로운 source commit을 게시하지 않는다. 상태 commit 또는 push가 실패한 경우도 같은 source commit의 배포를 다시 완료한다.

CLI는 `--commit`·`--rebuild` 옵션을 제공하지 않는다. 재실행 대상은 `manifest.json.sourceCommit`과 Git checkout만으로 재현한다.

## 실제 Supabase 검증

이 문서가 기술하는 동작은 fake storage를 사용한 자동 테스트(`TestUploadCommand`, `TestSupabaseUploadStorage`, `TestUploadSettings`)로 검증됐다. 다음 항목은 실제 Supabase Pro 또는 Team 프로젝트와 자격증명이 필요해 이 CLI 구현 세션에서는 수행하지 않았다. 운영 배포를 시작하기 전에 지정된 배포 환경에서 아래 항목을 실제로 확인해야 한다.

- [ ] Pro 또는 Team 프로젝트에서 전역·버킷 파일 제한을 가장 큰 `storedSize` 이상으로 설정한 뒤 50 MB 초과 ~ 1 GiB 이하 아카이브가 `UploadOrResume`으로 올라가는지 확인
- [ ] 직접 Storage API URL과 `sb_secret_...` key를 `apikey` 헤더로 사용한 실제 인증 성공, 권한 부족 시 key 노출 없이 실패하는지 확인
- [ ] 최초 실행(전량 upsert) → 동일 재실행(전량 건너뛰기, 세대 매니페스트 재사용) → 증분 빌드 후 업로드(새 산출물만 upsert) 순서 확인
- [ ] 업로드 중단 후 같은 `sourceCommit` SHA로 재실행했을 때 완료되는지 확인
- [ ] 서로 다른 `releaseVersion`으로 두 번 업로드해 `manifests/` 아래에 두 세대가 함께 남고 이전 세대 바이트가 바뀌지 않는지, 같은 세대 경로에 다른 바이트를 게시하려 할 때 버전 충돌로 중단하는지 확인
- [ ] 원격 루트 `manifest.json`이 게시되지 않는지 확인
- [ ] 별도 상태 Git 저장소에 성공한 두 상태 파일만 한 commit으로 보존되고 포인터 갱신보다 먼저 push되는 운영 절차 확인
- [ ] 지정된 수동 배포 환경에서 동일 `--output`의 배포가 동시에 시작되지 않도록 운영 스크립트가 직렬화하는지 확인

이 항목들이 검증되기 전까지는 실제 운영 배포를 시작하지 않는다.

## 관련 파일

- [`CommandArguments.cs`](../../src/GamePatchKit.Cli/CommandArguments.cs)
- [`UploadSettings.cs`](../../src/GamePatchKit.Cli/UploadSettings.cs)
- [`UploadStorage.cs`](../../src/GamePatchKit.Cli/UploadStorage.cs)
- [`UploadCommand.cs`](../../src/GamePatchKit.Cli/UploadCommand.cs)
- [`ManifestStore.cs`](../../src/GamePatchKit.Cli/ManifestStore.cs)
- [`RelativePathValidator.cs`](../../src/GamePatchKit.Cli/RelativePathValidator.cs)
- [`Program.cs`](../../src/GamePatchKit.Cli/Program.cs)
- [`game-patch-kit-cli-upload-design.md`](../design/game-patch-kit-cli-upload-design.md)
- [`gamepatch-kit-cli-upload-prd.md`](../prd/gamepatch-kit-cli-upload-prd.md)

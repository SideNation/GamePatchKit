# 프로젝트 초기 설정

새 게임 하나를 GamePatchKit으로 게시하고 내려받을 수 있게 만드는 1회 절차다. 버킷 하나가 게임 하나에 대응하며, 한 Supabase 프로젝트에 여러 버킷을 둘 수 있다.

각 단계의 상세 계약은 해당 명령 문서에 있다. 이 문서는 순서와 프로젝트마다 새로 만들어야 하는 값만 다룬다.

## 준비물

| 항목 | 내용 |
| --- | --- |
| Supabase 프로젝트 | Pro 또는 Team 요금제. Free의 50 MB 전역 파일 제한은 지원 대상이 아니다 |
| project ref | `https://<project-ref>.supabase.co`의 소문자 영문 20자. 프로젝트 이름이 아니다 |
| API key 2종 | 게시용 `sb_secret_...`, 소비용 `sb_publishable_...` |
| 데이터 저장소 | 패치 원본이 들어 있는 Git 저장소 |
| 패치 데이터 폴더 | 산출물과 상태 파일을 두는 폴더. 데이터 저장소 **바깥**이어야 한다 |
| `gpk` | [설치와 배포](cli/distribution.md) 참고 |

## 1. Supabase 준비

프로젝트당 1회 준비한다. 게임별 버킷은 첫 `gpk upload`가 없을 때 공개 버킷으로 생성한다.

1. **파일 크기 제한 조정.** 프로젝트 전역 제한을 가장 큰 산출물의 `storedSize` 이상으로 올린다. 기존 버킷에 별도 제한이 있으면 그 제한도 함께 조정한다. `gpk upload`는 제한을 만들거나 조회하지 않으며, 별도로 산출물 하나를 1 GiB 이하로 제한한다.
2. **포인터 테이블 생성.** 아래 SQL을 **프로젝트의 버전 관리되는 마이그레이션으로** 1회 적용한다. 운영자가 만들며 `gpk`는 테이블을 만들지 않는다.

| 항목 | 조건 |
| --- | --- |
| 포인터 테이블 | 아래 SQL을 1회 실행. **GRANT까지 전부 실행해야 한다** |
| 버킷 | 첫 `gpk upload`가 없으면 공개 버킷으로 생성한다. 기존 버킷 설정은 바꾸지 않는다 |
| 자격증명 | 소비 측은 읽기용 publishable key만 필요하다. secret key를 소비 머신에 두지 않는다 |
| 게시 순서 | 배포 스크립트가 상태 Git push까지 끝낸 뒤에 포인터를 갱신해야 한다 |

```sql
create table public.gamepatch_pointer (
  bucket text primary key,
  release_version bigint not null check (release_version >= 0),
  updated_at timestamptz not null default now()
);

alter table public.gamepatch_pointer enable row level security;

-- GRANT와 RLS는 별개 계층이다. 2026-05-30 이후 만든 프로젝트는 public 스키마의 새 테이블에 자동
-- 권한을 주지 않으므로, 명시하지 않으면 RLS에 닿기도 전에 42501 permission denied로 거부된다.
grant select on public.gamepatch_pointer to anon;                         -- gpk sync (publishable key)
grant select, insert, update on public.gamepatch_pointer to service_role; -- 배포 스크립트 (secret key)

-- 읽기만 공개한다. 쓰기 정책은 만들지 않는다 - 갱신은 배포 스크립트가 secret key로만 한다.
create policy gamepatch_pointer_read on public.gamepatch_pointer
  for select to anon using (true);
```

3. **seed 행을 넣지 않는다.** `releaseVersion` 0이 실제 첫 세대이므로 미리 0을 넣으면 아직 게시되지 않은 세대를 가리키게 된다. 첫 행은 첫 게시 때 배포 스크립트가 만든다.

테이블은 프로젝트당 하나이고 버킷마다 행이 하나씩 생긴다. 버킷을 추가할 때 테이블을 다시 만들지 않는다.

## 2. 빌드 설정 작성

기본 설정은 데이터 저장소 루트의 `gamepatchkit.yml`이다. 여러 데이터 source가 설정을 공유하면 같은 Git 저장소의 데이터 그룹 밖에 설정 파일을 두고 `gpk build --config <설정 파일>`로 선택한다. 설정은 커밋된 일반 파일이어야 하며 심볼릭 링크는 사용할 수 없다.

```yaml
groups:
  - id: data/maps          # 통째로 묶어 한 아카이브로
  - id: data/config        # 파일 단위로 개별 객체
    packing: file
    compression: none
  - id: data/audio
    version: 1             # 그룹을 통째로 다시 만들 때 올린다
```

| 키 | 필수 | 기본값 | 규칙 |
| --- | --- | --- | --- |
| `groups` | 필수 | 없음 | 키가 없으면 빌드가 중단된다. `groups: []`는 오류는 아니지만 아무 산출물도 만들지 않는다 |
| `groups[].id` | 필수 | 없음 | source 기준 정규화된 상대 경로. 저장소 루트(`.`)는 쓸 수 없고 중복도 안 된다 |
| `groups[].version` | 선택 | `0` | 0 이상의 정수 |
| `groups[].packing` | 선택 | `group` | `group`(묶어서 아카이브) 또는 `file`(파일마다 객체) |
| `groups[].compression` | 선택 | `zstd` | `zstd` 또는 `none` |

자주 통째로 바뀌거나 항상 전부 필요한 데이터는 `group`, 일부만 자주 바뀌는 데이터는 `file`이 유리하다. 이미 압축된 포맷(예: 오디오·동영상)은 `compression: none`으로 둔다.

## 3. 패치 데이터 폴더를 상태 저장소로 만들기

산출물과 함께 `manifest.json`, `.gpk-upload-state.json`을 보존하는 **private Git 저장소**가 프로젝트마다 하나 필요하다. 이 두 파일이 없으면 다음 배포가 증분으로 진행되지 못한다.

```shell
cd <패치 데이터 폴더>
git init -b main
git remote add origin <private 저장소 주소>
printf '/archives/\n/files/\n.*.tmp\n' > .gitignore
git add .gitignore
git commit -m "chore: 상태 저장소 초기화"
git push -u origin main
```

기본 예시는 산출물을 `.gitignore`로 제외한다. 완전한 로컬 복원이 필요하면 전체 output을 추적할 수 있다. 자세한 내용은 [상태 저장소 설정](cli/upload.md#상태-저장소-설정)에 있다.

## 4. env 파일 만들기

게시용과 소비용은 쓰는 값이 다르다. 각각 별도 파일로 두고 **저장소에 커밋하지 않는다.**

게시 머신 (`gpk upload`):

```ini
GPK_SUPABASE_STORAGE_URL=https://<project-ref>.storage.supabase.co/storage/v1
GPK_SUPABASE_SECRET_KEY=sb_secret_...
GPK_SUPABASE_BUCKET=<버킷 이름>
```

소비 머신 (`gpk sync`):

```ini
GPK_SUPABASE_PROJECT_URL=https://<project-ref>.supabase.co
GPK_SUPABASE_PUBLISHABLE_KEY=sb_publishable_...
GPK_SUPABASE_BUCKET=<버킷 이름>
```

> **프로젝트마다 세 값을 모두 적는다.** `--env-file`은 프로세스 환경 변수를 덮어쓰는 것이지 대체하지 않는다. `GPK_SUPABASE_BUCKET`을 전역에 export해 둔 상태에서 그 이름이 빠진 env 파일로 실행하면 조용히 다른 프로젝트의 버킷에 게시되거나 그 버킷에서 동기화된다.

`sb_secret_...` key는 `service_role`로 동작해 RLS를 우회하므로 소비 머신에 두지 않는다.

## 5. 첫 게시

```shell
gpk build  --source <데이터 루트> --output <패치 데이터 폴더>
gpk verify --output <패치 데이터 폴더>
gpk upload --output <패치 데이터 폴더> --env-file <게시용 env>

cd <패치 데이터 폴더>
git add manifest.json .gpk-upload-state.json
git commit -m "release: releaseVersion 0"
git push
```

push까지 끝난 **뒤에** 포인터를 갱신한다. 이 순서를 지켜야 "포인터가 N이면 그 세대가 전부 게시돼 있다"는 소비 측 전제가 성립한다. 포인터 갱신은 `gpk`가 하지 않으므로 프로젝트의 배포 스크립트가 secret key로 수행한다.

```sql
insert into public.gamepatch_pointer (bucket, release_version, updated_at)
values ('<버킷 이름>', <releaseVersion>, now())
on conflict (bucket) do update
set release_version = excluded.release_version,
    updated_at = now();
```

`updated_at`은 갱신 시 자동으로 바뀌지 않으므로 위처럼 명시해야 운영 화면에서 마지막 게시 시각을 볼 수 있다.

두 번째 배포부터는 상태 파일을 먼저 복원한다.

```shell
git -C <패치 데이터 폴더> pull --ff-only
```

전체 절차와 실패 복구는 [배포 1회 절차](cli/upload.md#배포-1회-절차)에 있다.

## 6. 패치 버전 조회 함수 배포 (선택)

클라이언트가 DB를 직접 조회하지 않고 현재 버전을 묻게 하려면 Edge Function을 배포한다.

```shell
gpk deploy-function --project-id <project-ref> --access-token <Supabase Access Token>
```

- Access Token은 [대시보드 Account → Access Tokens](https://supabase.com/dashboard/account/tokens)에서 발급하고 `edge_functions_write`(OAuth는 `edge_functions:write`) 권한이 필요하다. publishable key나 secret key로 대체되지 않는다.
- 2단계의 포인터 테이블이 먼저 적용돼 있어야 한다. 이 명령은 DB·버킷·포인터 데이터를 만들지 않는다.
- 함수는 프로젝트당 한 번 배포하면 그 프로젝트의 모든 버킷을 처리한다. 호출자는 API key 없이 `bucket`만 넘긴다.

자세한 계약은 [`gpk deploy-function`](cli/deploy-function.md)에 있다.

## 7. 클라이언트 연결

| 소비 측 | 방법 |
| --- | --- |
| 서버·PC (CLI) | [`gpk sync`](cli/sync.md)를 스케줄러에 걸고 소비용 env 파일을 지정한다 |
| Unity | [Unity 패치 클라이언트](unity/patch-client.md) UPM 패키지를 Git URL로 설치한다 |

## 확인

- [ ] 첫 업로드 뒤 버킷이 공개이고 적용되는 파일 크기 제한이 가장 큰 산출물보다 크다
- [ ] `select * from public.gamepatch_pointer`가 첫 게시 후 행 하나를 반환한다
- [ ] 기본 `gamepatchkit.yml` 또는 `--config`로 지정할 설정 파일이 데이터 저장소에 커밋돼 있다
- [ ] 상태 저장소에 `manifest.json`과 `.gpk-upload-state.json`이 push돼 있다
- [ ] env 파일 2종에 각각 세 값이 모두 있고 저장소에 커밋되지 않았다
- [ ] 소비 측에서 `gpk sync` 또는 Unity 클라이언트가 첫 세대를 받아 원본 트리를 복원한다

## 관련 문서

- [`gpk build`](cli/build.md)
- [`gpk verify`](cli/verify.md)
- [`gpk upload`](cli/upload.md)
- [`gpk sync`](cli/sync.md)
- [`gpk deploy-function`](cli/deploy-function.md)
- [CLI 설치와 배포](cli/distribution.md)
- [Unity 패치 클라이언트](unity/patch-client.md)

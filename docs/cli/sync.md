# `gpk sync`

## 개요

`gpk sync`는 Supabase Postgres의 릴리스 버전 포인터를 읽고, 로컬 폴더가 그 세대와 다르면 공개 Storage에서 세대 매니페스트와 없는 산출물만 받아 로컬을 게시된 세대로 맞춘다. 소비 측 명령이며 게시 상태를 바꾸지 않는다.

- 스케줄러가 주기적으로 돌리는 것과 사람이 강제로 돌리는 것이 **같은 명령**이다. 별도의 강제 옵션이 없다.
- 같은 폴더에 대한 동시 실행은 배타 파일 락으로 한 개만 진행하고 나머지는 조용히 건너뛴다.
- 포인터와 로컬이 **다르면** 동기화한다. 크면뿐 아니라 작으면도 포함하므로 롤백이 같은 경로로 처리된다.

## 사전 조건

포인터 테이블은 운영자가 1회 만든다. `gpk`는 테이블을 만들지 않는다.

| 항목 | 조건 |
| --- | --- |
| 포인터 테이블 | 아래 SQL을 Supabase SQL Editor에서 1회 실행. **GRANT까지 전부 실행해야 한다** |
| 버킷 | 공개 버킷. 소비 측은 키 없이 객체를 받는다 |
| 자격증명 | 읽기용 publishable key만 필요하다. secret key를 소비 머신에 두지 않는다 |
| 게시 순서 | 배포 스크립트가 상태 Git push까지 끝낸 뒤에 포인터를 갱신해야 한다 |

```sql
create table public.gamepatch_pointer (
  bucket text primary key,
  release_version integer not null check (release_version >= 0),
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

seed 행은 넣지 않는다. `releaseVersion` 0이 실제 첫 세대이므로 미리 0을 넣으면 아직 게시되지 않은 세대를 가리키게 된다. 첫 게시 때 배포 스크립트가 행을 만든다.

## 사용법

```shell
gpk sync --output <동기화 대상 폴더> [--env-file <환경 변수 파일>]
```

- `--output`은 필수다. 폴더가 없으면 만든다.
- 이 폴더는 소비 머신의 로컬 미러이며, 게시자의 `--output`(상태 Git 저장소)과 다른 폴더다.
- 하나의 동기화 폴더는 하나의 프로젝트·버킷에만 연결한다.

## 설정 값

업로드용 설정과 분리돼 있다. `GPK_SUPABASE_URL`과 `GPK_SUPABASE_KEY`는 sync에서 쓰지 않는다.

| 이름 | 형식 | 의미 |
| --- | --- | --- |
| `GPK_SUPABASE_PROJECT_URL` | `https://<project-ref>.supabase.co` | 경로·포트·쿼리가 없어야 한다 |
| `GPK_SUPABASE_PUBLISHABLE_KEY` | `sb_publishable_`로 시작 | 포인터 조회용 읽기 key |
| `GPK_SUPABASE_BUCKET` | 영숫자와 `.`, `_`, `-` | 공개 버킷 이름이자 포인터 행의 키 |

환경 변수로 주거나 `--env-file`로 파일을 지정한다. 같은 이름이 양쪽에 있으면 파일 값이 이긴다.

```shell
export GPK_SUPABASE_PROJECT_URL=https://<project-ref>.supabase.co
export GPK_SUPABASE_PUBLISHABLE_KEY=sb_publishable_...
export GPK_SUPABASE_BUCKET=<버킷 이름>
gpk sync --output /srv/gamedata
```

업로드용 Storage API URL(`https://<ref>.storage.supabase.co/storage/v1`)을 `GPK_SUPABASE_PROJECT_URL`에 넣는 것이 가장 흔한 실수이며, 형식 검사에서 걸러진다.

## 주기 실행과 강제 실행

주기는 스케줄러가 맡는다. CLI는 상주하지 않는다.

```text
*/5 * * * *  /usr/local/bin/gpk sync --output /srv/gamedata --env-file /etc/gpk/sync.env
```

게시 직후 바로 반영하려면 같은 명령을 손으로 실행하면 된다. 별도 옵션이 필요 없고, 스케줄 실행과 겹쳐도 안전하다.

```shell
gpk sync --output /srv/gamedata --env-file /etc/gpk/sync.env
```

`<output>/.gpk-sync.lock`을 배타 락으로 잡아 동시 실행을 막는다. 체크와 획득이 한 번에 일어나고 프로세스가 죽으면 OS가 해제하므로, 진행 중 표시 파일과 달리 경합이나 stale 상태가 없다. 락 파일 자체는 지우지 않는다.

락을 잡지 못하면 원격을 한 번도 호출하지 않고 종료 코드 0으로 끝난다. cron 겹침은 정상 상황이라 실패로 알리지 않으며, 놓친 변경은 다음 실행이 따라잡는다.

## 동작 순서

1. `.gpk-sync.lock` 배타 락을 잡는다. 실패하면 건너뛴다.
2. 포인터를 읽는다.
3. 로컬 `manifest.json`의 `releaseVersion`이 포인터와 같으면 아무것도 받지 않고 끝낸다.
4. `manifests/<포인터>.json`을 받아 검증한다. 그 안의 `releaseVersion`이 포인터와 다르면 중단한다.
5. 매니페스트가 참조하는 산출물 중 로컬에 없거나 크기가 다른 것만 이름 순서로 받는다.
6. 받는 동안 SHA-256을 계산해 `storedSize`·`checksum`과 대조하고, 통과한 것만 최종 이름으로 옮긴다.
7. 전부 성공한 뒤에만 `manifest.json`을 원자적으로 교체한다.

7단계가 마지막이므로 어느 지점에서 멈춰도 로컬 `manifest.json`이 가리키는 세대는 그대로 완전하다. 받다 만 임시 파일은 참조되지 않으며 실패 시 삭제된다. 로컬 무결성이 의심되면 [`gpk verify`](verify.md)를 같은 폴더에 실행하면 된다.

교체하는 매니페스트는 받은 바이트 그대로다. 동기화가 끝난 폴더의 `manifest.json`은 원격 `manifests/<releaseVersion>.json`과 바이트까지 같다.

## 성공 출력

```text
동기화 완료: releaseVersion=7 (이전: 6), downloaded=3, reused=12, downloadedBytes=48213
```

```text
이미 최신입니다: releaseVersion=7
```

```text
다른 sync가 진행 중입니다. 건너뜁니다.
```

세 경우 모두 종료 코드 0이다.

## 실패와 재실행

실패하면 원인을 출력하고 종료 코드 `1`로 끝낸다. 재시도·백오프는 넣지 않는다. 다음 스케줄 실행이 재시도 역할을 하며, 이미 받아 둔 산출물은 다시 받지 않는다.

| 메시지 | 원인 | 조치 |
| --- | --- | --- |
| `포인터 테이블 'gamepatch_pointer'이 없습니다` | 사전 조건 SQL 미실행 | 위 SQL을 1회 실행 |
| `bucket '<b>'의 포인터 행이 없습니다` | 아직 첫 게시 전이거나 버킷 이름이 다름 | 게시 여부와 `GPK_SUPABASE_BUCKET` 확인 |
| `포인터를 읽을 권한이 없습니다` | key, `anon`의 `select` GRANT, 또는 읽기 RLS 정책 문제 | 사전 조건 SQL을 **전부** 실행했는지 확인. 2026-05-30 이후 만든 프로젝트는 새 테이블에 자동 권한이 없어 GRANT를 빠뜨리면 `42501`로 거부된다 |
| `게시된 객체가 없습니다: <경로>` | 포인터가 완전히 게시되지 않은 세대를 가리킴 | 게시 순서 위반. 포인터를 이전 값으로 되돌리고 게시를 마저 완료 |
| `받은 산출물의 checksum이 다릅니다` | 전송 손상 또는 원격 객체 변조 | 재실행. 반복되면 해당 객체를 다시 게시 |
| `세대 매니페스트의 releaseVersion이 포인터와 다릅니다` | 세대 경로에 다른 세대의 매니페스트가 올라감 | 게시 산출물 점검 |

실패 메시지에는 key를 출력하지 않는다.

## 관련 파일

- [`SyncCommand.cs`](../../src/GamePatchKit.Cli/SyncCommand.cs)
- [`SyncRemote.cs`](../../src/GamePatchKit.Cli/SyncRemote.cs)
- [`SyncSettings.cs`](../../src/GamePatchKit.Cli/SyncSettings.cs)
- [`ManifestStore.cs`](../../src/GamePatchKit.Cli/ManifestStore.cs)
- [`Program.cs`](../../src/GamePatchKit.Cli/Program.cs)
- [`game-patch-kit-cli-sync-design.md`](../design/game-patch-kit-cli-sync-design.md)

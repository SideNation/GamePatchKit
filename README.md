# GamePatchKit CLI

Git이 추적하는 게임 데이터를 버전별 패치 아카이브와 파일 객체로 만들고, 산출물의 무결성을 검사하고, Supabase Storage에 게시하고, 게시된 세대를 다시 내려받는 .NET tool이다.

## 설치

.NET 10 SDK로 설치한다. 설치된 tool을 실행하는 환경에는 .NET 10 런타임이 필요하다.

```shell
dotnet tool install --global GamePatchKit.Cli
```

설치 후 명령 이름은 `gpk`다.

.NET 런타임 없이 실행하려면 [GitHub Releases](https://github.com/SideNation/GamePatchKit/releases)에서 플랫폼별 self-contained 아카이브를 내려받는다.

## 빌드

Git 저장소의 데이터 루트에 `gamepatchkit.yml`을 만든다.

```yaml
groups:
  - id: content
    version: 1
    packing: group
    compression: zstd
```

패치 출력 폴더는 source가 속한 Git 저장소 바깥에 지정한다.

```shell
gpk build --source ./data --output ../patches
gpk verify --output ../patches
```

## 업로드

빌드한 패치 데이터를 Supabase Storage 버킷에 올린다. 대상 버킷과 인증 정보는 환경 변수로 전달한다.

```shell
export GPK_SUPABASE_STORAGE_URL=https://<project-ref>.storage.supabase.co/storage/v1
export GPK_SUPABASE_SECRET_KEY=sb_secret_...
export GPK_SUPABASE_BUCKET=<버킷 이름>
gpk upload --output ../patches
```

## 동기화

게시된 세대를 CLI가 설치된 머신의 폴더로 내려받는다. 릴리스 버전 포인터(Supabase Postgres)를 읽어 로컬과 다를 때만 바뀐 산출물을 받는다. 읽기용 key만 쓰며 게시 상태를 바꾸지 않는다.

```shell
export GPK_SUPABASE_PROJECT_URL=https://<project-ref>.supabase.co
export GPK_SUPABASE_PUBLISHABLE_KEY=sb_publishable_...
export GPK_SUPABASE_BUCKET=<버킷 이름>
gpk sync --output /srv/gamedata
```

주기 실행은 스케줄러에 맡기고, 게시 직후 바로 반영하려면 같은 명령을 손으로 실행한다. 동시 실행은 배타 락으로 하나만 진행한다.

```text
*/5 * * * *  gpk sync --output /srv/gamedata --env-file /etc/gpk/sync.env
```

포인터 테이블은 최초 1회 만들어야 한다. SQL은 [`gpk sync` 문서](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/sync.md)에 있다.

자세한 내용은 다음 문서를 참고한다.

- [설치와 배포](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/distribution.md)
- [`gpk build`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/build.md)
- [`gpk verify`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/verify.md)
- [`gpk upload`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/upload.md)
- [`gpk sync`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/sync.md)

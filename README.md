# GamePatchKit CLI

Git이 추적하는 게임 데이터를 버전별 패치 아카이브와 파일 객체로 만들고, 산출물의 무결성을 검사하고, Supabase Storage에 게시하는 .NET tool이다.

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
export GPK_SUPABASE_URL=https://<project-ref>.storage.supabase.co/storage/v1
export GPK_SUPABASE_KEY=sb_secret_...
export GPK_SUPABASE_BUCKET=<버킷 이름>
gpk upload --output ../patches
```

자세한 내용은 다음 문서를 참고한다.

- [설치와 배포](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/distribution.md)
- [`gpk build`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/build.md)
- [`gpk verify`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/verify.md)
- [`gpk upload`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/upload.md)

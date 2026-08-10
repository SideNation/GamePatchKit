# GamePatchKit CLI

Git이 추적하는 게임 데이터를 버전별 패치 아카이브와 파일 객체로 만들고, 배포 전 산출물의 무결성을 검사하는 .NET tool이다.

## 설치

.NET 10 SDK로 설치한다. 설치된 tool을 실행하는 환경에는 .NET 10 런타임이 필요하다.

```shell
dotnet tool install --global GamePatchKit.Cli
```

설치 후 명령 이름은 `gpk`다.

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

자세한 내용은 다음 문서를 참고한다.

- [설치와 배포](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/distribution.md)
- [`gpk build`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/build.md)
- [`gpk verify`](https://github.com/SideNation/GamePatchKit/blob/main/docs/cli/verify.md)

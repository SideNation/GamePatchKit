# CLI 설치와 배포

## 개요

GamePatchKit CLI는 nuget.org의 `GamePatchKit.Cli` 패키지와 GitHub Release의 self-contained 실행파일로 배포한다. .NET tool은 Windows x64, Linux x64, macOS Apple Silicon에서 동일한 패키지를 설치하며 명령 이름은 `gpk`다.

## 설치

.NET 10 SDK가 설치된 환경에서 다음 명령으로 tool을 설치한다. 설치된 tool을 실행하는 환경에는 .NET 10 런타임이 필요하다. `gpk build`에는 Git CLI도 필요하지만 `gpk verify`는 Git 없이 실행할 수 있다.

```shell
dotnet tool install --global GamePatchKit.Cli
gpk build --source <데이터 루트> --output <패치 데이터 폴더>
```

로컬에서 만든 패키지를 확인할 때는 별도 tool path에 설치한다.

```shell
dotnet pack src/GamePatchKit.Cli/GamePatchKit.Cli.csproj --configuration Release --output artifacts
dotnet tool install GamePatchKit.Cli --tool-path <임시 tool 폴더> --add-source artifacts --version <패키지 버전>
```

## 독립 실행파일

.NET 런타임을 별도로 설치하지 않을 환경에서는 GitHub Release에서 플랫폼에 맞는 아카이브를 내려받아 `gpk`를 실행한다.

| 플랫폼 | Release asset | 실행파일 |
| --- | --- | --- |
| Windows x64 | `GamePatchKit.Cli-<버전>-win-x64.tar.gz` | `gpk.exe` |
| Linux x64 | `GamePatchKit.Cli-<버전>-linux-x64.tar.gz` | `gpk` |
| macOS Apple Silicon | `GamePatchKit.Cli-<버전>-osx-arm64.tar.gz` | `gpk` |

각 아카이브는 .NET 런타임과 네이티브 zstd 라이브러리를 포함한 self-contained single-file 실행파일 하나를 담는다. Linux와 macOS 아카이브는 실행 권한을 보존하기 위해 `tar.gz` 형식을 사용한다.

현재 standalone 실행파일에는 별도의 플랫폼 코드 서명·공증을 적용하지 않는다. 특히 macOS 자산은 ad-hoc 서명만 포함하므로 GitHub에서 내려받은 파일을 Gatekeeper가 차단할 수 있다. Apple Developer ID 서명·공증을 도입하기 전까지 macOS에서는 위의 .NET tool 설치 방식을 우선 사용한다.

## GitHub Actions 검증

일반 push와 pull request에서는 다음 세 환경이 각각 restore, test, pack, 로컬 tool 설치, zstd smoke build를 수행한다. 이어서 같은 환경의 self-contained single-file 실행파일을 만들고 그 실행파일로 smoke build를 한 번 더 검증한다.

- `windows-latest`: `win-x64`
- `ubuntu-latest`: `linux-x64`
- `macos-26`: `osx-arm64`

각 환경의 테스트는 zstd 산출물을 원본으로 해제해 확인한다. smoke build 결과는 별도 job에서 매니페스트와 아카이브 SHA-256을 비교한다.

## NuGet 게시

저장소 secret `NUGET_API_KEY`를 등록하고 `v<NuGet version>` 형식의 GitHub Release를 게시한다. 예를 들어 `v1.2.3` Release는 `GamePatchKit.Cli` 버전 `1.2.3`을 만든다.

세 환경 검증과 결과 비교가 모두 성공한 경우에만 패키지를 다시 만들고 nuget.org에 게시한다. 같은 버전이 이미 있으면 `--skip-duplicate`로 건너뛴다.

같은 GitHub Release에는 `.nupkg`와 세 플랫폼의 검증된 standalone 아카이브를 asset으로 첨부한다. Release asset을 쓰기 위해 publish job에 `contents: write` 권한을 부여하며, 인증에는 해당 job의 `GITHUB_TOKEN`만 사용한다.

## 관련 파일

- [dotnet.yml](../../.github/workflows/dotnet.yml)
- [smoke-test.sh](../../.github/scripts/smoke-test.sh)
- [compare-smoke-results.sh](../../.github/scripts/compare-smoke-results.sh)
- [GamePatchKit.Cli.csproj](../../src/GamePatchKit.Cli/GamePatchKit.Cli.csproj)

# GamePatchKit

여러 게임이 엔진과 Storage 제품에 종속되지 않고 게임 데이터를
패키징·배포·다운로드·검증·활성화할 수 있게 하는 범용 도구 모음.

- PRD: [docs/prd/game-patch-kit-prd.md](docs/prd/game-patch-kit-prd.md)
- 개발 계획: [docs/plan/README.md](docs/plan/README.md)

## Build

```bash
./build.sh              # Compile (default)
./build.sh Test          # Run tests
./build.sh Pack          # Create NuGet packages (auto-increments patch version)
./build.sh Push --nuget-api-key <KEY>  # Publish to NuGet (also runs Pack, auto-increments patch version)
```

`Pack`/`Push`는 `GamePatchKit.Core`, `GamePatchKit.Runtime`,
`GamePatchKit.Compression.NativeCompressions`, `GamePatchKit.DotNet` 4개 패키지를
`artifacts/`에 생성한다. `GamePatchKit.Packager`·`GamePatchKit.Cli`는 NuGet으로
배포하지 않는다.

### Version Management

현재 버전은 `Directory.Build.props`에 정의되어 있다. `--version` 없이 `Pack`이나
`Push`를 실행하면 patch 버전을 자동으로 증가시키고 파일을 갱신한다.

```bash
# Auto-increment patch (e.g. 0.1.0 → 0.1.1)
./build.sh Pack
./build.sh Push --nuget-api-key <KEY>

# Specify version explicitly
./build.sh Pack --version 1.0.0
./build.sh Push --nuget-api-key <KEY> --version 1.0.0
```

> **Note:** `Push`는 내부적으로 `Pack`을 실행하므로, `Pack`과 `Push`를 따로 실행하면
> 버전이 두 번 증가한다. Pack과 Push를 한 번에 하려면 `Push`만 실행한다.

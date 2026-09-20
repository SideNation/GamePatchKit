# GamePatchKit CLI 공유 빌드 설정 개발 계획

> 상태: 구현 완료. 기존 구현 대조 기준: `77700ec`. 구현·검증일: 2026-09-20.
>
> 기준: [CLI PRD](../prd/gamepatch-kit-cli-prd.md), [CLI 설계](game-patch-kit-cli-design.md),
> [릴리스 버전 설계](game-patch-kit-cli-release-version-design.md), [build 사용법](../cli/build.md)

## 1. 목적과 범위

`gpk build`에 선택적 `--config <설정 파일>`을 추가해 같은 Git 저장소의 여러 source가 하나의 빌드 설정을 사용할 수 있게 한다.
함께 변경하는 릴리스 규칙은 **완성된 매니페스트의 릴리스 내용이 같다면 Git HEAD만 달라져도 새 세대를 만들지 않는다**는 것이다.

이 문서는 GamePatchKit CLI의 인자, 설정 검증, 빌드 입력, 릴리스 비교, 호환성과 검증만 정의한다.
사용하는 프로젝트의 데이터 복사, 환경 승격, 배포 스크립트, 자격증명 파일 배치, Git 자동화와 포인터 갱신 구현은 범위 밖이다.

```text
source-repository/
├── config/patch.yml
└── datasets/
    ├── alpha/content/
    └── beta/content/
```

source 저장소 루트에서 실행하는 예:

```shell
gpk build --source datasets/alpha --config config/patch.yml --output ../patch-output/alpha
gpk build --source datasets/beta --config config/patch.yml --output ../patch-output/beta
```

설정의 `groups[].id: content`는 각각의 source 아래 `content/`를 가리킨다. CLI에 환경 이름이나 profile 개념은 추가하지 않는다.

## 2. 기존 구현과의 대조 결과

기존 구현과 같은 부분, 의도적으로 바꾼 부분, 설계에서 보완한 부분을 구분한다.

| 항목 | 현재 코드·계약 | 이번 계획의 처리 |
| --- | --- | --- |
| 설정 위치 | `BuildCommand.Execute`와 `GitRepository.EnsureConfigurationTracked`가 source 루트의 고정 파일명을 사용 | 옵션 생략 시 유지. 명시 경로만 선택적으로 허용 |
| 설정 파일의 심볼릭 링크 | tracked 링크를 따라 YAML을 읽어 저장소 밖 대상도 허용될 수 있음 | 의도적 동작 변경. 기본·명시 설정 모두 파일 자체가 심볼릭 링크이면 거부 |
| source 밖 dirty 파일 | `EnsureTrackedSourceClean`이 무시 | 명시적으로 선택한 설정만 추가 검사. 나머지 바깥 파일은 계속 무시 |
| Git 변경 감지 | 이전 `manifest.sourceCommit`과 현재 HEAD의 source 범위 `git diff` | 그대로 유지. 전체 파일 해시 기반 감지로 교체하지 않음 |
| HEAD만 다른 빌드 | `ManifestStore.SerializeForComparison`이 `sourceCommit`을 포함하여 새 릴리스 생성 | 의도적 동작 변경. `--config` 사용 여부와 관계없이 같은 릴리스 내용이면 이전 매니페스트 유지 |
| 매니페스트 쓰기 | 성공 시 항상 `WriteAtomically`로 재직렬화 | 내용이 같으면 쓰기를 생략하여 기존 파일 바이트까지 보존 |
| packing/compression 변경 | 같은 그룹 버전에서는 `ValidateGroupVersions`가 거부 | 유지. 그룹 버전을 먼저 올린 유효한 변경만 새 릴리스 대상 |
| 데이터 탐색 | source 아래 tracked 파일을 그룹 경로로 할당 | 그대로 유지. 설정은 데이터 그룹 밖에 배치하고 별도 제외 로직은 추가하지 않음 |
| output의 source 변경 | 기존 `sourcePath`와 다르면 실패 | 유지. source별 output을 구분해야 함 |
| build와 upload 상태 | build는 `manifest.json`, upload는 `.gpk-upload-state.json` 사용 | 유지. output Git 커밋은 릴리스 계산에 관여하지 않음 |
| 업로드 세대 불변성 | `CreateOrVerifyManifestAsync`가 같은 세대의 바이트 차이를 거부 | 유지. 무변경 빌드에서 `sourceCommit`이나 JSON 바이트를 바꾸면 안 됨 |
| 산출물의 Git 추적 | CLI는 output의 Git 추적 여부를 검사하지 않음. 업로드 문서는 상태 파일만 추적하도록 안내 | CLI 변경 불필요. 산출물 Git 추적 여부를 도구 제한과 분리해 문서화 |
| sync·Unity 소비 | 기존 스키마와 세대 번호로 소비 | 스키마와 소비 계약 유지. 기존 sync 락도 변경하지 않음 |

### 직접 변경되는 기존 회귀 기대값

- [`TestBuildCommand`](../../tests/GamePatchKit.Cli.Tests/TestBuildCommand.cs)의
  `Execute_OnlySourceCommitChanges_IncrementsReleaseVersionWithIdenticalGroupContent`는 현재 버전 증가를 검증한다.
  구현 시 버전·기존 SHA·매니페스트 바이트 보존을 검증하도록 변경한다.
- [`TestManifestStore`](../../tests/GamePatchKit.Cli.Tests/TestManifestStore.cs)의
  `HasSameReleaseContent_ContentFieldDiffers_ReturnsFalse` 중 `sourceCommit` 사례는 같다고 판정하는 테스트로 분리한다.
  `sourcePath`, 그룹, 아카이브와 엔트리 차이에 대한 기존 기대값은 유지한다.
- 따라서 “기존 동작 전체가 동일하다”는 호환성을 약속하지 않는다. 설정 탐색의 기본값과 산출물 형식은 유지하지만,
  HEAD만 달라진 성공 빌드의 릴리스 증가 규칙과 설정 파일 자체의 심볼릭 링크 허용 여부는 의도적으로 바뀐다.

### 구현 전 기준 검증

2026-09-20에 기존 `TestBuildCommand`, `TestManifestStore`, `TestGitRepository`, `TestCommandArguments`,
`TestUploadCommand`, `TestSyncCommand`를 Release 구성으로 실행해 166개 모두 통과했다.
이는 변경 전 기준 동작을 확인한 결과다.

## 3. CLI와 설정 계약

```text
gpk build [--source <데이터 루트>] [--config <설정 파일>] --output <패치 데이터 폴더>
```

- `--source` 생략 시 현재 디렉터리를 사용하는 기존 동작을 유지한다.
- `--config` 생략 시 `<source>/gamepatchkit.yml`을 사용한다.
- `--config`를 지정하면 그 파일만 설정으로 읽는다. source 루트의 기본 설정 파일은 없어도 된다.
- 상대 경로는 source나 설정의 디렉터리가 아닌 프로세스 현재 디렉터리를 기준으로 해석한다.
- 명시 설정의 파일명·확장자는 제한하지 않는다. YAML 문법과 그룹 검증은 기존 `BuildConfigurationLoader.Load`를 재사용한다.
- source별로 다른 설정이 필요하면 데이터 그룹 밖에 별도 파일을 두고 `--config`로 선택한다. CLI가 파일명을 자동 추론하지 않는다.
- 설정 파일은 source와 같은 Git 저장소의 추적 중인 일반 파일이어야 하며 staged·unstaged 변경이 없어야 한다.
- 기본·명시 설정 모두 설정 파일 자체가 심볼릭 링크이면 대상의 위치·추적 여부와 관계없이 거부한다. 끊어진 링크도 허용하지 않는다.
- 설정 경로 검증은 YAML 읽기와 산출물 쓰기 전에 수행한다. 누락, 디렉터리, 미추적, dirty, 저장소 밖 또는 다른 저장소의
  설정이거나 설정 파일 자체가 심볼릭 링크이면 `BuildException`과 종료 코드 1로 실패한다.
- source 아래 tracked 파일의 clean 검사도 그대로 유지한다. 선택한 설정 외 source 밖 변경과 untracked 데이터는 기존처럼 무시한다.
- `groups[].id`는 항상 source 기준이다. 공유 설정의 그룹 버전도 공유되지만 각 output의 이전 그룹 버전과 독립적으로 비교한다.
- 옵션 중복·값 누락·빈 값은 기존 파서 오류 방식으로 처리한다.
- `verify`, `upload`, `sync`, `deploy-function`에 `--config`를 추가하지 않는다.
- `--output`은 source가 속한 Git 저장소 바깥이어야 한다. 설정 공유가 이 제한을 완화하지 않는다.

## 4. Git 경계와 데이터 입력

기존 `GitRepository`에 임의 설정 경로 검증을 연결한다. 단순 문자열 접두사만으로 같은 저장소라고 판정하지 않는다.

1. 기존 경로 정규화 방식을 사용해 source와 선택한 설정의 경로를 확정한다. 디렉터리 링크 해석은 기존 방식을 유지하되,
   설정 파일 자체의 링크는 대상을 따라 읽지 않고 거부한다.
2. 설정이 source 저장소 경계 안에 있고 실제 소속 Git 저장소도 같은지 확인한다. 중첩 저장소·submodule의 설정은 허용하지 않는다.
3. 저장소 기준의 정확한 파일 경로로 tracked·clean을 검사한다. 경로의 glob 문자가 다른 파일을 매칭하지 않게 처리한다.
4. `GetTrackedPaths`가 반환한 source 상대 경로를 기존 규칙 그대로 그룹에 할당한다.

설정은 source 밖 또는 기존 기본 설정처럼 source 루트 등 데이터 그룹에 속하지 않는 위치에 둔다.
설정 파일을 그룹 내부에 배치하는 사용 방식은 이번 요구 범위에 포함하지 않는다. 이를 위한 별도 제외 로직이나 위치 거부 검사는
추가하지 않으며, 파일 탐색·그룹 할당·빈 그룹 검사는 기존 동작을 유지한다.
기본 설정과 명시 설정의 검증을 별도 구현으로 복제하지 않는다. 설정 파일 자체의 심볼릭 링크 거부도 같은 검증 경로에 둔다.

설정 경로를 매니페스트에 기록하지 않으므로 재현 시 해당 릴리스를 만든 source commit과 원래의 `--config` 인자도 보존해야 한다.
설정 경로만 이동한 무변경 빌드는 기존 SHA를 유지하므로 새 경로가 이전 SHA에 존재한다고 보장할 수 없다.
이때는 이전 릴리스 당시의 설정 경로로 재현한다. 이는 CLI 호출자의 책임이며 새 설정 복사본이나 설정 경로 상태 파일을 만들지 않는다.

## 5. 릴리스 판정과 무변경 빌드

### 비교 대상

기존 `ManifestStore.HasSameReleaseContent`의 정렬·직렬화 규칙을 재사용한다.
비교용 사본에서 `releaseVersion`과 `sourceCommit`만 정규화하고 다음은 계속 비교한다.

- `schemaVersion`, `sourcePath`
- 그룹 id, version, packing, compression
- archive와 entries의 모든 필드

설정 파일 경로, YAML 주석이나 표현 방식 자체는 비교 대상이 아니다. 해석된 설정이 만든 릴리스 내용으로 판단한다.
내용 비교는 기존 증분 빌드가 완성한 후보에 적용하며, 원본 바이트만 비교하는 새 변경 감지기를 만들지 않는다.
예를 들어 Git이 변경 경로로 판정해 파일 리비전이 달라졌다면 그 리비전도 릴리스 내용에 포함된다.

### 확정 순서

1. 이전 매니페스트가 없으면 기존처럼 `releaseVersion: 0`과 현재 HEAD로 기록한다.
2. 이전 매니페스트와 후보의 릴리스 내용이 같으면 **기존 manifest.json을 다시 쓰지 않는다**.
   이전 `releaseVersion`, `sourceCommit`과 파일 바이트를 모두 유지한다.
3. 다르면 기존 값에 1을 더하고 현재 HEAD를 기록해 기존 원자적 쓰기로 게시한다.
4. 증가가 필요한데 기존 값이 `long.MaxValue`이면 기존 오류와 매니페스트 보존을 유지한다.
   내용이 같으면 최대값에서도 새 증가 없이 성공할 수 있다.

SHA만 복사한 뒤 재직렬화하는 것으로 바이트 보존을 대신하지 않는다. 유효한 매니페스트는 공백·필드 순서가 다를 수 있고,
기존 `sync`도 원격 매니페스트 바이트를 그대로 저장한다. 같은 세대에 다른 바이트가 생기면 upload가 충돌로 거부한다.

`sourceCommit`은 이제 “가장 최근에 실행한 성공 빌드의 HEAD”가 아니라
“현재 릴리스 내용을 확정했을 때의 Git 커밋”을 나타낸다. 무변경 빌드 뒤 실제 데이터가 바뀌면 보존된 SHA부터 현재 HEAD까지의
기존 `git diff`로 증분을 계산한다. 이전 커밋 객체가 필요하다는 기존 증분 전제도 유지한다.

이전 버전의 CLI가 이미 만든 매니페스트는 그대로 승계한다. 이미 게시된 세대를 합치거나 버전을 낮추거나 기존 바이트를 고치지 않는다.

## 6. 유지해야 하는 기존 기능

- 기존 그룹 버전 증가 시 전체 빌드, 동일 버전 시 증분 빌드, 버전 감소 거부를 유지한다.
- `packing`·`compression` 변경은 그룹 버전 증가를 요구한다. 같은 버전에서 단순히 releaseVersion만 올려 허용하지 않는다.
- sourcePath가 다른 이전 매니페스트는 릴리스 비교에 이르기 전에 거부한다.
- 증분 대상이 있으면 이전 sourceCommit의 존재와 승계 산출물의 존재·크기를 검사한다. 무변경 판정을 이유로 검사를 건너뛰지 않는다.
- 설정만 바뀌어도 산출물 검증·충돌 처리·파일 리비전 조정은 기존 `ArtifactWriter` 규칙을 따른다.
- 실제 내용 변경이 없는 재실행에서는 산출물 checksum을 새로 전량 계산하는 로직을 추가하지 않는다. 전량 검증은 `verify`의 책임이다.
- 빌드 실패 전 생성한 미참조 산출물의 재사용·충돌 처리와 기존 매니페스트 보존 계약을 유지한다.
- build의 기준은 이전 `manifest.json`이다. upload state, 상태 저장소 commit SHA나 원격 포인터로 기준을 바꾸지 않는다.
- output에 Git 메타데이터가 있거나 산출물이 Git tracked여도 build·verify·upload 계약은 같다. Git/LFS 정책은 CLI가 정하지 않는다.
- upload는 기존 산출물 델타 선택, 불변 세대 매니페스트와 성공 후 upload state 갱신을 유지한다.
- 하나의 upload output을 고정된 원격 대상에 연결하는 기존 전제를 유지한다. 설정 공유를 원격 대상 변경 기능으로 해석하지 않는다.
- 매니페스트 필드·`schemaVersion: 1`·64비트 `releaseVersion`과 `verify`·`upload`·`sync`·Unity 계약은 그대로다.

## 7. 최소 변경 구조

새 타입·인터페이스·프로젝트·생산 코드 파일·의존성은 추가하지 않는다.

| 기존 파일 | 변경 책임 |
| --- | --- |
| [CommandArguments.cs](../../src/GamePatchKit.Cli/CommandArguments.cs) | `BuildArguments`에 nullable `ConfigurationPath` 추가, build 전용 옵션 파싱 |
| [BuildCommand.cs](../../src/GamePatchKit.Cli/BuildCommand.cs) | 설정 선택·검증 연결, 무변경 시 쓰기 생략 |
| [GitRepository.cs](../../src/GamePatchKit.Cli/GitRepository.cs) | 고정 이름 전용 검증을 선택 경로의 같은 저장소·tracked·clean 검증으로 확장 |
| [ManifestStore.cs](../../src/GamePatchKit.Cli/ManifestStore.cs) | 릴리스 비교에서 `sourceCommit`도 제외 |
| [BuildConfigurationLoader.cs](../../src/GamePatchKit.Cli/BuildConfigurationLoader.cs) | YAML 계약은 유지. 명시 설정 실패도 선택한 파일을 식별하도록 오류 문구의 고정 파일명 사용을 보완 |

`BuildConfigurationLoader.Load(string path)`와 `PatchManifest`의 시그니처는 유지한다.
파일 접근 실패를 기존 CLI 오류로 안내하는 데 필요한 처리는 선택 설정을 읽는 경계에서만 보완한다.
설정 Resolver, 릴리스 계산 서비스, 설정 상속·override·profile, scoped commit 계산은 도입하지 않는다.

## 8. 구현 단계와 검증 계획

### C1. 인자와 설정 검증

기존 `TestCommandArguments`, `TestGitRepository`, `TestBuildCommand`에서 다음을 검증한다.

- 생략·명시·옵션 순서·상대/절대 경로, 중복·값 누락, 다른 명령의 거부
- 기본 설정이 없어도 명시 설정만으로 빌드 성공
- 저장소 밖·다른 저장소·중첩 저장소·미추적·staged/unstaged dirty·누락·디렉터리 설정의 쓰기 전 실패
- 기본·명시 설정 각각에서 tracked 심볼릭 링크가 같은 저장소의 tracked 파일, 저장소 밖 파일 또는 없는 대상을 가리켜도
  YAML 읽기 전에 종료 코드 1로 실패하고 기존 output을 보존함을 확인
- source 밖의 선택 설정은 검사하되 그 외 바깥 dirty 파일은 계속 무시
- 경로에 공백이나 Git pathspec 문자가 있어도 정확한 설정 파일만 검사

### C2. 입력 연결과 기존 빌드 보존

- 하나의 설정으로 서로 다른 source를 각자의 output에 빌드하고 그룹의 source 상대 경로를 유지한다.
- source별로 그룹 밖의 서로 다른 설정 파일을 명시해 선택한 설정이 적용됨을 확인한다.
- source 범위의 파일 탐색·그룹 할당·빈 그룹 검사는 기존 테스트의 기대값을 유지한다.
- 기본 설정 빌드와 같은 파일을 명시한 빌드의 결과가 동일하다.
- 같은 그룹 버전의 packing/compression 변경 실패, 버전 증가 성공, sourcePath 불일치 실패를 유지한다.

### C3. 릴리스 비교와 업로드 호환

- 기존 HEAD만 변경되는 테스트와 `TestManifestStore`의 해당 비교 사례를 새 정책으로 변경한다.
- source 밖 커밋·설정 주석만 변경하면 버전·SHA·매니페스트 바이트를 유지한다. 옵션 생략 경로도 동일하다.
- 공백·필드 순서가 다른 유효한 이전 매니페스트도 무변경 시 원본 바이트 그대로 보존한다.
- 여러 번의 무변경 빌드 뒤 실제 파일 변경 시 보존한 SHA에서 증분하고 한 세대만 증가한다.
- 그룹 버전·엔트리·산출물 등 유효한 릴리스 내용 변경은 기존 방식으로 증가한다.
- 이전 SHA 또는 승계 산출물이 없으면 무변경 후보여도 기존 오류로 실패한다.
- 최대 releaseVersion의 무변경 성공과 변경 시 실패를 검증한다.
- upload state 유무가 build의 릴리스 계산에 영향을 주지 않음을 확인한다.
- 기존 fake Storage 기반 `TestUploadCommand` 흐름에서 upload 후 HEAD만 변경한 재빌드·재업로드가 같은 세대·바이트로
  성공하는지 확인한다. 원격 동일 세대 다른 바이트의 거부는 계속 유지한다.

### C4. 문서와 회귀

- 9절의 계약 변경 문서를 구현과 함께 갱신한다.
- 관련 기존 테스트를 먼저 실행하고, 구현 완료 시 Release 전체 테스트와 CLI pack을 실행한다.
- 로컬 tool의 기본 설정·명시 설정 smoke build를 확인한다.
- 플랫폼 경로 차이는 기존 Windows/macOS/Linux CI 검증 범위에서 확인한다.
- NuGet 버전 변경이나 릴리스 게시를 이 구현에 포함하지 않는다.

## 9. 함께 동기화한 GamePatchKit 문서

다음 문서를 구현 계약과 함께 갱신했다.

| 문서 | 필요한 동기화 |
| --- | --- |
| [CLI PRD](../prd/gamepatch-kit-cli-prd.md) | 고정 설정 위치의 선택적 확장, source 밖 설정 clean 검사, 설정의 그룹 밖 배치, sourceCommit 의미와 릴리스 비교 변경 |
| [CLI 설계](game-patch-kit-cli-design.md) | 설정 선택·검증 흐름과 무변경 시 매니페스트 쓰기 생략 |
| [릴리스 버전 설계](game-patch-kit-cli-release-version-design.md) | sourceCommit만 다른 경우 증가한다는 규칙을 대체. 오래된 `int` 표기는 현재 구현의 `long`과 일치시킴 |
| [build 사용법](../cli/build.md) | `--config` 사용법, 기본값, 같은 내용일 때 기존 SHA 보존, 의도적인 동작 변경 안내 |
| [upload 사용법](../cli/upload.md), [업로드 PRD](../prd/gamepatch-kit-cli-upload-prd.md), [업로드 설계](game-patch-kit-cli-upload-design.md) | 상태 파일만 Git 추적하는 기존 예시와 CLI 제약을 구분. 산출물 추적도 호출자 선택이며 Git 자동화를 CLI에 추가하지 않음을 명시 |
| [설정 안내](../project-setup.md), [프로젝트 설명](../project-description.md), [README](../../README.md) | 필요한 CLI 예시·기능 설명을 동일 계약에 맞춤 |

산출물 전체 추적을 모든 사용자에게 의무화하거나 기존 상태 파일 전용 운영 예시를 특정 사용 프로젝트의 정책으로 교체하지 않는다.
실패 복구 문서에서는 기존의 동일한 게시 대상 재현 전제를 유지하고 `--config` 사용 시 그 호출 인자도 필요함을 명시한다.

## 10. 완료 조건

- 하나의 tracked·clean 설정으로 같은 Git 저장소의 여러 source를 각각 빌드할 수 있다.
- 잘못된 설정은 기존 output을 바꾸기 전에 실패한다. 설정을 데이터 그룹 밖에 배치하고 기존 데이터 탐색을 유지한다.
- 옵션 생략의 설정 탐색, 그룹·파일 증분 규칙, output 경계와 매니페스트 스키마가 유지된다.
- 릴리스 내용이 같으면 이전 버전·SHA·파일 바이트를 보존하고, 유효한 내용 변경만 다음 세대를 만든다.
- 기존 업로드의 세대 불변성과 sync·Unity 소비 방식이 유지된다.
- 변경되는 기존 테스트 기대값과 PRD·설계·사용법 문서가 함께 갱신된다.
- 관련 테스트·Release 회귀·pack·두 설정 방식 smoke가 통과한다.
- 사용하는 프로젝트의 스크립트·Git 운영·환경 구성은 이 문서와 GamePatchKit 구현에 포함하지 않는다.

## 11. 구현 검증 결과

- `dotnet test GamePatchKit.sln --configuration Release --no-restore`: 354개 통과
- `dotnet pack src/GamePatchKit.Cli/GamePatchKit.Cli.csproj --configuration Release --no-restore`: 성공
- 생성한 `GamePatchKit.Cli.0.1.0.nupkg`의 로컬 tool 설치: 성공
- 설치한 로컬 tool로 기본 설정과 명시 `--config`의 build·verify 및 결과 바이트 비교 smoke test 통과

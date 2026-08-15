# `gpk build`

## 개요

`gpk build`는 Git이 추적하는 패치 원본을 `gamepatchkit.yml` 설정에 따라 아카이브 또는 파일 객체로 만들고 `manifest.json`을 갱신한다. 이전 매니페스트가 있으면 마지막 성공 커밋부터 현재 `HEAD`까지의 변경만 증분 처리한다.

## 사용법

```shell
gpk build --source <데이터 루트> --output <패치 데이터 폴더>
```

- `--source`를 생략하면 현재 폴더를 사용한다.
- `--output`은 필수이며 source가 속한 Git 저장소 바깥이어야 한다.
- source 아래의 추적 파일과 추적 중인 `gamepatchkit.yml`만 빌드 입력으로 사용한다.
- source 아래의 추적 파일에 커밋되지 않은 변경이 있으면 빌드를 중단한다. untracked 파일과 source 바깥 변경은 무시한다.
- `--output` 폴더를 상태 Git 저장소로 운영하는 절차는 [`upload.md`의 상태 저장소 설정](upload.md#상태-저장소-설정)을 따른다.

## releaseVersion

`manifest.json` 루트의 `releaseVersion`은 매니페스트 전체의 세대를 가리키는 0 이상의 정수다. 그룹 버전과 달리 사용자가 yaml이나 옵션으로 정하지 않고 CLI가 계산한다.

- 이전 매니페스트가 없는 첫 빌드는 `releaseVersion: 0`을 쓴다.
- `releaseVersion`을 제외한 매니페스트 내용이 이전 성공 매니페스트와 같으면 이전 값을 그대로 쓴다. 아무것도 바꾸지 않고 다시 빌드해도 매니페스트 바이트가 이전과 같다.
- 내용이 다르면 이전 값보다 1 증가시킨다. `sourceCommit`만 바뀐 경우도 내용 변경으로 본다.
- 증가시켜야 하는데 이전 값이 `int.MaxValue`이면 빌드를 중단하고 기존 `manifest.json`을 바꾸지 않는다.

## 성공 출력

성공하면 그룹별 요약과 전체 합계를 출력한다.

```text
그룹 'content': version=3, entries=12, archiveCreated=false, fileObjects=2, writtenBytes=1840
합계: groups=1, entries=12, archivesCreated=0, fileObjects=2, writtenBytes=1840
```

- `entries`: 새 매니페스트에 기록된 현재 엔트리 수
- `archiveCreated`: 이번 실행에서 새 아카이브를 게시했는지 여부
- `fileObjects`: 이번 실행에서 새로 게시한 파일 객체 수
- `writtenBytes`: 이번 실행에서 새로 게시한 아카이브·파일 객체의 저장 바이트 합계

기존 산출물을 그대로 재사용하면 `archiveCreated`, `fileObjects`, `writtenBytes`에 포함하지 않는다.

output에 요청 리비전보다 높은 파일 객체가 있어 실제 리비전이 조정되면 모든 정상 요약 뒤에 경고를 한 번 출력한다. 그룹 버전은 yaml 값에서 바뀌지 않는다.

```text
경고: 파일 리비전이 자동 증가했습니다.
  group='content', path='data.bin', requested=3.1, actual=3.4
```

## 실패와 재실행

빌드가 실패하면 종료 코드 `1`과 원인을 출력하고 기존 `manifest.json`을 유지한다. 산출물은 매니페스트보다 먼저 게시되므로 실패 전에 만든 미참조 객체가 남을 수 있다.

- 같은 바이트의 미참조 산출물은 다음 실행에서 재사용한다.
- 파일 객체의 바이트가 다르면 마지막 리비전 다음 번호로 새 객체를 만든다.
- 같은 그룹 버전의 아카이브 바이트가 다르면 중단한다. yaml의 그룹 버전을 올린 뒤 다시 빌드해야 한다.
- 재실행은 기존 매니페스트의 마지막 성공 `sourceCommit`부터 변경을 다시 계산한다.

## 관련 파일

- [`BuildCommand.cs`](../../src/GamePatchKit.Cli/BuildCommand.cs)
- [`BuildSummary.cs`](../../src/GamePatchKit.Cli/BuildSummary.cs)
- [`Program.cs`](../../src/GamePatchKit.Cli/Program.cs)
- [`game-patch-kit-cli-design.md`](../design/game-patch-kit-cli-design.md)

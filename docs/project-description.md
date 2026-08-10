# GamePatchKit

## 프로젝트 개요

GamePatchKit은 게임 데이터를 버전별 패치로 만들고, 무결성을 검사하고, Supabase Storage에 게시하는 `gpk`라는 .NET CLI 도구다. Git 저장소로 원본 데이터를 관리하는 게임 프로젝트가 대상이며, 변경 여부 판단은 해시가 아니라 Git 커밋 diff와 사용자가 지정한 버전 번호로 한다.

핵심 아이디어는 "폴더를 하나의 아카이브로 묶어 배포하되, 그 안의 파일 하나가 바뀌어도 아카이브 전체를 다시 만들지 않는다"는 것이다. 바뀐 파일만 별도 객체(오버레이)로 추가하고, 클라이언트는 기존 아카이브 위에 오버레이를 덮어 받으므로 재배포량이 실제 변경분만큼으로 줄어든다. 아카이브가 오버레이로 너무 지저분해지면 사용자가 그룹 버전을 올려 재패키징한다.

프로젝트는 nuget.org에 `GamePatchKit.Cli` 패키지로 배포하는 framework-dependent .NET tool이며, 설치 후 명령 이름은 `gpk`다. `win-x64`·`linux-x64`·`osx-arm64` 세 환경을 지원하고, 실행 환경에는 .NET 10 런타임이 필요하다.

```shell
dotnet tool install --global GamePatchKit.Cli
```

## 주요 기능

### 패치 데이터 빌드 (`gpk build`)

- 하는 일: Git이 추적하는 데이터 폴더를 읽어 `gamepatchkit.yml` 설정대로 그룹을 묶은 아카이브 또는 파일 단위 객체를 만들고, 그 결과를 `manifest.json`에 기록한다. 이전 매니페스트가 있으면 마지막 성공 커밋부터 현재 `HEAD`까지 Git이 알려준 변경 경로만 증분 처리한다.
- 사용 방법:

  ```shell
  gpk build --source <데이터 루트> --output <패치 데이터 폴더>
  ```

  `--source`를 생략하면 현재 폴더를 쓴다. `--output`은 반드시 `--source`가 속한 Git 저장소 바깥이어야 하며, `--source` 루트에는 Git이 추적하는 `gamepatchkit.yml`이 있어야 한다.

  ```yaml
  groups:
    - id: group1
      version: 1
      packing: group      # group: 폴더 전체를 아카이브 하나로 묶음 / file: 파일마다 개별 객체
      compression: zstd    # zstd: 압축 / none: 원본 그대로
  ```

  `id`는 `--source` 기준 그룹 폴더의 상대 경로이자 식별자다. `version`은 사용자가 올리는 값으로, 이전 성공 버전과 같으면 증분 빌드, 더 크면 그룹 전체 재빌드, 더 작으면 빌드를 중단한다.

- 동작 결과: `<output>/archives/<그룹 id>/<그룹 버전>.gpka`(아카이브)와 `<output>/files/<그룹 id>/<그룹 버전>/<상대 경로>.v<파일 버전>`(오버레이 또는 `packing: file` 객체)를 만들고 `manifest.json`을 갱신한다. 파일 버전은 `<그룹 버전>.<파일 리비전>` 형식이며, 파일 리비전은 그 파일이 바뀔 때마다 0부터 1씩 오른다. 매니페스트 루트의 `releaseVersion`은 CLI가 계산하는 세대 번호로, 내용이 이전과 같으면 값이 유지되고 달라지면 1 증가한다. 성공하면 그룹별 엔트리 수·아카이브 생성 여부·새 파일 객체 수·바이트를 요약 출력한다.

  ```text
  그룹 'content': version=3, entries=12, archiveCreated=false, fileObjects=2, writtenBytes=1840
  합계: groups=1, entries=12, archivesCreated=0, fileObjects=2, writtenBytes=1840
  ```

- 참고: `--source` 아래 추적 파일에 커밋되지 않은 변경이 있으면 빌드를 중단한다(untracked 파일과 `--source` 바깥 변경은 무시). 기존 아카이브나 파일 객체를 덮어쓰지 않으며, 같은 그룹 버전에서 저장 바이트가 달라지면 그룹 버전 충돌로 중단한다. 아카이브·파일 객체는 SHA-256과 함께 매니페스트에 기록되고, 파일을 통째로 메모리에 올리지 않고 스트림으로 처리한다.

### 산출물 검증 (`gpk verify`)

- 하는 일: `<output>/manifest.json`이 참조하는 모든 아카이브·파일 객체가 실제로 존재하고, 저장 크기와 SHA-256이 매니페스트 값과 일치하는지 확인한다. 새로 빌드하거나 산출물을 바꾸지 않는다.
- 사용 방법:

  ```shell
  gpk verify --output <패치 데이터 폴더>
  ```

  `--output`만 받으며 `--source`, Git 저장소, `gamepatchkit.yml` 없이도 실행할 수 있다.

- 동작 결과: 모두 일치하면 출력 없이 종료 코드 `0`이다. 불일치가 있으면 첫 건에서 멈추지 않고 모든 대상을 상대 경로와 이유로 나열한 뒤 종료 코드 `1`을 반환한다.

  ```text
  archives/content/1.gpka: 파일이 없습니다.
  files/content/1/data.bin.v1.2: checksum이 다릅니다. (expected: ..., actual: ...)
  ```

- 참고: 매니페스트가 참조하지 않는 객체는 검사·정리 대상이 아니다. 손상을 복구하지 않으며, 복구가 필요하면 그룹 버전을 올려 새로 빌드한다.

### Supabase Storage 업로드 (`gpk upload`)

- 하는 일: `gpk build`가 만든 산출물과 세대 매니페스트를 Supabase Storage 버킷에 올린다. 로컬에 남긴 마지막 업로드 성공 상태(`.gpk-upload-state.json`)를 기준으로 새로 생긴 산출물만 골라 올리고, 세대 매니페스트(`manifests/<releaseVersion>.json`)는 한 번 게시하면 절대 덮어쓰지 않는 불변 객체로 남긴다.
- 사용 방법:

  ```shell
  gpk upload --output <패치 데이터 폴더> [--env-file <환경 변수 파일>]
  ```

  `GPK_SUPABASE_URL`(직접 Storage API URL, `https://<project-ref>.storage.supabase.co/storage/v1` 형식), `GPK_SUPABASE_KEY`(`sb_secret_...` 형식의 API key), `GPK_SUPABASE_BUCKET`(대상 버킷 이름) 세 값이 필요하다. 프로세스 환경 변수를 먼저 읽고, `--env-file`을 지정하면 그 파일의 같은 이름 값이 덮어쓴다. Supabase 요금제는 Pro 또는 Team이 필요하고(Free 플랜의 50 MB 전역 파일 제한은 지원 대상이 아님), 대상 프로젝트의 전역·버킷 파일 제한은 가장 큰 산출물 크기 이상으로 미리 설정돼 있어야 한다.

- 동작 결과: 선택한 산출물을 `UploadOrResume`(6 MiB TUS 청크 전송)으로 upsert한 뒤, 세대 매니페스트를 create-only로 게시하고, 둘 다 성공한 뒤에만 로컬 성공 상태를 교체한다. 세대 매니페스트가 이미 있으면 원격 바이트와 현재 매니페스트를 비교해 같으면 재사용하고 다르면 버전 충돌로 중단한다. 성공하면 올린 산출물 수·바이트·건너뛴 수와 게시한 `releaseVersion`을 출력한다.

  ```text
  업로드 산출물: uploaded=2, uploadedBytes=12, skipped=0
  세대 매니페스트: releaseVersion=0
  ```

- 참고: 산출물 하나는 1 GiB(1,073,741,824바이트)를 넘을 수 없다. 원격 객체 경로에는 ASCII 영문 대소문자·숫자·`.`·`_`·`-`만 허용한다. API key는 `apikey` 헤더로만 전달하고 표준 출력·표준 에러·예외 메시지 어디에도 노출하지 않는다. `--source`, Git 저장소, `gamepatchkit.yml`은 요구하지 않는다. 원격 루트 `manifest.json`은 올리지 않으며, 소비자는 세대별 매니페스트(`manifests/<releaseVersion>.json`)만 받는다. 같은 `--output`을 대상으로 하는 `build`·`verify`·`upload`의 동시 실행은 CLI가 아니라 운영 배포 절차가 직렬화해야 한다.

## 전체 동작 흐름

```text
Git 저장소(gamepatchkit.yml + 데이터 파일, 모두 커밋됨)
  → gpk build --source <데이터 루트> --output <패치 데이터 폴더>
      그룹별로 첫 빌드/증분 빌드/전체 재빌드를 판단해 아카이브·파일 객체를 쓰고 manifest.json 갱신
  → gpk verify --output <패치 데이터 폴더>   (선택, 배포 전이나 스토리지 이전·복원 후 무결성 확인)
  → gpk upload --output <패치 데이터 폴더> [--env-file <파일>]
      manifest.json이 참조하는 산출물 중 새로 생긴 것만 Supabase Storage에 upsert
      → manifests/<releaseVersion>.json을 불변 객체로 게시
      → 모두 성공하면 로컬 .gpk-upload-state.json 교체
```

세 명령은 각각 독립 실행되는 CLI 명령이며, `gpk build` 완료 후 `gpk upload`가 자동으로 이어지지는 않는다. 같은 데이터 루트를 다시 빌드하면 `gpk build`가 이전 `manifest.json`을 읽어 마지막 성공 커밋 이후의 변경만 반영하고, `gpk upload`도 마지막 업로드 성공 상태를 읽어 새로 생긴 산출물만 다시 올린다.

# `gpk verify`

## 개요

`gpk verify`는 기존 패치 데이터 폴더의 `manifest.json`과 실제 산출물이 일치하는지 검사한다. 새 패치를 빌드하거나 파일을 수정하지 않으며, 배포 전이나 스토리지 이전·복원 후 무결성을 확인할 때 사용한다.

## 사용법

```shell
gpk verify --output <패치 데이터 폴더>
```

`--output`만 받으며 `--source`는 사용할 수 없다. Git 저장소와 `gamepatchkit.yml`이 없는 환경에서도 실행할 수 있다.

정상이라면 출력 없이 종료 코드 `0`을 반환한다. 불일치가 있으면 모든 대상의 output 상대 경로와 이유를 한 줄씩 출력하고 종료 코드 `1`을 반환한다.

```text
archives/content/1.gpka: 파일이 없습니다.
files/content/1/data.bin.v1.2: checksum이 다릅니다. (expected: ..., actual: ...)
```

## 검증 범위

1. `manifest.json`의 스키마와 그룹·엔트리 관계를 검증한다.
2. 각 그룹의 아카이브와 `source: file` 엔트리가 참조하는 파일 객체를 찾는다.
3. 각 산출물을 존재 여부, `storedSize`, 저장된 바이트의 SHA-256 순서로 검사한다.
4. 첫 불일치에서 멈추지 않고 전체 목록을 출력한다.

`source: archive` 엔트리는 해당 아카이브의 크기와 checksum 검사에 포함되므로 개별 구간을 다시 읽지 않는다.

## 종료 코드와 오류

| 결과 | 종료 코드 | 출력 |
| --- | --- | --- |
| 모든 참조 산출물이 정상 | `0` | 없음 |
| 산출물 누락·크기 불일치·checksum 불일치 | `1` | 불일치 전체 목록 |
| 매니페스트 누락·관계 오류·잘못된 옵션 | `1` | 실패 이유 |

## 제약 및 대응

- 매니페스트가 참조하지 않는 객체는 검사하거나 정리하지 않는다.
- 아카이브를 해제해 엔트리 구간을 다시 검증하지 않는다.
- 손상된 산출물을 복구하지 않는다. 복구가 필요하면 yaml의 그룹 버전을 올려 새 패치를 빌드한다.

## 관련 파일

- [`VerifyCommand.cs`](../../src/GamePatchKit.Cli/VerifyCommand.cs)
- [`Program.cs`](../../src/GamePatchKit.Cli/Program.cs)
- [`game-patch-kit-cli-design.md`](../design/game-patch-kit-cli-design.md)

# gpk (GamePatchKit.Cli)

GamePatchKit의 명령행 도구. 게임 데이터 release를 만들고(`package`), 검증하고
(`verify`), bundle group을 다시 묶고(`compact`), canonical manifest에 서명한다
(`sign`).

## 설치

```bash
dotnet tool install --global GamePatchKit.Cli
gpk --help
```

## 명령

| 명령 | 책임 |
| --- | --- |
| `package` | 최초 또는 incremental release 생성 |
| `diff` | 두 release의 논리 파일 차이와 물리 artifact 차이 |
| `verify` | schema → 참조 무결성 → artifact byte → signature 검증 |
| `compact` | 선택한 bundle group의 새 baseline 생성 |
| `plan-download` | 로컬 상태에서 목표 release까지 필요한 artifact 계산 |
| `sign` | canonical manifest signature 생성 |

```bash
gpk package --config gamepatchkit.yml --output-root publish --json
gpk verify --output-root publish --package-id game-data --manifest-hash <hex64>
```

## exit code

| code | 의미 |
| --- | --- |
| `0` | 성공 (compact no-op 포함) |
| `1` | 입력 오류 |
| `2` | 무결성 오류 |
| `3` | 실행 실패 |

`--json`은 key가 정렬된 canonical JSON envelope 한 줄을 stdout에 출력하므로 CI에서
그대로 파싱할 수 있다. `--dry-run`은 package·compact·sign에서 계산을 모두 수행하고
게시만 하지 않는다. 개인키 내용은 결과·로그·오류 어디에도 기록하지 않는다.

## 문서

- [README](https://github.com/SideNation/GamePatchKit/blob/main/README.md)
- [CLI 계약](https://github.com/SideNation/GamePatchKit/blob/main/docs/contracts/cli.md)
- [package 설정과 glob](https://github.com/SideNation/GamePatchKit/blob/main/docs/guide/package-config.md)

MIT License.

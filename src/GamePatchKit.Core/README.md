# GamePatchKit.Core

GamePatchKit의 엔진 독립 core 계약. 게임 데이터 identity, canonical JSON, diff와
download plan 계산을 담당하며 filesystem·HTTP·`UnityEngine`·특정 Storage SDK를
참조하지 않는다.

- target framework: `netstandard2.1`
- 경로와 glob은 문자열로만 처리한다.

## 포함하는 것

- `gamepatchkit.yml`을 파싱한 설정 모델과 release manifest 모델
- RFC 8785 JCS·I-JSON canonical JSON writer
- SHA-256, Ed25519 signature 검증, `ed25519-<hex64>` key ID 파생
- `dataVersion`·`compactVersion`·`manifestHash` 규칙
- 정규화 상대 경로와 platform 독립 glob parsing·matching
- release diff와 download plan
- zstd codec 식별자와 `ICompressionCodec` contract

## 함께 쓰는 패키지

| 패키지 | 역할 |
| --- | --- |
| `GamePatchKit.Runtime` | 다운로드·검증·활성화 상태 머신 |
| `GamePatchKit.DotNet` | `HttpClient`·filesystem 기반 Runtime adapter |
| `GamePatchKit.Compression.NativeCompressions` | 기본 zstd codec |
| `GamePatchKit.Cli` (`gpk`) | package·verify·compact·sign 명령행 도구 |

## 문서

- [README](https://github.com/SideNation/GamePatchKit/blob/main/README.md)
- [identity와 canonical JSON](https://github.com/SideNation/GamePatchKit/blob/main/docs/guide/identity.md)
- [package 설정과 glob](https://github.com/SideNation/GamePatchKit/blob/main/docs/guide/package-config.md)

MIT License.

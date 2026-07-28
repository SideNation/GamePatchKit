# package 설정과 파일 선택

`gamepatchkit.yml` 작성법, v1 glob dialect, 파일 선택 순서, group 설계 규칙과
compression 정책을 다룬다. 구현 계약은
[`gamepatchkit.yml` 입력 계약](../contracts/gamepatchkit-yml.md)과
[file package 생성](../contracts/file-packager.md)에 있다.

## 세 가지 계약의 역할

이름이 비슷한 세 산출물의 역할이 다르다.

| 대상 | 누가 만드나 | 무엇인가 |
| --- | --- | --- |
| `gamepatchkit.yml` | 사용자 | package 설정을 적는 versioned YAML **입력 파일** |
| `schemas/package-config.schema.json` | GamePatchKit | 그 설정 데이터를 검증하는 **배포 산출물**. package마다 생기는 파일이 아니다 |
| release manifest (`manifest.json`) | Packager | 검증된 설정과 source로 생성하는 **canonical JSON 산출물**. 손으로 쓰지 않는다 |

검증은 항상 이 순서다.

1. YAML 문법과 문서 구조 제약을 검사한다 (CLI `YamlConfigDocument`).
2. 통과한 document를 JSON-compatible 데이터 구조로 변환한다 (CLI).
3. `package-config.schema.json`으로 검증한다 (Packager `PackageConfigReader`).
4. Core 설정 모델의 의미 규칙을 적용한다 — `packageId`·group 이름 규칙, group 중복
   일치, 예약 group `default` 처리 (`PackageConfig`/`PackageConfigValidator`).
5. 검증된 설정으로 package를 만들고 별도의 release manifest를 출력한다.

CLI가 아닌 host도 JSON-compatible 데이터만 만들면 3~4단계를 `PackageConfigReader`로
그대로 재사용할 수 있다.

## YAML 입력 규칙

`gamepatchkit.yml`은 일반 YAML이 아니라 canonical JSON으로 표현 가능한 부분집합만
받는다.

- 파일 하나에 **비어 있지 않은 document가 정확히 하나**만 있어야 하고 root는
  **mapping**이어야 한다.
- 두 번째 document(`---`), anchor(`&name`), alias(`*name`), merge key(`<<`), custom
  tag(`!Foo`)를 허용하지 않는다.
- 같은 mapping 안의 중복 key를 허용하지 않는다. 마지막 값으로 덮어쓰지 않고 거부한다.
- 주석과 스칼라 표현 방식은 설정값이 아니며 release manifest에 기록하지 않는다.

tag 없는 plain scalar 해석은 다음만 typed 값이 된다.

| 입력 | 결과 |
| --- | --- |
| 빈 값, `~`, `null`/`Null`/`NULL` | null |
| `true`/`True`/`TRUE`, `false`/`False`/`FALSE` | boolean |
| `[-+]?[0-9]+` 중 ±(2^53-1) 범위 | 정수 |
| 그 외 | 문자열 |

인용된 scalar는 언제나 문자열이다. hex·8진 정수, 부동소수점, 안전 범위를 넘는 정수는
문자열이 되어 schema type 오류로 거부된다. 명시적 tag는 `!!str`, `!!int`, `!!bool`,
`!!null`, `!!map`, `!!seq`만 허용하고 tag가 철자·인용보다 우선한다 — `!!str 1`은
문자열 `"1"`이다.

거부 예시는 [`tests/fixtures/gamepatchkit-yml/invalid/`](../../tests/fixtures/gamepatchkit-yml/invalid)에
위반 사유별로 하나씩 있다.

## 필드

package 수준:

| 필드 | 필수 | 기본값 | 내용 |
| --- | --- | --- | --- |
| `schemaVersion` | O | - | 설정 형식 버전. v1은 `1` |
| `packageId` | O | - | 소문자 kebab-case. artifact·manifest 저장 namespace가 된다 |
| `inputRoot` | O | - | source root. host 경로이므로 Core의 상대 경로 문법 대상이 아니다 |
| `include` | O | - | 상대 경로 glob allowlist. **비울 수 없다** |
| `exclude` | X | `[]` | allowlist에서 빼는 glob. 항상 우선하며 re-include는 없다 |
| `maxArtifactBytes` | X | `10485760` | file part와 bundle payload의 최대 물리 크기 |
| `defaultArtifactMode` | X | `file` | 예약 group `default`의 artifact mode |
| `compression` | O | - | 새 artifact를 만들 때 적용할 압축 정책 |
| `groups` | O | - | group 목록. 비어 있어도 된다 |

group 수준(`groups[]`):

| 필드 | 필수 | 내용 |
| --- | --- | --- |
| `name` | O | package 안에서 유일한 소문자 kebab-case. `default`는 예약어라 선언할 수 없다 |
| `include` | O | 이 group에 속할 상대 경로 glob. 최소 하나 |
| `artifactMode` | O | `file` 또는 `bundle` |
| `required` | O | 활성화 전에 반드시 필요한 group인지. 기본값이 없으므로 항상 명시한다 |
| `compression` | X | 이 group에만 적용할 압축 정책 override |

`compression`은 두 형태 중 하나다.

```yaml
compression:
  kind: none
```

```yaml
compression:
  kind: zstd
  codecId: zstd
```

v1의 `codecId`는 `zstd` 하나이며 compression level과 frame option은
`GamePatchKit.Compression.NativeCompressions`가 내부에 고정한다. deterministic 출력을
위해 사용자가 바꿀 수 없고, 그래서 설정 필드로도 노출하지 않는다.

## 예시

```yaml
schemaVersion: 1
packageId: sample-game-client-data
inputRoot: ./game-data
include:
  - "**/*"
exclude:
  - "**/*.tmp"
  - "**/.DS_Store"
  - ".vscode/**/*"
maxArtifactBytes: 10485760
defaultArtifactMode: file
compression:
  kind: zstd
  codecId: zstd
groups:
  - name: core
    include:
      - "core/**/*"
    artifactMode: file
    required: true
  - name: maps
    include:
      - "maps/**/*"
    artifactMode: bundle
    required: false
    compression:
      kind: none
```

필수 필드만 채운 최소 설정:

```yaml
schemaVersion: 1
packageId: sample-game-client-data
inputRoot: ./game-data
include:
  - "**/*.json"
compression:
  kind: none
groups: []
```

## group 설계 규칙

- 파일 확장자가 아니라 **소비자·다운로드 시점·변경 주기**로 나눈다.
- 모든 소비자가 전체 데이터를 항상 쓴다면 group 하나로 관리해도 된다.
- 선택 언어, 게임 모드, 맵처럼 필요한 시점이 다른 데이터는 별도 group으로 나눈다.
- 자주 바뀌는 밸런스·이벤트·운영 설정은 `artifactMode: file`을 우선한다.
- 크고 안정적인 맵·퀘스트·정적 콘텐츠는 `artifactMode: bundle`을 우선한다.
- 로그인과 최초 화면의 최소 데이터는 `required: true` core group으로 분리할 수 있다.
- package당 초기 group 수는 **4~6개 이하**를 권장한다.
- group별 독립 `dataVersion`이나 target manifest는 만들지 않는다.
- **공개 범위가 다르면 group이 아니라 package를 분리한다.** group은 다운로드와
  compact의 단위이지 보안 경계가 아니다. client가 공개 CDN에서 읽는 데이터와 서버만
  읽는 비공개 데이터는 서로 다른 `packageId`로 패키징한다.

## v1 glob dialect

OS와 glob library에 따라 의미가 달라지지 않도록 자체 dialect만 쓴다.

| 문법 | 의미 |
| --- | --- |
| 일반 문자 | 그대로 일치 |
| `*` | 한 path segment 안의 0개 이상 문자 |
| `**` | **완전한 segment**로만 쓸 수 있고 0개 이상의 path segment와 일치 |

- pattern은 `inputRoot` 기준 상대 경로이며 `/`만 구분자로 쓰고 NFC로 정규화한다.
- `**`가 0개 segment와도 일치하므로 `**/*.json`은 root의 `a.json`과 하위의
  `nested/b.json`을 **모두** 일치시킨다.
- 모든 OS에서 ordinal case-sensitive다. filesystem과 locale의 대소문자 규칙을 쓰지
  않는다.

거부하는 문법:

| 거부 대상 | 예 |
| --- | --- |
| 부분 `**` | `foo**bar`, `**.json` |
| 절대 경로·빈 segment | `/a/b`, `a//b` |
| `.`·`..` segment | `./a`, `../a` |
| 역슬래시·NUL | `a\b` |
| `?` | `a?.json` |
| character class | `[abc].json` |
| brace expansion | `{a,b}.json` |
| extglob·negation | `!(a).json`, `!a.json` |

## 파일 선택 순서

1. Packager가 filesystem entry를 열거하고 Core의 정규화 상대 경로로 바꾼다.
2. `include[]` 중 **하나에도** 일치하지 않는 경로를 제외한다(pattern 사이는 OR).
3. `exclude[]` 중 하나에 일치하는 경로를 제외한다. **exclude가 항상 우선하며
   negation이나 re-include는 없다.**
4. 남은 파일에 모든 group의 `include[]`를 적용한다. 둘 이상의 group에 일치하면
   실패하고, 어느 group에도 일치하지 않으면 예약 group `default`에 들어간다.
5. 정규화 상대 경로의 **UTF-8 byte ordinal 오름차순**으로 최종 목록을 고정한다.

group matching은 전역 `include`·`exclude`를 통과한 파일에만 적용한다. 설정에는
`default.required` 필드가 없으므로 Packager는 `default` group을 `required: true`로
manifest에 기록한다.

### 숨김 경로

segment가 `.`으로 시작하는 경로가 숨김 경로다. `*`와 `**`는 숨김 segment를
**암묵적으로 일치시키지 않는다.** 일치시키려면 해당 pattern segment가 literal `.`으로
시작해야 한다. Windows의 Hidden attribute는 선택 의미에 쓰지 않는다.

| pattern | `.vscode/settings.json` | `core/data.json` |
| --- | --- | --- |
| `**/*` | 일치하지 않음 | 일치 |
| `.vscode/**/*` | 일치 | 일치하지 않음 |

### editor metadata와 임시 파일

이름으로 추정해 자동 제외하지 않는다. 제외하려면 `exclude`에 **명시한다.**

```yaml
exclude:
  - "**/*.tmp"
  - "**/*.bak"
  - "**/.DS_Store"       # 숨김 파일이라 include의 '**/*'에는 애초에 걸리지 않는다
  - ".vscode/**/*"       # 숨김 디렉터리도 마찬가지
  - "**/Thumbs.db"
```

`.DS_Store`나 `.vscode/`처럼 이름이 `.`으로 시작하는 항목은 위 숨김 규칙 때문에
`include: ["**/*"]`에 이미 걸리지 않는다. 그래도 명시해 두면 의도가 드러나고, 나중에
숨김 경로를 일부러 포함시키는 pattern을 추가해도 계속 제외된다.

## compression 정책

`compression`은 **새 artifact를 만들 때 적용하는 정책**이다. 이미 만들어진 artifact의
유효성을 좌우하지 않는다.

- 이전 manifest의 유효한 artifact는 현재 `compression`과 달라도 **다시 압축하지 않고
  재사용한다.** 기존 payload와 compression metadata를 그대로 유지한다.
- 하나의 release에 이전 설정으로 만든 artifact와 현재 설정으로 만든 artifact가 함께
  존재할 수 있다. 검증·해제는 각 artifact에 기록된 **실제** compression metadata를
  기준으로 한다. `groups[]`에는 build-time compression 설정을 기록하지 않는다.
- compression 설정만 바뀌고 source와 artifact 참조가 같으면 새 release를 만들지 않고
  기존 `dataVersion`·`manifestHash`를 그대로 반환한다.

> **compression을 꺼도 codec 지원은 사라지지 않는다.**
> 설정을 `zstd`에서 `none`으로 바꿔도 이전 release에서 재사용하는 artifact는 여전히
> zstd다. Runtime은 **목표 manifest가 참조하는 모든 codec**을 지원해야 하며, 주입되지
> 않은 codec을 manifest가 요구하면 활성화 전에 `runtime.missing-compression-codec`으로
> 실패한다. 설정 변경만으로 client의 codec 요구사항을 없앨 수는 없고, 해당 group을
> compact해 새 baseline을 만들어야 한다.

## Core와 Packager의 책임 경계

같은 "파일 선택"이라도 둘의 책임이 다르다. 어느 쪽 오류인지 구분하면 진단이 빨라진다.

| 책임 | Core | Packager |
| --- | --- | --- |
| 경로 | 문자열 정규화(`/`, NFC)와 상대 경로 문법 검증 | 실제 filesystem 열거 |
| glob | dialect parsing과 matching | 열거 결과에 Core matcher 적용 |
| filesystem 객체 종류 | 판정하지 않는다 | symlink·junction 등 reparse point를 따르지 않고 오류 처리 |
| 경합 | 관여하지 않는다 | source snapshot 검증으로 거부 |

Core는 filesystem, HTTP, `UnityEngine`, 특정 Storage SDK를 참조하지 않는다. NFC 정규화
후 UTF-8 byte가 같거나 `OrdinalIgnoreCase` 비교에서만 같아지는 둘 이상의 경로는 OS와
무관하게 중복 오류다.

Packager는 최초 열거에서 상대 경로·entry 종류·stable file identity(Windows의 volume
ID·file ID, Unix 계열의 device·inode)·크기·수정 시각 snapshot을 만들고, 파일을 열기
전과 stream hash 완료 후 같은 값인지 재검증한다. manifest 확정 전에 같은 규칙으로 다시
열거·선별해 최초 선택 결과와 비교하고, 파일 추가·삭제·교체·변경이나 entry 종류 변경이
감지되면 `packager.source-changed`로 실패하며 manifest를 만들지 않는다.

stable file identity 검증은 Windows·Linux·macOS에서 지원한다. identity를 제공할 수 없는
filesystem에서는 검증을 생략하지 않고 package를 실패시킨다.

**재현 가능한 package를 위해 호출자는 실행 중 source를 변경하지 않아야 한다.** Packager는
관찰 가능한 경합만 거부할 수 있다.

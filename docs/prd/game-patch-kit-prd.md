# GamePatchKit

## 개요

`GamePatchKit`은 여러 게임이 엔진과 Storage 제품에 종속되지 않고 게임 데이터를
패키징·배포·다운로드·검증·활성화할 수 있게 하는 범용 도구 모음이다. 작은 데이터는
콘텐츠 주소 기반 개별 파일로 관리하고, 데이터가 커지면 선택한 그룹만 bundle로 묶으며,
공통 Runtime과 .NET reference adapter를 제공한다. Unity를 포함한 다른 host의
platform 기능은 Runtime interface 구현으로 연결한다.

## 개발 v1

### 목표

- 같은 입력과 설정으로 항상 같은 artifact와 manifest를 생성한다.
- 개별 파일 방식으로 시작한 프로젝트가 manifest 계약을 바꾸지 않고 bundle 방식을
  추가할 수 있게 한다.
- 항상 개별 파일로 유지할 데이터와 bundle로 묶을 데이터를 그룹 정책으로 구분한다.
- incremental 변경을 개별 파일로 누적하고 필요할 때 bundle group만 compact한다.
- 게임 데이터 내용의 버전과 물리적 패키징 버전을 분리한다.
- 이전 어떤 release에서도 목표 release의 완전한 최종 파일 상태를 직접 계산한다.
- 다운로드 계획·검증·cache·staging·활성화 로직을 공통 Runtime에 한 번만 구현한다.
- 네트워크·저장소·압축의 platform 차이는 Runtime interface 뒤로 격리한다.
- 기본 zstd 구현은 `NativeCompressions.Zstandard`를 사용한다.
- 특정 게임 엔진, CI, Storage와 CDN 제품에 종속되지 않는다.
- 1만 개 이상의 파일과 1GiB 이상의 입력을 처리할 수 있다.

### 트리거

- 게임 데이터 source root에서 최초 release를 패키징한다.
- 이전 release와 현재 source를 비교해 incremental release를 생성한다.
- 두 release의 논리 파일 상태와 예상 다운로드량을 비교한다.
- file·bundle artifact와 manifest의 무결성을 검증한다.
- bundle group에 누적된 개별 파일 override를 새 baseline으로 compact한다.
- canonical manifest에 서명하거나 배포 전에 서명을 검증한다.
- Runtime을 소비하는 애플리케이션이 서버가 요구하는 release를 확인한다.
- 로컬 파일 상태와 목표 release를 비교해 누락 artifact를 다운로드한다.
- 전체 필수 group을 검증한 뒤 목표 `dataVersion`을 활성화한다.
- 활성 release에서 필요한 optional group을 요청 시점에 별도로 다운로드·활성화한다.

### 범위

포함:

- deterministic 파일 탐색·선별·경로 정규화
- 파일별 SHA-256과 콘텐츠 주소 계산
- 개별 file artifact와 선택적 파일별 압축
- 그룹별 deterministic bundle
- 큰 개별 파일의 고정 크기 part 분할
- 완전한 release manifest
- `dataVersion`, `compactVersion`과 `manifestHash`
- package, incremental, diff, verify, compact, sign
- 다운로드 계획과 content-addressed cache
- staging, 원자적 활성화와 중단 복구
- 로컬 `PackageState`와 optional group 설치 상태
- .NET Runtime adapter
- platform 전송·저장소·압축 interface
- `NativeCompressions.Zstandard` 기반 zstd codec
- manifest JSON Schema
- 단위·통합·adapter conformance·성능 검증

제외:

- Supabase Storage, S3, Steam 등 특정 원격 저장소에 직접 업로드하는 publisher
- `dev`·`stage`·`live` 승인 UI와 CI 제품별 pipeline
- Unity Addressables와 Unreal Pak 등 엔진 전용 asset 변환
- 공식 Unity 전용 프로젝트와 엔진 전용 배포물
- 게임 실행 바이너리와 앱 스토어 빌드 배포
- 게임별 데이터 deserialize와 hot reload
- Remote Config, 유저별 콘텐츠와 DRM
- content-defined chunking
- HTTP Range 기반 bundle 내부 부분 다운로드
- artifact garbage collection

### 핵심 원칙

1. release manifest의 `files[]`가 해당 release의 최종 파일 상태를 완전하게 표현한다.
2. artifact는 콘텐츠 해시 경로에 생성하고 한번 publish한 객체를 덮어쓰지 않는다.
3. 파일 변경 여부는 artifact 위치가 아니라 정규화된 경로와 원본 SHA-256으로 판단한다.
4. bundle은 저장·다운로드 최적화이며 게임 데이터의 논리적 버전을 바꾸지 않는다.
5. file group과 bundle group은 하나의 package manifest와 글로벌 `dataVersion`을
   공유한다.
6. 공개 범위가 다른 데이터는 group이 아니라 별도 package로 분리한다.
7. 공통 Runtime은 platform API와 구체 compression package를 직접 사용하지 않고
   interface contract만 의존한다.
8. DotNet adapter와 외부 host 구현은 Runtime 내부 규칙을 복제하지 않는다.
9. 명시적 설정을 우선하며 용량에 따라 artifact mode를 자동으로 왕복 전환하지 않는다.
10. package 생성과 publish·promote를 분리한다.

### 용어

| 용어 | 정의 |
| --- | --- |
| package | 동일한 공개 범위와 버전 계약을 공유하는 데이터 배포 단위 |
| `packageId` | 게임과 배포 단위를 식별하는 소문자 kebab-case 이름 |
| group | 다운로드 시점·변경 주기·소비 목적이 같은 파일 집합 |
| file artifact | 원본 파일 하나를 직접 표현하는 불변 객체 또는 part 집합 |
| bundle artifact | 같은 group의 여러 파일을 묶은 불변 archive |
| baseline | 신규 설치가 시작점으로 사용하는 물리적 묶음 상태 |
| override | baseline 이후 추가·변경된 개별 file artifact |
| compact | 현재 최종 상태로 bundle group의 새 baseline을 만드는 작업 |
| `dataVersion` | 최종 논리 파일 상태와 전달 의미의 digest |
| `compactVersion` | 최초 package의 `0`에서 시작해 compact할 때만 증가하는 물리 packaging 세대 |
| `manifestHash` | canonical manifest 원본 byte의 SHA-256이자 불변 manifest 식별자 |
| channel | 환경이 사용할 `manifestHash`와 `dataVersion`을 가리키는 외부 pointer |
| target pointer | channel 또는 서버가 클라이언트에 요구하는 `dataVersion`·`manifestHash` |
| active pointer | required group까지 준비되어 현재 활성화된 로컬 `dataVersion`·`manifestHash` |
| `PackageState` | active pointer와 group별 설치 상태를 원자적으로 기록하는 package별 로컬 상태 |
| group install state | optional group이 어느 manifest 기준으로 설치·검증됐는지 나타내는 로컬 상태 |
| activation batch | 한 Runtime 요청의 target group 집합을 하나의 `PackageState` revision으로 활성화하는 단위 |
| Runtime | 다운로드 계획부터 검증·활성화까지 수행하는 platform 독립 코어 |
| adapter | Runtime의 전송·저장소·압축 contract를 구체 구현으로 연결하는 모듈 |

### 프로젝트 명명

| 대상 | 이름 |
| --- | --- |
| 제품명 | `GamePatchKit` |
| 저장소 | `game-patch-kit` |
| solution | `GamePatchKit.sln` |
| C# namespace | `GamePatchKit.*` |
| CLI | `gpk` |
| 기본 설정 파일 | `gamepatchkit.yml` |

### 프로젝트 구조

```text
game-patch-kit/
├── GamePatchKit.sln
├── Directory.Packages.props
├── schemas/
│   ├── package-config.schema.json
│   ├── release-manifest.schema.json
│   └── channel.schema.json
├── src/
│   ├── GamePatchKit.Core/
│   ├── GamePatchKit.Compression.NativeCompressions/
│   ├── GamePatchKit.Packager/
│   ├── GamePatchKit.Cli/
│   ├── GamePatchKit.Runtime/
│   └── GamePatchKit.DotNet/
├── tests/
│   ├── GamePatchKit.Core.Tests/
│   ├── GamePatchKit.Compression.NativeCompressions.Tests/
│   ├── GamePatchKit.Packager.Tests/
│   ├── GamePatchKit.Runtime.Tests/
│   ├── GamePatchKit.DotNet.Tests/
│   └── GamePatchKit.IntegrationTests/
├── README.md
└── LICENSE
```

target framework:

- `GamePatchKit.Core`: `netstandard2.1`
- `GamePatchKit.Runtime`: `netstandard2.1`
- `GamePatchKit.Compression.NativeCompressions`: `netstandard2.1`
- `GamePatchKit.Packager`: `net10.0`
- `GamePatchKit.Cli`: `net10.0`
- `GamePatchKit.DotNet`: `net10.0`

### 프로젝트 책임

#### `GamePatchKit.Core`

- config와 manifest 모델
- canonical JSON
- SHA-256과 signature 검증
- `dataVersion`, `compactVersion` 규칙과 `manifestHash`
- release diff와 download plan
- zstd codec 식별자와 `ICompressionCodec` contract
- platform 독립 오류 모델

Core는 filesystem, HTTP, `UnityEngine`과 특정 Storage SDK를 참조하지 않는다.

#### `GamePatchKit.Compression.NativeCompressions`

- Core의 `ICompressionCodec` 구현
- NuGet `NativeCompressions.Zstandard` 기반 zstd 압축·해제
- streaming 입력·출력
- 고정된 compression level과 frame option
- deterministic package 생성을 위한 단일 compression worker
- Runtime에서 사용할 zstd codec factory

`NativeCompressions.Zstandard`의 version은 중앙 package 설정에 고정하고 자동으로
업그레이드하지 않는다. 선정한 version의 native runtime 지원 범위를 release마다
smoke test로 검증한다. 이 package가 preview인 동안은 API 변경 가능성을 전제로 adapter
내부에서만 사용하고 Core와 Runtime의 public contract로 노출하지 않는다.

#### `GamePatchKit.Packager`

- source 탐색과 입력 검증
- file artifact와 part
- deterministic bundle
- 최초·incremental package
- compact
- artifact와 manifest verify
- manifest sign
- publish 가능한 로컬 output tree

Packager는 `GamePatchKit.Compression.NativeCompressions`를 기본 zstd 구현으로 사용한다.

#### `GamePatchKit.Cli`

- Packager와 Core 기능의 명령행 진입점
- CI용 machine-readable 결과와 exit code
- dry-run
- secret 비노출

#### `GamePatchKit.Runtime`

- channel과 manifest 검증
- 로컬 파일 상태와 목표 release 비교
- download plan 실행
- content-addressed cache
- staging
- required·optional group 준비 상태
- `PackageState` 모델·검증과 상태 전환
- active pointer의 원자적 전환
- 취소·재시도·중단 복구
- 이전 release fallback

Runtime은 artifact 전송과 설치 저장소를 위한 최소 contract만 정의하고 구체적인
`HttpClient`, `UnityWebRequest`, OS 경로, Unity lifecycle과
`NativeCompressions.Zstandard` 구체 type은 포함하지 않는다.

#### `GamePatchKit.DotNet`

- Runtime의 .NET adapter
- `HttpClient` 기반 artifact 다운로드
- 일반 filesystem cache·staging·immutable installation
- package별 `package-state.json`, writer lock과 원자적 교체
- async stream, cancellation과 progress
- ASP.NET, worker, console과 desktop 애플리케이션 연동
- `GamePatchKit.Compression.NativeCompressions` 기본 codec 구성

### 의존 방향

```text
GamePatchKit.Packager ──→ GamePatchKit.Core
        │
        └───────────────→ GamePatchKit.Compression.NativeCompressions
                                          │
                                          └──→ GamePatchKit.Core

GamePatchKit.Runtime ───→ GamePatchKit.Core
        ↑
        ├── GamePatchKit.DotNet ──→ GamePatchKit.Compression.NativeCompressions
        └── external host implementation

GamePatchKit.Cli ───────→ GamePatchKit.Packager
```

- Packager와 Runtime은 서로 의존하지 않고 Core 계약으로만 연결한다.
- Runtime은 `GamePatchKit.Compression.NativeCompressions`를 직접 참조하지 않는다.
- DotNet은 Runtime과 Core에 의존하고 기본 codec adapter를 조합한다.
- Core와 Runtime은 adapter를 참조하지 않는다.
- Unity를 포함한 외부 host는 Runtime interface를 직접 구현한다.
- 역방향 참조는 빌드 검증에서 금지한다.

### 보안·공개 경계

- 하나의 package에 포함된 모든 파일은 같은 접근 정책을 가진다고 간주한다.
- client가 공개 CDN에서 읽는 데이터와 server만 읽는 비공개 데이터는 서로 다른
  `packageId`로 패키징한다.
- group은 다운로드와 compact 단위이며 보안 경계로 사용하지 않는다.
- 서로 다른 공개 경계를 넘는 artifact deduplication은 수행하지 않는다.
- package manifest에는 다른 package의 파일 경로·hash·artifact 위치를 기록하지 않는다.

예시 package:

| `packageId` | 대상 | 공개 정책 |
| --- | --- | --- |
| `<game>-client-data` | 클라이언트와 공개 가능한 공용 데이터 | public read |
| `<game>-server-data` | 서버 판정·운영 전용 데이터 | private |

### package 설정

사용자가 작성하는 package 설정은 기본 파일명 `gamepatchkit.yml`인 versioned YAML 한
파일로 관리한다. `package-config.schema.json`은 YAML을 파싱한 뒤의 설정 데이터 구조를
검증하는 GamePatchKit 배포 산출물이며 package별로 생성되는 파일이 아니다.
release manifest는 검증된 설정과 source로 Packager가 별도로 생성하는 canonical JSON
산출물이다.

설정 처리 순서:

1. `gamepatchkit.yml`의 YAML 문법과 허용 기능을 검증한다.
2. YAML을 JSON-compatible 데이터 구조로 변환한다.
3. `package-config.schema.json`과 설정 모델의 의미 규칙을 검증한다.
4. 검증된 설정으로 package를 생성하고 별도의 release manifest를 출력한다.

YAML 입력 규칙:

- 파일 하나에는 비어 있지 않은 YAML document가 정확히 하나만 있어야 하고 root는
  mapping이어야 한다.
- 두 번째 document, anchor, alias, merge key(`<<`)와 custom tag를 허용하지 않는다.
- 같은 mapping 안의 중복 key를 허용하지 않는다.
- 값은 JSON-compatible scalar, array와 mapping으로 표현할 수 있어야 한다.
- 주석과 YAML 표현 방식은 설정값이 아니며 release manifest에 기록하지 않는다.

| 필드 | 내용 |
| --- | --- |
| `schemaVersion` | 설정 형식 버전 |
| `packageId` | artifact namespace와 manifest package |
| `inputRoot` | source root |
| `include` | 상대 경로 glob allowlist |
| `exclude` | allowlist 제외 glob |
| `maxArtifactBytes` | file part와 bundle 최대 물리 크기 |
| `defaultArtifactMode` | 기본 `file` 또는 `bundle` |
| `compression` | `none` 또는 codec ID·level·frame option이 고정된 `zstd` 설정 |
| `groups[]` | group 파일 선택과 artifact 정책 |

group 필드:

| 필드 | 내용 |
| --- | --- |
| `name` | package 안에서 유일한 소문자 kebab-case 이름 |
| `include` | group에 속할 상대 경로 glob |
| `artifactMode` | `file` 또는 `bundle` |
| `required` | 활성화 전에 반드시 필요한 group인지 여부 |
| `compression` | 선택적 group 전용 압축 정책 |

설정 규칙:

- 기본 `defaultArtifactMode`는 `file`이다.
- bundle은 명시적으로 `artifactMode: bundle`인 group에만 생성한다.
- 항상 개별 파일로 관리할 데이터는 `artifactMode: file` group에 둔다.
- 하나의 파일이 둘 이상의 group에 일치하면 오류로 중단한다.
- 어떤 group에도 속하지 않은 파일은 예약 group `default`에 들어간다.
- 파일의 group 이동은 논리 전달 의미 변경으로 처리한다.
- `maxArtifactBytes` 기본값은 `10,485,760 bytes`다.
- `compression`은 새 artifact를 생성할 때 적용하는 정책이다.
- 이전 manifest의 유효한 artifact는 현재 `compression`과 달라도 다시 압축하지 않고
  재사용한다.
- compression 설정만 바뀌고 source와 artifact 참조가 같으면 새 release를 생성하지
  않는다.
- 하나의 release에는 이전 설정으로 만든 artifact와 현재 설정으로 만든 artifact가 함께
  존재할 수 있으며, 각 artifact에 기록된 실제 compression metadata를 기준으로 검증·
  해제한다.
- compression 설정을 바꿔도 재사용 artifact가 요구하는 codec 지원은 사라지지 않으며,
  Runtime은 목표 manifest가 참조하는 모든 codec을 지원해야 한다.

### group 설계 규칙

- 파일 확장자가 아니라 소비자, 다운로드 시점과 변경 주기를 기준으로 나눈다.
- 모든 소비자가 전체 데이터를 항상 사용하면 하나의 group으로 관리해도 된다.
- 선택 언어, 게임 모드와 맵처럼 필요한 시점이 다른 데이터는 별도 group으로 나눈다.
- 자주 바뀌는 밸런스·이벤트·운영 설정은 file group을 우선한다.
- 크고 안정적인 맵·퀘스트·정적 콘텐츠는 bundle group을 우선한다.
- 로그인과 최초 화면의 최소 데이터는 `required: true` core group으로 분리할 수 있다.
- 초기 group 수는 package당 4~6개 이하를 권장한다.
- group별 독립 `dataVersion`이나 channel을 만들지 않는다.
- 공개 범위가 다르면 group이 아니라 package를 분리한다.

### 배포 대상 파일 규칙

- `include`는 비어 있을 수 없고 명시적 allowlist로 동작한다.
- 상대 경로 구분자는 `/`로 정규화하고 Unicode 정규화 규칙을 고정한다.
- 절대 경로, 빈 segment, `.`·`..`, 역슬래시, NUL과 symlink를 허용하지 않는다.
- 숨김 파일, editor metadata와 임시 파일은 명시하지 않는 한 포함하지 않는다.
- 대소문자만 다른 경로, 정규화 후 중복 경로와 group 중복 일치는 오류다.
- 파일 순서는 정규화 상대 경로의 ordinal byte 순서로 고정한다.
- 각 파일의 원본 byte 크기와 SHA-256을 계산한다.
- package 실행 중 입력 파일이 바뀌면 일관되지 않은 입력으로 실패한다.

### file artifact

- `artifactMode: file` 파일은 최초·incremental·compact 모두에서 bundle에 넣지 않는다.
- 원본 파일 byte의 SHA-256을 `fileHash`로 사용한다.
- 무압축 파일은 원본 byte를 payload로 사용한다.
- zstd 파일 압축은 `ICompressionCodec`으로 수행하며 기본 구현은
  `GamePatchKit.Compression.NativeCompressions`다.
- 압축 시 원본 `fileHash`와 압축 payload의 `artifactHash`를 모두 기록한다.
- payload가 `maxArtifactBytes` 이하이면 하나의 artifact로 만든다.
- payload가 제한을 넘으면 고정 크기의 순서 있는 part로 나눈다.
- 각 part의 크기와 SHA-256을 manifest에 기록한다.
- part 결합과 압축 해제 후 최종 크기와 `fileHash`를 검증한다.
- 같은 `artifactHash`가 이미 있으면 byte를 검증하고 재생성하지 않는다.
- 재사용한 file artifact는 현재 group의 compression 설정과 달라도 기존 payload와
  compression metadata를 유지한다.

```text
<packageId>/
└── artifacts/
    └── files/
        └── <artifactHash>/
            ├── content
            ├── content.zst
            └── part-#####
```

한 artifact 디렉터리에는 실제 형식에 맞는 payload 한 종류만 존재한다.

### bundle artifact

- bundle은 같은 group의 파일만 포함한다.
- archive는 deterministic tar, 압축은 `ICompressionCodec`의 zstd 또는 무압축 tar다.
- Packager의 기본 zstd 구현은 `GamePatchKit.Compression.NativeCompressions`다.
- entry 경로는 정규화된 전체 상대 경로를 사용하고 순서를 고정한다.
- timestamp, UID, GID, 이름, permission과 tar header 형식을 고정한다.
- symlink, hardlink, device, sparse entry와 확장 attribute를 생성하지 않는다.
- 실제 bundle payload는 `maxArtifactBytes` 이하여야 한다.
- 제한을 넘으면 마지막 entry를 다음 bundle로 이동해 다시 생성한다.
- 단일 파일이 제한을 만족하지 못하면 file artifact part로 fallback한다.
- bundle payload SHA-256을 `bundleHash`와 `artifactHash`로 사용한다.
- 같은 입력 파일 집합은 같은 bundle 경계와 byte를 생성해야 한다.
- v1 Runtime은 필요한 bundle 전체를 다운로드하고 압축 해제한다.

```text
<packageId>/
└── artifacts/
    └── bundles/
        └── <groupName>/
            ├── <bundleHash>.tar
            └── <bundleHash>.tar.zst
```

### release manifest

release manifest 최소 필드:

| 필드 | 내용 |
| --- | --- |
| `schemaVersion` | manifest 형식 버전 |
| `packageId` | manifest package |
| `dataVersion` | 최종 논리 상태 digest |
| `compactVersion` | 현재 물리 packaging 세대 |
| `groups[]` | group 이름, `required`와 논리 정책 |
| `artifacts[]` | type, 경로, 크기, hash, 압축과 part |
| `files[]` | 최종 경로, group, 원본 크기, `fileHash`, artifact 참조 |

manifest 규칙:

- `files[]`는 최종 상태의 모든 파일을 정확히 한 번 포함한다.
- 삭제 파일은 `files[]`에서 제외한다.
- 추가·변경·삭제 목록은 build report에만 기록하고 최종 상태 판단에 사용하지 않는다.
- 배열 순서와 객체 key 순서를 포함한 canonical JSON 규칙을 고정한다.
- 생성 시각, 머신과 source revision은 별도 build report에 기록한다.
- JSON 원본과 선택적 `.json.zst` 전송본을 만들 수 있다.
- `manifestHash`는 manifest 내부에 기록하지 않는다.
- `groups[]`에는 build-time compression 설정을 기록하지 않고, 실제 compression은
  `artifacts[]`에만 기록한다.
- `artifacts[]`의 compression metadata가 각 payload의 실제 형식을 나타내며, 같은
  release와 group 안에서도 artifact마다 다를 수 있다.
- `dataVersion`은 아래에 정의한 canonical identity byte를 기준으로 계산한다.
- `manifestHash`와 signature는 같은 canonical manifest 원본 byte를 기준으로 계산한다.

### `dataVersion`, `compactVersion`과 `manifestHash`

`dataVersion` 계산 입력:

- `packageId`
- group 이름과 `required` 같은 소비 의미
- 각 파일의 정규화 경로, group, 원본 크기와 `fileHash`

`dataVersion` 제외 항목:

- file 또는 bundle artifact 종류
- bundle 경계와 압축 방식
- artifact 경로와 part 구성
- `compactVersion`과 `manifestHash`
- build 시각, source revision과 실행 환경

`compactVersion` 규칙:

- 최초 package는 `0`이다.
- incremental package는 이전 manifest의 값을 그대로 상속한다.
- compact는 source manifest의 값보다 1 증가시킨다.
- 같은 `compactVersion`이 같은 manifest를 의미하지 않으며, manifest 식별에는 사용하지
  않는다.

`manifestHash` 규칙:

- `manifestHash` 필드가 없는 최종 canonical manifest 원본 byte의 SHA-256이다.
- lowercase hexadecimal 64자로 표현한다.
- `schemaVersion`, `compactVersion`, group, artifact와 file 참조를 포함한 manifest의
  모든 필드가 hash 입력에 포함된다.
- 선택적 `.json.zst` 전송본과 `manifest.sig`는 hash 입력에서 제외한다.

결과:

- 파일 내용이나 group 의미가 바뀌면 `dataVersion`과 `manifestHash`가 바뀐다.
- 같은 데이터를 compact하면 `dataVersion`은 유지되고 `compactVersion`은 1 증가하며
  `manifestHash`가 바뀐다.
- 같은 데이터를 실제로 다른 압축 artifact 배치로 패키징하면 `dataVersion`은 같고
  `manifestHash`는 다르다.
- compression 설정만 바뀌고 기존 artifact 참조를 모두 재사용하면 `dataVersion`과
  `manifestHash`가 모두 유지된다.
- manifest `schemaVersion`만 바뀌면 `dataVersion`은 유지되고 `manifestHash`는 바뀐다.

```text
<packageId>/
└── manifests/
    └── <manifestHash>/
        ├── manifest.json
        ├── manifest.json.zst
        └── manifest.sig
```

### 최초 package

1. 설정과 source root를 검증한다.
2. 파일을 정규화하고 group을 결정한다.
3. 모든 파일의 크기와 `fileHash`를 계산한다.
4. file group은 개별 artifact로 만든다.
5. bundle group은 deterministic bundle baseline을 만든다.
6. 큰 bundle entry는 file artifact part로 fallback한다.
7. 모든 artifact의 크기와 SHA-256을 다시 검증한다.
8. 최종 논리 상태에서 `dataVersion`을 계산한다.
9. `compactVersion`을 `0`으로 설정하고 canonical manifest를 생성한다.
10. canonical manifest 원본 byte에서 `manifestHash`를 계산하고 build report와 전체
    참조를 검증한다.

### incremental package

1. 이전 manifest의 schema, packageId, canonical byte `manifestHash`와 참조를 검증한다.
2. 현재 source의 최종 파일 목록을 이전 `files[]`와 비교해 추가·내용 변경·삭제·group
   이동을 서로 구분한다.
3. 경로와 `fileHash`가 같은 기존 file artifact는 group·`artifactMode`·compression
   설정이 바뀌어도 새 group의 file override로 재사용한다.
4. 기존 bundle 참조는 경로와 `fileHash`가 같고 group이 그대로이며 현재
   `artifactMode`도 `bundle`일 때만 재사용한다.
5. 기존 bundle 참조가 4의 조건을 만족하지 않으면 현재 파일을 개별 file artifact
   override로 만들거나, 같은 payload의 검증된 기존 file artifact가 있으면 재사용한다.
6. 추가·내용 변경 파일은 group mode와 관계없이 개별 file artifact override로 만들며,
   새 artifact에만 현재 compression 설정을 적용한다.
7. compression 설정만 바뀐 기존 artifact는 종류와 compression metadata를 그대로
   유지하고 다시 만들지 않는다.
8. 삭제 파일은 새 `files[]`에서 제외한다.
9. 새 manifest는 patch chain이 아니라 현재 최종 상태 전체를 기록한다.
10. 새 artifact와 재사용 참조를 검증하고 `compactVersion`을 상속한 canonical
    manifest의 `dataVersion`과 `manifestHash`를 계산한다.

incremental package는 기존 bundle을 수정하거나 동일 경로에 다시 업로드하지 않는다.

### compact

1. source release의 `manifestHash`·`compactVersion`과 대상 bundle group을 실행 시작 시
   고정한다.
2. source manifest와 참조 artifact를 검증해 최종 상태를 복원한다.
3. file group은 기존 file artifact를 그대로 재사용한다.
4. 선택한 bundle group만 현재 최종 파일로 다시 묶는다.
5. bundle group의 file override를 새 bundle에 포함한다.
6. 삭제 파일과 미참조 byte는 새 bundle에 포함하지 않는다.
7. compact 전후 경로·크기·group·`fileHash`가 같은지 검증한다.
8. `dataVersion`은 유지하고 `compactVersion`은 source 값보다 1 증가시킨다.
9. 새 canonical manifest의 `manifestHash`를 계산하고 새 bundle과 manifest를 불변
   경로에 생성한다.
10. channel 변경과 이전 artifact 삭제는 수행하지 않는다.

기존 설치는 경로와 `fileHash`가 같으면 artifact 위치가 달라도 새 bundle을 다운로드하지
않는다.

### CLI 기능

| 명령 | 책임 |
| --- | --- |
| `package` | 최초 또는 incremental release 생성 |
| `diff` | 논리 파일 차이와 물리 artifact 차이 계산 |
| `verify` | source, artifact, manifest와 signature 검증 |
| `compact` | 선택 bundle group의 새 baseline 생성 |
| `plan-download` | 로컬 상태에서 목표 release까지 필요한 artifact 계산 |
| `sign` | canonical manifest signature 생성 |

- 성공은 `0`, 입력·무결성·실행 실패는 구분된 non-zero exit code를 반환한다.
- CI용 machine-readable JSON 결과를 지원한다.
- 의미 있는 명령은 파일을 만들지 않는 dry-run을 지원한다.
- secret과 개인키 내용을 로그와 결과에 기록하지 않는다.

### diff와 download plan

- 최종 `files[]`를 기준으로 추가·변경·삭제·group 이동을 분류한다.
- 논리 데이터 차이와 물리 artifact 차이를 별도로 출력한다.
- download plan은 명시적인 target group 집합만 대상으로 계산한다.
- 최초 설치와 전역 release 갱신은 target manifest의 required group만 계획한다.
- optional group 요청은 현재 active manifest에서 요청한 group만 계획한다.
- 로컬 경로와 검증된 `fileHash`를 목표 manifest와 비교한다.
- artifact 위치가 달라도 경로와 `fileHash`가 같으면 다운로드하지 않는다.
- 필요한 파일이 bundle에만 있으면 해당 bundle 전체를 계획에 한 번 포함한다.
- file part는 누락 part만 받을 수 있지만 최종 결합 hash를 검증한다.
- 예상 다운로드 byte, 임시 공간, 파일 수와 bundle 수를 계산한다.

### Runtime 동작

1. 신뢰하는 channel 또는 서버 응답에서 `packageId`, `dataVersion`, `manifestHash`를
   받는다.
2. 목표 canonical manifest 원본 byte의 SHA-256이 `manifestHash`와 같은지 확인하고
   manifest와 signature를 검증한다.
3. 한 요청의 target group 집합을 activation batch로 고정한다. 최초 설치와 전역 release
   갱신에서는 target manifest의 required group만 선택한다.
4. adapter를 통해 batch의 누락 artifact를 content-addressed cache에 받는다. group별
   다운로드와 staging은 병렬로 수행할 수 있다.
5. artifact hash와 압축 해제 결과를 검증한다.
6. batch의 모든 group별 목표 파일을 staging에 구성하고 크기와 `fileHash`를 검증한 뒤
   각각 immutable installation으로 승격한다.
7. batch의 모든 group이 준비된 경우에만 새 active pointer와 group install state를
   포함한 `PackageState` 전체를 한 번 원자적으로 교체한다.
8. 취소·실패·프로세스 종료 시 검증된 cache를 보존하고 다음 실행에서 이어받는다.
9. 활성화 실패 시 이전 `PackageState`와 installation을 계속 사용한다.
10. optional group 요청은 현재 active manifest를 목표로 요청한 group 집합만 준비하고,
    전역 active pointer는 바꾸지 않은 채 모든 요청 group의 상태를 단일
    `PackageState` revision으로 갱신한다.

멀티플레이 클라이언트는 독립적으로 최신 channel을 선택하지 않고 접속할 서버가 요구하는
정확한 client package `dataVersion`을 사용한다.

release manifest는 patch chain이 아니라 최종 상태 전체를 기록하므로 다음 release의
download plan은 이전 release pointer가 없어도 검증된 로컬 경로·`fileHash`로 계산할 수
있다. 로컬 active pointer와 group install state는 활성 상태 증명·복구·검사 최적화를
위해 유지한다.

### `PackageState`와 optional group 상태

`PackageState`는 package마다 하나만 유지하고 다음 필드를 포함한다.

파일이 없으면 활성 package가 없는 상태로 간주한다. 파일은 UTF-8 canonical JSON으로
직렬화하지만 hash나 signature를 신뢰 근거로 사용하지 않는다. v1 state의
`schemaVersion`은 `1`이다. 초기 commit의 `stateRevision`은 `1`이고 이후 성공한
commit마다 1 증가한다. `groups[]`는 group 이름의 ordinal byte 순서로 정렬한다.

| 필드 | JSON 형식 | 규칙 |
| --- | --- | --- |
| `schemaVersion` | integer | v1에서는 `1` |
| `stateRevision` | integer | `1` 이상이며 state commit마다 1 증가 |
| `packageId` | string | state가 속한 package |
| `active.dataVersion` | string | required group까지 활성화된 전역 논리 버전 |
| `active.manifestHash` | string | lowercase SHA-256 64자 |
| `groups[]` | array | active manifest의 모든 group을 이름 순으로 기록 |
| `groups[].name` | string | manifest에 선언된 group 이름 |
| `groups[].status` | string | `ready`, `notInstalled`, `stale` 중 하나 |
| `groups[].verifiedManifestHash` | string | `ready`·`stale`에서 필수인 lowercase SHA-256 64자 |
| `groups[].installationKey` | string | `ready`·`stale`에서 필수인 비어 있지 않은 opaque key |

group 상태:

| `status` | 추가 필드 | 의미 |
| --- | --- | --- |
| `ready` | `verifiedManifestHash`, `installationKey` | active manifest 기준으로 사용 가능 |
| `notInstalled` | 없음 | 다운로드하지 않은 optional group |
| `stale` | 이전 `verifiedManifestHash`, `installationKey` | byte는 보존하지만 active release에서 사용 불가 |

`installationKey`는 storage adapter가 해석하는 불투명 식별자이며 절대 OS 경로를
저장하지 않는다. `installing`과 다운로드 진행률은 committed `PackageState`에 기록하지
않고 staging과 cache의 일시 상태로 관리한다.

`PackageState` 불변 조건:

- `groups[]`는 active manifest의 모든 group을 정확히 한 번 포함하고 알 수 없는 group을
  포함하지 않는다.
- 모든 required group은 `ready`이고 `verifiedManifestHash`가
  `active.manifestHash`와 같다.
- `ready` optional group도 `verifiedManifestHash`가 `active.manifestHash`와 같다.
- `notInstalled`와 `stale`는 optional group에만 허용한다.
- `notInstalled` group에는 `installationKey`가 없다.
- `stale` group의 installation은 보존할 수 있지만 애플리케이션에 활성 데이터로
  노출하지 않는다.
- 모든 `installationKey`는 준비·검증이 끝난 immutable installation을 가리킨다.
- group별 독립 `dataVersion`·channel·공개 release pointer는 만들지 않는다.

최초 `PackageState` commit에서는 target manifest의 required group을 모두 `ready`로
기록하고 optional group을 모두 `notInstalled`로 기록한다. 이후 optional group 설치가
완료되면 active pointer는 유지하고 해당 group만 `ready`로 바꾼 새 state revision을
commit한다.

여러 group을 한 번에 처리하는 activation batch 규칙:

- 같은 요청에 포함된 target group 집합 전체가 하나의 commit 단위다. 서로 다른 요청은
  별도 batch이며 자동으로 하나의 transaction으로 합치지 않는다.
- download, artifact 검증과 staging은 group별로 병렬 수행할 수 있지만 이 과정은
  committed `PackageState`를 변경하지 않는다.
- 모든 target group을 검증된 immutable installation으로 승격한 뒤에만 state commit을
  시작한다. commit 전 installation은 state가 참조하지 않으므로 활성 데이터로 노출하지
  않는다.
- group 하나라도 준비에 실패하면 batch 전체의 state 변경을 취소하고 이전
  `PackageState`를 유지한다.
- state commit 직전에 package writer lock을 얻고 `active.manifestHash`와
  `stateRevision`을 다시 확인한다. 둘 중 하나라도 작업 시작 snapshot과 다르면 stale
  state를 쓰지 않고 최신 state 기준으로 batch를 다시 계획한다. 이미 검증한
  cache·installation은 재사용할 수 있다.
- 성공 시 모든 target group 상태를 반영한 `PackageState` 전체를 한 번 교체하고
  `stateRevision`도 한 번만 증가시킨다. reader는 batch 전 state 또는 batch 후 state만
  관찰하며 일부 group만 `ready`인 중간 state를 관찰하지 않는다.

전역 release 갱신 시 optional group 처리:

- 설치하지 않은 group은 `notInstalled`를 유지한다.
- 이전·목표 manifest에서 경로·크기·`fileHash`가 모두 같으면 다운로드 없이 기존
  installation을 재사용하고 `verifiedManifestHash`만 새 active manifest로 갱신한다.
- 파일 상태가 달라지면 기존 installation은 보존하되 group을 `stale`로 바꾼다.
- target manifest에서 삭제된 group은 state에서 제거하고 installation은 더 이상
  활성 데이터로 노출하지 않는다.
- optional group이 target manifest에서 required로 바뀌면 전역 active pointer 교체 전에
  반드시 준비한다.

### Runtime adapter contract

`GamePatchKit.Runtime`은 최소한 다음 platform contract를 정의한다.

- `IArtifactTransport`: immutable channel·manifest·artifact를 읽기 전용 stream으로 연다.
- `IRuntimeStorage`: package별 writer lock, `PackageState` 읽기·원자적 교체,
  content-addressed cache, staging stream과 immutable installation 승격을 제공한다.
- `ICompressionCodec`: Core가 정의한 codec ID별 streaming 압축 해제 contract다.
- `CancellationToken`과 `IProgress<PatchProgress>`: host의 취소와 진행 상태를 전달한다.

contract 규칙:

- Runtime의 download plan과 검증 순서는 adapter와 무관하게 동일하다.
- Runtime이 `PackageState` 모델·직렬화·불변 조건과 group 상태 전환을 소유하고,
  adapter는 byte와 opaque `installationKey`만 저장한다.
- adapter는 manifest 의미, hash 판단과 retry 정책을 별도로 구현하지 않는다.
- Runtime이 오류 분류, retry와 복구 상태를 소유한다.
- package별 writer는 하나만 허용하며, state writer는 기존 `stateRevision`을 확인한 뒤
  다음 revision을 commit한다.
- 여러 group activation batch도 단일 state 교체로 commit하며 group별 state commit을
  허용하지 않는다.
- reader는 원자적으로 교체된 완전한 state snapshot만 읽는다.
- adapter 구현 차이로 활성화 결과가 달라지지 않아야 한다.
- Core와 Runtime의 public API는 `UnityEngine`과
  `NativeCompressions.Zstandard` type을 노출하지 않는다.
- 외부 adapter가 contract를 만족하는지 확인할 수 있도록 동일한 conformance fixture와
  test suite를 제공한다.

### DotNet adapter

- `HttpClient`를 외부에서 주입받아 연결 재사용과 테스트를 지원한다.
- streaming download와 파일 write를 사용한다.
- cancellation token과 progress를 Runtime에 전달한다.
- package별 root 아래에 state, manifest cache, artifact cache, immutable installation과
  staging을 분리한다.
- `PackageState`는 `<runtimeRoot>/packages/<packageId>/state/package-state.json` 한
  파일로 저장한다.
- package writer lock은 OS file lock 또는 동등한 exclusive handle로 구현하고 lock
  파일의 존재 여부만으로 판단하지 않는다.
- activation batch의 모든 installation을 완성·검증·승격한 뒤 같은 filesystem의 임시
  state 파일을 flush하고 rename/`File.Replace` 등 OS별 원자적 연산으로
  `package-state.json`을 한 번 교체한다.
- state 파일은 in-place로 덮어쓰지 않으며 state 교체 전에는 기존 state와 referenced
  installation을 변경하거나 삭제하지 않는다.
- 손상되거나 알 수 없는 schema의 state는 활성 근거로 사용하지 않는다. host가 제공한
  신뢰 가능한 target pointer의 manifest를 다시 검증하고 cache·installation의 file
  hash를 확인해 새 state를 구성하며, 복구 중 기존 cache·installation은 보존한다.
- 신뢰 가능한 target pointer가 없으면 cache나 디렉터리 이름만으로 active release를
  추정하지 않고 활성 package가 없는 상태를 반환한다.
- ASP.NET 서버와 worker는 시작 전 필수 package를 검증하고 준비되지 않으면 기동을
  실패시킬 수 있다.

DotNet adapter filesystem layout:

```text
<runtimeRoot>/
└── packages/
    └── <packageId>/
        ├── state/
        │   ├── package-state.json
        │   └── package-state.lock
        ├── manifests/
        │   └── <manifestHash>/
        ├── cache/
        │   └── artifacts/
        ├── installs/
        │   └── <installationKey>/
        └── staging/
            └── <operationId>/
```

### 외부 host 연동

- 외부 host는 호환되는 `GamePatchKit.Core`와 `GamePatchKit.Runtime` assembly를 소비하고
  필요한 interface만 애플리케이션 코드에서 구현한다.
- Unity 사용자는 `IArtifactTransport`를 `UnityWebRequest`,
  `IRuntimeStorage`를 `Application.persistentDataPath` 기반으로 구현할 수 있다.
- 큰 payload를 managed memory에 모두 올리지 않고 interface의 stream으로 전달한다.
- Unity lifecycle과 애플리케이션 중단은 `CancellationToken`으로 Runtime에 전달한다.
- progress는 `IProgress<PatchProgress>`를 UI에 연결한다.
- zstd가 필요한 host는 호환되는 `ICompressionCodec`을 주입한다. 선정한
  `NativeCompressions.Zstandard` version이 해당 target에서 지원되면 기본 adapter를
  재사용할 수 있다.
- 현재 NativeCompressions preview가 지원하지 않는 iOS IL2CPP target은 기본 codec
  지원 대상으로 선언하지 않는다. 해당 target은 호환 zstd codec을 별도로 구현하거나
  package 설정에서 압축을 사용하지 않아야 한다.
- 외부 구현은 download plan, manifest·hash 검증, retry와 활성화 규칙을 복제하지 않는다.
- 외부 storage 구현은 Runtime의 `PackageState` byte와 `installationKey`를 해석하지 않고
  package별 atomic read·replace와 immutable installation contract만 구현한다.
- GamePatchKit 저장소는 Unity 전용 assembly와 package를 빌드하거나 배포하지 않는다.

### publish와 channel 연동

Packager는 다음 publish tree를 로컬에 생성한다.

1. 불변 file·bundle artifact
2. 불변 release manifest와 signature
3. 선택적 channel pointer 입력 자료

외부 publisher 순서:

1. 새 artifact 업로드
2. 원격 크기와 SHA-256 검증
3. manifest와 signature 업로드
4. 원격 manifest와 모든 참조 재검증
5. 승인 후 channel pointer 교체

cache 정책:

- hash 경로 artifact와 `manifestHash` 경로 manifest는 immutable 장기 cache
- channel pointer는 `no-cache` 또는 짧은 TTL과 재검증
- rollback은 artifact 복사 없이 이전 release로 channel 전환

Storage credential, 원격 원자적 교체와 승인 정책은 publisher 책임이다.

### 서명과 무결성

- SHA-256은 손상 검출이며 manifest 출처를 단독으로 보장하지 않는다.
- public 배포는 Ed25519 canonical manifest 서명을 기본 운영 조건으로 한다.
- 개인키는 설정, Git, manifest, build report와 로그에 저장하지 않는다.
- `manifest.sig`에는 알고리즘, key ID와 signature만 기록한다.
- Runtime은 신뢰하는 public key 목록과 key ID로 manifest를 검증한다.
- key rotation 동안 둘 이상의 public key를 신뢰할 수 있다.
- 서명 필수 모드에서는 누락·알 수 없는 key ID·검증 실패를 허용하지 않는다.

### 재현성과 멱등성

- 같은 source, 설정과 이전 manifest는 같은 artifact, canonical manifest와
  `manifestHash`를 생성한다.
- hash 경로에 다른 byte가 있으면 덮어쓰지 않고 실패한다.
- 같은 release 재생성은 기존 결과를 검증하고 재사용한다.
- source revision과 시각은 build report에만 기록한다.
- 임시 파일은 최종 output과 같은 filesystem에서 만들고 검증 후 rename한다.
- 실패한 package와 compact는 완성된 기존 결과를 변경하지 않는다.

### 오류 처리

다음 상황은 실패다.

- `gamepatchkit.yml`의 빈 document·다중 document·mapping이 아닌 root
- YAML anchor·alias·merge key·custom tag·중복 mapping key 사용
- config schema 오류
- packageId·group 이름 규칙 위반
- 경로 정규화 실패, symlink와 중복 경로
- 둘 이상의 group에 일치하는 파일
- 실행 중 source 파일 변경
- hash 경로의 기존 byte 불일치
- 최대 크기를 넘는 bundle
- 누락 artifact와 존재하지 않는 bundle entry
- `dataVersion`·`manifestHash` 재계산 불일치 또는 잘못된 `compactVersion`
- signature 검증 실패
- compact 전후 논리 상태 불일치
- staging 검증 또는 원자적 활성화 실패
- `PackageState` schema·불변 조건 위반, stale `stateRevision`과 package writer lock 실패
- `PackageState`가 참조하는 manifest 또는 installation 누락·불일치

오류에는 packageId, 상대 경로, group과 단계는 포함하되 데이터 내용과 secret은 포함하지
않는다.

### 성능·관측 기준

- 기준 fixture는 최소 1만 파일, 원본 합계 1GiB다.
- 전체 입력을 메모리에 올리지 않고 streaming hash·compression·download를 사용한다.
- 1만 파일 수준에서는 단일 canonical JSON manifest를 우선 사용한다.
- 병렬 처리가 파일 순서, bundle 경계와 manifest byte를 바꾸지 않아야 한다.
- bundle group은 작은 파일의 객체·요청 수를 줄이는 용도로 사용한다.
- file group은 객체 수보다 독립 변경·선택 다운로드를 우선한다.
- content-defined chunking은 큰 단일 파일의 실제 비용을 측정한 뒤 검토한다.

package·diff·Runtime 결과는 최소한 다음 값을 제공한다.

- 전체·group별 파일 수와 byte
- 생성·재사용 file artifact와 bundle 수·byte
- 추가·변경·삭제 파일 수
- 예상 다운로드 byte와 artifact 요청 수
- 임시 저장공간
- compact 전후 신규 설치 byte 차이
- cache hit byte
- 단계별 실행 시간
- 취소·재시도·검증 실패 결과

### 배포 산출물

- NuGet `GamePatchKit.Core`
- NuGet `GamePatchKit.Runtime`
- NuGet `GamePatchKit.Compression.NativeCompressions`
- NuGet `GamePatchKit.DotNet`
- .NET tool 또는 실행 파일 `gpk`
- versioned JSON Schema 3종(`package-config`, `release-manifest`, `channel`)
- package별 manifest와 artifact
- Runtime adapter conformance fixture와 test suite

schema와 manifest 호환 버전은 같은 repository release에서 함께 관리한다.

### 구현 순서

1. solution, 프로젝트 의존 방향과 target framework를 구성한다.
2. config YAML 입력 계약, config·manifest JSON Schema와 canonicalization 규칙을
   고정한다.
3. Core의 hash, identity, manifest hash, diff, download plan과 `ICompressionCodec`을
   구현한다.
4. `NativeCompressions.Zstandard` 기반 기본 codec adapter와 round-trip test를 구현한다.
5. Packager의 파일 탐색, file artifact와 최초·incremental manifest를 구현한다.
6. deterministic bundle과 compact를 구현한다.
7. CLI의 package, diff, verify, compact, plan-download와 sign을 구현한다.
8. Runtime의 interface, cache, staging, 검증, 활성화와 복구 상태 머신을 구현한다.
9. DotNet adapter와 mock HTTP 통합 테스트를 구현한다.
10. 외부 host용 adapter conformance fixture와 test suite를 제공한다.
11. 서명·key rotation 검증을 구현한다.
12. 1만 파일·1GiB fixture로 성능과 메모리 사용량을 검증한다.
13. README에 CLI, package 설정, Runtime interface 통합과 publisher 계약을 문서화한다.

### 검증 기준

1. 같은 입력과 설정으로 두 번 package하면 artifact와 manifest byte가 같다.
2. 기본 설정은 모든 파일을 file artifact로 생성한다.
3. file group은 최초·incremental·compact 모두에서 bundle에 포함되지 않으며,
   bundle에서 file group으로 이동하거나 `artifactMode: file`로 바뀐 파일은 file
   artifact override를 사용한다.
4. bundle group은 group 경계를 넘지 않는 최대 크기 이하의 deterministic bundle을
   생성한다.
5. 큰 단일 bundle entry는 file part로 fallback한다.
6. 파일 하나만 변경하거나 compression 설정만 바뀐 incremental release는 기존
   artifact와 bundle을 다시 만들지 않는다.
7. 삭제 파일은 새 artifact 없이 최종 `files[]`와 `dataVersion`에 반영된다.
8. compact는 bundle override를 통합하고 file group artifact를 재사용한다.
9. compact 전후 논리 상태가 같으면 `dataVersion`은 같고 `compactVersion`은 1
   증가하며 `manifestHash`는 달라진다.
10. compact로 artifact 위치만 바뀐 동일 파일을 Runtime이 다시 다운로드하지 않는다.
11. 손상된 part, bundle, manifest와 signature를 모두 거부한다.
12. 모든 artifact는 `maxArtifactBytes` 이하다.
13. client package에 server-private 경로·hash가 포함되지 않는다.
14. DotNet과 conformance용 fake adapter가 같은 fixture에서 동일한 download plan과
    활성화 결과를 만든다.
15. 다운로드 취소 후 재개해 검증된 cache를 재사용한다.
16. staging 또는 활성화 실패 시 이전 release를 유지한다.
17. 1만 파일·1GiB fixture를 전체 메모리 적재 없이 package·verify·download한다.
18. 실패한 package와 compact가 기존 artifact와 manifest를 변경하지 않는다.
19. NativeCompressions adapter는 고정 option으로 같은 입력에 같은 zstd byte를 만들고
    streaming round-trip 후 원본 hash를 복원한다.
20. 지원하지 않거나 주입되지 않은 compression codec을 manifest가 요구하면 활성화 전에
    실패한다.
21. Core와 Runtime assembly는 Unity API reference를 포함하지 않으며 Unity 전용
    배포 산출물을 생성하지 않는다.
22. 최초 설치와 전역 release 갱신은 required group만 다운로드하며, optional group은
    요청 시 현재 active manifest 기준으로 별도 설치한다. 변경된 optional group은 새
    release에서 `stale`, 동일한 group은 다운로드 없이 새 manifest에 재연결된다.
23. `PackageState` 교체 전 실패·취소·프로세스 종료가 발생하면 이전 state와 installation이
    유지되고, 교체 후에는 새 state 전체만 관찰된다. 손상된 state는 활성 근거로 사용하지
    않고 검증 가능한 cache·installation을 보존한다. 여러 group activation batch에서
    일부 group만 `ready`인 중간 state는 관찰되지 않는다.
24. `gamepatchkit.yml`은 단일 non-empty document와 mapping root만 허용한다. 유효한
    설정은 `package-config.schema.json`과 모델 검증을 통과하고, 다중 document,
    anchor·alias·merge key·custom tag와 중복 key는 package 실행 전에 거부된다.

### 제약 / 비고

- v1은 단순성과 재현성을 위해 bundle 전체 다운로드를 사용한다.
- bundle 크기와 group 구성은 자동 최적화하지 않고 측정 결과로 조정한다.
- override 누적 시점의 compact 판단은 외부 운영 계층이 결정한다.
- compact는 channel promote와 garbage collection을 수행하지 않는다.
- Runtime은 데이터를 안전하게 전달·활성화하지만 게임별 데이터 로딩 의미는 알지 못한다.
- NativeCompressions가 preview인 동안 지원 platform은 고정 version의 검증 결과로
  제한하며 upstream API·runtime 변경은 adapter 내부에서 흡수한다.

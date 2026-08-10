# GamePatchKit CLI `releaseVersion` 개발 계획

> 기준 문서: [`docs/prd/gamepatch-kit-cli-prd.md`](../prd/gamepatch-kit-cli-prd.md)
>
> 연계 문서: [`game-patch-kit-cli-upload-design.md`](game-patch-kit-cli-upload-design.md)
>
> 상태: 구현 전 합의용 초안

## 1. 기능 요약

`gpk build`가 생성하는 `manifest.json` 루트에 0 이상의 `releaseVersion`을 기록한다. 첫 성공 빌드는 0이고, 이후에는 `releaseVersion`을 제외한 매니페스트 내용이 이전 성공 매니페스트와 달라질 때만 이전 값보다 1 증가한다.

이 값은 `gpk upload`가 `manifests/<releaseVersion>.json`을 게시하고 지정된 수동 배포 스크립트가 배포 포인터를 갱신할 때 사용하는 세대 식별자다.

## 2. 가정과 확인 필요 사항

### 가정

- `releaseVersion`은 기존 그룹 버전과 같은 C# `int`로 표현한다. JSON에서는 정수로 기록한다.
- 변경 여부는 원본 `manifest.json`의 공백이나 필드 순서가 아니라, 기존 정렬·직렬화 규칙을 적용한 두 `PatchManifest`에서 `releaseVersion`만 같은 값으로 정규화한 결과로 판정한다.
- 따라서 `groups`가 같더라도 `sourcePath` 또는 `sourceCommit`이 달라지면 새 릴리스다. 특히 source 밖의 커밋으로 `sourceCommit`만 바뀐 경우에도 `releaseVersion`은 증가한다.
- 현재 기능은 첫 공개 매니페스트 계약에 포함되는 것으로 본다. `schemaVersion: 1`이지만 `releaseVersion`이 없는 과거 개발 산출물은 승계하지 않고 기존 매니페스트 검증에서 거부한다.
- 소스 저장소와 분리된 private 상태 Git 저장소가 마지막 게시 성공 시점의 `manifest.json`과 `.gpk-upload-state.json` 두 파일만 보존한다. 지정된 수동 배포 스크립트는 빌드 전에 이 상태를 `--output`에 복원한다.
- 이전 `manifest.json`이 없는 상태는 실제 첫 배포에만 허용한다. 기존 Supabase 버킷이 있는 프로젝트에서 상태 저장소를 잃은 경우 0부터 다시 시작하지 않는다.
- 같은 패치 데이터 프로젝트의 `build`·`verify`·`upload`·상태 저장소 갱신은 지정된 수동 배포 환경 한 곳에서 동시에 실행하지 않는다.
- 첫 Storage 호출 뒤 배포가 실패하면 `manifest.json.sourceCommit`의 정확한 SHA를 checkout해 같은 CLI·압축 구현 버전으로 다시 빌드한다. 그 배포가 끝나기 전에는 더 새로운 source commit을 게시하지 않는다.

### 확인 필요

- 이미 배포되어 계속 승계해야 하는 `releaseVersion` 없는 매니페스트가 있다면 구현 전에 마이그레이션 규칙 또는 `schemaVersion` 증가를 별도 결정해야 한다. 이 계획에는 자동 마이그레이션을 넣지 않는다.

## 3. 요구사항 정리

### 입력

- 이전 성공 `<output>/manifest.json`의 `releaseVersion`
- 현재 빌드가 완성한 새 `PatchManifest`

### 출력

- 첫 빌드: `releaseVersion: 0`
- 이전과 내용이 같은 빌드: 이전 `releaseVersion` 유지
- 이전과 내용이 다른 빌드: 이전 `releaseVersion + 1`

### 제약

- 사용자가 yaml이나 명령 옵션으로 값을 지정하지 않는다.
- `releaseVersion`은 `[JsonProperty]`로 이름과 직렬화 순서를 고정하고 필수 필드로 읽는다.
- 음수 값은 그룹 판단과 산출물 쓰기 전에 이전 매니페스트 검증에서 거부한다.
- 변경된 빌드에서 이전 값이 `int.MaxValue`이면 더 늘릴 수 없다는 `BuildException`으로 중단하고 기존 매니페스트를 유지한다.
- `schemaVersion`은 1을 유지한다.
- 매니페스트 비교와 최종 쓰기는 같은 정렬·JSON 설정을 사용해야 한다.
- CLI는 `manifest.json` 부재가 실제 첫 배포인지 상태 유실인지 원격 조회로 판정하지 않는다. 별도 상태 Git 저장소 복원과 수동 배포 직렬화가 이 전제를 보장한다.

## 4. 실행 방식

- [x] skill 단독 (`csharp-feature-architect` skill만 사용)
- [ ] agent + skill

선택 사유: 단일 CLI 프로젝트의 기존 DTO·직렬화·빌드 조정 코드 세 곳만 바꾸며 외부 시스템이나 새 bounded context가 없다.

## 5. 가장 단순한 구조 후보

- `PatchManifest`에 `ReleaseVersion` 속성 하나를 추가한다.
- `ManifestStore`에 두 매니페스트의 릴리스 내용이 같은지 비교하는 정적 메서드 하나를 추가한다. 정렬과 JSON 설정의 소유권을 기존 클래스에 그대로 둔다.
- `BuildCommand`가 새 매니페스트 후보를 완성한 뒤 이전 매니페스트와 비교해 최종 값을 정하고 기존 `WriteAtomically`로 기록한다.
- 새 생산 코드 파일, 새 타입, 인터페이스, DI 등록은 만들지 않는다.

## 6. 단순 구조 실패 조건

없음 — 단순 구조로 확정한다.

버전 계산은 이전 매니페스트 하나와 새 후보 하나만 비교하는 순차 절차다. 계산 코드 내부에는 새 영속 저장소, 동시 쓰기 조정이나 별도 정책 객체가 필요하지 않다. 상태 Git 저장소와 수동 배포 직렬화는 외부 운영 계약으로 둔다.

## 7. 책임/도메인 분해

| 책임 단위 | 종류 | 설명 |
| --- | --- | --- |
| `PatchManifest.ReleaseVersion` | JSON DTO 필드 | 매니페스트 전체의 세대 번호 |
| `ManifestStore.HasSameReleaseContent` | 정적 비교 절차 | 두 매니페스트를 기존 규칙으로 정규화하고 `releaseVersion`을 제외한 내용이 같은지 판정 |
| `BuildCommand` | Application service | 첫 값, 유지, 증가, 최대값 중단을 결정하고 최종 매니페스트 기록 |

별도 Entity, Value Object, Domain Event는 만들지 않는다. 이 기능은 기존 매니페스트의 단일 정수 필드 계산이다.

## 8. 적용 패턴과 정당화

없음.

Strategy, Factory, Mediator를 도입할 정책 분기나 대체 구현이 없다.

## 9. 인터페이스 생성 근거

없음.

외부 의존성이나 대체 구현이 추가되지 않고 실제 파일 시스템을 사용하는 기존 통합 테스트로 검증할 수 있다.

## 10. 인터페이스·클래스 시그니처

```csharp
internal sealed class PatchManifest
{
    [JsonProperty("releaseVersion", Required = Required.Always, Order = 1)]
    public int ReleaseVersion { get; init; }
}

internal static class ManifestStore
{
    internal static bool HasSameReleaseContent(PatchManifest left, PatchManifest right);
}

internal sealed class BuildCommand
{
    public BuildSummary Execute(BuildArguments arguments);
}
```

`SchemaVersion` 뒤에 `ReleaseVersion`을 두고 기존 `SourcePath`·`SourceCommit`·`Groups`의 `Order`는 각각 한 칸 뒤로 옮긴다.

`HasSameReleaseContent`는 두 입력을 직접 바꾸지 않는다. 각각을 기존 `Sort`와 동일한 규칙으로 복사하면서 `ReleaseVersion`을 0으로 맞추고, `Formatting.None`, UTF-8 BOM 없음, 끝 개행 없음이라는 기존 직렬화 계약으로 얻은 바이트를 비교한다. 비교 전용 JSON 설정을 새로 만들지 않는다.

`BuildCommand.Execute`의 외부 시그니처는 바꾸지 않는다. 현재 그룹 빌드가 끝난 뒤 매니페스트 후보를 만들고 다음 순서로 값을 확정한다.

1. 이전 매니페스트가 없으면 0을 쓴다.
2. 이전 매니페스트가 있고 릴리스 내용이 같으면 이전 값을 쓴다.
3. 내용이 다르면 이전 값보다 1 큰 값을 쓴다.
4. 3번에서 이전 값이 `int.MaxValue`이면 사용자 메시지가 있는 `BuildException`으로 중단한다.

## 11. 파일·폴더 배치 제안

```text
src/GamePatchKit.Cli/
├── PatchManifest.cs             (변경: ReleaseVersion)
├── ManifestStore.cs             (변경: 검증·정렬 복사·릴리스 내용 비교)
└── BuildCommand.cs              (변경: 최종 버전 계산)

tests/GamePatchKit.Cli.Tests/
├── TestManifestStore.cs         (변경: 필수 필드·음수·비교 규칙)
└── TestBuildCommand.cs           (변경: 첫 값·유지·증가·실패 보존)

docs/cli/
└── build.md                      (구현 완료 시 변경: releaseVersion 동작 설명)
```

새 생산 코드 파일과 새 테스트 파일은 없다. 기존 책임 위치를 그대로 사용한다.

## 12. 도입하지 않은 구조

- `ReleaseVersionService` 또는 `IReleaseVersionPolicy`: 계산 규칙 하나에 호출자도 `BuildCommand` 하나뿐이다.
- `releaseVersion` 전용 상태 파일: 계산 기준은 기존 `manifest.json` 하나면 충분하다. 업로드 계획의 `.gpk-upload-state.json`은 업로드 델타용이며 두 파일은 외부 상태 Git 저장소가 함께 보존한다.
- CLI 락과 원격 버전 조회: 같은 패치 데이터 프로젝트의 수동 배포를 외부에서 직렬화하고 상태를 먼저 복원한다.
- CLI `--commit`·`--rebuild` 옵션: 실패한 대상은 `manifest.json.sourceCommit`으로 식별하고 Git checkout 뒤 기존 build 명령을 재실행한다.
- yaml 설정과 CLI 옵션: 그룹 버전과 달리 사용자가 결정할 정책이 없다.
- 해시 기반 릴리스 ID: PRD가 단조 증가 정수를 요구한다.
- 이전 매니페스트 자동 마이그레이션: 호환 정책이 확정되지 않았고 현재 요구사항은 필수 필드 검증이다.
- `schemaVersion` 증가: PRD가 초기값 1 유지를 확정했다.

## 13. 단순화 자가 검토 결과

- 새 인터페이스 수: 0
- 새 생산 코드 타입 수: 0
- 새 생산 코드 파일 수: 0
- 새 폴더 계층 증가: 0
- 적용 패턴 수: 0
- 단일 구현 인터페이스: 없음
- 요구사항에서 직접 도출되지 않은 도메인 객체: 없음
- 종합 판정: 기존 세 클래스에 필요한 변경만 추가하는 단순 구조 유지

## 14. 위임 다음 단계

- 구현: 일반 C# 코딩 작업으로 진행하며 `csharp-coding-standards`를 적용한다.
- 테스트: 기존 xUnit 통합 테스트 구조 안에서 작성한다. 새 테스트 프레임워크나 테스트 대역은 추가하지 않는다.
- 구현 완료 후 [`docs/cli/build.md`](../cli/build.md)에 새 필드와 증가 규칙을 반영한다.
- `releaseVersion` 구현과 검증이 끝난 뒤 upload 계획을 진행한다.
- 지정된 수동 배포 스크립트는 [`upload 계획`](game-patch-kit-cli-upload-design.md)의 상태 Git 저장소 복원·단일 commit 저장·동시 실행 금지·같은 SHA 재실행 순서를 적용한다. 이 스크립트는 releaseVersion·upload 구현과 별도 작업이지만 운영 배포의 필수 선행 조건이다.

## 15. 단계별 구현 계획

`R1 → R2 → R3` 순서로 진행한다. 각 단계의 테스트가 통과한 뒤 다음 단계로 넘어간다.

### R1. 매니페스트 계약 추가

- 수행:
  - `PatchManifest.ReleaseVersion`을 필수 JSON 필드로 추가하고 직렬화 순서를 고정한다.
  - `ManifestStore.Validate`에 0 이상 검증을 추가한다.
  - `ManifestStore.Sort`가 값을 보존하도록 한다.
- 검증:
  - `WriteAtomically`가 `schemaVersion` 바로 뒤에 `releaseVersion`을 기록한다.
  - 정상 값 0을 왕복해서 읽는다.
  - 필드가 없거나 음수인 이전 매니페스트를 거부하고 파일을 바꾸지 않는다.

### R2. 릴리스 내용 비교

- 수행:
  - `ManifestStore.HasSameReleaseContent`를 추가한다.
  - 기존 정렬과 serializer 설정을 재사용하도록 직렬화 내부 절차를 한 곳으로 모은다.
- 검증:
  - `ReleaseVersion`만 다르면 같은 내용으로 판정한다.
  - 그룹이나 엔트리 입력 순서만 다르면 같은 내용으로 판정한다.
  - `sourcePath`, `sourceCommit`, 그룹 설정, 아카이브, 엔트리 중 하나가 다르면 다른 내용으로 판정한다.

### R3. 빌드 계산과 회귀 검증

- 수행:
  - `BuildCommand`가 완성한 후보에 첫 값·유지·증가 규칙을 적용한다.
  - 값 확정 뒤 기존 원자적 쓰기 경로를 그대로 사용한다.
  - `docs/cli/build.md`를 갱신한다.
- 검증:
  - 첫 빌드가 0이다.
  - 추적 파일 변경, 파일 추가·삭제, 그룹 버전 증가 중 대표 변경이 이전 값보다 1 큰 값을 만든다.
  - 같은 `HEAD`에서 다시 빌드하면 값과 매니페스트 바이트가 그대로다.
  - source 밖의 새 커밋으로 `sourceCommit`만 바뀌어도 값이 증가한다.
  - 실패한 빌드는 이전 매니페스트와 그 `releaseVersion`을 유지하고, 다음 성공 빌드는 마지막 성공 값에서 한 번만 증가한다.
  - 변경이 필요한 상태에서 이전 값이 `int.MaxValue`이면 기존 매니페스트를 바꾸지 않고 중단한다.
  - 실제 첫 배포가 아닌 실행은 상태 Git 저장소의 이전 `manifest.json`을 복원한 뒤 빌드해 마지막 성공 값에서 이어진다.
  - 첫 Storage 호출 뒤 실패한 배포는 기록된 `sourceCommit` SHA를 checkout하고 같은 CLI·압축 구현 버전으로 다시 빌드했을 때 같은 `releaseVersion`과 매니페스트 바이트를 만든다.
  - `dotnet test`와 `dotnet pack`이 통과한다.

## 16. 완료 정의와 추적성

| PRD 완료 조건 | 대표 검증 |
| --- | --- |
| CLI 4. 무변경 재빌드의 매니페스트 동일성 | 같은 `HEAD` 재빌드 후 버전·파일 바이트 동일 |
| CLI 20. 실패 시 기존 매니페스트 보존 | 실패 전후 `releaseVersion`과 매니페스트 바이트 동일 |
| CLI 21. 이전 매니페스트 관계 검증 | 필드 누락·음수 거부 |
| CLI 23. 첫 값·증가·유지 | 첫 빌드 0, `releaseVersion` 제외 내용 변경 시 +1, 같은 내용이면 유지 |

다음 조건을 모두 만족하면 구현 완료다.

- 새 생산 코드 파일·타입·인터페이스 없이 기존 세 클래스에서 동작한다.
- `releaseVersion`이 필수 필드로 직렬화·역직렬화되고 음수를 거부한다.
- 변경 판정이 기존 정렬·직렬화 설정과 단일 구현을 공유한다.
- 첫 값 0, `releaseVersion` 제외 내용 변경 시 1 증가, 같은 내용이면 유지가 자동 테스트로 검증된다.
- 실제 첫 배포가 아닌 수동 배포는 별도 상태 Git 저장소의 `manifest.json`을 복원하고 지정된 환경에서 같은 프로젝트의 전체 흐름을 직렬화한다.
- 기존 P0~P10 테스트와 패키징 검증이 모두 통과한다.

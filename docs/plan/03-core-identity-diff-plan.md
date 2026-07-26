# 03. Core identity·manifest hash·diff·download plan

> PRD 섹션: `dataVersion`·`compactVersion`·`manifestHash`, diff와 download plan,
> 프로젝트 책임
> `GamePatchKit.Core`, Runtime adapter contract

## 목표

Packager와 Runtime이 공유하는 순수 로직을 Core에 구현한다: streaming hash,
`dataVersion`·`manifestHash` 계산, release diff, download plan, `ICompressionCodec`
contract.

## 선행 단계

02

## 작업 항목

### hash

- [x] streaming SHA-256 유틸리티 (전체 입력을 메모리에 올리지 않는다)

### `dataVersion` 계산

- [x] 입력: `packageId`, group 이름과 `required` 같은 소비 의미, 각 파일의 정규화
      경로·group·원본 크기·`fileHash`
- [x] 제외: artifact 종류, bundle 경계·압축 방식, artifact 경로·part 구성,
      build 시각·source revision·실행 환경
- [x] 계산 입력의 canonical 직렬화 형식을 고정하고 digest를 계산한다

### `compactVersion` 규칙

- [x] 최초 package는 `0`, incremental package는 이전 값을 상속한다
- [x] candidate manifest를 source `compactVersion`으로 canonicalize한 byte가 source와
      같으면 compact no-op으로 판정하고 기존 `compactVersion`·`manifestHash`를 반환한다
- [x] candidate의 물리 배치가 source와 다를 때만 `compactVersion + 1`을 사용한다
- [x] `compactVersion`은 물리 packaging 세대이며 `dataVersion` 계산에서 제외한다

### `manifestHash` 계산

- [x] `manifestHash` 필드가 없는 최종 canonical manifest 원본 byte의 SHA-256을
      lowercase hex 64자로 표현한다
- [x] `schemaVersion`, `compactVersion`, artifact 배치와 file 참조를 포함한 manifest의
      모든 필드는 hash 입력에 포함된다
- [x] 선택적 `manifest.json.zst` 전송본과 `manifest.sig`는 hash 입력에서 제외한다
- [x] manifest 원본 byte와 기대 `manifestHash`를 비교하는 검증 API를 제공한다
- [x] 02의 `single-file`·`multipart-file`·`bundle-entry` golden vector에서 canonical
      identity·manifest byte, `dataVersion`과 `manifestHash`를 정확히 재현한다

### release diff

- [x] 두 manifest의 최종 `files[]`를 기준으로 추가·변경·삭제·group 이동을 분류한다
- [x] 경로와 `fileHash`가 같고 artifact 참조도 재사용됐더라도 group이 다르면 논리
      group 이동으로 분류한다
- [x] 논리 데이터 차이와 물리 artifact 차이를 별도 결과로 출력한다

### download plan

- [x] 입력으로 명시적인 target group 집합을 받고 해당 group의 목표 `files[]`만 계획한다
- [x] 로컬 상태(경로 + 검증된 `fileHash`)와 선택된 목표 `files[]`를 비교한다
- [x] artifact 위치가 달라도 경로·`fileHash`가 같으면 다운로드하지 않는다
- [x] 필요한 파일이 bundle에만 있으면 해당 bundle 전체를 계획에 한 번만 포함한다
- [x] file part는 누락 part만 포함하되 최종 결합 hash 검증을 요구한다
- [x] 예상 다운로드 byte, 임시 공간, 파일 수, bundle 수를 계산한다

### compression contract

- [x] `ICompressionCodec` contract와 zstd codec 식별자를 정의한다
- [x] Core는 구체 압축 구현을 참조하지 않는다

## 결정 사항

- [x] Core 계산 API의 실패 표현: 02에서 정한 두 가지(parser는 `bool` +
      `IReadOnlyList<GamePatchKitError>`, validator는 `ValidationResult`)에 더해
      **계산 API는 선행 조건 위반을 예외로 던진다**. `ReleaseDiff.Compute`의
      packageId 불일치, `DownloadPlanner.Plan`의 미선언 group·해소 불가 artifact
      참조, `CompactVersionRule.Resolve`의 논리 상태 불일치가 여기 해당한다.
      이유: 이 입력들은 이미 schema·`ManifestValidator`를 통과한 manifest를
      전제하므로 정상 경로에서 발생할 수 없고, 조용히 잘못된 diff·plan을
      반환하는 것보다 즉시 실패하는 편이 안전하다. 사용자 입력을 typed error로
      바꾸는 책임은 호출자(CLI·Runtime)에 둔다.
- [x] `ICompressionCodec`은 **async 전용**(`CompressAsync`/`DecompressAsync` +
      `CancellationToken`)으로 정의한다. Runtime의 취소·진행 보고와 Packager의
      asynchronous streaming verify API가 모두 async 경로를 요구하므로 sync
      overload는 두지 않는다. codec 식별자는 `CompressionCodecIds.Zstd`로 두고
      `CompressionKindJson`의 wire 값도 이 상수를 참조한다.
- [x] 물리 diff와 cache 판정의 단위는 artifact가 아니라 **저장된 object의
      `(경로, objectHash)` 쌍**이다. artifact 단위 비교는 `maxArtifactBytes`가
      바뀌어 `content` 하나가 part로 재분할되는 경우를 놓친다. 여기서 더 나아가
      경로만으로 비교하면 안 되는 이유는 part 경로가
      `files/<artifactHash>/part-#####`, 즉 **자기 byte가 아니라 부모 artifact의
      hash로 주소화**되기 때문이다. 같은 30 byte payload를 20+10에서 16+14로
      다시 나누면 `artifactHash`·part 개수·part 경로가 모두 그대로인 채 각 part의
      크기와 `partHash`만 바뀐다. 경로만 비교하면 diff는 "물리 변경 없음"으로,
      planner는 옛 part를 cache hit으로 판정해 결합 hash가 절대 맞지 않는 계획을
      만든다. 그래서 `DownloadPlanner`는 경로와 `objectHash`를 함께 담은
      `CachedArtifactObject`를 받아 둘 다 일치할 때만 cache hit으로 본다. part
      경로 규칙 자체는 02에서 고정한 PRD 계약이므로 바꾸지 않는다.
- [x] 내용과 group이 동시에 바뀐 파일은 `ContentChanged`로 분류한다(우선순위:
      Added > Removed > ContentChanged > GroupMoved). 어차피 다시 내려받아야
      하므로 재다운로드가 필요한 분류를 선택하고, `FileChange`가 source·target
      entry를 모두 들고 있어 group 이동 사실은 그대로 읽을 수 있다.
- [x] download plan의 "임시 공간"은 **새로 받을 object byte + staging에 쓸 목표
      파일 byte**로 정의한다. 이미 cache에 있는 object는 호스트가 이미 쓴 공간이라
      제외한다.
- [x] 다운로드가 전혀 필요 없는 artifact도 plan에 남긴다(`ObjectsToDownload`가 빈
      `PlannedArtifact`). part가 모두 cache에 있어도 파일 생성과 결합 hash 검증은
      남아 있으므로, 목록에서 빼면 호출자가 그 작업을 잃는다.
- [x] `ReleaseIdentity.Finalize`는 draft의 `groups`·`artifacts`·`files`를
      **복사한 뒤** identity 값을 계산한다. `ReleaseManifest`는 넘겨받은
      collection을 복사하지 않고 참조로 들고 있어서, draft의 원본 list를 쥔
      호출자가 나중에 그것을 수정하면 `FinalizedManifest.Manifest`만 바뀌고 이미
      확정된 canonical byte·`manifestHash`는 그대로 남는다. 즉 서명·게시 대상
      byte와 모델이 서로 다른 release를 가리키게 된다.

## 산출물

- Core identity·diff·download plan API와 단위 테스트

## 완료 기준

- 파일 내용 또는 group 의미가 바뀌면 `dataVersion`·`manifestHash`가 모두 바뀐다.
- 같은 논리 상태에서 물리 배치가 바뀌면 `dataVersion`은 유지되고
  `compactVersion`과 `manifestHash`만 바뀐다. candidate byte가 source와 같으면
  no-op으로 세 값을 모두 재사용한다(검증 기준 9의 단위 수준).
- 같은 데이터를 다른 압축으로 표현해도 `dataVersion`이 같다.
- compression 설정만 달라지고 artifact 참조가 모두 같으면 두 manifest의 논리·물리
  diff가 없다.
- manifest의 `schemaVersion`만 바꾸면 `dataVersion`은 유지되고 `manifestHash`는
  바뀐다.
- 공용 golden vector의 canonical identity·manifest byte, `dataVersion`과
  `manifestHash`가 모두 예상값과 정확히 일치한다(검증 기준 25).
- diff가 추가·변경·삭제·group 이동을 정확히 분류한다(table-driven).
- download plan이 재사용·bundle 전체 포함·누락 part 규칙을 만족하고 예상 수치를
  계산한다.
- required group만 선택한 최초 plan과 optional group 하나만 선택한 후속 plan이 다른
  group의 artifact를 포함하지 않는다(검증 기준 22의 Core 수준).

모든 완료 기준은 `dotnet test`(`GamePatchKit.Core.Tests`, 166개 테스트)로 검증했다.
02의 golden vector 재현은 별도 계산을 새로 쓰지 않고 `ReleaseIdentity`를 그대로
통과시켜 확인하므로, fixture가 실제 identity API의 출력을 고정한다.

구현 직후 Codex adversarial review에서 3건을 지적받아 모두 수정했다(part 경로가
자기 byte로 주소화되지 않아 생기는 cache 오판정과 물리 diff 누락, `Finalize`의
draft collection aliasing). 각 수정에 회귀 테스트를 붙였고, 수정을 임시로 되돌려
그 3개 테스트만 실패하는 것을 확인했다.

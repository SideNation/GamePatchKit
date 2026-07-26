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
- [x] 물리 diff의 단위는 artifact가 아니라 **저장된 object**다. artifact 단위
      비교는 `maxArtifactBytes`가 바뀌어 `content` 하나가 part로 재분할되는 경우를
      놓친다.
- [x] 같은 저장 경로에 서로 다른 byte를 주장하는 두 release는 **표현하지 않고
      거부한다**(`ReleaseStorageCompatibility`, `ReleaseDiff.Compute`와
      `CompactVersionRule.Resolve`가 gate로 호출). 이유: 저장소는 불변이다(PRD
      "한 artifact 디렉터리에는 payload 한 종류만 존재", "동일 경로에 다시
      업로드하지 않는다", "hash 경로의 기존 byte 불일치"는 오류). 그런데 part
      경로는 `files/<artifactHash>/part-#####`, 즉 **자기 byte가 아니라 부모
      artifact의 hash로 주소화**된다. 같은 30 byte payload를 20+10에서 16+14로
      다시 나누면 `artifactHash`·part 개수·part 경로가 모두 그대로인 채 각 part의
      크기와 `partHash`만 바뀌므로, 두 세대는 같은 경로를 두고 충돌한다. 이를
      diff 결과로 "표현"하면 공존할 수 없는 두 release가 공존 가능한 것처럼
      보이고 rollback·동시 reader가 깨진다. part 경로 규칙 자체는 02에서 고정한
      PRD 계약이라 바꾸지 않고, 위반 manifest를 거부하는 쪽을 택했다.
- [x] 게시 가능 여부는 **직전 release가 아니라 package가 아직 보관 중인 전체
      object 목록**을 기준으로 판정한다(`ReleaseStorageCompatibility.Validate(
      retainedObjects, candidate)`). 두 manifest만 비교하면 중간 release가 경로를
      비워주는 순간 우회된다: R1이 payload H를 20+10 part로 저장 → R2가 H를 단일
      object로 표현(part 경로 미사용) → R3이 H를 16+14로 재분할하면, R2와 R3은
      공유 경로가 없어 통과하지만 R3은 rollback용으로 남아 있는 R1의 part를
      덮어쓴다. 그래서 `CompactVersionRule.Resolve`는 `retainedObjects`를 필수
      인자로 받고(생략 overload를 두지 않는다), source 자신의 object는 항상
      포함한다. `ReleaseDiff.Compute`는 "이 두 release가 공존 가능한가"만 묻는
      pairwise 질문이므로 그대로 두되, 게시 안전성을 보장하지 않는다고 명시했다.
- [x] `DownloadPlanner`는 cache 항목을 경로와 `objectHash`를 함께 담은
      `CachedArtifactObject`로 받아 둘 다 일치할 때만 cache hit으로 본다. 게시된
      저장소는 불변이지만 client의 local cache는 오래되거나 손상될 수 있는 로컬
      상태이고, part 경로는 자기 digest를 담지 않아 경로만으로는 실제 byte를 알 수
      없다. hash를 함께 보면 잘못된 항목은 그냥 다시 받게 되고, 결합될 수 없는
      계획을 반복 생성하지 않는다.
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
- [x] manifest 모델은 **생성자에서 collection을 복사**한다
      (`ReadOnlySnapshot.Of`, 적용 대상: `ReleaseManifest`의 3개,
      `ManifestArtifact.BundleArtifact.Entries`, `FilePayload.Parts.PartList`,
      `ManifestIdentity`의 2개). 이전에는 참조로 보관해서, 호출자가 원본 list를
      나중에 수정하면 `FinalizedManifest.Manifest`만 바뀌고 이미 확정된 canonical
      byte·`manifestHash`는 그대로 남았다. 즉 서명·게시 대상 byte와 모델이 서로
      다른 release를 가리켰다. `Finalize`에서만 바깥 list를 복사하는 방식은 part·
      bundle entry 같은 중첩 list를 그대로 aliasing해 절반만 고치는 셈이라,
      모델 자체를 불변으로 만드는 쪽을 택했다.

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

모든 완료 기준은 `dotnet test`(`GamePatchKit.Core.Tests`, 175개 테스트)로 검증했다.
02의 golden vector 재현은 별도 계산을 새로 쓰지 않고 `ReleaseIdentity`를 그대로
통과시켜 확인하므로, fixture가 실제 identity API의 출력을 고정한다.

구현 직후 Codex adversarial review를 세 차례 받아 6건을 모두 수정했다. 지적은
매번 같은 뿌리를 한 겹씩 더 파고들었다: (1) part 경로가 자기 byte로 주소화되지
않아 생기는 cache 오판정·물리 diff 누락과 `Finalize`의 draft collection aliasing,
(2) 재분할 충돌은 in-memory에서 구분만 할 게 아니라 불변 저장소 규칙 위반으로
거부해야 하고 collection 복사도 중첩 list까지 내려가야 한다는 것, (3) 그 거부가
pairwise면 중간 release가 경로를 비워주는 3-release 순서로 우회된다는 것. 각
수정에 회귀 테스트를 붙였고, 매번 수정을 임시로 되돌려 해당 테스트만 실패하는
것을 확인했다.

# file package 생성

## 개요

`GamePatchKit.Packager`는 검증된 `PackageConfig`와 source directory에서 file
artifact와 canonical release manifest를 생성한다. 이전 release를 함께 전달하면
경로·원본 hash·group 정책을 비교해 incremental package를 만들며, 새 bundle 생성과
compact 계약은 [deterministic bundle과 compact](bundle-compact.md)에 설명한다.

## 사용법

`FilePackageBuilder`의 기본 생성자는 고정된 zstd codec을 사용한다. `OutputRoot`에는
`<packageId>/artifacts`와 `<packageId>/manifests`만 게시하며, source 안쪽 경로를
output으로 지정할 수 없다.

```csharp
using GamePatchKit.Core.Configuration;
using GamePatchKit.Packager;

public static class PackageBuild
{
    public static async Task<FilePackageResult> CreateAsync(
        PackageConfig validatedConfig,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        var builder = new FilePackageBuilder();
        var request = new FilePackageRequest(
            validatedConfig,
            outputRoot,
            sourceRevision: "git-revision-from-host");

        return await builder.BuildAsync(request, cancellationToken);
    }
}
```

incremental package는 이전 canonical `manifest.json`의 원본 byte와 해당 byte의
`manifestHash`를 함께 전달한다. Packager는 schema, canonical byte, hash, Core 참조
무결성과 실제 artifact byte를 검증한 뒤에만 기존 참조를 재사용한다.

```csharp
byte[] previousManifestBytes = await File.ReadAllBytesAsync(
    "publish/test-package/manifests/<manifestHash>/manifest.json",
    cancellationToken);
var previous = new PreviousRelease(previousManifestBytes, previousManifestHash);
var request = new FilePackageRequest(validatedConfig, "publish", previous);

FilePackageResult result = await new FilePackageBuilder().BuildAsync(
    request,
    cancellationToken);
```

`WriteCompressedManifest`를 `true`로 지정하면 같은 manifest directory에 선택적
`manifest.json.zst`도 생성한다. `manifestHash`는 항상 압축 전 canonical
`manifest.json` byte를 기준으로 한다.

## incremental 재사용 규칙

- 같은 경로·크기·`fileHash`의 file artifact는 group, `artifactMode`, compression
  정책이 달라져도 실제 compression metadata와 함께 재사용한다.
- 같은 내용의 다른 경로가 이전 file artifact를 참조했다면 그 artifact를 공유할 수
  있다.
- 기존 bundle은 모든 entry가 현재도 같은 경로·hash·group이고 대상 mode도
  `bundle`일 때만 전체를 재사용한다.
- bundle entry 일부가 변경·삭제·이동되면 미참조 bundle entry를 남길 수 없으므로
  해당 bundle의 현재 파일 전체를 file override로 전환한다.
- 새 파일과 내용이 바뀐 파일은 target mode가 `bundle`이어도 file override로
  생성한다. 06단계 compact가 이후 새 bundle baseline을 만든다.
- compression 정책만 바뀌고 모든 참조를 재사용하면 새 codec 실행 없이 기존
  `manifestHash`를 그대로 반환하며 `ReusedManifest`가 `true`다.
- 삭제 파일은 새 `files[]`에서 빠지며 새 artifact를 만들지 않는다.

명시적 group에 속하지 않은 파일은 `default` group에 들어간다. 설정에는
`default.required` 필드가 없으므로 Packager는 이 group을 `required: true`로
manifest에 기록한다.

최초 package의 `bundle` group은 deterministic PAX tar baseline으로 생성된다.
incremental에서 새 파일·변경분은 계속 file override를 사용하며, 운영자가
`BundleCompactor`를 호출할 때만 선택 group의 새 baseline으로 통합된다.

## 산출물과 report

불변 게시 경로는 다음과 같다.

```text
<outputRoot>/
└── <packageId>/
    ├── artifacts/files/<artifactHash>/
    │   ├── content 또는 content.zst
    │   └── part-#####
    ├── artifacts/bundles/<groupName>/
    │   └── <bundleHash>.tar 또는 <bundleHash>.tar.zst
    └── manifests/<manifestHash>/
        ├── manifest.json
        └── manifest.json.zst  # 선택
```

`PackageBuildReport`는 생성 시각·machine·source revision, 추가·변경·삭제·group 이동,
생성·재사용 artifact 수와 적용 compression 정책을 반환한다. 이 값들은 재현 가능한
manifest identity가 아니므로 불변 publish tree에는 파일로 기록하지 않는다.
bundle 생성 수·byte는 `CreatedBundleArtifactCount`와
`CreatedBundleArtifactBytes`로 구분한다.

## 검증과 에러 처리

`PackagePayloadVerifier.VerifyAsync`는 이미 parse된 manifest의 Core 의미 규칙과 실제
artifact object 크기·SHA-256, part 결합, zstd 해제 후 원본 크기·`fileHash`를
streaming으로 검증한다. manifest byte parsing·`manifestHash`와 signature까지 받는
통합 verify API는 07·11단계에서 이 payload 검증을 조합한다.

예상 가능한 package 실패는 `PackageException`으로 발생하며 상세 항목은
`Errors`의 `GamePatchKitError`에서 확인한다. 취소는 `OperationCanceledException`을
그대로 전달한다.

| 코드 | 의미 | 권장 처리 |
| --- | --- | --- |
| `packager.invalid-configuration` | config 또는 input/output 배치가 잘못됨 | 설정을 고치고 재실행 |
| `packager.unsupported-entry` | source에 symlink·reparse point·특수 entry가 있음 | source에서 제거 |
| `packager.source-changed` | 실행 중 source entry나 파일 metadata·byte가 바뀜 | source writer를 멈춘 뒤 재실행 |
| `packager.invalid-previous-manifest` | 이전 manifest가 schema/canonical 계약을 위반함 | 신뢰할 수 있는 원본 manifest를 다시 제공 |
| `packager.artifact-corrupted` | 기존 또는 생성 artifact의 크기·hash·복원 결과가 다름 | 손상 저장소를 복구하고 재실행 |
| `packager.immutable-path-conflict` | 불변 hash 경로에 다른 표현·byte가 있음 | 기존 경로를 덮어쓰지 말고 저장소 상태 조사 |
| `packager.missing-compression-codec` | 새 zstd artifact 또는 전송본 생성에 codec이 없음 | 호환 zstd codec 주입 |

staging은 output과 같은 filesystem에 만들고 final source snapshot 검증 뒤 artifact,
manifest 순서로 게시한다. source 경합이나 artifact 생성 실패는 staging을 폐기하며,
이미 완료된 manifest를 변경하지 않는다. manifest 게시 전 실패로 남은
content-addressed artifact는 참조되지 않는 불변 object일 수 있지만 기존 release의
byte를 덮어쓰지는 않는다.

stable file identity 검증은 Windows, Linux, macOS에서 지원한다. 다른 OS나 identity를
제공할 수 없는 filesystem은 검증을 생략하지 않고 실패한다.

## 관련 파일

- [FilePackageBuilder.cs](../../src/GamePatchKit.Packager/FilePackageBuilder.cs)
- [PackagePayloadVerifier.cs](../../src/GamePatchKit.Packager/PackagePayloadVerifier.cs)
- [FilePackageRequest.cs](../../src/GamePatchKit.Packager/FilePackageRequest.cs)
- [FilePackageResult.cs](../../src/GamePatchKit.Packager/FilePackageResult.cs)
- [PackageBuildReport.cs](../../src/GamePatchKit.Packager/PackageBuildReport.cs)

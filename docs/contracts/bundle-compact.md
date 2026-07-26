# deterministic bundle과 compact

## 개요

`artifactMode: bundle` group은 최초 package에서 deterministic PAX tar baseline으로
생성된다. incremental package는 기존 bundle을 수정하지 않고 변경분을 file override로
남기며, `BundleCompactor`가 운영자가 선택한 group만 현재 최종 상태로 다시 묶는다.

## 최초 bundle 생성

별도 bundle API는 필요하지 않다. `PackageConfigGroup.ArtifactMode`를
`ArtifactMode.Bundle`로 설정하고 기존 `FilePackageBuilder.BuildAsync`를 호출한다.

```csharp
using GamePatchKit.Core;
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Globbing;
using GamePatchKit.Packager;

public static class BundlePackage
{
    public static async Task<FilePackageResult> BuildAsync(
        string inputRoot,
        string outputRoot,
        CancellationToken cancellationToken)
    {
        GlobPattern.TryParse("maps/**/*", out GlobPattern? mapsPattern, out _);
        var maps = new PackageConfigGroup(
            "maps",
            new[] { mapsPattern! },
            ArtifactMode.Bundle,
            required: false,
            compression: CompressionKind.Zstd);
        var config = new PackageConfig(
            schemaVersion: 1,
            packageId: "game-data",
            inputRoot,
            include: new[] { mapsPattern! },
            exclude: Array.Empty<GlobPattern>(),
            maxArtifactBytes: PackageConfig.DefaultMaxArtifactBytes,
            defaultArtifactMode: ArtifactMode.File,
            compression: CompressionKind.None,
            groups: new[] { maps });

        return await new FilePackageBuilder().BuildAsync(
            new FilePackageRequest(config, outputRoot),
            cancellationToken);
    }
}
```

bundle에는 한 group의 파일만 들어가며 entry는 정규화된 전체 상대 경로의 UTF-8 byte
ordinal 순서로 기록된다. 단일 bundle이 `maxArtifactBytes`를 넘으면 entry 경계에서
분리하고, 한 entry만으로도 제한을 만족하지 못하면 같은 compression 정책의 file
artifact/part로 fallback한다.

`maxArtifactBytes`는 이번 실행에서 새로 만드는 payload에 적용한다. content-addressed
불변 경로에 이미 존재하는 검증된 file artifact를 fallback에서 재사용할 때는 현재
제한보다 크더라도 기존 표현과 compression metadata를 그대로 유지한다.

## compact 사용법

compact는 source manifest가 참조하는 artifact에서 파일을 복원하므로 원래 source
directory가 필요하지 않다. `TargetGroups`만 새 bundle로 만들고 나머지 group의
artifact와 file group artifact는 그대로 재사용한다.

```csharp
using GamePatchKit.Core.Configuration;
using GamePatchKit.Core.Manifests;
using GamePatchKit.Packager;

public static class BundleCompact
{
    public static async Task<BundleCompactResult> RunAsync(
        PackageConfig config,
        string outputRoot,
        string sourceManifestPath,
        string sourceManifestHash,
        IReadOnlyList<ArtifactPayloadObject> storageInventory,
        CancellationToken cancellationToken)
    {
        byte[] sourceManifestBytes = await File.ReadAllBytesAsync(
            sourceManifestPath,
            cancellationToken);
        var sourceRelease = new PreviousRelease(sourceManifestBytes, sourceManifestHash);
        var request = new BundleCompactRequest(
            config,
            outputRoot,
            sourceRelease,
            new[] { "maps" },
            storageInventory);

        return await new BundleCompactor().CompactAsync(request, cancellationToken);
    }
}
```

`RetainedObjects`는 생략할 수 없다. 빈 목록은 “source release만 저장소에 남아 있다”는
뜻이며, 과거 release를 보관 중이라면 해당 release들이 점유한 전체 object 경로·크기·
hash를 전달해야 part 경로 충돌과 rollback 데이터 덮어쓰기를 막을 수 있다.

`DryRun`을 `true`로 지정하면 compact를 끝까지 판단하고 게시만 하지 않는다. `Changed`와
identity는 실제 실행과 같고 output tree는 실행 전과 같다.

`Changed`가 `false`면 candidate의 canonical byte가 source와 같았던 성공 no-op이다.
이 경우 기존 `dataVersion`, `compactVersion`, `manifestHash`를 그대로 반환하고
artifact나 manifest를 게시하지 않는다. `Changed`가 `true`일 때만 `dataVersion`을
유지한 채 `compactVersion`을 1 증가시키고 새 manifest를 게시한다.

## PAX byte 계약

bundle archive는 POSIX PAX 형식으로 고정한다.

- regular file entry만 허용하며 directory, symlink, hardlink, device, sparse entry를
  만들지 않는다.
- PAX record 순서는 `path`, `size`, `mtime`이고 extended-header 이름은
  `PaxHeaders/<8자리 entry index>`다.
- UID/GID는 `0`, user/group name은 빈 문자열, mode는 `0644`, mtime은 Unix epoch다.
- entry header 이름이 UTF-8 100 byte를 넘으면 `PaxEntry/<8자리 entry index>`를
  사용하고 실제 전체 경로는 `path` record로 보존한다.
- 마지막에는 정확히 두 개의 512-byte zero block만 기록한다.

`PackagePayloadVerifier`는 payload SHA-256에 더해 위 metadata, entry 순서·경로,
각 entry의 원본 크기·`fileHash`와 archive 종단까지 검증한다. parser가 숨기는 PAX
extended-header 이름과 record byte도 검증하도록 archive를 canonical PAX로 다시
직렬화하고 원본 tar의 길이·SHA-256과 비교한다. zstd 해제 출력은 manifest entry에서
계산한 정확한 canonical tar 크기를 넘을 수 없다.

## 에러 처리

| 코드 | 의미 | 권장 처리 |
| --- | --- | --- |
| `packager.invalid-configuration` | target group 중복·누락 또는 현재 mode가 bundle이 아님 | source manifest와 현재 config의 group 정책 확인 |
| `packager.missing-compression-codec` | 새 zstd bundle 또는 압축 manifest에 codec이 없음 | 호환 zstd codec 주입 |
| `packager.artifact-corrupted` | source object, zstd frame, tar metadata/entry byte가 계약과 다름 | 저장소 object를 복구한 뒤 재실행 |
| `packager.manifest-invalid` | compact가 논리 상태를 바꾸거나 retained object 경로와 충돌함 | 정확한 source manifest·전체 retained inventory 확인 |
| `packager.immutable-path-conflict` | 기존 content-addressed 경로의 byte가 다름 | 기존 경로를 덮어쓰지 말고 저장소 조사 |

## 관련 파일

- [BundleCompactor.cs](../../src/GamePatchKit.Packager/BundleCompactor.cs)
- [BundleCompactRequest.cs](../../src/GamePatchKit.Packager/BundleCompactRequest.cs)
- [BundleCompactResult.cs](../../src/GamePatchKit.Packager/BundleCompactResult.cs)
- [DeterministicPaxTarWriter.cs](../../src/GamePatchKit.Packager/DeterministicPaxTarWriter.cs)
- [PackagePayloadVerifier.cs](../../src/GamePatchKit.Packager/PackagePayloadVerifier.cs)

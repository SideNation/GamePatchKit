using GamePatchKit.Packager;

namespace GamePatchKit.Cli.Tests;

// Verification criterion 11 at the CLI boundary: a damaged part, a damaged bundle and a damaged manifest each
// have to come back as the integrity exit code, not as a crash and not as a success.
//
// Corruption is written at the exact original length every time, so what fails is the hash rather than a size
// check that would have caught a much cruder mistake.
public class TestCliVerifyRejectsCorruption
{
    [Fact]
    public void CorruptedFilePart_IsAnIntegrityError()
    {
        using var fixture = SplitPackage();
        string manifestHash = Package(fixture);
        string[] parts = Directory
            .EnumerateFiles(fixture.OutputPath($"{fixture.PackageId}/artifacts/files"), "part-*", SearchOption.AllDirectories)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToArray();
        Assert.True(parts.Length > 1, "the fixture must split at least one payload into parts");
        Corrupt(parts[0]);

        CliRun run = Verify(fixture, manifestHash);

        Assert.Equal(ExitCode.IntegrityError, run.ExitCode);
        Assert.Equal(PackageErrorCodes.ArtifactCorrupted, run.FirstErrorCode());
    }

    [Fact]
    public void CorruptedBundleArchive_IsAnIntegrityError()
    {
        using var fixture = SplitPackage();
        string manifestHash = Package(fixture);
        string bundle = Directory
            .EnumerateFiles(fixture.OutputPath($"{fixture.PackageId}/artifacts/bundles"), "*", SearchOption.AllDirectories)
            .Single();
        Corrupt(bundle);

        CliRun run = Verify(fixture, manifestHash);

        Assert.Equal(ExitCode.IntegrityError, run.ExitCode);
        Assert.Equal(PackageErrorCodes.ArtifactCorrupted, run.FirstErrorCode());
    }

    [Fact]
    public void CorruptedManifestPayload_IsAnIntegrityError()
    {
        using var fixture = SplitPackage();
        string manifestHash = Package(fixture);
        string manifestPath = fixture.OutputPath(PackageLayout.ManifestPath(fixture.PackageId, manifestHash));
        string manifest = File.ReadAllText(manifestPath);

        // Same length, still valid JSON, still schema-valid: only the manifestHash the path claims can catch
        // this, which is why verify compares against the reference rather than recomputing it.
        File.WriteAllText(manifestPath, manifest.Replace("\"core\"", "\"c0re\"", StringComparison.Ordinal));
        Assert.Equal(manifest.Length, File.ReadAllText(manifestPath).Length);

        CliRun run = Verify(fixture, manifestHash);

        Assert.Equal(ExitCode.IntegrityError, run.ExitCode);
        Assert.Equal(GamePatchKit.Core.Manifests.ManifestErrorCodes.ManifestHashMismatch, run.FirstErrorCode());
    }

    private static CliRun Verify(CliFixture fixture, string manifestHash)
    {
        return fixture.Run(
            "verify", "--output-root", fixture.OutputRoot, "--package-id", fixture.PackageId,
            "--manifest-hash", manifestHash, "--json");
    }

    private static void Corrupt(string path)
    {
        File.WriteAllBytes(path, Enumerable.Repeat((byte)0x5a, (int)new FileInfo(path).Length).ToArray());
    }

    // maxArtifactBytes is small enough that the core file has to be stored as parts and the maps bundle still
    // fits, so one package produces both shapes.
    private static CliFixture SplitPackage()
    {
        var fixture = new CliFixture();
        fixture.WriteConfig($"""
            schemaVersion: 1
            packageId: {fixture.PackageId}
            inputRoot: {fixture.SourceRoot.Replace('\\', '/')}
            include:
              - "**/*"
            maxArtifactBytes: 4096
            compression:
              kind: none
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
            """);
        fixture.WriteSource("core/large.bin", new string('c', 10_000));
        fixture.WriteSource("maps/level1.bin", "map bytes");
        return fixture;
    }

    private static string Package(CliFixture fixture)
    {
        CliRun run = fixture.RunExpectingSuccess(
            "package", "--config", fixture.ConfigPath, "--output-root", fixture.OutputRoot, "--json");

        return (string)run.Result()["identity"]!["manifestHash"]!;
    }
}

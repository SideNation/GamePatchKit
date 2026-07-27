using GamePatchKit.Core.Manifests;

namespace GamePatchKit.Runtime.Tests;

public class TestPackageStateSerializer
{
    [Fact]
    public void SerializesCanonicalStateAndRoundTrips()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        PackageState state = State(
            release,
            new PackageGroupState("maps", PackageGroupStatus.NotInstalled),
            new PackageGroupState("core", PackageGroupStatus.Ready, release.ManifestHash, "install/core"));

        byte[] bytes = PackageStateSerializer.Serialize(state);
        bool parsed = PackageStateSerializer.TryDeserialize(bytes, out PackageState? roundTripped, out _);

        Assert.True(parsed);
        Assert.NotNull(roundTripped);
        Assert.Equal(new[] { "core", "maps" }, roundTripped.Groups.Select(group => group.Name));
        Assert.Equal(
            "{\"active\":{\"dataVersion\":\"" + release.Manifest.DataVersion
            + "\",\"manifestHash\":\"" + release.ManifestHash
            + "\"},\"groups\":[{\"installationKey\":\"install/core\",\"name\":\"core\",\"status\":\"ready\",\"verifiedManifestHash\":\""
            + release.ManifestHash
            + "\"},{\"name\":\"maps\",\"status\":\"notInstalled\"}],\"packageId\":\"runtime-package\",\"schemaVersion\":1,\"stateRevision\":1}",
            System.Text.Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void RejectsNonCanonicalAndDuplicateProperties()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        byte[] canonical = PackageStateSerializer.Serialize(
            State(
                release,
                new PackageGroupState("core", PackageGroupStatus.Ready, release.ManifestHash, "install/core"),
                new PackageGroupState("maps", PackageGroupStatus.NotInstalled)));
        string json = System.Text.Encoding.UTF8.GetString(canonical);
        byte[] whitespace = System.Text.Encoding.UTF8.GetBytes(json + "\n");
        byte[] duplicate = System.Text.Encoding.UTF8.GetBytes(
            json.Replace("\"schemaVersion\":1", "\"schemaVersion\":1,\"schemaVersion\":1", StringComparison.Ordinal));

        Assert.False(PackageStateSerializer.TryDeserialize(whitespace, out _, out _));
        Assert.False(PackageStateSerializer.TryDeserialize(duplicate, out _, out _));
    }

    [Fact]
    public void ValidatorRejectsPartialRequiredAndReadyOptionalFromAnotherManifest()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        PackageState state = State(
            release,
            new PackageGroupState("core", PackageGroupStatus.NotInstalled),
            new PackageGroupState("maps", PackageGroupStatus.Ready, new string('a', 64), "install/maps"));

        var validation = PackageStateValidator.Validate(state, release.Manifest);

        Assert.False(validation.IsValid);
        Assert.Contains(validation.Errors, error => error.Message.Contains("Required groups", StringComparison.Ordinal));
        Assert.Contains(validation.Errors, error => error.Message.Contains("active manifest", StringComparison.Ordinal));
    }

    [Fact]
    public void ValidatorRejectsAbsoluteInstallationKey()
    {
        FinalizedManifest release = RuntimeFixture.CreateRelease();
        PackageState state = State(
            release,
            new PackageGroupState("core", PackageGroupStatus.Ready, release.ManifestHash, "/tmp/core"),
            new PackageGroupState("maps", PackageGroupStatus.NotInstalled));

        Assert.False(PackageStateValidator.Validate(state, release.Manifest).IsValid);
    }

    private static PackageState State(
        FinalizedManifest release,
        params PackageGroupState[] groups)
    {
        return new PackageState(
            PackageState.CurrentSchemaVersion,
            stateRevision: 1,
            release.Manifest.PackageId,
            new PackageActiveState(release.Manifest.DataVersion, release.ManifestHash),
            groups);
    }
}

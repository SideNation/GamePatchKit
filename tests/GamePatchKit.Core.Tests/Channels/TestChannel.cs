using GamePatchKit.Core.Channels;
using GamePatchKit.Core.Errors;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Core.Tests.Channels;

public class TestChannel
{
    private const string ValidManifestHash = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";
    private const string ValidDataVersion = "v1-e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855";

    private static string ValidJson()
    {
        return $@"{{
            ""schemaVersion"": 1,
            ""packageId"": ""sample-game-client-data"",
            ""manifestHash"": ""{ValidManifestHash}"",
            ""dataVersion"": ""{ValidDataVersion}""
        }}";
    }

    [Fact]
    public void ParsesValidChannel()
    {
        var json = (JObject)JToken.Parse(ValidJson());

        bool ok = Channel.TryParse(json, out Channel? channel, out IReadOnlyList<GamePatchKitError> errors);

        Assert.True(ok, string.Join("; ", errors));
        Assert.Equal(ValidManifestHash, channel!.ManifestHash);
        Assert.Equal(ValidDataVersion, channel.DataVersion);
    }

    [Fact]
    public void RejectsManifestHashMissingV1Prefix()
    {
        var json = (JObject)JToken.Parse(ValidJson());
        json["dataVersion"] = ValidManifestHash;

        bool ok = Channel.TryParse(json, out Channel? channel, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(channel);
        Assert.Contains(errors, e => e.Code == ChannelErrorCodes.InvalidDataVersion);
    }

    [Fact]
    public void RejectsUppercaseManifestHash()
    {
        var json = (JObject)JToken.Parse(ValidJson());
        json["manifestHash"] = ValidManifestHash.ToUpperInvariant();

        bool ok = Channel.TryParse(json, out Channel? channel, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Contains(errors, e => e.Code == ChannelErrorCodes.InvalidManifestHash);
    }

    [Fact]
    public void RejectsUnknownProperty()
    {
        var json = (JObject)JToken.Parse(ValidJson());
        json["unexpectedField"] = true;

        bool ok = Channel.TryParse(json, out Channel? channel, out IReadOnlyList<GamePatchKitError> errors);

        Assert.False(ok);
        Assert.Null(channel);
        Assert.Contains(errors, e => e.Code == ChannelErrorCodes.UnknownProperty);
    }
}

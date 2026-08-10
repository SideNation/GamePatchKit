using System.Text;
using GamePatchKit.Cli;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Tests;

public sealed class TestManifestStore
{
    [Fact]
    public void WriteAndRead_ValidManifest_PreservesContractAndWritesCanonicalBytes()
    {
        string outputPath = CreateOutputPath();

        try
        {
            ManifestStore.WriteAtomically(outputPath, CreateValidManifest(reverseOrder: true, releaseVersion: 5));

            string manifestPath = Path.Combine(outputPath, "manifest.json");
            byte[] bytes = File.ReadAllBytes(manifestPath);
            string json = Encoding.UTF8.GetString(bytes);
            PatchManifest result = Assert.IsType<PatchManifest>(ManifestStore.ReadPrevious(outputPath));

            Assert.False(HasUtf8Bom(bytes));
            Assert.DoesNotContain('\r', json);
            Assert.DoesNotContain('\n', json);
            Assert.NotEqual((byte)'\r', bytes[^1]);
            Assert.NotEqual((byte)'\n', bytes[^1]);
            Assert.StartsWith("{\"schemaVersion\":1,\"releaseVersion\":5,", json, StringComparison.Ordinal);
            Assert.Contains("\"packing\":\"group\"", json, StringComparison.Ordinal);
            Assert.Contains("\"compression\":\"zstd\"", json, StringComparison.Ordinal);
            Assert.Contains("\"source\":\"archive\"", json, StringComparison.Ordinal);
            Assert.Equal(5, result.ReleaseVersion);
            Assert.Equal("data", result.SourcePath);
            Assert.Equal("group-a", result.Groups[0].Id);
            Assert.Equal("a.bin", result.Groups[0].Entries[0].Path);
            Assert.Equal("group-b", result.Groups[1].Id);
            Assert.Null(result.Groups[1].Archive);
            Assert.Null(result.Groups[1].Entries[0].Offset);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void WriteAtomically_InputOrderDiffers_WritesSameBytes()
    {
        string firstOutputPath = CreateOutputPath();
        string secondOutputPath = CreateOutputPath();

        try
        {
            ManifestStore.WriteAtomically(firstOutputPath, CreateValidManifest(reverseOrder: false));
            ManifestStore.WriteAtomically(secondOutputPath, CreateValidManifest(reverseOrder: true));

            byte[] first = File.ReadAllBytes(Path.Combine(firstOutputPath, "manifest.json"));
            byte[] second = File.ReadAllBytes(Path.Combine(secondOutputPath, "manifest.json"));

            Assert.Equal(first, second);
        }
        finally
        {
            Directory.Delete(firstOutputPath, recursive: true);
            Directory.Delete(secondOutputPath, recursive: true);
        }
    }

    [Fact]
    public void ReadPrevious_ManifestIsMissing_ReturnsNull()
    {
        string outputPath = CreateOutputPath();

        try
        {
            Assert.Null(ManifestStore.ReadPrevious(outputPath));
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void ReadUploadState_StateFileIsMissing_ReturnsNull()
    {
        string outputPath = CreateOutputPath();

        try
        {
            Assert.Null(ManifestStore.ReadUploadState(outputPath));
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void ReadUploadState_StateFileIsLocked_ReturnsNullInsteadOfThrowing()
    {
        string outputPath = CreateOutputPath();
        string statePath = Path.Combine(outputPath, ".gpk-upload-state.json");
        File.WriteAllText(statePath, "{}");

        try
        {
            using (new FileStream(statePath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                Assert.Null(ManifestStore.ReadUploadState(outputPath));
            }
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void WriteUploadStateAtomically_CopiesCurrentManifestBytesAndReadUploadStateReturnsEquivalentManifest()
    {
        string outputPath = CreateOutputPath();

        try
        {
            ManifestStore.WriteAtomically(outputPath, CreateValidManifest(reverseOrder: false, releaseVersion: 3));
            byte[] manifestBytes = File.ReadAllBytes(Path.Combine(outputPath, "manifest.json"));

            ManifestStore.WriteUploadStateAtomically(outputPath);

            byte[] stateBytes = File.ReadAllBytes(Path.Combine(outputPath, ".gpk-upload-state.json"));
            PatchManifest? state = ManifestStore.ReadUploadState(outputPath);
            Assert.Equal(manifestBytes, stateBytes);
            Assert.NotNull(state);
            Assert.Equal(3, state.ReleaseVersion);
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    [Fact]
    public void ReadPrevious_SchemaOrRequiredFieldIsInvalid_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => root["schemaVersion"] = 2, "schemaVersion");
        AssertInvalid(root => root.Remove("sourceCommit"), "sourceCommit");
        AssertInvalid(root => root.Remove("releaseVersion"), "releaseVersion");
    }

    [Fact]
    public void ReadPrevious_SourcePathOrRelativePathIsInvalid_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => root["sourcePath"] = "./data", "sourcePath");
        AssertInvalid(root => GetGroup(root, 0)["id"] = "../group", "groups[0].id");
        AssertInvalid(root => GetEntry(root, 0, 0)["path"] = "/a.bin", "entries[0].path");
    }

    [Fact]
    public void ReadPrevious_GroupOrEntryIsDuplicated_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => GetGroups(root).Add(GetGroup(root, 0).DeepClone()), "groups[2].id");
        AssertInvalid(root => GetEntries(root, 0).Add(GetEntry(root, 0, 0).DeepClone()), "entries[2].path");
    }

    [Fact]
    public void ReadPrevious_ArtifactNameOrVersionIsInvalid_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => GetArchive(root, 0)["name"] = "archives/wrong/1.gpka", "archive.name");
        AssertInvalid(root => GetEntry(root, 0, 0)["version"] = "1.01", "entries[0].version");
        AssertInvalid(root => GetEntry(root, 1, 0)["name"] = "files/wrong", "entries[0].name");
        AssertInvalid(root => GetGroup(root, 0)["version"] = -1, "groups[0].version");
    }

    [Fact]
    public void ReadPrevious_SourceConditionalFieldsAreInvalid_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => GetEntry(root, 0, 0)["name"] = JValue.CreateNull(), "entries[0].name");
        AssertInvalid(root => GetEntry(root, 1, 0)["offset"] = JValue.CreateNull(), "entries[0].offset");
        AssertInvalid(root => GetGroup(root, 1)["archive"] = JValue.CreateNull(), "groups[1].archive");
        AssertInvalid(root => GetGroup(root, 0).Remove("archive"), "groups[0].archive");
        AssertInvalid(root => GetGroup(root, 0)["entries"] = new JArray(), "groups[0].entries");
    }

    [Fact]
    public void ReadPrevious_ChecksumOrSizeIsInvalid_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => GetArchive(root, 0)["checksum"] = new string('A', 64), "archive.checksum");
        AssertInvalid(root => GetEntry(root, 1, 0)["storedSize"] = -1, "entries[0].storedSize");
        AssertInvalid(root => GetEntry(root, 0, 0)["size"] = -1, "entries[0].size");
        AssertInvalid(root => root["releaseVersion"] = -1, "releaseVersion");
    }

    [Fact]
    public void HasSameReleaseContent_OnlyReleaseVersionDiffers_ReturnsTrue()
    {
        PatchManifest left = CreateValidManifest(reverseOrder: false, releaseVersion: 0);
        PatchManifest right = CreateValidManifest(reverseOrder: false, releaseVersion: 7);

        Assert.True(ManifestStore.HasSameReleaseContent(left, right));
    }

    [Fact]
    public void HasSameReleaseContent_GroupOrEntryOrderDiffers_ReturnsTrue()
    {
        PatchManifest left = CreateValidManifest(reverseOrder: false);
        PatchManifest right = CreateValidManifest(reverseOrder: true);

        Assert.True(ManifestStore.HasSameReleaseContent(left, right));
    }

    [Theory]
    [InlineData("sourcePath")]
    [InlineData("sourceCommit")]
    [InlineData("groupSetting")]
    [InlineData("archive")]
    [InlineData("entry")]
    public void HasSameReleaseContent_ContentFieldDiffers_ReturnsFalse(string field)
    {
        PatchManifest left = CreateValidManifest(reverseOrder: false);
        PatchManifest right = field switch
        {
            "sourcePath" => CreateValidManifest(reverseOrder: false, sourcePath: "other-data"),
            "sourceCommit" => CreateValidManifest(reverseOrder: false, sourceCommit: "def456"),
            "groupSetting" => CreateValidManifest(reverseOrder: false, groupACompression: CompressionKind.None),
            "archive" => CreateValidManifest(reverseOrder: false, archiveChecksum: new string('c', 64)),
            "entry" => CreateValidManifest(reverseOrder: false, fileEntryChecksum: new string('d', 64)),
            _ => throw new InvalidOperationException()
        };

        Assert.False(ManifestStore.HasSameReleaseContent(left, right));
    }

    [Fact]
    public void ReadPrevious_ArchiveLayoutIsInvalid_ThrowsWithoutChangingManifest()
    {
        AssertInvalid(root => GetEntry(root, 0, 0)["length"] = 9, "entries[0].length");
        AssertInvalid(root => GetEntry(root, 0, 1)["offset"] = 5, "entries[1].offset");
        AssertInvalid(root => GetEntry(root, 0, 0)["offset"] = long.MaxValue, "entries[0].offset");
    }

    private static void AssertInvalid(Action<JObject> mutate, string expectedPath)
    {
        string outputPath = CreateOutputPath();

        try
        {
            ManifestStore.WriteAtomically(outputPath, CreateValidManifest(reverseOrder: false));
            string manifestPath = Path.Combine(outputPath, "manifest.json");
            JObject root = JObject.Parse(File.ReadAllText(manifestPath));
            mutate(root);
            File.WriteAllText(manifestPath, root.ToString(Formatting.None), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            byte[] before = File.ReadAllBytes(manifestPath);

            BuildException exception = Assert.Throws<BuildException>(() => ManifestStore.ReadPrevious(outputPath));

            Assert.Contains(expectedPath, exception.Message, StringComparison.Ordinal);
            Assert.Equal(before, File.ReadAllBytes(manifestPath));
        }
        finally
        {
            Directory.Delete(outputPath, recursive: true);
        }
    }

    private static PatchManifest CreateValidManifest(
        bool reverseOrder,
        int releaseVersion = 0,
        string sourcePath = "data",
        string sourceCommit = "abc123",
        CompressionKind groupACompression = CompressionKind.Zstd,
        string? archiveChecksum = null,
        string? fileEntryChecksum = null)
    {
        archiveChecksum ??= new string('a', 64);
        fileEntryChecksum ??= new string('b', 64);
        var archiveEntries = new[]
        {
            new ManifestEntry
            {
                Path = "a.bin",
                Version = "1.0",
                Size = 10,
                Source = EntrySource.Archive,
                Offset = 0,
                Length = 10
            },
            new ManifestEntry
            {
                Path = "b.bin",
                Version = "1.0",
                Size = 20,
                Source = EntrySource.Archive,
                Offset = 10,
                Length = 20
            }
        };
        var groupA = new ManifestGroup
        {
            Id = "group-a",
            Version = 1,
            Packing = PackingKind.Group,
            Compression = groupACompression,
            Archive = new ManifestArchive
            {
                Name = "archives/group-a/1.gpka",
                PayloadSize = 30,
                StoredSize = 18,
                Checksum = archiveChecksum
            },
            Entries = reverseOrder ? archiveEntries.Reverse().ToArray() : archiveEntries
        };
        var groupB = new ManifestGroup
        {
            Id = "group-b",
            Version = 2,
            Packing = PackingKind.File,
            Compression = CompressionKind.None,
            Entries = new[]
            {
                new ManifestEntry
                {
                    Path = "file.txt",
                    Version = "2.3",
                    Size = 12,
                    Source = EntrySource.File,
                    Name = "files/group-b/2/file.txt.v2.3",
                    StoredSize = 12,
                    Checksum = fileEntryChecksum
                }
            }
        };
        IReadOnlyList<ManifestGroup> groups = reverseOrder ? new[] { groupB, groupA } : new[] { groupA, groupB };

        return new PatchManifest
        {
            SchemaVersion = 1,
            ReleaseVersion = releaseVersion,
            SourcePath = sourcePath,
            SourceCommit = sourceCommit,
            Groups = groups
        };
    }

    private static string CreateOutputPath()
    {
        string outputPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}");
        Directory.CreateDirectory(outputPath);
        return outputPath;
    }

    private static bool HasUtf8Bom(byte[] bytes)
    {
        return bytes.Length >= 3 && bytes[0] == 0xef && bytes[1] == 0xbb && bytes[2] == 0xbf;
    }

    private static JArray GetGroups(JObject root)
    {
        return (JArray)root["groups"]!;
    }

    private static JObject GetGroup(JObject root, int groupIndex)
    {
        return (JObject)GetGroups(root)[groupIndex]!;
    }

    private static JArray GetEntries(JObject root, int groupIndex)
    {
        return (JArray)GetGroup(root, groupIndex)["entries"]!;
    }

    private static JObject GetEntry(JObject root, int groupIndex, int entryIndex)
    {
        return (JObject)GetEntries(root, groupIndex)[entryIndex]!;
    }

    private static JObject GetArchive(JObject root, int groupIndex)
    {
        return (JObject)GetGroup(root, groupIndex)["archive"]!;
    }
}
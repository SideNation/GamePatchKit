namespace GamePatchKit.Cli;

internal sealed record ArtifactMismatch(string Name, string Reason);

internal sealed class VerifyCommand
{
    public IReadOnlyList<ArtifactMismatch> Execute(VerifyArguments arguments)
    {
        PatchManifest manifest = ManifestStore.ReadPrevious(arguments.OutputPath)
            ?? throw new BuildException("manifest.json이 없습니다.");
        var mismatches = new List<ArtifactMismatch>();

        foreach (ManifestGroup group in manifest.Groups)
        {
            if (group.Archive is not null)
            {
                VerifyArtifact(
                    arguments.OutputPath,
                    group.Archive.Name,
                    group.Archive.StoredSize,
                    group.Archive.Checksum,
                    mismatches);
            }

            foreach (ManifestEntry entry in group.Entries)
            {
                if (entry.Source == EntrySource.File)
                {
                    VerifyArtifact(
                        arguments.OutputPath,
                        entry.Name!,
                        entry.StoredSize!.Value,
                        entry.Checksum!,
                        mismatches);
                }
            }
        }

        return mismatches;
    }

    private static void VerifyArtifact(
        string outputPath,
        string name,
        long expectedSize,
        string expectedChecksum,
        ICollection<ArtifactMismatch> mismatches)
    {
        string path = Path.Combine(outputPath, name.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            mismatches.Add(new ArtifactMismatch(name, "파일이 없습니다."));
            return;
        }

        long actualSize = new FileInfo(path).Length;

        if (actualSize != expectedSize)
        {
            mismatches.Add(
                new ArtifactMismatch(
                    name,
                    $"크기가 다릅니다. (expected: {expectedSize}, actual: {actualSize})"));
            return;
        }

        StoredArtifact stored = ArtifactWriter.ReadStored(path);

        if (stored.Checksum != expectedChecksum)
        {
            mismatches.Add(
                new ArtifactMismatch(
                    name,
                    $"checksum이 다릅니다. (expected: {expectedChecksum}, actual: {stored.Checksum})"));
        }
    }
}
namespace GamePatchKit.Cli;

internal sealed record UploadSummary(int UploadedCount, long UploadedBytes, int SkippedCount, long ReleaseVersion);

internal sealed class UploadCommand
{
    private const long MAX_ARTIFACT_SIZE_BYTES = 1_073_741_824;

    private readonly IUploadStorage _storage;
    private readonly string _bucket;

    public UploadCommand(IUploadStorage storage, string bucket)
    {
        _storage = storage;
        _bucket = bucket;
    }

    public async Task<UploadSummary> ExecuteAsync(string outputPath)
    {
        PatchManifest manifest = ManifestStore.ReadPrevious(outputPath)
            ?? throw new BuildException("manifest.json이 없습니다.");
        IReadOnlyList<ManifestArtifact> artifacts = manifest.EnumerateArtifacts().ToArray();
        ValidateLocalArtifacts(outputPath, artifacts);
        string manifestRemotePath = $"manifests/{manifest.ReleaseVersion}.json";
        ValidateRemotePaths(_bucket, artifacts, manifestRemotePath);

        PatchManifest? uploadedState = ManifestStore.ReadUploadState(outputPath);
        IReadOnlyList<ManifestArtifact> artifactsToUpload = PlanArtifacts(manifest, uploadedState);
        long uploadedBytes = 0;

        foreach (ManifestArtifact artifact in artifactsToUpload)
        {
            string localPath = Path.Combine(outputPath, artifact.Name.Replace('/', Path.DirectorySeparatorChar));
            await _storage.UpsertArtifactAsync(localPath, artifact.Name);
            uploadedBytes = checked(uploadedBytes + artifact.StoredSize);
        }

        string manifestLocalPath = Path.Combine(outputPath, "manifest.json");
        await _storage.CreateOrVerifyManifestAsync(manifestLocalPath, manifestRemotePath);

        ManifestStore.WriteUploadStateAtomically(outputPath);

        return new UploadSummary(
            artifactsToUpload.Count,
            uploadedBytes,
            artifacts.Count - artifactsToUpload.Count,
            manifest.ReleaseVersion);
    }

    internal static IReadOnlyList<ManifestArtifact> PlanArtifacts(PatchManifest current, PatchManifest? uploaded)
    {
        IReadOnlyList<ManifestArtifact> currentArtifacts = current.EnumerateArtifacts().ToArray();

        if (uploaded is null)
        {
            return currentArtifacts;
        }

        var uploadedNames = new HashSet<string>(
            uploaded.EnumerateArtifacts().Select(artifact => artifact.Name),
            StringComparer.Ordinal);
        return currentArtifacts.Where(artifact => !uploadedNames.Contains(artifact.Name)).ToArray();
    }

    private static void ValidateLocalArtifacts(string outputPath, IReadOnlyList<ManifestArtifact> artifacts)
    {
        foreach (ManifestArtifact artifact in artifacts)
        {
            ValidateLocalArtifact(outputPath, artifact);
        }
    }

    private static void ValidateLocalArtifact(string outputPath, ManifestArtifact artifact)
    {
        if (artifact.StoredSize > MAX_ARTIFACT_SIZE_BYTES)
        {
            throw new BuildException(
                $"산출물의 storedSize가 1 GiB 상한을 넘습니다: {artifact.Name} (storedSize: {artifact.StoredSize}, 상한: {MAX_ARTIFACT_SIZE_BYTES})");
        }

        string path = Path.Combine(outputPath, artifact.Name.Replace('/', Path.DirectorySeparatorChar));

        if (!File.Exists(path))
        {
            throw new BuildException($"산출물이 없습니다: {artifact.Name}");
        }

        long actualSize = new FileInfo(path).Length;

        if (actualSize != artifact.StoredSize)
        {
            throw new BuildException(
                $"산출물의 크기가 다릅니다: {artifact.Name} (expected: {artifact.StoredSize}, actual: {actualSize})");
        }
    }

    private static void ValidateRemotePaths(string bucket, IReadOnlyList<ManifestArtifact> artifacts, string manifestRemotePath)
    {
        var problems = new List<string>();

        if (!RelativePathValidator.IsRemotePathSegment(bucket))
        {
            problems.Add($"버킷 이름이 올바르지 않습니다: {bucket}");
        }

        foreach (ManifestArtifact artifact in artifacts)
        {
            if (!RelativePathValidator.IsRemoteObjectPath(artifact.Name))
            {
                problems.Add($"산출물 경로가 올바르지 않습니다: {artifact.Name}");
            }
        }

        if (!RelativePathValidator.IsRemoteObjectPath(manifestRemotePath))
        {
            problems.Add($"세대 매니페스트 경로가 올바르지 않습니다: {manifestRemotePath}");
        }

        if (problems.Count > 0)
        {
            throw new BuildException(
                $"원격 경로가 올바르지 않습니다.{Environment.NewLine}{string.Join(Environment.NewLine, problems)}");
        }
    }
}

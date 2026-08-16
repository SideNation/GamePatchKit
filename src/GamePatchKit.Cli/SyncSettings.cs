namespace GamePatchKit.Cli;

internal sealed record SyncSettings(string ProjectUrl, string PublishableKey, string Bucket);

internal static class SyncSettingsResolver
{
    private const string ProjectUrlName = "GPK_SUPABASE_PROJECT_URL";
    private const string PublishableKeyName = "GPK_SUPABASE_PUBLISHABLE_KEY";
    private const string BucketName = "GPK_SUPABASE_BUCKET";
    private const string PublishableKeyPrefix = "sb_publishable_";
    private const string ProjectHostSuffix = ".supabase.co";

    public static SyncSettings Resolve(string? envFilePath)
    {
        string? projectUrl = Environment.GetEnvironmentVariable(ProjectUrlName);
        string? publishableKey = Environment.GetEnvironmentVariable(PublishableKeyName);
        string? bucket = Environment.GetEnvironmentVariable(BucketName);

        if (envFilePath is not null)
        {
            if (!File.Exists(envFilePath))
            {
                throw new BuildException($"--env-file 경로가 없습니다: {envFilePath}");
            }

            // --env-file 형식은 upload와 같은 하나의 형식이므로 파싱도 같은 것을 쓴다.
            foreach ((string name, string value) in UploadSettingsResolver.ReadEnvFile(envFilePath))
            {
                switch (name)
                {
                    case ProjectUrlName:
                        projectUrl = value;
                        break;
                    case PublishableKeyName:
                        publishableKey = value;
                        break;
                    case BucketName:
                        bucket = value;
                        break;
                }
            }
        }

        var missingNames = new List<string>();

        if (string.IsNullOrWhiteSpace(projectUrl))
        {
            missingNames.Add(ProjectUrlName);
        }

        if (string.IsNullOrWhiteSpace(publishableKey))
        {
            missingNames.Add(PublishableKeyName);
        }

        if (string.IsNullOrWhiteSpace(bucket))
        {
            missingNames.Add(BucketName);
        }

        if (missingNames.Count > 0)
        {
            throw new BuildException($"다음 설정 값이 없습니다: {string.Join(", ", missingNames)}");
        }

        if (!IsProjectUrl(projectUrl!))
        {
            throw new BuildException(
                $"{ProjectUrlName}은 https://<project-ref>{ProjectHostSuffix} 형식의 프로젝트 URL이어야 합니다.");
        }

        if (!publishableKey!.StartsWith(PublishableKeyPrefix, StringComparison.Ordinal))
        {
            throw new BuildException($"{PublishableKeyName}은 {PublishableKeyPrefix}로 시작해야 합니다.");
        }

        // 버킷 이름은 포인터 조회의 쿼리 값이자 공개 객체 URL의 경로 세그먼트로 들어가므로 먼저 검증한다.
        if (!RelativePathValidator.IsRemotePathSegment(bucket))
        {
            throw new BuildException($"{BucketName}이 올바르지 않습니다: {bucket}");
        }

        return new SyncSettings(projectUrl!, publishableKey!, bucket!);
    }

    private static bool IsProjectUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
        {
            return false;
        }

        if (uri.Scheme != Uri.UriSchemeHttps
            || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0
            || uri.Query.Length > 0
            || uri.Fragment.Length > 0
            || uri.AbsolutePath != "/")
        {
            return false;
        }

        string host = uri.Host;

        if (!host.EndsWith(ProjectHostSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        // project-ref에 점이 있으면 프로젝트 URL이 아니다. 업로드용 <ref>.storage.supabase.co를 여기에
        // 잘못 넣는 경우가 이 검사에 걸린다.
        string projectRef = host[..^ProjectHostSuffix.Length];
        return projectRef.Length > 0 && !projectRef.Contains('.');
    }
}

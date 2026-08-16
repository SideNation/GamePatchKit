namespace GamePatchKit.Cli;

internal sealed record UploadSettings(string StorageUrl, string Key, string Bucket);

internal static class UploadSettingsResolver
{
    private const string UrlName = "GPK_SUPABASE_URL";
    private const string KeyName = "GPK_SUPABASE_KEY";
    private const string BucketName = "GPK_SUPABASE_BUCKET";
    private const string SecretKeyPrefix = "sb_secret_";
    private const string StorageApiUrlPath = "/storage/v1";
    private const string StorageApiHostSuffix = ".storage.supabase.co";

    public static UploadSettings Resolve(string? envFilePath)
    {
        string? url = Environment.GetEnvironmentVariable(UrlName);
        string? key = Environment.GetEnvironmentVariable(KeyName);
        string? bucket = Environment.GetEnvironmentVariable(BucketName);

        if (envFilePath is not null)
        {
            if (!File.Exists(envFilePath))
            {
                throw new BuildException($"--env-file 경로가 없습니다: {envFilePath}");
            }

            foreach ((string name, string value) in ReadEnvFile(envFilePath))
            {
                switch (name)
                {
                    case UrlName:
                        url = value;
                        break;
                    case KeyName:
                        key = value;
                        break;
                    case BucketName:
                        bucket = value;
                        break;
                }
            }
        }

        var missingNames = new List<string>();

        if (string.IsNullOrWhiteSpace(url))
        {
            missingNames.Add(UrlName);
        }

        if (string.IsNullOrWhiteSpace(key))
        {
            missingNames.Add(KeyName);
        }

        if (string.IsNullOrWhiteSpace(bucket))
        {
            missingNames.Add(BucketName);
        }

        if (missingNames.Count > 0)
        {
            throw new BuildException($"다음 설정 값이 없습니다: {string.Join(", ", missingNames)}");
        }

        if (!IsDirectStorageApiUrl(url!))
        {
            throw new BuildException(
                $"{UrlName}은 https://<project-ref>{StorageApiHostSuffix}{StorageApiUrlPath} 형식의 직접 Storage API URL이어야 합니다.");
        }

        if (!key!.StartsWith(SecretKeyPrefix, StringComparison.Ordinal))
        {
            throw new BuildException($"{KeyName}은 {SecretKeyPrefix}로 시작해야 합니다.");
        }

        return new UploadSettings(url!, key!, bucket!);
    }

    // sync도 같은 --env-file 형식을 쓴다.
    internal static IEnumerable<(string Name, string Value)> ReadEnvFile(string path)
    {
        foreach (string line in File.ReadAllLines(path))
        {
            string trimmedLine = line.Trim();

            if (trimmedLine.Length == 0 || trimmedLine.StartsWith('#'))
            {
                continue;
            }

            int separatorIndex = trimmedLine.IndexOf('=');

            if (separatorIndex < 0)
            {
                continue;
            }

            string name = trimmedLine[..separatorIndex].Trim();
            string value = trimmedLine[(separatorIndex + 1)..].Trim();
            yield return (name, value);
        }
    }

    private static bool IsDirectStorageApiUrl(string value)
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
            || uri.AbsolutePath != StorageApiUrlPath)
        {
            return false;
        }

        string host = uri.Host;

        if (!host.EndsWith(StorageApiHostSuffix, StringComparison.Ordinal))
        {
            return false;
        }

        string projectRef = host[..^StorageApiHostSuffix.Length];
        return projectRef.Length > 0 && !projectRef.Contains('.');
    }
}

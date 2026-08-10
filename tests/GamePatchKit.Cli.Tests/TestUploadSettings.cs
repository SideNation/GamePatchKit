using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestUploadSettings
{
    private const string UrlName = "GPK_SUPABASE_URL";
    private const string KeyName = "GPK_SUPABASE_KEY";
    private const string BucketName = "GPK_SUPABASE_BUCKET";
    private const string ValidUrl = "https://project-ref.storage.supabase.co/storage/v1";
    private const string ValidKey = "sb_secret_abcdefghijklmnop";
    private const string ValidBucket = "patch-data";

    [Fact]
    public void Resolve_EnvironmentVariablesAreSet_ReturnsSettings()
    {
        using var scope = EnvironmentScope.Set(ValidUrl, ValidKey, ValidBucket);

        UploadSettings result = UploadSettingsResolver.Resolve(envFilePath: null);

        Assert.Equal(ValidUrl, result.StorageUrl);
        Assert.Equal(ValidKey, result.Key);
        Assert.Equal(ValidBucket, result.Bucket);
    }

    [Fact]
    public void Resolve_AllValuesMissing_ReportsAllNamesTogether()
    {
        using var scope = EnvironmentScope.Set(url: null, key: null, bucket: null);

        BuildException exception = Assert.Throws<BuildException>(
            () => UploadSettingsResolver.Resolve(envFilePath: null));

        Assert.Contains(UrlName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(KeyName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(BucketName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_OnlyBucketMissing_ReportsOnlyBucket()
    {
        using var scope = EnvironmentScope.Set(ValidUrl, ValidKey, bucket: null);

        BuildException exception = Assert.Throws<BuildException>(
            () => UploadSettingsResolver.Resolve(envFilePath: null));

        Assert.Contains(BucketName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(UrlName, exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(KeyName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_EnvFileOverridesEnvironment()
    {
        using var scope = EnvironmentScope.Set(ValidUrl, ValidKey, "environment-bucket");
        string envFilePath = CreateEnvFile(
            $"{UrlName}={ValidUrl}",
            $"{KeyName}={ValidKey}",
            $"{BucketName}=file-bucket");

        try
        {
            UploadSettings result = UploadSettingsResolver.Resolve(envFilePath);

            Assert.Equal("file-bucket", result.Bucket);
        }
        finally
        {
            File.Delete(envFilePath);
        }
    }

    [Fact]
    public void Resolve_EnvFileOmitsName_KeepsEnvironmentValue()
    {
        using var scope = EnvironmentScope.Set(ValidUrl, ValidKey, "environment-bucket");
        string envFilePath = CreateEnvFile($"{BucketName}=file-bucket");

        try
        {
            UploadSettings result = UploadSettingsResolver.Resolve(envFilePath);

            Assert.Equal(ValidUrl, result.StorageUrl);
            Assert.Equal(ValidKey, result.Key);
            Assert.Equal("file-bucket", result.Bucket);
        }
        finally
        {
            File.Delete(envFilePath);
        }
    }

    [Fact]
    public void Resolve_EnvFileIgnoresCommentsBlankLinesAndUnknownKeys()
    {
        using var scope = EnvironmentScope.Set(url: null, key: null, bucket: null);
        string envFilePath = CreateEnvFile(
            "# comment line",
            "",
            "UNKNOWN_KEY=ignored",
            $"  {UrlName}  =  {ValidUrl}  ",
            $"{KeyName}={ValidKey}",
            $"{BucketName}={ValidBucket}");

        try
        {
            UploadSettings result = UploadSettingsResolver.Resolve(envFilePath);

            Assert.Equal(ValidUrl, result.StorageUrl);
            Assert.Equal(ValidKey, result.Key);
            Assert.Equal(ValidBucket, result.Bucket);
        }
        finally
        {
            File.Delete(envFilePath);
        }
    }

    [Fact]
    public void Resolve_EnvFilePathDoesNotExist_ThrowsBuildException()
    {
        using var scope = EnvironmentScope.Set(ValidUrl, ValidKey, ValidBucket);
        string missingPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}.env");

        Assert.Throws<BuildException>(() => UploadSettingsResolver.Resolve(missingPath));
    }

    [Theory]
    [InlineData("sb_publishable_abcdefghijklmnop")]
    [InlineData("anon-key-value")]
    [InlineData("")]
    public void Resolve_KeyIsNotSecretPrefixed_ThrowsBuildException(string key)
    {
        using var scope = EnvironmentScope.Set(ValidUrl, key, ValidBucket);

        Assert.Throws<BuildException>(() => UploadSettingsResolver.Resolve(envFilePath: null));
    }

    [Theory]
    [InlineData("https://project-ref.supabase.co")]
    [InlineData("http://project-ref.storage.supabase.co/storage/v1")]
    [InlineData("https://project-ref.storage.supabase.co/storage/v1?token=abc")]
    [InlineData("https://project-ref.storage.supabase.co/storage/v1#fragment")]
    [InlineData("https://collector.invalid/storage/v1")]
    [InlineData("https://collector.invalid/x?next=/storage/v1")]
    [InlineData("https://collector.invalid/x#next=/storage/v1")]
    [InlineData("https://storage.supabase.co/storage/v1")]
    [InlineData("https://evilstorage.supabase.co/storage/v1")]
    [InlineData("https://project.ref.storage.supabase.co/storage/v1")]
    [InlineData("https://project-ref.storage.supabase.co:8443/storage/v1")]
    [InlineData("https://user@project-ref.storage.supabase.co/storage/v1")]
    public void Resolve_UrlIsNotDirectStorageApiUrl_ThrowsBuildException(string url)
    {
        using var scope = EnvironmentScope.Set(url, ValidKey, ValidBucket);

        Assert.Throws<BuildException>(() => UploadSettingsResolver.Resolve(envFilePath: null));
    }

    [Fact]
    public void Resolve_KeyIsNotSecretPrefixed_ExceptionMessageDoesNotContainKeyValue()
    {
        const string invalidKey = "sb_publishable_secret-looking-value";
        using var scope = EnvironmentScope.Set(ValidUrl, invalidKey, ValidBucket);

        BuildException exception = Assert.Throws<BuildException>(
            () => UploadSettingsResolver.Resolve(envFilePath: null));

        Assert.DoesNotContain(invalidKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_UrlIsInvalidWithValidKey_ExceptionMessageDoesNotContainKeyValue()
    {
        using var scope = EnvironmentScope.Set("https://project-ref.supabase.co", ValidKey, ValidBucket);

        BuildException exception = Assert.Throws<BuildException>(
            () => UploadSettingsResolver.Resolve(envFilePath: null));

        Assert.DoesNotContain(ValidKey, exception.Message, StringComparison.Ordinal);
    }

    private static string CreateEnvFile(params string[] lines)
    {
        string path = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}.env");
        File.WriteAllLines(path, lines);
        return path;
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string? _previousUrl;
        private readonly string? _previousKey;
        private readonly string? _previousBucket;

        private EnvironmentScope(string? previousUrl, string? previousKey, string? previousBucket)
        {
            _previousUrl = previousUrl;
            _previousKey = previousKey;
            _previousBucket = previousBucket;
        }

        public static EnvironmentScope Set(string? url, string? key, string? bucket)
        {
            var scope = new EnvironmentScope(
                Environment.GetEnvironmentVariable(UrlName),
                Environment.GetEnvironmentVariable(KeyName),
                Environment.GetEnvironmentVariable(BucketName));
            Environment.SetEnvironmentVariable(UrlName, url);
            Environment.SetEnvironmentVariable(KeyName, key);
            Environment.SetEnvironmentVariable(BucketName, bucket);
            return scope;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(UrlName, _previousUrl);
            Environment.SetEnvironmentVariable(KeyName, _previousKey);
            Environment.SetEnvironmentVariable(BucketName, _previousBucket);
        }
    }
}

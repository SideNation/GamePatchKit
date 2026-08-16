using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

// GPK_SUPABASE_BUCKET을 TestUploadSettings와 공유하므로 두 클래스를 같은 collection에 묶어 병렬 실행을 막는다.
[Collection(SupabaseSettingsCollection.NAME)]
public sealed class TestSyncSettings
{
    private const string ProjectUrlName = "GPK_SUPABASE_PROJECT_URL";
    private const string PublishableKeyName = "GPK_SUPABASE_PUBLISHABLE_KEY";
    private const string BucketName = "GPK_SUPABASE_BUCKET";
    private const string ValidProjectUrl = "https://project-ref.supabase.co";
    private const string ValidKey = "sb_publishable_abcdefghijklmnop";
    private const string ValidBucket = "patch-data";

    [Fact]
    public void Resolve_EnvironmentVariablesAreSet_ReturnsSettings()
    {
        using var scope = EnvironmentScope.Set(ValidProjectUrl, ValidKey, ValidBucket);

        SyncSettings result = SyncSettingsResolver.Resolve(envFilePath: null);

        Assert.Equal(ValidProjectUrl, result.ProjectUrl);
        Assert.Equal(ValidKey, result.PublishableKey);
        Assert.Equal(ValidBucket, result.Bucket);
    }

    [Fact]
    public void Resolve_AllValuesMissing_ReportsAllNamesTogether()
    {
        using var scope = EnvironmentScope.Set(projectUrl: null, key: null, bucket: null);

        BuildException exception = Assert.Throws<BuildException>(() => SyncSettingsResolver.Resolve(envFilePath: null));

        Assert.Contains(ProjectUrlName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(PublishableKeyName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(BucketName, exception.Message, StringComparison.Ordinal);
    }

    // 업로드용 Storage API URL을 여기에 잘못 넣는 것이 가장 흔한 실수라 반드시 걸러야 한다.
    [Theory]
    [InlineData("https://project-ref.storage.supabase.co/storage/v1")]
    [InlineData("https://project-ref.storage.supabase.co")]
    [InlineData("http://project-ref.supabase.co")]
    [InlineData("https://project-ref.supabase.co/rest/v1")]
    [InlineData("https://project-ref.supabase.co:8443")]
    [InlineData("https://project-ref.supabase.co?query=1")]
    [InlineData("https://user@project-ref.supabase.co")]
    [InlineData("https://supabase.co")]
    [InlineData("https://project-ref.example.com")]
    [InlineData("not-a-url")]
    public void Resolve_ProjectUrlIsNotAProjectUrl_Throws(string projectUrl)
    {
        using var scope = EnvironmentScope.Set(projectUrl, ValidKey, ValidBucket);

        BuildException exception = Assert.Throws<BuildException>(() => SyncSettingsResolver.Resolve(envFilePath: null));

        Assert.Contains(ProjectUrlName, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("https://project-ref.supabase.co")]
    [InlineData("https://project-ref.supabase.co/")]
    public void Resolve_ProjectUrlWithOrWithoutTrailingSlash_IsAccepted(string projectUrl)
    {
        using var scope = EnvironmentScope.Set(projectUrl, ValidKey, ValidBucket);

        SyncSettings result = SyncSettingsResolver.Resolve(envFilePath: null);

        Assert.Equal(projectUrl, result.ProjectUrl);
    }

    // 소비 머신에는 secret key를 두지 않는다는 권한 분리가 이 검사로 강제된다.
    [Theory]
    [InlineData("sb_secret_abcdefghijklmnop")]
    [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9")]
    [InlineData("publishable_abcdefghijklmnop")]
    public void Resolve_KeyIsNotPublishable_Throws(string key)
    {
        using var scope = EnvironmentScope.Set(ValidProjectUrl, key, ValidBucket);

        BuildException exception = Assert.Throws<BuildException>(() => SyncSettingsResolver.Resolve(envFilePath: null));

        Assert.Contains(PublishableKeyName, exception.Message, StringComparison.Ordinal);
    }

    // 버킷은 포인터 쿼리 값이자 공개 객체 URL의 경로 세그먼트로 들어간다.
    [Theory]
    [InlineData("bucket/with-slash")]
    [InlineData("bucket with space")]
    [InlineData("버킷")]
    [InlineData("..")]
    public void Resolve_BucketIsNotRemoteSafe_Throws(string bucket)
    {
        using var scope = EnvironmentScope.Set(ValidProjectUrl, ValidKey, bucket);

        BuildException exception = Assert.Throws<BuildException>(() => SyncSettingsResolver.Resolve(envFilePath: null));

        Assert.Contains(BucketName, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Resolve_EnvFileOverridesEnvironment()
    {
        using var scope = EnvironmentScope.Set(ValidProjectUrl, ValidKey, "environment-bucket");
        using var envFile = new TemporaryEnvFile($"{BucketName}=file-bucket");

        SyncSettings result = SyncSettingsResolver.Resolve(envFile.Path);

        Assert.Equal("file-bucket", result.Bucket);
    }

    [Fact]
    public void Resolve_EnvFileIsMissing_Throws()
    {
        using var scope = EnvironmentScope.Set(ValidProjectUrl, ValidKey, ValidBucket);
        string missingPath = Path.Combine(Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}.env");

        BuildException exception = Assert.Throws<BuildException>(() => SyncSettingsResolver.Resolve(missingPath));

        Assert.Contains("--env-file", exception.Message, StringComparison.Ordinal);
    }

    // upload용 설정 이름은 sync에 영향을 주지 않는다. 두 명령의 자격증명이 분리돼 있다는 뜻이다.
    [Fact]
    public void Resolve_OnlyUploadSettingsAreSet_ReportsSyncNamesAsMissing()
    {
        using var scope = EnvironmentScope.Set(projectUrl: null, key: null, bucket: null);
        using var envFile = new TemporaryEnvFile(
            "GPK_SUPABASE_URL=https://project-ref.storage.supabase.co/storage/v1",
            "GPK_SUPABASE_KEY=sb_secret_abcdefghijklmnop");

        BuildException exception = Assert.Throws<BuildException>(() => SyncSettingsResolver.Resolve(envFile.Path));

        Assert.Contains(ProjectUrlName, exception.Message, StringComparison.Ordinal);
        Assert.Contains(PublishableKeyName, exception.Message, StringComparison.Ordinal);
    }

    private sealed class TemporaryEnvFile : IDisposable
    {
        public string Path { get; }

        public TemporaryEnvFile(params string[] lines)
        {
            Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"GamePatchKit-{Guid.NewGuid():N}.env");
            File.WriteAllLines(Path, lines);
        }

        public void Dispose()
        {
            File.Delete(Path);
        }
    }

    private sealed class EnvironmentScope : IDisposable
    {
        private readonly string? _previousProjectUrl;
        private readonly string? _previousKey;
        private readonly string? _previousBucket;

        private EnvironmentScope(string? previousProjectUrl, string? previousKey, string? previousBucket)
        {
            _previousProjectUrl = previousProjectUrl;
            _previousKey = previousKey;
            _previousBucket = previousBucket;
        }

        public static EnvironmentScope Set(string? projectUrl, string? key, string? bucket)
        {
            var scope = new EnvironmentScope(
                Environment.GetEnvironmentVariable(ProjectUrlName),
                Environment.GetEnvironmentVariable(PublishableKeyName),
                Environment.GetEnvironmentVariable(BucketName));
            Environment.SetEnvironmentVariable(ProjectUrlName, projectUrl);
            Environment.SetEnvironmentVariable(PublishableKeyName, key);
            Environment.SetEnvironmentVariable(BucketName, bucket);
            return scope;
        }

        public void Dispose()
        {
            Environment.SetEnvironmentVariable(ProjectUrlName, _previousProjectUrl);
            Environment.SetEnvironmentVariable(PublishableKeyName, _previousKey);
            Environment.SetEnvironmentVariable(BucketName, _previousBucket);
        }
    }
}

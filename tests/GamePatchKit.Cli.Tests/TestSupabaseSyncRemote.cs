using System.Net;
using System.Text;
using GamePatchKit.Cli;

namespace GamePatchKit.Cli.Tests;

public sealed class TestSupabaseSyncRemote
{
    private const string ProjectUrl = "https://project-ref.supabase.co";
    private const string PublishableKey = "sb_publishable_abcdefghijklmnop";
    private const string Bucket = "patch-data";

    [Fact]
    public void ParseReleaseVersion_SingleRow_ReturnsValue()
    {
        Assert.Equal(7, SupabaseSyncRemote.ParseReleaseVersion("[{\"release_version\":7}]", Bucket));
    }

    // 첫 세대가 0이므로 0도 정상 값이다.
    [Fact]
    public void ParseReleaseVersion_ZeroIsAValidGeneration()
    {
        Assert.Equal(0, SupabaseSyncRemote.ParseReleaseVersion("[{\"release_version\":0}]", Bucket));
    }

    [Fact]
    public void ParseReleaseVersion_NoRow_ReportsBucketName()
    {
        BuildException exception = Assert.Throws<BuildException>(
            () => SupabaseSyncRemote.ParseReleaseVersion("[]", Bucket));

        Assert.Contains(Bucket, exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("not-json")]
    [InlineData("{\"release_version\":1}")]
    public void ParseReleaseVersion_ResponseIsNotAnArray_Throws(string content)
    {
        Assert.Throws<BuildException>(() => SupabaseSyncRemote.ParseReleaseVersion(content, Bucket));
    }

    [Theory]
    [InlineData("[{\"release_version\":\"1\"}]")]
    [InlineData("[{\"release_version\":1.5}]")]
    [InlineData("[{\"release_version\":null}]")]
    [InlineData("[{\"other\":1}]")]
    [InlineData("[1]")]
    public void ParseReleaseVersion_ValueIsNotAnInteger_Throws(string content)
    {
        Assert.Throws<BuildException>(() => SupabaseSyncRemote.ParseReleaseVersion(content, Bucket));
    }

    [Theory]
    [InlineData("[{\"release_version\":-1}]")]
    [InlineData("[{\"release_version\":2147483648}]")]
    public void ParseReleaseVersion_ValueIsOutOfRange_Throws(string content)
    {
        Assert.Throws<BuildException>(() => SupabaseSyncRemote.ParseReleaseVersion(content, Bucket));
    }

    // publishable key는 JWT가 아니므로 apikey 헤더로만 보낸다. Authorization: Bearer에 넣으면 Supabase가
    // JWT로 파싱하려다 거부할 수 있어, 그 헤더가 없다는 것까지 계약으로 고정한다.
    [Fact]
    public async Task GetReleaseVersionAsync_SendsTheKeyOnlyInTheApiKeyHeader()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, "[{\"release_version\":3}]"));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        int result = await sut.GetReleaseVersionAsync();

        Assert.Equal(3, result);
        Assert.Equal(
            $"{ProjectUrl}/rest/v1/gamepatch_pointer?bucket=eq.{Bucket}&select=release_version",
            handler.LastRequestUri);
        Assert.Equal(PublishableKey, handler.LastApiKeyHeader);
        Assert.Null(handler.LastAuthorizationHeader);
    }

    [Fact]
    public async Task GetReleaseVersionAsync_TableIsMissing_PointsAtThePreconditionSql()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.NotFound, "{\"code\":\"42P01\"}"));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(() => sut.GetReleaseVersionAsync());

        Assert.Contains("gamepatch_pointer", exception.Message, StringComparison.Ordinal);
        Assert.Contains("docs/cli/sync.md", exception.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task GetReleaseVersionAsync_NotPermitted_ReportsPermissionWithoutTheKey(HttpStatusCode statusCode)
    {
        var handler = new FakeHandler(_ => JsonResponse(statusCode, $"{{\"message\":\"denied {PublishableKey}\"}}"));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(() => sut.GetReleaseVersionAsync());

        Assert.Contains("권한", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PublishableKey, exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReleaseVersionAsync_TransportFails_ReportsStageWithoutTheKey()
    {
        var handler = new FakeHandler(_ => throw new HttpRequestException($"connection to {PublishableKey} failed"));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(() => sut.GetReleaseVersionAsync());

        Assert.Contains("pointer-select", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(PublishableKey, exception.Message, StringComparison.Ordinal);
    }

    // 공개 버킷이라 객체 요청에는 인증 헤더를 붙이지 않는다.
    [Fact]
    public async Task OpenObjectAsync_RequestsThePublicObjectUrl()
    {
        var handler = new FakeHandler(_ => BinaryResponse(HttpStatusCode.OK, "payload"u8.ToArray()));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        await using Stream stream = await sut.OpenObjectAsync("archives/group/1.gpka");
        using var reader = new StreamReader(stream, Encoding.UTF8);

        Assert.Equal("payload", await reader.ReadToEndAsync());
        Assert.Equal($"{ProjectUrl}/storage/v1/object/public/{Bucket}/archives/group/1.gpka", handler.LastRequestUri);
        Assert.Null(handler.LastApiKeyHeader);
        Assert.Null(handler.LastAuthorizationHeader);
    }

    // 포인터가 가리키는 세대의 객체는 이미 올라가 있어야 한다. 404는 게시 절차가 깨졌다는 뜻이다.
    [Fact]
    public async Task OpenObjectAsync_ObjectIsMissing_ReportsIncompletePublish()
    {
        var handler = new FakeHandler(_ => BinaryResponse(HttpStatusCode.NotFound, Array.Empty<byte>()));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.OpenObjectAsync("archives/group/1.gpka"));

        Assert.Contains("archives/group/1.gpka", exception.Message, StringComparison.Ordinal);
        Assert.Contains("게시", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OpenObjectAsync_ServerFails_ReportsStatusCode()
    {
        var handler = new FakeHandler(_ => BinaryResponse(HttpStatusCode.InternalServerError, Array.Empty<byte>()));
        using var sut = new SupabaseSyncRemote(CreateSettings(), handler);

        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => sut.OpenObjectAsync("archives/group/1.gpka"));

        Assert.Contains("500", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task GetReleaseVersionAsync_ProjectUrlHasTrailingSlash_DoesNotDoubleTheSeparator()
    {
        var handler = new FakeHandler(_ => JsonResponse(HttpStatusCode.OK, "[{\"release_version\":1}]"));
        var settings = new SyncSettings($"{ProjectUrl}/", PublishableKey, Bucket);
        using var sut = new SupabaseSyncRemote(settings, handler);

        await sut.GetReleaseVersionAsync();

        Assert.StartsWith($"{ProjectUrl}/rest/v1/", handler.LastRequestUri, StringComparison.Ordinal);
    }

    private static SyncSettings CreateSettings()
    {
        return new SyncSettings(ProjectUrl, PublishableKey, Bucket);
    }

    private static HttpResponseMessage JsonResponse(HttpStatusCode statusCode, string content)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(content, Encoding.UTF8, "application/json")
        };
    }

    private static HttpResponseMessage BinaryResponse(HttpStatusCode statusCode, byte[] content)
    {
        return new HttpResponseMessage(statusCode) { Content = new ByteArrayContent(content) };
    }

    private sealed class FakeHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, HttpResponseMessage> _handler;

        public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> handler)
        {
            _handler = handler;
        }

        public string? LastRequestUri { get; private set; }

        public string? LastApiKeyHeader { get; private set; }

        public string? LastAuthorizationHeader { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            LastRequestUri = request.RequestUri?.ToString();
            LastApiKeyHeader = request.Headers.TryGetValues("apikey", out IEnumerable<string>? apiKeys)
                ? apiKeys.Single()
                : null;
            LastAuthorizationHeader = request.Headers.TryGetValues("Authorization", out IEnumerable<string>? authorizations)
                ? authorizations.Single()
                : null;
            return Task.FromResult(_handler(request));
        }
    }
}

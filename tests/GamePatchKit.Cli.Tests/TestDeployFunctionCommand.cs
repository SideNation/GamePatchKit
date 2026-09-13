using System.Net;
using GamePatchKit.Cli;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli.Tests;

public sealed class TestDeployFunctionCommand : IDisposable
{
    private sealed class FakeHandler : HttpMessageHandler
    {
        public Func<HttpRequestMessage, Task<HttpResponseMessage>> RespondAsync { get; set; } =
            _ => Task.FromResult(CreateResponse(HttpStatusCode.Created, SuccessfulResponse));

        public int RequestCount { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestCount++;
            return RespondAsync(request);
        }
    }

    private const string ProjectId = "abcdefghijklmnopqrst";
    private const string OtherProjectId = "tsrqponmlkjihgfedcba";
    private const string AccessToken = "sbp_do_not_print_this_token";
    private const string SuccessfulResponse = "{\"slug\":\"get-patch-version\",\"status\":\"ACTIVE\",\"version\":1}";
    private readonly FakeHandler _handler;
    private readonly HttpClient _httpClient;
    private readonly DeployFunctionCommand _sut;

    public TestDeployFunctionCommand()
    {
        _handler = new FakeHandler();
        _httpClient = new HttpClient(_handler);
        _sut = new DeployFunctionCommand(_httpClient);
    }

    [Fact]
    public async Task ExecuteAsync_CreateUpdateAndOtherProject_SendsSameEmbeddedSourceAndReturnsVersions()
    {
        // Arrange
        string? firstSource = null;
        string[] projects = [ProjectId, ProjectId, OtherProjectId];
        _handler.RespondAsync = async request =>
        {
            string projectId = projects[_handler.RequestCount - 1];
            Assert.Equal(HttpMethod.Post, request.Method);
            Assert.Equal(
                $"https://api.supabase.com/v1/projects/{projectId}/functions/deploy?slug=get-patch-version",
                request.RequestUri!.AbsoluteUri);
            Assert.Equal("Bearer", request.Headers.Authorization?.Scheme);
            Assert.Equal(AccessToken, request.Headers.Authorization?.Parameter);
            Assert.False(request.Headers.Contains("apikey"));

            var multipart = Assert.IsType<MultipartFormDataContent>(request.Content);
            HttpContent[] parts = multipart.ToArray();
            Assert.Equal(4, parts.Length);
            HttpContent metadataPart = Assert.Single(parts, part => part.Headers.ContentDisposition?.Name?.Trim('"') == "metadata");
            HttpContent[] fileParts = parts.Where(part => part.Headers.ContentDisposition?.Name?.Trim('"') == "file").ToArray();
            Assert.Equal(3, fileParts.Length);
            JObject metadata = JObject.Parse(await metadataPart.ReadAsStringAsync());
            Assert.Equal("get-patch-version.ts", (string?)metadata["entrypoint_path"]);
            Assert.Equal("get-patch-version", (string?)metadata["name"]);
            Assert.False((bool)metadata["verify_jwt"]!);
            var files = new Dictionary<string, string>();

            foreach (HttpContent filePart in fileParts)
            {
                files.Add(
                    filePart.Headers.ContentDisposition!.FileName!.Trim('"'),
                    await filePart.ReadAsStringAsync());
            }

            string source = files["get-patch-version.ts"];
            Assert.Contains("Deno.serve", source, StringComparison.Ordinal);
            Assert.Contains("@supabase/supabase-js", source, StringComparison.Ordinal);
            Assert.Contains("npm:@supabase/supabase-js@2.116.0", files["deno.json"], StringComparison.Ordinal);
            Assert.Contains("@supabase/supabase-js@2.116.0", files["deno.lock"], StringComparison.Ordinal);
            Assert.DoesNotContain(AccessToken, await multipart.ReadAsStringAsync(), StringComparison.Ordinal);
            Assert.DoesNotContain(projectId, source, StringComparison.Ordinal);
            firstSource ??= source;
            Assert.Equal(firstSource, source);
            return CreateResponse(HttpStatusCode.Created, SuccessfulResponse.Replace("\"version\":1", $"\"version\":{_handler.RequestCount}"));
        };

        // Act / Assert
        foreach (string projectId in projects)
        {
            string summary = await _sut.ExecuteAsync(new DeployFunctionArguments(projectId, AccessToken));
            Assert.Contains($"project={projectId}", summary, StringComparison.Ordinal);
            Assert.Contains($"functionVersion={_handler.RequestCount}", summary, StringComparison.Ordinal);
            Assert.Contains($"https://{projectId}.supabase.co/functions/v1/get-patch-version", summary, StringComparison.Ordinal);
            Assert.DoesNotContain(AccessToken, summary, StringComparison.Ordinal);
        }

        Assert.Equal(3, _handler.RequestCount);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "인증")]
    [InlineData(HttpStatusCode.Forbidden, "권한")]
    [InlineData(HttpStatusCode.TooManyRequests, "한도")]
    [InlineData(HttpStatusCode.InternalServerError, "서버")]
    [InlineData(HttpStatusCode.BadGateway, "서버")]
    [InlineData(HttpStatusCode.PaymentRequired, "402")]
    [InlineData(HttpStatusCode.Redirect, "302")]
    public async Task ExecuteAsync_RemoteFailure_ReportsStatusWithoutResponseOrRetry(HttpStatusCode status, string expected)
    {
        // Arrange
        _handler.RespondAsync = _ => Task.FromResult(CreateResponse(status, AccessToken));

        // Act
        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => _sut.ExecuteAsync(new DeployFunctionArguments(ProjectId, AccessToken)));

        // Assert
        Assert.Contains(expected, exception.Message, StringComparison.Ordinal);
        Assert.Contains(((int)status).ToString(), exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, _handler.RequestCount);
    }

    [Theory]
    [InlineData("network")]
    [InlineData("timeout")]
    [InlineData("stream")]
    public async Task ExecuteAsync_TransportThrows_ReportsUncertainDeploymentWithoutToken(string failure)
    {
        // Arrange
        _handler.RespondAsync = _ => throw failure switch
        {
            "timeout" => new TaskCanceledException(AccessToken),
            "stream" => new IOException(AccessToken),
            _ => new HttpRequestException(AccessToken)
        };

        // Act
        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => _sut.ExecuteAsync(new DeployFunctionArguments(ProjectId, AccessToken)));

        // Assert
        Assert.Contains("불명확", exception.Message, StringComparison.Ordinal);
        Assert.Contains("재실행", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, exception.ToString(), StringComparison.Ordinal);
        Assert.Equal(1, _handler.RequestCount);
    }

    [Theory]
    [InlineData("not-json sbp_do_not_print_this_token")]
    [InlineData("null")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"slug\":\"other\",\"status\":\"ACTIVE\",\"version\":1}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":\"THROTTLED\",\"version\":1}")]
    [InlineData("{\"slug\":[],\"status\":\"ACTIVE\",\"version\":1}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":{},\"version\":1}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":\"ACTIVE\",\"version\":0}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":\"ACTIVE\",\"version\":-1}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":\"ACTIVE\",\"version\":\"1\"}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":\"ACTIVE\",\"version\":1.5}")]
    [InlineData("{\"slug\":\"get-patch-version\",\"status\":\"ACTIVE\",\"version\":9223372036854775808}")]
    public async Task ExecuteAsync_InvalidDeploymentResult_DoesNotReportSuccess(string body)
    {
        // Arrange
        _handler.RespondAsync = _ => Task.FromResult(CreateResponse(HttpStatusCode.Created, body));

        // Act
        BuildException exception = await Assert.ThrowsAsync<BuildException>(
            () => _sut.ExecuteAsync(new DeployFunctionArguments(ProjectId, AccessToken)));

        // Assert
        Assert.Contains("확인할 수 없는", exception.Message, StringComparison.Ordinal);
        Assert.DoesNotContain(AccessToken, exception.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK)]
    [InlineData(HttpStatusCode.Accepted)]
    [InlineData(HttpStatusCode.NoContent)]
    public async Task ExecuteAsync_UnexpectedSuccessfulHttpStatus_DoesNotReportDeploymentSuccess(HttpStatusCode status)
    {
        // Arrange
        _handler.RespondAsync = _ => Task.FromResult(CreateResponse(status, SuccessfulResponse));

        // Act / Assert
        await Assert.ThrowsAsync<BuildException>(() => _sut.ExecuteAsync(new DeployFunctionArguments(ProjectId, AccessToken)));
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    private static HttpResponseMessage CreateResponse(HttpStatusCode status, string body)
    {
        return new HttpResponseMessage(status)
        {
            Content = new StringContent(body)
        };
    }
}

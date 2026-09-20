using System.Net;
using System.Net.Sockets;
using System.Text;
using GamePatchKit.Cli;
using Newtonsoft.Json.Linq;
using Supabase.Storage.Exceptions;

namespace GamePatchKit.Cli.Tests;

public sealed class TestSupabaseUploadStorage
{
    [Fact]
    public async Task EnsureBucketExistsAsync_LegacyNoSuchBucket_CreatesPublicBucket()
    {
        int port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        var requests = new List<(string Method, string Path, string Body, string? ApiKey)>();
        Task serverTask = Task.Run(async () =>
        {
            for (int index = 0; index < 2; index++)
            {
                HttpListenerContext context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
                using var reader = new StreamReader(context.Request.InputStream, context.Request.ContentEncoding);
                string body = await reader.ReadToEndAsync();
                requests.Add(
                    (context.Request.HttpMethod, context.Request.Url!.AbsolutePath, body, context.Request.Headers["apikey"]));

                if (index == 0)
                {
                    await WriteJsonResponseAsync(
                        context.Response,
                        HttpStatusCode.BadRequest,
                        """{"statusCode":"400","code":"NoSuchBucket","message":"Bucket not found"}""");
                }
                else
                {
                    await WriteJsonResponseAsync(context.Response, HttpStatusCode.OK, """{"name":"patch-data"}""");
                }
            }
        });

        try
        {
            var settings = new UploadSettings(
                $"http://127.0.0.1:{port}/storage/v1",
                "sb_secret_test",
                "patch-data");
            var sut = new SupabaseUploadStorage(settings);

            await sut.EnsureBucketExistsAsync();
            await serverTask;
        }
        finally
        {
            listener.Stop();
        }

        Assert.Equal(2, requests.Count);
        Assert.Equal(("GET", "/storage/v1/bucket/patch-data"), (requests[0].Method, requests[0].Path));
        Assert.Equal(("POST", "/storage/v1/bucket"), (requests[1].Method, requests[1].Path));
        Assert.Equal("sb_secret_test", requests[0].ApiKey);
        Assert.Equal("sb_secret_test", requests[1].ApiKey);
        Assert.True((bool)JObject.Parse(requests[1].Body)["public"]!);
    }

    [Fact]
    public async Task UpsertArtifactAsync_TusCreateReturnsPlainText404_IncludesRequestAndActionableGuidance()
    {
        int port = GetFreeTcpPort();
        using var listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        listener.Start();
        Task serverTask = Task.Run(async () =>
        {
            HttpListenerContext context = await listener.GetContextAsync().WaitAsync(TimeSpan.FromSeconds(5));
            await WriteTextResponseAsync(context.Response, HttpStatusCode.NotFound, "The parent resource is not found");
        });
        string localPath = Path.GetTempFileName();
        await File.WriteAllBytesAsync(localPath, "artifact"u8.ToArray());

        try
        {
            var settings = new UploadSettings(
                $"http://127.0.0.1:{port}/storage/v1",
                "sb_secret_test",
                "patch-data");
            var sut = new SupabaseUploadStorage(settings);

            BuildException exception = await Assert.ThrowsAsync<BuildException>(
                () => sut.UpsertArtifactAsync(localPath, "files/Data/GameData/0/tblBook.json.v0.0"));
            await serverTask;

            Assert.Contains("stage=artifact-upsert", exception.Message, StringComparison.Ordinal);
            Assert.Contains("bucket=patch-data", exception.Message, StringComparison.Ordinal);
            Assert.Contains($"storageHost=127.0.0.1:{port}", exception.Message, StringComparison.Ordinal);
            Assert.Contains("requestMethod=POST", exception.Message, StringComparison.Ordinal);
            Assert.Contains("requestPath=/storage/v1/upload/resumable", exception.Message, StringComparison.Ordinal);
            Assert.Contains("statusCode=404", exception.Message, StringComparison.Ordinal);
            Assert.Contains("errorMessage=The parent resource is not found", exception.Message, StringComparison.Ordinal);
            Assert.Contains("GPK_SUPABASE_STORAGE_URL", exception.Message, StringComparison.Ordinal);
            Assert.DoesNotContain("sb_secret_test", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            listener.Stop();
            File.Delete(localPath);
        }
    }

    [Fact]
    public void IsMissingBucketError_LegacyHttp400NoSuchBucket_ReturnsTrue()
    {
        string content = """{"statusCode":"404","code":"NoSuchBucket","message":"Bucket not found"}""";

        bool result = SupabaseUploadStorage.IsMissingBucketError(400, FailureHint.Reason.Unknown, content);

        Assert.True(result);
    }

    [Theory]
    [InlineData("NoSuchKey")]
    [InlineData("InvalidRequest")]
    [InlineData("nosuchbucket")]
    public void IsMissingBucketError_Http400WithOtherCode_ReturnsFalse(string code)
    {
        string content = $$"""{"statusCode":"404","code":"{{code}}","message":"Bucket not found"}""";

        bool result = SupabaseUploadStorage.IsMissingBucketError(400, FailureHint.Reason.Unknown, content);

        Assert.False(result);
    }

    [Fact]
    public void IsMissingBucketError_NotFoundReason_ReturnsTrue()
    {
        bool result = SupabaseUploadStorage.IsMissingBucketError(404, FailureHint.Reason.NotFound, content: null);

        Assert.True(result);
    }

    [Fact]
    public void IsDuplicateObjectError_Http409WithAlreadyExistsReason_ReturnsTrue()
    {
        bool result = SupabaseUploadStorage.IsDuplicateObjectError(409, FailureHint.Reason.AlreadyExists, content: null);

        Assert.True(result);
    }

    [Theory]
    [InlineData("ResourceAlreadyExists")]
    [InlineData("KeyAlreadyExists")]
    public void IsDuplicateObjectError_Http409WithKnownErrorCode_ReturnsTrue(string code)
    {
        string content = $$"""{"code":"{{code}}","message":"already exists"}""";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(409, FailureHint.Reason.Unknown, content);

        Assert.True(result);
    }

    [Fact]
    public void IsDuplicateObjectError_Http409WithUnrelatedCode_ReturnsFalse()
    {
        string content = """{"code":"InvalidJWT","message":"bad token"}""";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(409, FailureHint.Reason.Unknown, content);

        Assert.False(result);
    }

    [Fact]
    public void IsDuplicateObjectError_LegacyHttp400JsonMessage_ReturnsTrue()
    {
        string content = """{"statusCode":"400","error":"Duplicate","message":"Asset Already Exists"}""";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(400, FailureHint.Reason.Unknown, content);

        Assert.True(result);
    }

    [Fact]
    public void IsDuplicateObjectError_LegacyHttp400JsonMessageWithSurroundingWhitespace_ReturnsFalse()
    {
        string content = """{"statusCode":"400","error":"Duplicate","message":" Asset Already Exists "}""";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(400, FailureHint.Reason.Unknown, content);

        Assert.False(result);
    }

    [Fact]
    public void IsDuplicateObjectError_LegacyHttp400RawTextWithSurroundingWhitespace_ReturnsTrue()
    {
        const string content = "\n  Asset Already Exists  \n";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(400, FailureHint.Reason.Unknown, content);

        Assert.True(result);
    }

    [Fact]
    public void IsDuplicateObjectError_Http400PartialSubstringMatch_ReturnsFalse()
    {
        string content = """{"statusCode":"400","error":"Duplicate","message":"Asset Already Exists in bucket"}""";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(400, FailureHint.Reason.Unknown, content);

        Assert.False(result);
    }

    [Fact]
    public void IsDuplicateObjectError_Http400UnrelatedMessage_ReturnsFalse()
    {
        string content = """{"statusCode":"400","error":"InvalidRequest","message":"Invalid key"}""";

        bool result = SupabaseUploadStorage.IsDuplicateObjectError(400, FailureHint.Reason.Unknown, content);

        Assert.False(result);
    }

    [Fact]
    public void IsDuplicateObjectError_UnrelatedStatusCode_ReturnsFalse()
    {
        bool result = SupabaseUploadStorage.IsDuplicateObjectError(500, FailureHint.Reason.AlreadyExists, content: null);

        Assert.False(result);
    }

    [Fact]
    public void FormatStorageFailure_ConfirmableFieldsProvided_IncludesAllFields()
    {
        string message = SupabaseUploadStorage.FormatStorageFailure(
            "artifact-upsert",
            "files/group/1/a.txt.v1.0",
            "SupabaseStorageException",
            409,
            "ResourceAlreadyExists",
            "already exists");

        Assert.Contains("stage=artifact-upsert", message, StringComparison.Ordinal);
        Assert.Contains("remotePath=files/group/1/a.txt.v1.0", message, StringComparison.Ordinal);
        Assert.Contains("exceptionType=SupabaseStorageException", message, StringComparison.Ordinal);
        Assert.Contains("statusCode=409", message, StringComparison.Ordinal);
        Assert.Contains("errorCode=ResourceAlreadyExists", message, StringComparison.Ordinal);
        Assert.Contains("errorMessage=already exists", message, StringComparison.Ordinal);
        Assert.Contains("확인:", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStorageFailure_OptionalFieldsAreNull_OmitsThoseFields()
    {
        string message = SupabaseUploadStorage.FormatStorageFailure(
            "manifest-create",
            "manifests/0.json",
            "HttpRequestException",
            statusCode: null,
            errorCode: null,
            errorMessage: null);

        Assert.Contains("stage=manifest-create", message, StringComparison.Ordinal);
        Assert.Contains("remotePath=manifests/0.json", message, StringComparison.Ordinal);
        Assert.Contains("exceptionType=HttpRequestException", message, StringComparison.Ordinal);
        Assert.DoesNotContain("statusCode=", message, StringComparison.Ordinal);
        Assert.DoesNotContain("errorCode=", message, StringComparison.Ordinal);
        Assert.DoesNotContain("errorMessage=", message, StringComparison.Ordinal);
    }

    [Fact]
    public void FormatStorageFailure_TusPatch404_ExplainsExpiredSession()
    {
        string message = SupabaseUploadStorage.FormatStorageFailure(
            "artifact-upsert",
            "files/group/1/a.txt.v1.0",
            "SupabaseStorageException",
            404,
            errorCode: null,
            errorMessage: null,
            requestMethod: "PATCH");

        Assert.Contains("TUS 업로드 세션이 없거나 만료되었습니다", message, StringComparison.Ordinal);
        Assert.Contains("같은 SHA로 gpk upload를 다시 실행하세요", message, StringComparison.Ordinal);
    }

    [Fact]
    public void Redact_ValueContainsKey_ReplacesKeyWithPlaceholder()
    {
        string? result = SupabaseUploadStorage.Redact("token sb_secret_abc123 rejected", "sb_secret_abc123");

        Assert.Equal("token [REDACTED] rejected", result);
    }

    [Fact]
    public void Redact_ValueDoesNotContainKey_ReturnsValueUnchanged()
    {
        string? result = SupabaseUploadStorage.Redact("invalid request", "sb_secret_abc123");

        Assert.Equal("invalid request", result);
    }

    [Fact]
    public void Redact_ValueIsNull_ReturnsNull()
    {
        string? result = SupabaseUploadStorage.Redact(null, "sb_secret_abc123");

        Assert.Null(result);
    }

    [Fact]
    public void GetSafeErrorMessage_PlainTextContent_NormalizesAndLimitsMessage()
    {
        string content = $"  The parent resource{Environment.NewLine}is not found  ";

        string? result = SupabaseUploadStorage.GetSafeErrorMessage(content, content, "sb_secret_test");

        Assert.Equal("The parent resource is not found", result);
    }

    [Fact]
    public void GetSafeErrorMessage_UnrecognizedJson_DoesNotExposeRawBody()
    {
        const string content = "{\"internal\":\"private response detail\"}";

        string? result = SupabaseUploadStorage.GetSafeErrorMessage(content, content, "sb_secret_test");

        Assert.Null(result);
    }

    private static int GetFreeTcpPort()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return port;
    }

    private static async Task WriteJsonResponseAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        response.StatusCode = (int)statusCode;
        response.ContentType = "application/json";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }

    private static async Task WriteTextResponseAsync(
        HttpListenerResponse response,
        HttpStatusCode statusCode,
        string content)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(content);
        response.StatusCode = (int)statusCode;
        response.ContentType = "text/plain";
        response.ContentLength64 = bytes.Length;
        await response.OutputStream.WriteAsync(bytes);
        response.Close();
    }
}

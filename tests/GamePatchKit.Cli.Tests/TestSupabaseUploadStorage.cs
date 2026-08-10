using GamePatchKit.Cli;
using Supabase.Storage.Exceptions;

namespace GamePatchKit.Cli.Tests;

public sealed class TestSupabaseUploadStorage
{
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
}

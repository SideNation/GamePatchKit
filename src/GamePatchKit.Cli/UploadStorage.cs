using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Supabase.Storage;
using Supabase.Storage.Exceptions;
using Supabase.Storage.Interfaces;
using FileOptions = Supabase.Storage.FileOptions;

namespace GamePatchKit.Cli;

internal interface IUploadStorage
{
    Task UpsertArtifactAsync(string localPath, string remotePath);
    Task CreateOrVerifyManifestAsync(string localPath, string remotePath);
}

internal sealed class SupabaseUploadStorage : IUploadStorage
{
    private const string ArtifactContentType = "application/octet-stream";
    private const string ManifestContentType = "application/json";
    private const string LegacyDuplicateMessage = "Asset Already Exists";

    private readonly UploadSettings _settings;
    private Client? _client;

    public SupabaseUploadStorage(UploadSettings settings)
    {
        _settings = settings;
    }

    public async Task UpsertArtifactAsync(string localPath, string remotePath)
    {
        try
        {
            await GetBucket().UploadOrResume(
                localPath,
                remotePath,
                new FileOptions { Upsert = true, ContentType = ArtifactContentType });
        }
        catch (SupabaseStorageException exception)
        {
            throw ToBuildException("artifact-upsert", remotePath, exception);
        }
        catch (Exception exception)
        {
            throw ToBuildException("artifact-upsert", remotePath, exception);
        }
    }

    public async Task CreateOrVerifyManifestAsync(string localPath, string remotePath)
    {
        try
        {
            await GetBucket().Upload(
                localPath,
                remotePath,
                new FileOptions { Upsert = false, ContentType = ManifestContentType });
            return;
        }
        catch (SupabaseStorageException exception)
        {
            if (!IsDuplicateObjectError(exception.StatusCode, exception.Reason, exception.Content))
            {
                throw ToBuildException("manifest-create", remotePath, exception);
            }
        }
        catch (Exception exception)
        {
            throw ToBuildException("manifest-create", remotePath, exception);
        }

        byte[] remoteBytes;

        try
        {
            remoteBytes = await GetBucket().Download(remotePath, transformOptions: null);
        }
        catch (SupabaseStorageException exception)
        {
            throw ToBuildException("manifest-download", remotePath, exception);
        }
        catch (Exception exception)
        {
            throw ToBuildException("manifest-download", remotePath, exception);
        }

        byte[] localBytes = File.ReadAllBytes(localPath);

        if (!remoteBytes.AsSpan().SequenceEqual(localBytes))
        {
            throw new BuildException($"세대 매니페스트가 이미 다른 내용으로 존재합니다: {remotePath}");
        }
    }

    internal static bool IsDuplicateObjectError(int statusCode, FailureHint.Reason reason, string? content)
    {
        if (statusCode == 409)
        {
            if (reason == FailureHint.Reason.AlreadyExists)
            {
                return true;
            }

            string? code = TryGetJsonStringField(content, "code");
            return code is "ResourceAlreadyExists" or "KeyAlreadyExists";
        }

        if (statusCode == 400)
        {
            string? message = TryGetJsonStringField(content, "message");

            if (message == LegacyDuplicateMessage)
            {
                return true;
            }

            return content is not null && content.Trim() == LegacyDuplicateMessage;
        }

        return false;
    }

    internal static string FormatStorageFailure(
        string stage,
        string remotePath,
        string exceptionType,
        int? statusCode,
        string? errorCode,
        string? errorMessage)
    {
        var fields = new List<string> { $"stage={stage}", $"remotePath={remotePath}", $"exceptionType={exceptionType}" };

        if (statusCode is not null)
        {
            fields.Add($"statusCode={statusCode}");
        }

        if (errorCode is not null)
        {
            fields.Add($"errorCode={errorCode}");
        }

        if (errorMessage is not null)
        {
            fields.Add($"errorMessage={errorMessage}");
        }

        return $"Storage 요청이 실패했습니다. {string.Join(", ", fields)}";
    }

    private IStorageFileApi<FileObject> GetBucket()
    {
        _client ??= new Client(
            _settings.StorageUrl.TrimEnd('/'),
            new Dictionary<string, string> { ["apikey"] = _settings.Key });
        return _client.From(_settings.Bucket);
    }

    private BuildException ToBuildException(string stage, string remotePath, SupabaseStorageException exception)
    {
        string? errorCode = Redact(TryGetJsonStringField(exception.Content, "code"), _settings.Key);
        string? errorMessage = Redact(TryGetJsonStringField(exception.Content, "message"), _settings.Key);
        string formatted = FormatStorageFailure(
            stage,
            remotePath,
            exception.GetType().Name,
            exception.StatusCode,
            errorCode,
            errorMessage);
        return new BuildException(formatted);
    }

    private BuildException ToBuildException(string stage, string remotePath, Exception exception)
    {
        string? errorMessage = Redact(exception.Message, _settings.Key);
        string formatted = FormatStorageFailure(stage, remotePath, exception.GetType().Name, null, null, errorMessage);
        return new BuildException(formatted);
    }

    internal static string? Redact(string? value, string key)
    {
        if (value is null || key.Length == 0 || !value.Contains(key, StringComparison.Ordinal))
        {
            return value;
        }

        return value.Replace(key, "[REDACTED]", StringComparison.Ordinal);
    }

    private static string? TryGetJsonStringField(string? content, string fieldName)
    {
        if (string.IsNullOrEmpty(content))
        {
            return null;
        }

        try
        {
            JObject json = JObject.Parse(content);
            return json[fieldName]?.Type == JTokenType.String ? json[fieldName]!.Value<string>() : null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

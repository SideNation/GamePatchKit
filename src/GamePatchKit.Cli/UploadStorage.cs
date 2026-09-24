using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Supabase.Storage;
using Supabase.Storage.Exceptions;
using Supabase.Storage.Interfaces;
using FileOptions = Supabase.Storage.FileOptions;

namespace GamePatchKit.Cli;

internal interface IUploadStorage
{
    Task EnsureBucketExistsAsync();
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

    public async Task EnsureBucketExistsAsync()
    {
        Client client;

        try
        {
            client = GetClient();
        }
        catch (Exception exception)
        {
            throw ToBuildException("bucket-get", _settings.Bucket, exception);
        }

        try
        {
            if (await client.GetBucket(_settings.Bucket) is not null)
            {
                return;
            }
        }
        catch (SupabaseStorageException exception)
        {
            if (!IsMissingBucketError(exception.StatusCode, exception.Reason, exception.Content))
            {
                throw ToBuildException("bucket-get", _settings.Bucket, exception);
            }
        }
        catch (Exception exception)
        {
            throw ToBuildException("bucket-get", _settings.Bucket, exception);
        }

        try
        {
            await client.CreateBucket(_settings.Bucket, new BucketUpsertOptions { Public = true });
        }
        catch (SupabaseStorageException exception)
        {
            throw ToBuildException("bucket-create", _settings.Bucket, exception);
        }
        catch (Exception exception)
        {
            throw ToBuildException("bucket-create", _settings.Bucket, exception);
        }
    }

    public async Task UpsertArtifactAsync(string localPath, string remotePath)
    {
        try
        {
            // 경로를 넘기면 Supabase.Storage 2.7.0의 TUS 경로가 파일을 열고 실패했을 때 닫지 않는다. Windows에서는
            // GC가 거둘 때까지 그 파일을 지우거나 옮길 수 없다. 우리가 읽고 닫은 뒤 바이트로 넘겨 핸들을 라이브러리에
            // 맡기지 않는다. byte[] 오버로드도 중단된 업로드 재개를 지원한다.
            byte[] data = await File.ReadAllBytesAsync(localPath);
            await GetBucket().UploadOrResume(
                data,
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

    internal static bool IsMissingBucketError(int statusCode, FailureHint.Reason reason, string? content)
    {
        if (reason == FailureHint.Reason.NotFound)
        {
            return true;
        }

        return statusCode == 400 && TryGetJsonStringField(content, "code") == "NoSuchBucket";
    }

    internal static string FormatStorageFailure(
        string stage,
        string remotePath,
        string exceptionType,
        int? statusCode,
        string? errorCode,
        string? errorMessage,
        string? bucket = null,
        string? storageHost = null,
        string? requestMethod = null,
        string? requestPath = null)
    {
        var fields = new List<string> { $"stage={stage}" };

        if (bucket is not null)
        {
            fields.Add($"bucket={bucket}");
        }

        fields.Add($"remotePath={remotePath}");

        if (storageHost is not null)
        {
            fields.Add($"storageHost={storageHost}");
        }

        if (requestMethod is not null)
        {
            fields.Add($"requestMethod={requestMethod}");
        }

        if (requestPath is not null)
        {
            fields.Add($"requestPath={requestPath}");
        }

        fields.Add($"exceptionType={exceptionType}");

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

        string guidance = GetStorageFailureGuidance(stage, statusCode, requestMethod);
        return $"Storage 요청이 실패했습니다.{Environment.NewLine}진단: {string.Join(", ", fields)}{Environment.NewLine}확인: {guidance}";
    }

    internal static string? GetSafeErrorMessage(string? content, string? exceptionMessage, string key)
    {
        string? message = TryGetJsonStringField(content, "message");

        if (message is null && !string.IsNullOrWhiteSpace(content) && !LooksLikeJson(content))
        {
            message = content;
        }

        if (message is null
            && !string.IsNullOrWhiteSpace(exceptionMessage)
            && !string.Equals(content, exceptionMessage, StringComparison.Ordinal))
        {
            message = exceptionMessage;
        }

        if (message is null)
        {
            return null;
        }

        string normalized = string.Join(" ", message.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        string shortened = normalized.Length <= 300 ? normalized : $"{normalized[..300]}...";
        return Redact(shortened, key);
    }

    private IStorageFileApi<FileObject> GetBucket()
    {
        return GetClient().From(_settings.Bucket);
    }

    private Client GetClient()
    {
        return _client ??= new Client(
            _settings.StorageUrl.TrimEnd('/'),
            new Dictionary<string, string> { ["apikey"] = _settings.Key });
    }

    private BuildException ToBuildException(string stage, string remotePath, SupabaseStorageException exception)
    {
        string? errorCode = Redact(TryGetJsonStringField(exception.Content, "code"), _settings.Key);
        string? errorMessage = GetSafeErrorMessage(exception.Content, exception.Message, _settings.Key);
        Uri? requestUri = exception.Response?.RequestMessage?.RequestUri;
        string formatted = FormatStorageFailure(
            stage,
            remotePath,
            exception.GetType().Name,
            exception.StatusCode,
            errorCode,
            errorMessage,
            _settings.Bucket,
            GetStorageHost(),
            exception.Response?.RequestMessage?.Method.Method,
            requestUri?.AbsolutePath);
        return new BuildException(formatted);
    }

    private BuildException ToBuildException(string stage, string remotePath, Exception exception)
    {
        string? errorMessage = GetSafeErrorMessage(content: null, exception.Message, _settings.Key);
        string formatted = FormatStorageFailure(
            stage,
            remotePath,
            exception.GetType().Name,
            statusCode: null,
            errorCode: null,
            errorMessage: errorMessage,
            bucket: _settings.Bucket,
            storageHost: GetStorageHost());
        return new BuildException(formatted);
    }

    private string? GetStorageHost()
    {
        return Uri.TryCreate(_settings.StorageUrl, UriKind.Absolute, out Uri? uri) ? uri.Authority : null;
    }

    private static string GetStorageFailureGuidance(string stage, int? statusCode, string? requestMethod)
    {
        if (statusCode is 401 or 403)
        {
            return "GPK_SUPABASE_SECRET_KEY가 Storage URL의 프로젝트에서 버킷 조회·생성·객체 쓰기 권한을 갖는지 확인하세요.";
        }

        if (stage == "artifact-upsert" && statusCode == 404)
        {
            return requestMethod == HttpMethod.Patch.Method
                ? "TUS 업로드 세션이 없거나 만료되었습니다. manifest.json.sourceCommit의 같은 SHA로 gpk upload를 다시 실행하세요."
                : "GPK_SUPABASE_STORAGE_URL과 GPK_SUPABASE_BUCKET이 같은 프로젝트를 가리키고 버킷이 존재하는지 확인하세요. 방금 버킷을 만들었다면 같은 sourceCommit으로 다시 실행하세요.";
        }

        if (statusCode == 413)
        {
            return "산출물 크기가 프로젝트 전역 제한과 버킷 파일 제한을 넘지 않는지 확인하세요.";
        }

        return stage switch
        {
            "bucket-get" => "GPK_SUPABASE_STORAGE_URL, GPK_SUPABASE_BUCKET과 secret key의 프로젝트가 일치하는지 확인하세요.",
            "bucket-create" => "secret key에 버킷 생성 권한이 있는지와 같은 이름의 버킷이 이미 생성됐는지 확인하세요.",
            _ => "Supabase Storage 상태와 진단 필드를 확인한 뒤 manifest.json.sourceCommit의 같은 SHA로 다시 실행하세요."
        };
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

    private static bool LooksLikeJson(string content)
    {
        string trimmed = content.TrimStart();
        return trimmed.StartsWith('{') || trimmed.StartsWith('[');
    }
}

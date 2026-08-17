using System.Globalization;
using System.Net;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli;

internal interface ISyncRemote
{
    Task<long> GetReleaseVersionAsync();
    Task<Stream> OpenObjectAsync(string objectPath);
}

// 게시된 원격은 두 부분이다. 버전 포인터는 PostgREST로 읽고, 산출물과 세대 매니페스트는 공개 버킷에서
// 인증 없이 받는다. 둘 다 GET 하나씩이라 HTTP 클라이언트만 쓰고 Postgres client나 Storage SDK를 두지 않는다.
internal sealed class SupabaseSyncRemote : ISyncRemote, IDisposable
{
    private const string PointerTableName = "gamepatch_pointer";
    private const string BucketColumnName = "bucket";
    private const string ReleaseVersionColumnName = "release_version";

    private readonly SyncSettings _settings;
    private readonly HttpClient _httpClient;

    public SupabaseSyncRemote(SyncSettings settings)
        : this(settings, new HttpClientHandler())
    {
    }

    internal SupabaseSyncRemote(SyncSettings settings, HttpMessageHandler handler)
    {
        _settings = settings;
        _httpClient = new HttpClient(handler);
    }

    public async Task<long> GetReleaseVersionAsync()
    {
        string requestUri =
            $"{BaseUrl()}/rest/v1/{PointerTableName}"
            + $"?{BucketColumnName}=eq.{Uri.EscapeDataString(_settings.Bucket)}"
            + $"&select={ReleaseVersionColumnName}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);

        // publishable key는 JWT가 아니라 불투명 key라 apikey 헤더로만 보낸다. Authorization: Bearer에
        // 넣으면 Supabase가 JWT로 파싱하려다 거부할 수 있고, 역할(anon)은 apikey가 정하므로 필요도 없다.
        request.Headers.TryAddWithoutValidation("apikey", _settings.PublishableKey);

        using HttpResponseMessage response = await SendAsync(request, "pointer-select");

        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            throw new BuildException(
                $"포인터 테이블 '{PointerTableName}'이 없습니다. docs/cli/sync.md의 사전 조건 SQL을 실행하세요.");
        }

        if (response.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
        {
            // PostgREST는 GRANT 누락(42501)도 이 상태 코드로 돌려주므로 RLS만 안내하면 엉뚱한 곳을
            // 뒤지게 된다. 세 원인을 모두 짚어준다.
            throw new BuildException(
                $"포인터를 읽을 권한이 없습니다. (statusCode={(int)response.StatusCode}) "
                + "publishable key, anon 역할의 select GRANT, 읽기 RLS 정책을 확인하세요. "
                + "docs/cli/sync.md의 사전 조건 SQL을 참고하세요.");
        }

        if (!response.IsSuccessStatusCode)
        {
            throw new BuildException($"포인터 조회가 실패했습니다. (statusCode={(int)response.StatusCode})");
        }

        string content = await response.Content.ReadAsStringAsync();
        return ParseReleaseVersion(content, _settings.Bucket);
    }

    public async Task<Stream> OpenObjectAsync(string objectPath)
    {
        string requestUri = $"{BaseUrl()}/storage/v1/object/public/{_settings.Bucket}/{objectPath}";
        using var request = new HttpRequestMessage(HttpMethod.Get, requestUri);
        HttpResponseMessage response = await SendAsync(request, "object-get", HttpCompletionOption.ResponseHeadersRead);

        try
        {
            if (response.StatusCode == HttpStatusCode.NotFound)
            {
                throw new BuildException(
                    $"게시된 객체가 없습니다: {objectPath}. 포인터가 가리키는 세대가 완전히 게시되지 않았습니다.");
            }

            if (!response.IsSuccessStatusCode)
            {
                throw new BuildException(
                    $"객체 다운로드가 실패했습니다: {objectPath} (statusCode={(int)response.StatusCode})");
            }
        }
        catch
        {
            response.Dispose();
            throw;
        }

        // 반환한 스트림이 연결 수명을 쥔다. 호출자가 dispose하면 연결이 반환되므로 여기서 response를
        // dispose하지 않는다.
        return await response.Content.ReadAsStreamAsync();
    }

    public void Dispose()
    {
        _httpClient.Dispose();
    }

    internal static long ParseReleaseVersion(string content, string bucket)
    {
        JArray rows;

        try
        {
            rows = JArray.Parse(content);
        }
        catch (JsonException)
        {
            throw new BuildException("포인터 응답을 해석하지 못했습니다.");
        }

        if (rows.Count == 0)
        {
            throw new BuildException(
                $"bucket '{bucket}'의 포인터 행이 없습니다. 아직 게시 전이거나 bucket 이름이 다릅니다.");
        }

        JToken? value = rows[0] is JObject row ? row[ReleaseVersionColumnName] : null;

        if (value?.Type != JTokenType.Integer)
        {
            throw new BuildException($"포인터의 {ReleaseVersionColumnName} 값이 정수가 아닙니다.");
        }

        // long 범위를 넘는 정수는 Newtonsoft가 BigInteger로 읽어 Value<long>()이 InvalidCastException을
        // 던진다. 그러면 아래 범위 검사에 닿기도 전에 처리되지 않은 예외가 되므로 문자열을 거쳐 읽는다.
        if (!long.TryParse(
                value.ToString(),
                NumberStyles.AllowLeadingSign,
                CultureInfo.InvariantCulture,
                out long releaseVersion))
        {
            throw new BuildException($"포인터의 {ReleaseVersionColumnName} 값이 범위를 벗어났습니다.");
        }

        if (releaseVersion < 0)
        {
            throw new BuildException($"포인터의 {ReleaseVersionColumnName} 값이 범위를 벗어났습니다: {releaseVersion}");
        }

        return releaseVersion;
    }

    private string BaseUrl()
    {
        return _settings.ProjectUrl.TrimEnd('/');
    }

    private async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        string stage,
        HttpCompletionOption completionOption = HttpCompletionOption.ResponseContentRead)
    {
        try
        {
            return await _httpClient.SendAsync(request, completionOption);
        }
        catch (HttpRequestException exception)
        {
            // key는 헤더에만 있어 메시지에 들어가지 않지만, 예외 메시지도 그대로 싣지 않고 단계만 알린다.
            throw new BuildException($"원격 요청이 실패했습니다. stage={stage}, exceptionType={exception.GetType().Name}");
        }
        catch (TaskCanceledException exception)
        {
            throw new BuildException($"원격 요청이 시간 초과됐습니다. stage={stage}, exceptionType={exception.GetType().Name}");
        }
    }
}

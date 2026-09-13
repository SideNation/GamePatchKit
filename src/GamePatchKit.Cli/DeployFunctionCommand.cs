using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace GamePatchKit.Cli;

internal sealed class DeployFunctionCommand
{
    private const string FunctionName = "get-patch-version";
    private const string SourceFileName = "get-patch-version.ts";
    private const string ResourcePrefix = "GamePatchKit.Cli.Functions.";
    private const string InvalidResponseMessage = "함수 배포 성공을 확인할 수 없는 응답입니다. 대상 프로젝트를 확인한 뒤 재실행하세요.";
    private readonly HttpClient _httpClient;

    public DeployFunctionCommand(HttpClient httpClient)
    {
        _httpClient = httpClient;
    }

    public async Task<string> ExecuteAsync(DeployFunctionArguments arguments)
    {
        try
        {
            using var request = new HttpRequestMessage(
                HttpMethod.Post,
                $"https://api.supabase.com/v1/projects/{arguments.ProjectId}/functions/deploy?slug={FunctionName}");
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", arguments.AccessToken);
            var metadata = new JObject
            {
                ["entrypoint_path"] = SourceFileName,
                ["name"] = FunctionName,
                ["verify_jwt"] = false
            };
            var content = new MultipartFormDataContent();
            request.Content = content;
            content.Add(new StringContent(metadata.ToString(Formatting.None), Encoding.UTF8, "application/json"), "metadata");
            await AddFileAsync(content, SourceFileName, "application/typescript");
            await AddFileAsync(content, "deno.json", "application/json");
            await AddFileAsync(content, "deno.lock", "application/json");

            using HttpResponseMessage response = await _httpClient.SendAsync(request);

            if (!response.IsSuccessStatusCode)
            {
                throw new BuildException(GetFailureMessage(response.StatusCode));
            }

            if (response.StatusCode != HttpStatusCode.Created)
            {
                throw new BuildException(InvalidResponseMessage);
            }

            JObject result = JObject.Parse(await response.Content.ReadAsStringAsync());
            JToken? versionToken = result["version"];

            if (result["slug"]?.Type != JTokenType.String || (string?)result["slug"] != FunctionName
                || result["status"]?.Type != JTokenType.String || (string?)result["status"] != "ACTIVE"
                || versionToken?.Type != JTokenType.Integer
                || !long.TryParse(versionToken.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out long version)
                || version <= 0)
            {
                throw new BuildException(InvalidResponseMessage);
            }

            string summary = $"함수 배포 완료: project={arguments.ProjectId}, function={FunctionName}, functionVersion={version}\n"
                + $"호출 URL: https://{arguments.ProjectId}.supabase.co/functions/v1/{FunctionName}";
            return summary.Replace(arguments.AccessToken, "[redacted]", StringComparison.Ordinal);
        }
        catch (JsonException)
        {
            throw new BuildException(InvalidResponseMessage);
        }
        catch (Exception exception) when (exception is HttpRequestException or OperationCanceledException or IOException)
        {
            throw new BuildException("함수 배포 중 네트워크 오류가 발생했습니다. 배포 적용 여부가 불명확합니다. 대상 프로젝트를 확인한 뒤 재실행하세요.");
        }
    }

    private static async Task AddFileAsync(MultipartFormDataContent content, string fileName, string mediaType)
    {
        using Stream source = typeof(DeployFunctionCommand).Assembly.GetManifestResourceStream(ResourcePrefix + fileName)
            ?? throw new BuildException("동봉된 함수 파일을 읽을 수 없습니다. CLI를 다시 설치하세요.");
        using var reader = new StreamReader(source, Encoding.UTF8);
        content.Add(new StringContent(await reader.ReadToEndAsync(), Encoding.UTF8, mediaType), "file", fileName);
    }

    private static string GetFailureMessage(HttpStatusCode statusCode)
    {
        return statusCode switch
        {
            HttpStatusCode.Unauthorized => "함수 배포 인증 실패 (401). Access Token을 확인하세요.",
            HttpStatusCode.Forbidden => "함수 배포 권한 부족 (403). 대상 프로젝트 접근 및 edge_functions_write 권한을 확인하세요.",
            HttpStatusCode.TooManyRequests => "함수 배포 요청 한도 초과 (429). 잠시 후 재실행하세요.",
            >= HttpStatusCode.InternalServerError => $"함수 배포 서버 오류 ({(int)statusCode}). 대상 프로젝트를 확인한 뒤 재실행하세요.",
            _ => $"함수 배포 실패 (HTTP {(int)statusCode}). 대상 프로젝트와 배포 설정을 확인하세요."
        };
    }
}

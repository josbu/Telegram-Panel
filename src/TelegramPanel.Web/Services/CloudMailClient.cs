using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace TelegramPanel.Web.Services;

public sealed class CloudMailClient
{
    private readonly HttpClient _http;

    public CloudMailClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<string> GenerateTokenAsync(string baseUrl, string adminEmail, string adminPassword, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(adminEmail))
            throw new ArgumentException("管理员邮箱不能为空", nameof(adminEmail));
        if (string.IsNullOrWhiteSpace(adminPassword))
            throw new ArgumentException("管理员密码不能为空", nameof(adminPassword));

        var uri = BuildUri(baseUrl, "/api/public/genToken");
        var resp = await _http.PostAsJsonAsync(uri, new { email = adminEmail.Trim(), password = adminPassword }, cancellationToken);
        var payload = await resp.Content.ReadFromJsonAsync<CloudMailEnvelope<GenTokenData>>(cancellationToken: cancellationToken);
        EnsureOk(resp, payload);

        var token = (payload?.Data?.Token ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Cloud Mail 未返回 token");

        return token;
    }

    public async Task AddUsersAsync(string baseUrl, string token, IEnumerable<string> emails, string? roleName = null, CancellationToken cancellationToken = default)
    {
        var list = (emails ?? Array.Empty<string>())
            .Select(e => (e ?? string.Empty).Trim())
            .Where(e => !string.IsNullOrWhiteSpace(e))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Select(e => new AddUserItem(e, Password: null, RoleName: roleName))
            .ToList();

        if (list.Count == 0)
            return;

        var uri = BuildUri(baseUrl, "/api/public/addUser");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Content = JsonContent.Create(new { list });

        var resp = await _http.SendAsync(req, cancellationToken);
        var payload = await resp.Content.ReadFromJsonAsync<CloudMailEnvelope<object>>(cancellationToken: cancellationToken);
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Cloud Mail 请求失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

        if (payload == null)
            throw new InvalidOperationException("Cloud Mail 返回为空");

        if (payload.Code == 200)
            return;

        var msg = string.IsNullOrWhiteSpace(payload.Message) ? "" : payload.Message.Trim();
        if (payload.Code == 501 && msg.Contains("已存在", StringComparison.OrdinalIgnoreCase))
            return;

        throw new InvalidOperationException($"Cloud Mail 返回失败：code={payload.Code} message={(string.IsNullOrWhiteSpace(msg) ? "未知错误" : msg)}");
    }

    public async Task<List<CloudMailEmail>> GetEmailListAsync(string baseUrl, string token, CloudMailEmailListRequest request, CancellationToken cancellationToken = default)
    {
        var uri = BuildUri(baseUrl, "/api/public/emailList");
        using var req = new HttpRequestMessage(HttpMethod.Post, uri);
        req.Headers.TryAddWithoutValidation("Authorization", token);
        req.Content = JsonContent.Create(request ?? new CloudMailEmailListRequest());

        var resp = await _http.SendAsync(req, cancellationToken);
        var json = await resp.Content.ReadAsStringAsync(cancellationToken);

        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Cloud Mail 请求失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

        using var doc = System.Text.Json.JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
        var root = doc.RootElement;

        var code = root.TryGetProperty("code", out var codeEl) && codeEl.ValueKind == System.Text.Json.JsonValueKind.Number
            ? codeEl.GetInt32()
            : -1;
        var message = root.TryGetProperty("message", out var msgEl) && msgEl.ValueKind == System.Text.Json.JsonValueKind.String
            ? msgEl.GetString()
            : null;

        if (code != 200)
            throw new InvalidOperationException($"Cloud Mail 返回失败：code={code} message={(string.IsNullOrWhiteSpace(message) ? "未知错误" : message.Trim())}");

        if (!root.TryGetProperty("data", out var dataEl))
            return new List<CloudMailEmail>();

        // 文档示例：data 为数组；历史版本可能是 { total, list }
        if (dataEl.ValueKind == System.Text.Json.JsonValueKind.Array)
        {
            return System.Text.Json.JsonSerializer.Deserialize<List<CloudMailEmail>>(dataEl.GetRawText())
                   ?? new List<CloudMailEmail>();
        }

        if (dataEl.ValueKind == System.Text.Json.JsonValueKind.Object)
        {
            if (dataEl.TryGetProperty("list", out var listEl) && listEl.ValueKind == System.Text.Json.JsonValueKind.Array)
            {
                return System.Text.Json.JsonSerializer.Deserialize<List<CloudMailEmail>>(listEl.GetRawText())
                       ?? new List<CloudMailEmail>();
            }
        }

        return new List<CloudMailEmail>();
    }

    private static Uri BuildUri(string baseUrl, string path)
    {
        baseUrl = (baseUrl ?? string.Empty).Trim();
        if (string.IsNullOrWhiteSpace(baseUrl))
            throw new InvalidOperationException("未配置 Cloud Mail BaseUrl");

        if (!baseUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
            !baseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            baseUrl = "http://" + baseUrl;
        }

        if (!baseUrl.EndsWith("/", StringComparison.Ordinal))
            baseUrl += "/";

        return new Uri(new Uri(baseUrl, UriKind.Absolute), path.TrimStart('/'));
    }

    private static void EnsureOk<T>(HttpResponseMessage resp, CloudMailEnvelope<T>? payload)
    {
        if (!resp.IsSuccessStatusCode)
            throw new HttpRequestException($"Cloud Mail 请求失败：HTTP {(int)resp.StatusCode} {resp.ReasonPhrase}");

        if (payload == null)
            throw new InvalidOperationException("Cloud Mail 返回为空");

        if (payload.Code != 200)
        {
            var msg = string.IsNullOrWhiteSpace(payload.Message) ? "未知错误" : payload.Message.Trim();
            throw new InvalidOperationException($"Cloud Mail 返回失败：code={payload.Code} message={msg}");
        }
    }

    private sealed record CloudMailEnvelope<T>(
        [property: JsonPropertyName("code")] int Code,
        [property: JsonPropertyName("message")] string? Message,
        [property: JsonPropertyName("data")] T? Data);

    private sealed record GenTokenData([property: JsonPropertyName("token")] string? Token);

    private sealed record AddUserItem(
        [property: JsonPropertyName("email")] string Email,
        [property: JsonPropertyName("password")] string? Password,
        [property: JsonPropertyName("roleName")] string? RoleName);
}

public sealed record CloudMailEmailListRequest
{
    [JsonPropertyName("toEmail")] public string? ToEmail { get; init; }
    [JsonPropertyName("sendName")] public string? SendName { get; init; }
    [JsonPropertyName("sendEmail")] public string? SendEmail { get; init; }
    [JsonPropertyName("subject")] public string? Subject { get; init; }
    [JsonPropertyName("content")] public string? Content { get; init; }
    [JsonPropertyName("timeSort")] public string? TimeSort { get; init; } = "desc";
    [JsonPropertyName("type")] public int? Type { get; init; }
    [JsonPropertyName("isDel")] public int? IsDel { get; init; }
    [JsonPropertyName("num")] public int Num { get; init; } = 1;
    [JsonPropertyName("size")] public int Size { get; init; } = 20;
}

public sealed record CloudMailEmail
{
    [JsonPropertyName("emailId")] public int? EmailId { get; init; }
    [JsonPropertyName("toEmail")] public string? ToEmail { get; init; }
    [JsonPropertyName("toName")] public string? ToName { get; init; }
    [JsonPropertyName("sendEmail")] public string? SendEmail { get; init; }
    [JsonPropertyName("sendName")] public string? SendName { get; init; }
    [JsonPropertyName("subject")] public string? Subject { get; init; }
    [JsonPropertyName("text")] public string? Text { get; init; }
    [JsonPropertyName("content")] public string? Content { get; init; }
    [JsonPropertyName("createTime")] public string? CreateTime { get; init; }
    [JsonPropertyName("type")] public int? Type { get; init; }
    [JsonPropertyName("isDel")] public int? IsDel { get; init; }
}

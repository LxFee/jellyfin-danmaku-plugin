using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Http;

namespace Jellyfin.Plugin.Danmaku;

public sealed record SourceResponse(int Status, byte[] Body, string? RetryAfter = null);

// Only the administrator-configured API is signed. Browser requests cannot choose an upstream target.
public sealed class DanmakuSource(Func<PluginConfiguration> configuration, HttpClient client) : IDisposable
{
    private const int MaximumBody = 12 * 1024 * 1024;

    public async Task<SourceResponse> GetAsync(string path, IQueryCollection query, CancellationToken cancellationToken)
    {
        var config = configuration();
        if (!config.Enabled || !config.UseOwnCredentials) return Error(404, "主站凭据模式未启用。");
        if (string.IsNullOrWhiteSpace(config.AppId) || string.IsNullOrWhiteSpace(config.AppSecret))
            return Error(503, "请管理员配置弹幕 AppId 和 AppSecret。");
        var allowed = path switch
        {
            "search/episodes" => new[] { "anime" },
            "search/anime" => new[] { "keyword" },
            "extcomment" => new[] { "url", "chConvert" },
            _ when Regex.IsMatch(path, "^comment/[0-9]{1,20}$") => ["withRelated", "chConvert"],
            _ when Regex.IsMatch(path, "^(bangumi|related)/[0-9]{1,20}$") => Array.Empty<string>(),
            _ => null
        };
        if (allowed is null || query.Count > 8 || query.Any(p => !allowed.Contains(p.Key) || p.Value.Count != 1)
            || query.Sum(p => p.Key.Length + p.Value.ToString().Length) > 8192)
            return Error(400, "不支持的弹幕读取接口或参数。");

        Uri apiBase;
        try { apiBase = Plugin.ValidateUrl(config.ApiBaseUrl, false)!; }
        catch (ArgumentException) { return Error(503, "请管理员检查弹幕 API 地址。"); }
        var uri = new Uri(apiBase.AbsoluteUri.TrimEnd('/') + "/api/v2/" + path + QueryString.Create(query.Select(p =>
            new KeyValuePair<string, string?>(p.Key, p.Value.ToString()))));
        var apiPath = uri.AbsolutePath;
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        var requestToken = deadline.Token;
        try
        {
            for (var redirects = 0; redirects <= 3; redirects++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Get, uri);
                request.Headers.Accept.ParseAdd("application/json");
                request.Headers.UserAgent.ParseAdd("Jellyfin-Danmaku/1.1");
                if (redirects == 0)
                {
                    var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
                    request.Headers.Add("X-AppId", config.AppId);
                    request.Headers.Add("X-Timestamp", timestamp);
                    request.Headers.Add("X-Signature", Sign(config.AppId, timestamp, apiPath, config.AppSecret));
                }
                using var response = await client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, requestToken).ConfigureAwait(false);
                if (response.StatusCode is HttpStatusCode.MovedPermanently or HttpStatusCode.Found or HttpStatusCode.SeeOther
                    or HttpStatusCode.TemporaryRedirect or HttpStatusCode.PermanentRedirect)
                {
                    if (redirects == 3 || response.Headers.Location is null) return Error(502, "弹幕接口重定向失败。");
                    uri = new Uri(uri, response.Headers.Location);
                    var sameOrigin = uri.Scheme == apiBase.Scheme && uri.IdnHost.Equals(apiBase.IdnHost, StringComparison.OrdinalIgnoreCase) && uri.Port == apiBase.Port;
                    var officialCdn = uri.Scheme == "https" && uri.IsDefaultPort
                        && (uri.Host == "dandanplay.net" || uri.Host.EndsWith(".dandanplay.net", StringComparison.OrdinalIgnoreCase));
                    if (uri.UserInfo.Length != 0 || !(sameOrigin || officialCdn))
                        return Error(502, "弹幕接口返回了不支持的下载地址。");
                    continue; // CDN requests carry no application or Jellyfin credentials.
                }
                var retryAfter = response.Headers.RetryAfter?.ToString();
                if (!response.IsSuccessStatusCode)
                {
                    var reason = response.StatusCode == HttpStatusCode.Forbidden
                        ? "弹弹play鉴权失败，请检查 AppId、AppSecret 和主站时间。"
                        : response.StatusCode == HttpStatusCode.TooManyRequests
                            ? "弹弹play请求额度或频率受限，请稍后重试。" : "弹弹play接口请求失败。";
                    return Error((int)response.StatusCode, reason, retryAfter);
                }
                if (response.Content.Headers.ContentLength > MaximumBody) return Error(502, "弹幕数据超过大小限制。");
                await using var stream = await response.Content.ReadAsStreamAsync(requestToken).ConfigureAwait(false);
                using var buffer = new MemoryStream();
                var chunk = new byte[65536];
                int read;
                while ((read = await stream.ReadAsync(chunk, requestToken).ConfigureAwait(false)) > 0)
                {
                    if (buffer.Length + read > MaximumBody) return Error(502, "弹幕数据超过大小限制。");
                    await buffer.WriteAsync(chunk.AsMemory(0, read), requestToken).ConfigureAwait(false);
                }
                var body = buffer.ToArray();
                try
                {
                    using var json = JsonDocument.Parse(body);
                    if (json.RootElement.ValueKind != JsonValueKind.Object) return Error(502, "弹幕接口返回格式无效。");
                    if (json.RootElement.TryGetProperty("success", out var success) && success.ValueKind == JsonValueKind.False)
                    {
                        var status = json.RootElement.TryGetProperty("errorCode", out var code) && code.ValueKind == JsonValueKind.Number && code.TryGetInt32(out var errorCode)
                            && errorCode is 401 or 403 or 429 ? errorCode : 502;
                        return Error(status, status == 429 ? "弹弹play请求额度或频率受限，请稍后重试。" : "弹弹play返回业务错误，请检查凭据和查询条件。", retryAfter);
                    }
                }
                catch (JsonException) { return Error(502, "弹幕接口返回格式无效。"); }
                return new SourceResponse(200, body);
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested) { return Error(504, "弹幕接口请求超时。"); }
        catch (HttpRequestException) { return Error(502, "无法连接弹弹play接口。"); }
        catch (IOException) { return Error(502, "弹幕数据传输失败。"); }
        return Error(502, "弹幕接口请求失败。");
    }

    public static string Sign(string appId, string timestamp, string path, string secret) =>
        Convert.ToBase64String(SHA256.HashData(Encoding.UTF8.GetBytes(appId + timestamp + path + secret)));

    private static SourceResponse Error(int status, string message, string? retryAfter = null) =>
        new(status, JsonSerializer.SerializeToUtf8Bytes(new { success = false, errorCode = status, errorMessage = message }), retryAfter);

    public void Dispose() => client.Dispose();
}

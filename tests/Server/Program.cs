using System.Net;
using Jellyfin.Plugin.Danmaku;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using System.Security.Claims;
using System.Text;
using System.Text.Encodings.Web;

var checks = 0;
void Check(bool condition, string message)
{
    checks++;
    if (!condition) throw new InvalidOperationException(message);
}
foreach (var edgeFirst in new[] { true, false })
{
    var services = new ServiceCollection();
    if (edgeFirst) services.AddTransient<IStartupFilter, EdgeFilter>();
    new ServiceRegistrator().RegisterServices(services, null!);
    if (!edgeFirst) services.AddTransient<IStartupFilter, EdgeFilter>();
    Check(services.First(s => s.ServiceType == typeof(IStartupFilter)).ImplementationType == typeof(WebBootstrap), "Danmaku must wrap Edge regardless of registration order");
}
var directory = Path.Combine(Path.GetTempPath(), "danmaku-tests-" + Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(directory);
await File.WriteAllTextAsync(Path.Combine(directory, "index.html"), "<!DOCTYPE html><html><body>Native index</body></html>");
try
{
    foreach (var prefix in new[] { "", "/jellyfin" })
    foreach (var edge in new[] { false, true })
    {
        var config = new PluginConfiguration();
        var builder = WebApplication.CreateBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.UseUrls("http://127.0.0.1:0");
        builder.Services.AddResponseCompression();
        await using var app = builder.Build();
        app.UseMiddleware<DanmakuMiddleware>((Func<string>)(() => prefix), (Func<PluginConfiguration>)(() => config));
        if (edge) app.Use(async (context, next) =>
        {
            if (context.Request.Path == prefix + "/web/index.html")
            {
                context.Response.ContentType = "text/html; charset=utf-8";
                var bytes = System.Text.Encoding.UTF8.GetBytes("<html><body><script src=\"/JellyfinEdge/edge.js\"></script></body></html>");
                context.Response.ContentLength = bytes.Length;
                await context.Response.Body.WriteAsync(bytes);
            }
            else await next(context);
        });
        app.UseResponseCompression();
        app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(directory), RequestPath = prefix + "/web" });
        await app.StartAsync();
        using var client = new HttpClient { BaseAddress = new Uri(app.Urls.Single()) };
        using var get = new HttpRequestMessage(HttpMethod.Get, prefix + "/web/index.html");
        get.Headers.AcceptEncoding.ParseAdd("gzip, br");
        using var response = await client.SendAsync(get);
        var html = await response.Content.ReadAsStringAsync();
        Check(response.StatusCode == HttpStatusCode.OK, "Index status");
        Check(html.Contains(prefix + "/JellyfinDanmaku/ede.js"), "Index script injection");
        Check(!edge || html.Contains("/JellyfinEdge/edge.js"), "Keep Edge script");
        Check(response.Content.Headers.ContentEncoding.Count == 0, "Uncompressed HTML composition");
        Check(response.Headers.CacheControl?.NoStore == true, "Injected HTML is not cached");
        using var head = await client.SendAsync(new HttpRequestMessage(HttpMethod.Head, prefix + "/web/index.html"));
        Check(head.Content.Headers.ContentLength == response.Content.Headers.ContentLength, "HEAD matches injected GET length");
        Check((await head.Content.ReadAsByteArrayAsync()).Length == 0, "HEAD has no body");
        var script = await client.GetStringAsync(prefix + "/JellyfinDanmaku/ede.js");
        Check(script.Contains("window.JellyfinDanmakuConfig=") && script.Contains("class EDE"), "Embedded script and defaults");
        config.UseOwnCredentials = true;
        config.AppId = "fixture-app";
        config.AppSecret = "not-a-real-secret";
        script = await client.GetStringAsync(prefix + "/JellyfinDanmaku/ede.js");
        Check(script.Contains("\"serverApiPrefix\":\"" + prefix + "/JellyfinDanmaku\""), "Server route follows BaseUrl");
        Check(!script.Contains(config.AppId) && !script.Contains(config.AppSecret), "Viewer script excludes credentials");
        config.Enabled = false;
        html = await client.GetStringAsync(prefix + "/web/index.html");
        Check(!html.Contains("JellyfinDanmaku"), "Disabled plugin leaves index alone");
        using var disabledScript = await client.GetAsync(prefix + "/JellyfinDanmaku/ede.js");
        Check(disabledScript.StatusCode == HttpStatusCode.NotFound, "Disabled script unavailable");
        await app.StopAsync();
    }
}
finally { Directory.Delete(directory, true); }
var sourceConfig = new PluginConfiguration { UseOwnCredentials = true, AppId = "fixture-app", AppSecret = "not-a-real-secret" };
Plugin.ValidateCredentials(sourceConfig);
try { Plugin.ValidateCredentials(new PluginConfiguration { UseOwnCredentials = true }); Check(false, "Missing credentials accepted"); }
catch (ArgumentException) { Check(true, "Missing credentials rejected"); }
var signExpected = Convert.ToBase64String(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes("fixture-app1735660800/api/v2/comment/123450001not-a-real-secret")));
Check(DanmakuSource.Sign(sourceConfig.AppId, "1735660800", "/api/v2/comment/123450001", sourceConfig.AppSecret) == signExpected, "Official signature vector");
var requests = new List<HttpRequestMessage>();
var upstream = new FixtureHandler(request =>
{
    requests.Add(request);
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"comments\":[]}") };
});
using var source = new DanmakuSource(() => sourceConfig, new HttpClient(upstream));
IQueryCollection Query(string query) => new QueryCollection(Microsoft.AspNetCore.WebUtilities.QueryHelpers.ParseQuery(query));
var result = await source.GetAsync("comment/123450001", Query("?withRelated=true&chConvert=1"), default);
Check(result.Status == 200 && requests.Count == 1, "Signed read succeeds");
var signed = requests.Single();
var stamp = signed.Headers.GetValues("X-Timestamp").Single();
Check(signed.RequestUri!.AbsoluteUri == "https://api.dandanplay.net/api/v2/comment/123450001?withRelated=true&chConvert=1", "Default official origin and preserved query");
Check(signed.Headers.GetValues("X-AppId").Single() == sourceConfig.AppId && signed.Headers.GetValues("X-Signature").Single()
    == DanmakuSource.Sign(sourceConfig.AppId, stamp, "/api/v2/comment/123450001", sourceConfig.AppSecret), "Query excluded from signature");
Check(!signed.Headers.Contains("X-AppSecret") && !signed.Headers.Contains("X-Emby-Token") && signed.Headers.Authorization is null, "No raw secret or account token sent upstream");
foreach (var invalid in new[] { "login", "../login", "comment/x", "https://evil.test", "comment/1/../../login" })
    Check((await source.GetAsync(invalid, Query(""), default)).Status == 400, "Unknown route rejected");
Check((await source.GetAsync("comment/1", Query("?target=https://evil.test"), default)).Status == 400, "Unknown parameter rejected");
Check((await source.GetAsync("comment/1", Query("?chConvert=1&chConvert=2"), default)).Status == 400, "Duplicate parameter rejected");
Check(requests.Count == 1, "Rejected requests never reach upstream");
sourceConfig.ApiBaseUrl = "https://danmaku.example.test/edge/";
result = await source.GetAsync("comment/123450001", Query("?withRelated=true&chConvert=1"), default);
signed = requests.Last();
stamp = signed.Headers.GetValues("X-Timestamp").Single();
Check(result.Status == 200 && signed.RequestUri!.AbsoluteUri == "https://danmaku.example.test/edge/api/v2/comment/123450001?withRelated=true&chConvert=1", "Configured API origin, prefix and trailing slash honored");
Check(signed.Headers.GetValues("X-Signature").Single() == DanmakuSource.Sign(sourceConfig.AppId, stamp, "/edge/api/v2/comment/123450001", sourceConfig.AppSecret), "Signature uses configured API path without query");
Check(!signed.Headers.Contains("X-AppSecret") && signed.Headers.Authorization is null, "Custom API receives no raw secret or Jellyfin token");
sourceConfig.ApiBaseUrl = "http://127.0.0.1:8080/danmaku";
result = await source.GetAsync("comment/1", Query(""), default);
Check(result.Status == 200 && requests.Last().RequestUri!.AbsoluteUri == "http://127.0.0.1:8080/danmaku/api/v2/comment/1", "Administrator-configured local API and port supported");
var validRequests = requests.Count;
foreach (var invalidBase in new[] { "ftp://danmaku.example.test", "https://user:secret@danmaku.example.test", "https://danmaku.example.test?target=x", "https://danmaku.example.test#fragment", "not-a-url" })
{
    sourceConfig.ApiBaseUrl = invalidBase;
    Check((await source.GetAsync("comment/1", Query(""), default)).Status == 503, "Invalid administrator API URL rejected");
}
Check(requests.Count == validRequests, "Invalid API configurations never reach upstream");
sourceConfig.ApiBaseUrl = "https://api.dandanplay.net";
upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.Forbidden) { Content = new StringContent(sourceConfig.AppSecret) };
result = await source.GetAsync("comment/1", Query(""), default);
Check(result.Status == 403 && !Encoding.UTF8.GetString(result.Body).Contains(sourceConfig.AppSecret), "Sanitized auth failure");
upstream.Respond = _ => { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); return response; };
result = await source.GetAsync("comment/1", Query(""), default);
Check(result.Status == 429 && result.RetryAfter == "60", "429 and Retry-After preserved");
upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"success\":false,\"errorCode\":429}") };
Check((await source.GetAsync("comment/1", Query(""), default)).Status == 429, "HTTP 200 quota business error mapped");
upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("<html>bad response</html>") };
Check((await source.GetAsync("comment/1", Query(""), default)).Status == 502, "Non-JSON rejected");
var redirected = false;
upstream.Respond = request =>
{
    if (request.RequestUri!.Host == "api.dandanplay.net")
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("https://cas.dandanplay.net/comments.json");
        return response;
    }
    redirected = true;
    Check(!request.Headers.Contains("X-AppId") && !request.Headers.Contains("X-Signature") && !request.Headers.Contains("X-Timestamp"), "CDN redirect strips application credentials");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"comments\":[]}") };
};
Check((await source.GetAsync("comment/1", Query(""), default)).Status == 200 && redirected, "Official CDN redirect succeeds");
upstream.Respond = _ => { var response = new HttpResponseMessage(HttpStatusCode.Found); response.Headers.Location = new Uri("https://evil.test/"); return response; };
Check((await source.GetAsync("comment/1", Query(""), default)).Status == 502, "Foreign redirect rejected");
sourceConfig.ApiBaseUrl = "http://127.0.0.1:8080/danmaku";
upstream.Respond = request =>
{
    if (request.RequestUri!.AbsolutePath.EndsWith("/api/v2/comment/1", StringComparison.Ordinal))
    {
        var response = new HttpResponseMessage(HttpStatusCode.Found);
        response.Headers.Location = new Uri("/assets/comments.json", UriKind.Relative);
        return response;
    }
    Check(request.RequestUri!.AbsoluteUri == "http://127.0.0.1:8080/assets/comments.json", "Configured origin redirect keeps its port");
    Check(!request.Headers.Contains("X-AppId") && !request.Headers.Contains("X-Signature") && request.Headers.Authorization is null, "Configured origin redirect strips credentials");
    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"comments\":[]}") };
};
Check((await source.GetAsync("comment/1", Query(""), default)).Status == 200, "Configured origin redirect succeeds");
sourceConfig.ApiBaseUrl = "https://api.dandanplay.net";
sourceConfig.UseOwnCredentials = false;
Check((await source.GetAsync("comment/1", Query(""), default)).Status == 404, "Own mode must be enabled");
sourceConfig.UseOwnCredentials = true;
upstream.Respond = _ => new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent("{\"comments\":[]}") };
var apiBuilder = WebApplication.CreateBuilder();
apiBuilder.Logging.ClearProviders();
apiBuilder.WebHost.UseUrls("http://127.0.0.1:0");
apiBuilder.Services.AddControllers().AddApplicationPart(typeof(DanmakuApiController).Assembly);
apiBuilder.Services.AddAuthentication("fixture").AddScheme<AuthenticationSchemeOptions, FixtureAuthentication>("fixture", _ => { });
apiBuilder.Services.AddAuthorization();
apiBuilder.Services.AddSingleton(source);
await using (var api = apiBuilder.Build())
{
    api.UseAuthentication();
    api.UseAuthorization();
    api.MapControllers();
    await api.StartAsync();
    using var browser = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
    using var anonymous = await browser.GetAsync("/JellyfinDanmaku/api/v2/comment/1");
    Check(anonymous.StatusCode == HttpStatusCode.Unauthorized, "API requires authenticated Jellyfin session");
    browser.DefaultRequestHeaders.Add("Authorization", "MediaBrowser Token=\"fixture-session\"");
    using var authenticated = await browser.GetAsync("/JellyfinDanmaku/api/v2/comment/1");
    Check(authenticated.StatusCode == HttpStatusCode.OK && authenticated.Headers.CacheControl?.NoStore == true, "Authenticated controller serves JSON");
    upstream.Respond = _ => { var response = new HttpResponseMessage(HttpStatusCode.TooManyRequests); response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(TimeSpan.FromSeconds(60)); return response; };
    using var quota = await browser.GetAsync("/JellyfinDanmaku/api/v2/comment/1");
    Check(quota.StatusCode == HttpStatusCode.TooManyRequests && quota.Headers.RetryAfter?.Delta == TimeSpan.FromSeconds(60), "Controller preserves failure status and retry header");
    using var write = await browser.PostAsync("/JellyfinDanmaku/api/v2/comment/1", new StringContent("{}"));
    Check(write.StatusCode == HttpStatusCode.MethodNotAllowed, "Read API cannot post comments");
    await api.StopAsync();
}
Console.WriteLine($"PASS {checks} server checks: HTML/Edge compatibility, secret isolation, signed reads, session auth, bounded routes, CDN redirects and error handling");

sealed class EdgeFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => next;
}

sealed class FixtureHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public Func<HttpRequestMessage, HttpResponseMessage> Respond { get; set; } = respond;
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) => Task.FromResult(Respond(request));
}

sealed class FixtureAuthentication(IOptionsMonitor<AuthenticationSchemeOptions> options, ILoggerFactory logger, UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    protected override Task<AuthenticateResult> HandleAuthenticateAsync() => Task.FromResult(Request.Headers.Authorization == "MediaBrowser Token=\"fixture-session\""
        ? AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, "fixture-user")], "fixture")), "fixture"))
        : AuthenticateResult.NoResult());
}

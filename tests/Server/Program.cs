using System.Net;
using Jellyfin.Plugin.Danmaku;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.DependencyInjection;

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
        config.Enabled = false;
        html = await client.GetStringAsync(prefix + "/web/index.html");
        Check(!html.Contains("JellyfinDanmaku"), "Disabled plugin leaves index alone");
        using var disabledScript = await client.GetAsync(prefix + "/JellyfinDanmaku/ede.js");
        Check(disabledScript.StatusCode == HttpStatusCode.NotFound, "Disabled script unavailable");
        await app.StopAsync();
    }
}
finally { Directory.Delete(directory, true); }
// Existing XML should preserve the source while ignoring the removed feature.
var serializer = new System.Xml.Serialization.XmlSerializer(typeof(PluginConfiguration));
using var legacy = new StringReader("<PluginConfiguration><UseOwnCredentials>true</UseOwnCredentials><AppId>old-app</AppId><AppSecret>old-secret</AppSecret><ApiBaseUrl>https://danmaku.example.test/edge</ApiBaseUrl><CorsProxyUrl></CorsProxyUrl><DefaultEnabled>false</DefaultEnabled></PluginConfiguration>");
var migrated = (PluginConfiguration)serializer.Deserialize(legacy)!;
Check(migrated.ApiBaseUrl == "https://danmaku.example.test/edge" && migrated.CorsProxyUrl == "" && !migrated.DefaultEnabled, "Existing source and player preferences survive old XML");
using var saved = new StringWriter();
serializer.Serialize(saved, migrated);
Check(!saved.ToString().Contains("UseOwnCredentials") && !saved.ToString().Contains("AppId") && !saved.ToString().Contains("AppSecret"), "Saved configuration drops obsolete credentials");
var apiBuilder = WebApplication.CreateBuilder();
apiBuilder.Logging.ClearProviders();
apiBuilder.WebHost.UseUrls("http://127.0.0.1:0");
apiBuilder.Services.AddControllers().AddApplicationPart(typeof(Plugin).Assembly);
await using (var api = apiBuilder.Build())
{
    api.MapControllers();
    await api.StartAsync();
    using var browser = new HttpClient { BaseAddress = new Uri(api.Urls.Single()) };
    using var removed = await browser.GetAsync("/JellyfinDanmaku/api/v2/comment/1");
    Check(removed.StatusCode == HttpStatusCode.NotFound, "Removed server read API has no controller route");
    await api.StopAsync();
}
Console.WriteLine($"PASS {checks} server checks: HTML/Edge compatibility, BaseUrl, HEAD, compression, public defaults, disabled plugin and legacy XML upgrade");

sealed class EdgeFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => next;
}

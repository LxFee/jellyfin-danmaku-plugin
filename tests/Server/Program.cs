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
Console.WriteLine($"PASS {checks} server checks: static-file response buffering, Edge coexistence, prefix, compression, HEAD, defaults and disable");

sealed class EdgeFilter : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => next;
}

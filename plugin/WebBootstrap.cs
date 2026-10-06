using System.Text;
using System.Text.Json;
using System.IO.Pipelines;
using MediaBrowser.Common.Net;
using MediaBrowser.Controller;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.DependencyInjection;

namespace Jellyfin.Plugin.Danmaku;

public sealed class ServiceRegistrator : IPluginServiceRegistrator
{
    public void RegisterServices(IServiceCollection services, IServerApplicationHost applicationHost)
    {
        // The outer filter composes the downstream index, including Edge's injected script.
        services.Insert(0, ServiceDescriptor.Transient<IStartupFilter, WebBootstrap>());
    }
}

public sealed class WebBootstrap(IServerConfigurationManager server) : IStartupFilter
{
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
    {
        app.UseMiddleware<DanmakuMiddleware>(
            (Func<string>)(() => server.GetNetworkConfiguration().BaseUrl.TrimEnd('/')),
            (Func<PluginConfiguration>)(() => Plugin.Instance.Configuration));
        next(app);
    };
}

public sealed class DanmakuMiddleware(RequestDelegate next, Func<string> prefix, Func<PluginConfiguration> configuration)
{
    private static readonly string Source = ReadResource();

    public async Task InvokeAsync(HttpContext context)
    {
        var config = configuration();
        if (!config.Enabled || context.Request.Method is not ("GET" or "HEAD"))
        {
            await next(context).ConfigureAwait(false);
            return;
        }
        var scriptPath = prefix() + "/JellyfinDanmaku/ede.js";
        var path = context.Request.Path.Value;
        if (path == scriptPath)
        {
            var defaults = JsonSerializer.Serialize(new { defaultEnabled = config.DefaultEnabled,
                preferLocalXml = config.PreferLocalXml, apiBaseUrl = config.ApiBaseUrl, corsProxyUrl = config.CorsProxyUrl });
            var body = Encoding.UTF8.GetBytes("window.JellyfinDanmakuConfig=" + defaults + ";\n" + Source);
            context.Response.ContentType = "text/javascript; charset=utf-8";
            context.Response.Headers.CacheControl = "no-store";
            context.Response.ContentLength = body.Length;
            if (context.Request.Method == "GET") await context.Response.Body.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
            return;
        }
        if (path != prefix() + "/web/" && path != prefix() + "/web/index.html")
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        var originalFeature = context.Features.Get<IHttpResponseBodyFeature>()!;
        var originalEncoding = context.Request.Headers.AcceptEncoding;
        var originalMethod = context.Request.Method;
        using var buffer = new MemoryStream();
        var bufferedFeature = new BufferedBodyFeature(buffer);
        context.Features.Set<IHttpResponseBodyFeature>(bufferedFeature);
        // Request an uncompressed GET for both verbs so HEAD describes the injected representation.
        context.Request.Headers.AcceptEncoding = "identity";
        context.Request.Method = "GET";
        try
        {
            await next(context).ConfigureAwait(false);
            await bufferedFeature.Writer.FlushAsync(context.RequestAborted).ConfigureAwait(false);
            if (context.Response.StatusCode == 200 && buffer.Length <= 1024 * 1024
                && context.Response.ContentType?.StartsWith("text/html", StringComparison.OrdinalIgnoreCase) == true
                && !context.Response.Headers.ContainsKey("Content-Encoding"))
            {
                var html = Encoding.UTF8.GetString(buffer.ToArray());
                var position = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
                if (position >= 0 && !html.Contains("/JellyfinDanmaku/ede.js", StringComparison.Ordinal))
                {
                    var script = "<script defer src=\"" + System.Net.WebUtility.HtmlEncode(scriptPath) + "\"></script>";
                    var body = Encoding.UTF8.GetBytes(html.Insert(position, script));
                    buffer.SetLength(0);
                    await buffer.WriteAsync(body, context.RequestAborted).ConfigureAwait(false);
                    context.Response.Headers.Remove("ETag");
                    context.Response.Headers.Remove("Last-Modified");
                    context.Response.Headers.CacheControl = "no-store";
                    context.Response.ContentLength = body.Length;
                }
            }
        }
        finally
        {
            context.Features.Set(originalFeature);
            context.Request.Headers.AcceptEncoding = originalEncoding;
            context.Request.Method = originalMethod;
        }
        if (originalMethod == "GET")
        {
            buffer.Position = 0;
            await buffer.CopyToAsync(originalFeature.Stream, context.RequestAborted).ConfigureAwait(false);
        }
    }

    private static string ReadResource()
    {
        using var source = typeof(DanmakuMiddleware).Assembly.GetManifestResourceStream("Jellyfin.Plugin.Danmaku.Web.ede.js")!;
        using var reader = new StreamReader(source, Encoding.UTF8);
        return reader.ReadToEnd();
    }

    // StaticFileMiddleware calls StartAsync/SendFileAsync. Delay the real response start until injection.
    private sealed class BufferedBodyFeature(MemoryStream buffer) : IHttpResponseBodyFeature
    {
        public Stream Stream => buffer;
        public PipeWriter Writer { get; } = PipeWriter.Create(buffer, new StreamPipeWriterOptions(leaveOpen: true));
        public void DisableBuffering() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public async Task CompleteAsync() => await Writer.FlushAsync().ConfigureAwait(false);
        public async Task SendFileAsync(string path, long offset, long? count, CancellationToken cancellationToken = default)
        {
            await using var file = System.IO.File.OpenRead(path);
            file.Position = offset;
            var remaining = count ?? file.Length - offset;
            var chunk = new byte[65536];
            while (remaining > 0)
            {
                var read = await file.ReadAsync(chunk.AsMemory(0, (int)Math.Min(chunk.Length, remaining)), cancellationToken).ConfigureAwait(false);
                if (read == 0) break;
                await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken).ConfigureAwait(false);
                remaining -= read;
            }
        }
    }
}

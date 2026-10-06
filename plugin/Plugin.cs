using MediaBrowser.Common.Configuration;
using MediaBrowser.Common.Plugins;
using MediaBrowser.Model.Plugins;
using MediaBrowser.Model.Serialization;

namespace Jellyfin.Plugin.Danmaku;

public sealed class Plugin : BasePlugin<PluginConfiguration>, IHasWebPages
{
    public static Plugin Instance { get; private set; } = null!;

    public Plugin(IApplicationPaths paths, IXmlSerializer serializer) : base(paths, serializer) => Instance = this;

    public override Guid Id => new("aab50987-0d10-4615-96e3-3a89b758dd28");
    public override string Name => "Jellyfin Danmaku";
    public override string Description => "Web player danmaku based on Izumiko/jellyfin-danmaku, adapted for Jellyfin 12.2.";
    public override string ConfigurationFileName => "Jellyfin.Plugin.Danmaku.xml";

    public IEnumerable<PluginPageInfo> GetPages()
    {
        yield return new PluginPageInfo { Name = "JellyfinDanmaku", DisplayName = "弹幕", EmbeddedResourcePath = "Jellyfin.Plugin.Danmaku.Configuration.config.html" };
    }

    public override void UpdateConfiguration(BasePluginConfiguration configuration)
    {
        var value = (PluginConfiguration)configuration;
        ValidateUrl(value.ApiBaseUrl, false);
        ValidateUrl(value.CorsProxyUrl, true);
        base.UpdateConfiguration(configuration);
    }

    private static void ValidateUrl(string value, bool allowEmpty)
    {
        if (allowEmpty && string.IsNullOrWhiteSpace(value)) return;
        if (value.Length > 2048 || !Uri.TryCreate(value, UriKind.Absolute, out var uri)
            || uri.Scheme is not ("http" or "https") || !string.IsNullOrEmpty(uri.UserInfo)
            || !string.IsNullOrEmpty(uri.Query) || !string.IsNullOrEmpty(uri.Fragment))
            throw new ArgumentException("弹幕接口地址必须为 HTTP(S) URL，不能包含账号、查询参数或片段。");
    }
}

public sealed class PluginConfiguration : BasePluginConfiguration
{
    public bool Enabled { get; set; } = true;
    public bool DefaultEnabled { get; set; } = true;
    public bool PreferLocalXml { get; set; }
    public string ApiBaseUrl { get; set; } = "https://api.dandanplay.net";
    public string CorsProxyUrl { get; set; } = "https://ddplay-api.930524.xyz/cors/";
}

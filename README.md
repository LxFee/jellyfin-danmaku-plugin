# Jellyfin Danmaku

独立的 Jellyfin 网页弹幕插件，基于 Izumiko/jellyfin-danmaku 1.61 适配，已验证 Jellyfin 12.2 与桌面 Chrome。

支持在线搜索、集数匹配、手动添加弹幕源，以及样式、过滤和时间偏移设置。通过插件配置下发 API 与 CORS 地址，安装时无需修改 Jellyfin Web 源文件。

从 [GitHub Releases](https://github.com/LxFee/jellyfin-danmaku-plugin/releases/tag/v1.1.0.4) 下载 ZIP，解压到 Jellyfin 插件目录并重启服务。当前版本为 1.1.0.4 预发布版，已移除 AppId／AppSecret 功能。

[安装、使用与边界](docs/README.md) · [公共 API 候选](docs/public-sources.md) · [兼容性验证](docs/compatibility.md) · [上游与构建](docs/upstream.md)

采用 [MIT](LICENSE)。代码来源、内嵌渲染引擎与接口参考见 [来源与许可](docs/attribution.md)。[公开迁移说明](docs/publication.md)记录历史与发布包的清理范围。

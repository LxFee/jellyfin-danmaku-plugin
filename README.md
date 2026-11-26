# Jellyfin Danmaku

独立的 Jellyfin 网页弹幕插件，基于 Izumiko/jellyfin-danmaku 1.61 适配，已验证 Jellyfin 12.2 与桌面 Chrome。

支持在线搜索、集数匹配、手动添加弹幕源，以及样式、过滤和时间偏移设置。通过插件配置下发 API 与 CORS 地址，安装时无需修改 Jellyfin Web 源文件。

从 [GitHub Releases](https://github.com/LxFee/jellyfin-danmaku-plugin/releases) 下载 ZIP，解压到 Jellyfin 插件目录并重启服务。当前版本为 1.1.0.5 预发布版，修复播放器个人设置保存报错，已移除 AppId／AppSecret 功能。也可在 Jellyfin 控制台添加插件仓库 `https://raw.githubusercontent.com/LxFee/jellyfin-danmaku-plugin/main/repository.json`（首次发布后可用）后从插件目录安装和升级，详见 [发布与插件仓库](docs/release.md)。

[安装、使用与边界](docs/README.md) · [公共 API 候选](docs/public-sources.md) · [兼容性验证](docs/compatibility.md) · [上游与构建](docs/upstream.md) · [发布与插件仓库](docs/release.md)

采用 [MIT](LICENSE)。代码来源、内嵌渲染引擎与接口参考见 [来源与许可](docs/attribution.md)。[公开迁移说明](docs/publication.md)记录历史与发布包的清理范围。

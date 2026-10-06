# Jellyfin Danmaku

独立的 Jellyfin 网页弹幕插件，服务端使用 C#/.NET 10，前端内置适配后的 `ede.js`。目标 ABI 为 Jellyfin 12.2.0.0。插件管理与 Edge、代理节点无关。

## 安装与使用

从 [GitHub Releases](https://github.com/LxFee/jellyfin-danmaku-plugin/releases/tag/v1.1.0.4) 下载 ZIP，解压到 Jellyfin 的 `config/plugins/Jellyfin.Danmaku_1.1.0.4/`，保留 DLL、manifest.json、LICENSE 与 docs。升级时把旧版本移出 plugins 目录，重启 Jellyfin。控制台 → 插件 → Jellyfin Danmaku 配置启用开关、默认弹幕源。修改配置后刷新网页。

在网页内置播放器底部，弹幕图标控制显示，旁边的调节图标打开弹幕设置。支持样式、过滤、偏移、手动匹配和增加弹幕源。个人设置与匹配记录保存在浏览器 localStorage，插件页面的默认开关和本地 XML 偏好只用于没有个人设置的浏览器。

“弹幕 API 地址”填写兼容弹弹play API 的基础地址，不包含 `/api/v2`。浏览器通过“CORS 代理前缀”请求该 API，留空则直接请求，接口需允许跨域。播放器中的个人 API／CORS 地址优先于插件默认值。[公共站点核验](public-sources.md)记录可配置的替代服务。

插件不提供 AppId／AppSecret 配置或主站签名接口。升级会忽略旧配置中的凭据字段，保存配置后移除它们，保留 API、CORS 和播放器偏好。公共接口可能限流或耗尽额度；失败不影响视频播放，可修改接口后保存弹幕设置重试。

本地 XML 优先模式仍需另装提供 `/api/danmu/{itemId}/raw` 的数据插件。独立弹幕插件没有实现该插件的下载、匹配和扫描任务。

## 边界

功能限于从该 Jellyfin 主站加载页面的网页内置播放器。外部播放器、原生播放器、视频画中画和分享点播视频不会自动携带弹幕；本插件没有把弹幕烘焙到视频。Android/iOS 的 WebView 未实机验证，不承诺所有客户端兼容。

插件通过启动中间件组合网页 HTML 响应并注入内置脚本，不改写 Jellyfin Web 文件。使用稳定的标准页面路径 `/web/`、`/web/index.html`，支持 BaseUrl。它可以与 Edge 的网页菜单脚本共存；其他任意网页注入插件仍需分别验证。

[兼容性证据](compatibility.md) · [上游与维护](upstream.md)

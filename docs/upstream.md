# 上游与适配

来源：[Izumiko/jellyfin-danmaku](https://github.com/Izumiko/jellyfin-danmaku/tree/f6447c1a102c12d92add6045a069111ad065aadf)，`jellyfin` 分支的提交 `f6447c1a102c12d92add6045a069111ad065aadf`，脚本版本 1.61。保留 MIT 许可证、原始作者头部及内嵌组件署名；完整来源记录见 [来源与许可](attribution.md)。

适配源码在 `plugin/Web/ede.js`，`tools/vendor.py` 从该提交的 `ede.js` 重现修改。更新上游时重新审查和验证，不在运行时拉取可变 CDN 版本。

适配点：

- 从成功的 fetch/XHR PlaybackInfo 请求路径获取 ItemId，支持参数；不把 MediaSourceId 当作条目 ID。
- 初始化可等待播放器和 ApiClient，切集使过期加载失效，退出销毁渲染器与播放器观察器。
- 提供独立设置按钮。12.2 原生播放器设置菜单会拒绝未知 action ID，上游菜单注入会引发未处理的 Promise rejection。
- 本地 XML API 使用标准 Authorization 中的当前 Jellyfin 会话，不依赖 12.2 默认关闭的旧鉴权头；XML sender 保留字符串。公共弹幕 API 请求不携带 Jellyfin 会话。
- 弹幕请求超时、失败后可重试，显示 HTTP 状态；转义输入属性，调试日志用 textContent。
- 服务端默认配置不覆盖浏览器个人设置，重复脚本不创建额外渲染器。
- API 与 CORS 代理可由插件配置下发；移除自有应用凭据模式和主站签名接口，播放器直接读取公共或自建兼容接口。
- 插件配置页使用独立的大小写命名空间，避免播放器的 `[id*="danmaku"]` 样式选择器误命中；API／CORS 输入框与复选框使用 Jellyfin 原生控件。

构建：`dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release`。打包：在 PowerShell 中执行 `./tools/package.ps1`。服务端引用官方 NuGet `Jellyfin.Common` / `Jellyfin.Controller` 12.2.0，插件 ZIP 不附带这些宿主程序集。发布 DLL 关闭调试符号，包中包含 LICENSE 与来源文档。

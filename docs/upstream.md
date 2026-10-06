# 上游与适配

来源：[Izumiko/jellyfin-danmaku](https://github.com/Izumiko/jellyfin-danmaku)，`jellyfin` 分支的提交 `f6447c1a102c12d92add6045a069111ad065aadf`，脚本版本 1.61。保留 MIT 许可证与完整脚本中的第三方声明。

适配源码在 `plugin/Web/ede.js`，`tools/vendor.py` 从该提交的 `ede.js` 重现修改。更新上游时重新审查和验证，不在运行时拉取可变 CDN 版本。

适配点：

- 从成功的 fetch/XHR PlaybackInfo 请求路径获取 ItemId，支持参数；不把 MediaSourceId 当作条目 ID。
- 初始化可等待播放器和 ApiClient，切集使过期加载失效，退出销毁渲染器与播放器观察器。
- 提供独立设置按钮。12.2 原生播放器设置菜单会拒绝未知 action ID，上游菜单注入会引发未处理的 Promise rejection。
- 同源 XML API 使用当前 Jellyfin 会话鉴权；XML sender 保留字符串。
- 弹幕请求超时、失败后可重试，显示 HTTP 状态；转义输入属性，调试日志用 textContent。
- 服务端默认配置不覆盖浏览器个人设置，重复脚本不创建额外渲染器。
- 自有应用凭据模式使用同源鉴权读取接口，优先于个人 API／CORS 覆盖；设置侧栏显示主站模式提示，应用凭据只由主站签名使用。

构建：`dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release`。打包：`pwsh tools/package.ps1`。服务端引用官方 NuGet `Jellyfin.Common` / `Jellyfin.Controller` 12.2.0，插件 ZIP 不附带这些宿主程序集。

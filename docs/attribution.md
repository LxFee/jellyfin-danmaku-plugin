# 来源与许可

本项目采用 [MIT](../LICENSE)，保留上游和内嵌组件的版权声明。新增的服务端插件、配置页面、适配代码及测试也按 MIT 发布。

| 来源 | 本项目的使用方式 | 许可与署名 |
| --- | --- | --- |
| [Izumiko/jellyfin-danmaku](https://github.com/Izumiko/jellyfin-danmaku/tree/f6447c1a102c12d92add6045a069111ad065aadf) | 直接代码来源：`jellyfin` 分支固定提交 `f6447c1a102c12d92add6045a069111ad065aadf` 的 `ede.js`，脚本版本 1.61；适配后内置于插件 | [该提交的 MIT 许可证](https://github.com/Izumiko/jellyfin-danmaku/blob/f6447c1a102c12d92add6045a069111ad065aadf/LICENSE)，Copyright (c) 2022 Lee；脚本原始头部署名 RyoLee，完整保留 |
| [9channel/dd-danmaku](https://github.com/9channel/dd-danmaku) | 上述仓库的原始项目，记录代码沿革；没有另行导入其当前版本 | [MIT](https://github.com/9channel/dd-danmaku/blob/master/LICENSE)，Copyright (c) 2022 Lee |
| [weizhenye/Danmaku](https://github.com/weizhenye/Danmaku) | 上游 `ede.js` 内嵌的弹幕渲染引擎派生代码，随固定脚本一起使用 | [MIT](https://github.com/weizhenye/Danmaku/blob/v2.0.8/LICENSE)，Copyright (c) 2014 Zhenye Wei |
| [Jellyfin](https://github.com/jellyfin/jellyfin) | 服务端插件 API、Web 播放器集成；编译引用官方 `Jellyfin.Common` / `Jellyfin.Controller` 12.2.0 | 宿主及官方程序集由 Jellyfin 项目提供，插件发布包不包含宿主程序集 |
| [弹弹play开放平台](https://doc.dandanplay.com/open/) | 在线搜索、集数匹配和弹幕响应格式的接口参考 | 外部服务与数据由各服务商提供，其使用条款及数据权利独立于插件代码许可证 |

内嵌渲染代码没有可唯一判定版本的头部，因此不将其标记为原封不动的 Danmaku 2.0.8；代码来源以固定的 `ede.js` 提交为准。链接中的 2.0.8 LICENSE 用于核对 MIT 声明。

本项目新增了 C# 插件封装、网页响应注入、插件配置下发，以及 Jellyfin 12.2 的条目识别、播放器生命周期、会话鉴权、设置按钮和错误处理适配。修改及重现方式见 [上游与适配](upstream.md)。公共 API 候选的参考链接与实测范围见 [公共站点核验](public-sources.md)。

发行包附带根目录 LICENSE 和本文件。公共弹幕接口可能限流或停止服务，插件的 MIT 许可不构成这些接口的服务承诺。

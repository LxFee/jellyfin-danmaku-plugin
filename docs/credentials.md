# 弹弹play应用凭据

在[开发者中心](https://dev.dandanplay.com/)申请应用，获得 AppId 和 AppSecret 后，进入 Jellyfin 控制台 → 插件 → Jellyfin Danmaku，将“在线弹幕来源”选择为“自己的 AppId 和 AppSecret”，填写两个字段并保存，刷新播放器网页。

密钥由管理员通过 Jellyfin 原生插件配置 API 编辑，保存在主站的插件配置 XML，按主站配置卷的权限和备份方式管理。它不会进入播放器脚本、localStorage、源代码或发布包。播放器只向同源 `/JellyfinDanmaku/api/v2/…` 请求弹幕，携带当前 Jellyfin 会话；BaseUrl 同样生效。

主站使用[官方签名规则](https://doc.dandanplay.com/open/)请求固定的 `https://api.dandanplay.net`：`base64(sha256(AppId + Timestamp + Path + AppSecret))`，Path 不含域名和查询参数。仅开放搜索、番剧信息、弹幕、相关来源和外部来源的 GET 读取接口；不开放账号登录或发送弹幕。官方 CDN 重定向不携带应用签名或 Jellyfin 会话。

两种在线来源互斥，配置页只显示所选模式的字段，保存时保留另一种模式已保存的配置。自有凭据模式不使用公共 CORS 代理，也忽略当前浏览器保存的 API／CORS 地址覆盖；播放器显示主站凭据模式提示。切回“公共／自建接口”后恢复该模式的地址，应用凭据保留便于再次使用。

所有已登录用户共用管理员配置的应用额度；自有凭据仍受弹弹play额度和频率限制。429 保留 Retry-After，403 提示检查凭据和主站时间。当前版本没有主站共享弹幕缓存，也不自动回退到公共代理；失败后可在播放器保存弹幕设置重试。

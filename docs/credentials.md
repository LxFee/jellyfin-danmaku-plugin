# 弹弹play应用凭据

在[开发者中心](https://dev.dandanplay.com/)申请应用，获得 AppId 和 AppSecret 后，进入 Jellyfin 控制台 → 插件 → Jellyfin Danmaku，填写共用的“弹幕 API 地址”，将“接口鉴权方式”选择为“自己的 AppId 和 AppSecret”，填写两个字段并保存，刷新播放器网页。AppId／AppSecret 用于请求签名，不需要先登录弹弹play账号。

密钥由管理员通过 Jellyfin 原生插件配置 API 编辑，保存在主站的插件配置 XML，按主站配置卷的权限和备份方式管理。它不会进入播放器脚本、localStorage、源代码或发布包。播放器只向同源 `/JellyfinDanmaku/api/v2/…` 请求弹幕，携带当前 Jellyfin 会话；BaseUrl 同样生效。

主站使用[官方签名规则](https://doc.dandanplay.com/open/)请求管理员配置的 API 地址，默认 `https://api.dandanplay.net`：`base64(sha256(AppId + Timestamp + Path + AppSecret))`。地址填写基础 URL，不包含 `/api/v2`，可包含自建服务的路径前缀；例如 `https://example.com/edge` 请求 `/edge/api/v2/…`，Path 使用实际请求路径，含此前缀但不含域名和查询参数。接口需兼容弹弹play API 和签名规则。仅开放搜索、番剧信息、弹幕、相关来源和外部来源的 GET 读取接口；不开放账号登录或发送弹幕。同源下载或官方 CDN 重定向不携带应用签名或 Jellyfin 会话。

两种鉴权方式互斥，API 地址始终显示并共用，只切换 CORS 代理与应用凭据字段，保存时保留另一种模式已保存的配置。公共／自建接口模式由浏览器通过 CORS 代理请求该 API；自有凭据模式由主站签名请求同一个 API，不使用 CORS 代理，也忽略当前浏览器保存的 API／CORS 地址覆盖。播放器显示主站凭据模式提示。

所有已登录用户共用管理员配置的应用额度；自有凭据仍受弹弹play额度和频率限制。429 保留 Retry-After，403 提示检查凭据和主站时间。当前版本没有主站共享弹幕缓存，也不自动回退到公共代理；失败后可在播放器保存弹幕设置重试。

# 公共弹幕 API 兼容性

核验日期：2026-10-06；适用插件：Jellyfin.Danmaku 1.1.0.4。公共接口状态和配额会变化，下面是本次检查结果。

## 当前插件的接口要求

公共模式由浏览器直接请求 API，因此需要同时满足返回格式和浏览器 CORS：

- `GET /api/v2/search/episodes?anime={作品名}` 返回 `animes[].episodes[]`。
- `GET /api/v2/comment/{episodeId}?withRelated=true&chConvert=0` 返回 `comments[]`，每条包含 `p`、`m`。
- 允许 Jellyfin 页面来源跨域读取响应。

调用契约见 [plugin/Web/ede.js](../plugin/Web/ede.js)。这里只提供平台视频 URL 到 XML 的解析站不能直接替换这个 API。

## 可作为替换候选的服务

| 服务 | 配置方式 | 本次接口结果 | 公开来源与限制 |
| --- | --- | --- | --- |
| `ddplay-api.7o7o.cc` | API 地址 `https://api.dandanplay.net`；CORS 代理 `https://ddplay-api.7o7o.cc/cors/` | 搜索与弹幕均 HTTP 200；返回兼容数据；`Access-Control-Allow-Origin: *` | [chen3861229/dd-danmaku 的公开脚本](https://github.com/chen3861229/dd-danmaku/blob/main/ede.js)将其用作默认代理。没有核实长期服务或配额承诺。 |
| `dandan-proxy.wiidede.space` | API 地址 `https://dandan-proxy.wiidede.space`；CORS 代理留空 | 搜索与弹幕均 HTTP 200；返回兼容数据；`Access-Control-Allow-Origin: *` | [wiidede/dandanplay-vi 的公开客户端源码](https://github.com/wiidede/dandanplay-vi/blob/main/src/utils/fetch.ts)使用该接口。未找到向第三方开放服务的明确政策或稳定性承诺。 |

上述检查使用《葬送的芙莉莲》搜索及第一集 `176170001` 的弹幕；两者返回 3 个搜索候选、7310 条弹幕。另用 Chrome 从 Jellyfin 测试页面来源加载当前插件脚本，两个服务均通过真实网络完成自动搜索、集数匹配和弹幕读取，渲染器接收 7310 条弹幕，没有未处理的页面异常；wiidede 的弹幕请求重定向至 CDN 后也能跨域读取。测试页面的 Jellyfin 元数据、播放器壳和媒体元素为模拟，未验证实际媒体播放、全部作品或所有客户端。测试没有发送 AppId、AppSecret、私人 API token 或 Jellyfin 会话。两个服务仍然是弹弹play代理，换代理地址不会换掉上游弹幕平台。

1.1.0.4 起仅提供公共／自建接口配置，不再提供 AppId／AppSecret 或鉴权方式选择。播放器个人设置中的 API／CORS 覆盖优先于插件默认值；测试或切换时需要清除旧覆盖，否则仍可能请求旧站点。

## 不能直接替换的候选

| 服务 | 主要来源 | 本次结果与结论 |
| --- | --- | --- |
| `danmaku.kaloscope.org` | [官方文档](https://kaloscope.org/docs/medialibs/danmaku)、[代理源码](https://github.com/kaloscope/danmaku) | 文档明确提供兼容代理。搜索、第一集弹幕 HTTP 200，返回 7310 条兼容弹幕；响应没有 CORS 许可，Chrome 从试用 Jellyfin 来源直接读取也失败，当前插件公共模式不能从浏览器直接读取。可供带转发能力的客户端使用。 |
| `danmaku-api.152468.xyz` | [uosc_danmaku README](https://github.com/Tony15246/uosc_danmaku)、[调用源码](https://github.com/Tony15246/uosc_danmaku/blob/main/apis/dandanplay.lua) | 项目公开提供代理、普通用户无需自己的应用凭据，并说明有缓存和频率限制、禁止批量抓取。当前插件需要的 `search/episodes` 返回 HTTP 404、`Unsupported API`；该客户端使用 `search/anime` 后接 `bangumi`，不能直接套用。 |
| `api.danmaku.weeblify.app/ddp` | [透明路由源码](https://github.com/Mr-Quin/danmaku-anywhere/blob/master/backend/proxy/src/routes/api/ddp/transparent.ts)、[CORS 实现](https://github.com/Mr-Quin/danmaku-anywhere/blob/master/backend/proxy/src/index.ts)、[生产配置](https://github.com/Mr-Quin/danmaku-anywhere/blob/master/backend/proxy/wrangler.json) | 搜索 HTTP 200，但跨域仅允许该项目自己的 `https://danmaku.weeblify.app` 页面。未发现向第三方开放代理的明确政策；不作为 Jellyfin 的直接替换地址。 |
| `bvd.xrzyun.eu.org/api/ddplay` | [站点自己的代理说明](https://bvd.xrzyun.eu.org/) | 文档提供 Dandan 透传代理，本次搜索 HTTP 403；不能用。 |
| `danmu.huangse.de`、`letitbe-danmu-api.com` | 对应站点公开 API 响应 | 搜索均 HTTP 401；需要运营者提供访问授权，不能作为无需凭据的公共站推荐。 |
| `dx.sld.tw` | [站点首页](https://dx.sld.tw/) | 搜索索引中的站点说明自称公益服务，但本次 API 读取 15 秒超时；未确认可用。 |
| `danmaku.movie.kg` | [站点自己的扩展说明](https://danmaku.movie.kg/) | 提供 Emby 扩展且说明个人使用政策，本次站点连接失败；未确认可供本插件配置的兼容基址。 |

默认代理 `ddplay-api.930524.xyz` 本次搜索仍 HTTP 200，但第一集弹幕 HTTP 429，响应说明代理当日回源配额已达到保护上限。因此只测试搜索成功不能判定一个弹幕站可用。

## 其他站点和自建项目

[danmu.zxz.ee](https://danmu.zxz.ee/) 和 [danmu.icu](https://www.danmu.icu/) 提供的是平台 URL 解析接口，不能直接填入当前插件的 Dandan API 地址。[LogVar 文档](https://bks.indevs.in/knowledge/api-reference)记录了兼容接口和实例 token 的填写方式；其源码与部署文档代表可以自建服务，不代表存在无需授权的公共实例。

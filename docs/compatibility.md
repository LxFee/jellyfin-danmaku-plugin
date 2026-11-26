# Jellyfin 12.2 兼容性

验证版本为插件 1.1.0.4；1.1.0.5 仅重新执行固定数据前端检查与 Release 构建，未重跑主站／代理入口验收。目标为服务端 12.2.0、官方 Web 12.2、桌面 Chrome。验证结果不能扩展为其他 Jellyfin 版本或所有移动客户端已兼容。

| 验证 | 结果 |
| --- | --- |
| 官方 NuGet 12.2.0 编译及宿主加载 | 通过，无编译警告 |
| 网页静态响应、Edge 共存、BaseUrl、HEAD、压缩请求、禁用开关和旧 XML 升级 | 1.1.0.4 的 45 项服务端检查通过；旧配置保留 API／CORS 与播放器偏好，保存后删除已废弃的凭据字段，旧签名接口不存在 |
| fetch/XHR 带参数请求、ItemId 与 MediaSourceId 不同、延迟初始化、重复加载、同一 video 元素切集 | 前端固定数据检查通过 |
| 源返回 429 后恢复并保存设置重试 | 前端固定数据检查通过 |
| 公共请求不携带 Jellyfin 会话 | 前端固定数据检查通过 |
| 原生插件配置页读取和保存 | 1.1.0.4 主站与代理入口通过；API 必填、CORS 可留空、两个原生输入框样式一致、保存刷新正确，已移除鉴权选择与凭据字段 |
| 移除主站签名功能 | 真实主站与代理入口的已鉴权请求均确认旧读取接口 HTTP 404，配置 API 不再返回凭据字段 |
| 原生视频播放、弹幕 canvas、开关、设置侧栏保存、退出清理与重新播放 | 真实 12.2 视频 + 固定弹幕响应通过；当时的设置保存只验证弹幕恢复，未校验本地偏好写入，回归见下节 |
| 代理入口完整页面验收 | 通过；真实视频 + 固定弹幕响应完成播放、设置保存、退出清理及重播 |
| 上游公共在线弹幕源 | 默认代理回源配额耗尽时返回 HTTP 429；替代服务的在线检查范围见 [公共站点核验](public-sources.md) |

### 播放器个人设置保存回归

1.1.0.4 发布包在移除 12.2 不接受的设置菜单注入代码块时，连带丢失了同块内的 `parseFloatOfRange`。播放器侧保存个人设置会弹出 `Invalid input: parseFloatOfRange is not defined`，本地偏好不会写入；已有偏好的浏览器还会在脚本初始化时中断。当前源码与 `tools/vendor.py` 已保留该函数。

本次通过：`node tests/browser.cjs` 新增断言保存后写入 `danmakuopacity`／`danmakuspeed`／`danmakusize`／`danmakuheight`、无输入无效弹窗，并在带偏好重新加载后初始化成功；`dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release` 0 警告 0 错误。未重新执行真实 12.2 主站与代理入口验收，未验证其他 Jellyfin 版本。该修复已包含在 1.1.0.5 包中；1.1.0.5 未重新执行真实主站与代理入口验收。

浏览器固定数据只替换弹幕 API 响应，真实测试保留 Jellyfin 登录、API、视频、页面和播放操作。没有把固定数据写入插件默认配置。主站直接入口与代理入口均完成验收。

重现验证：

```sh
dotnet run --project tests/Server -c Release
node tests/browser.cjs
```

以上两项由 [CI 工作流](../.github/workflows/ci.yml) 在推送和 PR 时执行，发布标签时 Release 工作流也会重跑。

浏览器测试需要 Playwright 和 Chrome，执行 `npm --prefix tests ci` 安装测试依赖，或用 `PLAYWRIGHT_MODULE` 指定既有模块路径。真实环境测试使用 `JELLYFIN_TEST_CREDENTIALS` 指向私密 JSON（Username、Password、ItemId），`JELLYFIN_TEST_URL` 指向测试主站/代理，`DANMAKU_FIXTURE=1` 使用固定弹幕响应，然后执行 `node tests/live.cjs`。该测试会播放指定视频并保存同值插件配置，仅用于试用环境；凭据不提交。

配置交互检查使用相同的 `JELLYFIN_TEST_CREDENTIALS`、`JELLYFIN_TEST_URL` 和 Playwright 环境执行 `node tests/configuration.cjs`。该检查临时修改 API／CORS，验证显示、校验、保存刷新及样式，finally 恢复并核对原配置，不请求在线弹幕源。

安装和升级可以通过 Jellyfin 插件仓库，或直接使用插件目录（见 [发布与插件仓库](release.md)）。卸载后重启主站即可停止注入，无需恢复 Web 源文件。

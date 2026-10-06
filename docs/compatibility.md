# Jellyfin 12.2 兼容性

验证目标为服务端 12.2.0、官方 Web 12.2、桌面 Chrome。验证结果不能扩展为其他 Jellyfin 版本或所有移动客户端已兼容。

| 验证 | 结果 |
| --- | --- |
| 官方 NuGet 12.2.0 编译及宿主加载 | 通过，无编译警告 |
| 网页静态响应、Edge 共存、BaseUrl、HEAD、压缩请求、禁用开关 | 42 项服务端检查通过 |
| fetch/XHR 带参数请求、ItemId 与 MediaSourceId 不同、延迟初始化、重复加载、同一 video 元素切集 | 前端固定数据检查通过 |
| 源返回 429 后恢复并保存设置重试 | 前端固定数据检查通过 |
| 原生插件配置页读取和保存 | 真实 12.2 页面通过 |
| 原生视频播放、弹幕 canvas、开关、设置侧栏保存、退出清理与重新播放 | 真实 12.2 视频 + 固定弹幕响应通过 |
| 代理入口完整页面验收 | 通过；真实视频 + 固定弹幕响应完成播放、设置保存、退出清理及重播 |
| 上游公共在线弹幕源 | 本次返回 HTTP 429，未验证真实在线弹幕获取成功 |

浏览器固定数据只替换弹幕 API 响应，真实测试保留 Jellyfin 登录、API、视频、页面和播放操作。没有把固定数据写入插件默认配置。主站直接入口与代理入口均完成验收。

最初的代理验收遇到部分原生资源/API 返回 503。故障时间与主站容器更新重合，启动探针记录 503，随后 Jellyfin 完成启动。恢复后对原失败资源与公共接口重复检查全部返回 200，代理浏览器验收也通过；证据指向更新期间的短暂不可用，没有修改弹幕脚本或代理代码。

重现验证：

```sh
dotnet run --project tests/Server -c Release
node tests/browser.cjs
```

浏览器测试需要 Playwright 和 Chrome，执行 `npm --prefix tests ci` 安装测试依赖，或用 `PLAYWRIGHT_MODULE` 指定既有模块路径。真实环境测试使用 `JELLYFIN_TEST_CREDENTIALS` 指向私密 JSON（Username、Password、ItemId），`JELLYFIN_TEST_URL` 指向测试主站/代理，`DANMAKU_FIXTURE=1` 使用固定弹幕响应，然后执行 `node tests/live.cjs`。该测试会播放指定视频并保存同值插件配置，仅用于试用环境；凭据不提交。

安装和升级使用 Jellyfin 的插件目录。卸载后重启主站即可停止注入，无需恢复 Web 源文件。

# 发布与插件仓库

发布由 GitHub Actions 完成，不再需要手工构建或上传 ZIP：

| 工作流 | 触发 | 作用 |
| --- | --- | --- |
| [CI](../.github/workflows/ci.yml) | 推送到 `main`、Pull Request | Release 构建、服务端兼容检查、浏览器固定数据检查；不发布 |
| [Release](../.github/workflows/release.yml) | 推送 `v*` 标签 | 构建与检查、校验版本、生成 ZIP 与 `repository.json`、创建 GitHub Release、把仓库清单提交回 `main` |

## 发一个新版本

1. 同步版本号：`manifest.json`、`plugin/Jellyfin.Plugin.Danmaku.csproj`，以及 README／文档里的版本和下载链接。
2. 本地检查（可选，CI 会再跑一遍）：

   ```sh
   dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release
   dotnet run --project tests/Server -c Release
   node tests/browser.cjs
   ```

3. 提交并推送到 `main`，确认 CI 通过。
4. 打标签并推送：

   ```sh
   git tag v1.1.0.5
   git push origin v1.1.0.5
   ```

5. Release 工作流会：
   - 检查标签 `v1.1.0.5` 与 `manifest.json` 的 `version` 是否一致，不一致直接失败；
   - 生成 `artifacts/jellyfin-danmaku-1.1.0.5.zip` 和 `SHA256SUMS.txt`；
   - 生成 `repository.json`（ZIP 的 **MD5** 用作 `checksum`）；
   - 创建／更新 GitHub Release（当前按预发布处理），上传 ZIP、`SHA256SUMS.txt`、`repository.json`；
   - 把更新后的 `repository.json` 提交回 `main`。

## Jellyfin 插件仓库

发布成功后，在 Jellyfin 控制台添加：

```
https://raw.githubusercontent.com/LxFee/jellyfin-danmaku-plugin/main/repository.json
```

控制台 → 插件 → 存储库 → 添加上述 URL → 保存，然后到 插件 → 目录 安装 Jellyfin Danmaku；后续升级同样在插件页完成。

清单由脚本 [tools/repository_manifest.py](../tools/repository_manifest.py) 维护：保留历史版本、按版本号倒序、同一版本重复发布会覆盖。Jellyfin 下载 ZIP 后会校验 MD5，不匹配会报 checksum 错误。

- 首次发布前 `repository.json` 不存在，添加仓库 URL 会失败；发布第一个 `v*` 标签后才可用。
- 清单里的历史版本指向旧 Release 资源，删除旧 Release 会导致该版本无法从目录安装。
- Jellyfin 会缓存仓库清单；发布后若看不到新版本，稍等片刻、重启 Jellyfin 或重新添加仓库 URL 再刷新。

> 如果 Jellyfin 服务器访问 GitHub 受限，仓库清单和 Release 资源都会下载失败；这种情况下仍使用 ZIP 手动安装，或把清单／ZIP 镜像到可访问的地址后修改 `sourceUrl`。

## 权限与分支保护

- 仓库 Settings → Actions → General → Workflow permissions 需要允许 **Read and write**，`publish-manifest` 才能提交 `repository.json`。
- 如果 `main` 开了分支保护，需要允许 GitHub Actions 推送（或为规则集添加 Actions 例外），否则提交清单的步骤会失败；Release 本身仍会创建。
- 清单提交使用 `[skip ci]`，并且 CI 忽略只改 `repository.json` 的推送，不会重复跑测试。

## 手动打包／补发

```sh
dotnet build plugin/Jellyfin.Plugin.Danmaku.csproj -c Release
python3 tools/package.py --output-dir artifacts
python3 tools/repository_manifest.py \
  --zip artifacts/jellyfin-danmaku-1.1.0.5.zip \
  --source-url https://github.com/LxFee/jellyfin-danmaku-plugin/releases/download/v1.1.0.5/jellyfin-danmaku-1.1.0.5.zip \
  --existing repository.json \
  --output repository.json
```

`tools/package.py` 固定 ZIP 内部时间戳，同一 DLL 重复打包得到相同的 MD5。Windows 可以用 [`tools/package.ps1`](../tools/package.ps1) 生成 ZIP，再用 `repository_manifest.py` 生成清单。

清单提交失败时可手动补：

```sh
cp artifacts/repository.json repository.json
git add repository.json
git commit -m "chore: update plugin repository for v1.1.0.5 [skip ci]"
git push
```

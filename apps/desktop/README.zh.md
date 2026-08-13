# Windows 版 DeepSeek Harness 桌面端

[English](README.md) | 中文

> **社区 Windows 发行版。** 本桌面宿主由 [`evanmormmm/deepseek-harness-desktop`](https://github.com/evanmormmm/deepseek-harness-desktop) 维护，不是 DeepSeek 官方发布。Harness 源码仍采用 MIT 许可证，并跟随 [`deepseek-ai/deepseek-harness`](https://github.com/deepseek-ai/deepseek-harness)。

Windows 桌面端把现有 DeepSeek Harness Web UI 承载在原生 WebView2 窗口中。双击应用会在操作系统分配的 loopback 端口上启动私有 Harness 进程；关闭窗口会保存原生窗口位置、释放 profile，并等待后端进程退出。不再保留终端或浏览器标签页。

![DeepSeek Harness 桌面端主页](assets/screenshots/desktop-home.png)

## 三步完成安装

### 1. 下载安装器

打开[最新 GitHub Release](https://github.com/evanmormmm/deepseek-harness-desktop/releases/latest)，下载 `DeepSeek-Harness-Desktop-<version>-win-x64-Setup.exe`。同一页面的 `SHA256SUMS.txt` 提供校验值。目前安装器尚未签名，因此 Windows SmartScreen 可能显示未知发布者提示；运行前可用校验文件核对下载内容。

安装器要求 x64 的 Windows 10 1809 或更新版本。Windows 11 通常已经包含 Microsoft Edge WebView2 Runtime；如果启动信息提示缺少 WebView2，请安装 [Evergreen Runtime](https://developer.microsoft.com/en-us/microsoft-edge/webview2/)。安装后无需另装 Node.js、pnpm、PowerShell 或 .NET SDK。

### 2. 完成首次启动

从开始菜单启动 **DeepSeek Harness**。阅读开发者预览声明，然后选择**继续**。

![首次启动的开发者预览声明](assets/screenshots/first-launch.png)

### 3. 添加模型和工作区

打开**设置 → 模型**，添加 DeepSeek provider，并填写你自己的 API Key。密钥由现有 Harness 凭据 provider 保存在 `$DSH_HOME`（默认 `~/.dsh`）下；桌面生命周期日志不会记录凭据或模型请求正文。

![设置页面中的模型入口](assets/screenshots/settings.png)

返回主页，选择**选择工作区**，然后选取项目目录。该目录会成为每个新会话不可变的 cwd 和 `workspace-write` 根。新建会话后即可开始使用 Harness。

## 桌面宿主改变了什么

![桌面宿主架构](assets/diagrams/desktop-architecture.svg)

桌面层只负责显示与进程生命周期：

- **一个窗口、一个后端。** 再次启动会激活已有窗口，不会新开一套服务器。
- **私有随机地址。** 每次运行只绑定精确 IPv4 loopback 和操作系统分配的端口；其他本地端口不能在高权限 WebView 中导航。
- **有界关闭。** 关闭、更新或卸载会先请求 profile 释放并等待所属 Node 进程；只有超时才强制终止进程树。
- **沿用 Harness 行为。** 模型、会话、凭据、插件、权限、工具和工作区仍由 `$DSH_HOME` 下的上游 Harness profile 管理。
- **隔离外部导航。** 普通非 loopback `http`、`https` 和 `mailto` 链接交给 Windows 打开；`file`、`data`、脚本 scheme 和其他 loopback 源均被阻止。

架构图的可编辑源文件是 [`desktop-architecture.mmd`](assets/diagrams/desktop-architecture.mmd)。

## 便携版与卸载

Release 还提供 `DeepSeek-Harness-Desktop-<version>-win-x64.zip`。请先完整解压，再运行 `DeepSeek Harness.exe`；旁边的 `runtime` 目录不可缺少。除非通过环境变量选择另一位置，便携版和安装版会有意共用同一个 `$DSH_HOME`。

通过 **Windows 设置 → 应用 → 已安装的应用 → DeepSeek Harness Desktop** 卸载。卸载会删除应用和快捷方式，但保留 Harness 凭据、设置、会话、插件、工作区记录及 WebView 数据。只有在确实要清除这些状态时，才另行删除 `~/.dsh` 和 `%LOCALAPPDATA%\DeepSeek Harness`。

## 从源码构建和打包

前提是 Windows x64、Node `^22.19 || >=24`、pnpm、.NET 8 SDK、Microsoft Edge WebView2 Runtime，以及用于发行打包的 Inno Setup 6。

```powershell
pnpm install --frozen-lockfile
pnpm run desktop:build
pnpm run desktop:package
```

`desktop:build` 会构建 Harness，运行桌面单元测试和已构建适配器生命周期测试，发布自包含 .NET 宿主，部署生产 Harness 闭包，并验证打包后的后端与 WebView 生命周期。便携版输出位于 `.artifacts/DeepSeek-Harness-Desktop/`。

`desktop:package` 会在 `.artifacts/desktop-release/` 下生成安装器和便携 ZIP，静默安装到隔离目录，以 WebView 冒烟模式启动安装版，要求后端优雅退出，然后卸载并写入 `SHA256SUMS.txt`。

如需从已验证目录安装到当前用户：

```powershell
pnpm run desktop:install
```

仅对于脚本式本地安装，可运行下列命令删除安装内容和快捷方式，同时保留 `$DSH_HOME`：

```powershell
pwsh -NoProfile -File scripts/uninstall-desktop-windows.ps1
```

## 运行时与排错

发行目录包含 `runtime/node/node.exe`，以及位于 `runtime/harness` 的无符号链接生产部署。宿主启动 `runtime/harness/node_modules/@deepseek-ai/dsh/lib/desktop-bin.js`，验证其进程 id 和精确 `http://127.0.0.1:<port>` HTML 首页，然后导航 WebView2。窗口关闭会通过私有 stdin 发送 `shutdown`；八秒后，宿主只会终止自己拥有的进程树。

诊断日志追加到 `%LOCALAPPDATA%\DeepSeek Harness\logs\desktop.log`。启动失败时会显示**重试**和**打开日志**操作。`--workspace <绝对路径>` 可更改初始回退工作区，`--runtime <绝对路径>` 可选择另一套打包运行时，`DSH_DESKTOP_DEVTOOLS=1` 可在排错时启用 WebView2 开发者工具。

## 发行限制

- Windows x64 是目前唯一的原生宿主目标。
- 应用依赖系统已安装的 Evergreen WebView2 Runtime，不附带 Chromium。
- 发行二进制可复现验证并附带哈希，但尚未使用 Authenticode 签名。
- 更新通过 GitHub Releases 安装；目前没有应用内自动更新器。

# DeepSeek Harness Desktop

[English](README.md) | 中文

这是 [DeepSeek Harness](https://github.com/deepseek-ai/deepseek-harness) 的社区维护 Windows 桌面发行版，提供原生窗口、一键安装器、私有内置运行时，并且无需保留终端。

> 本仓库由 [`evanmormmm`](https://github.com/evanmormmm) 维护，不是 DeepSeek 官方发布。DeepSeek Harness 由 [DeepSeek AI](https://deepseek.com) 开发，并继续采用 [MIT 许可证](LICENSE)。

[**下载最新版 Windows 安装器 →**](https://github.com/evanmormmm/deepseek-harness-desktop/releases/latest)

![DeepSeek Harness 桌面端](apps/desktop/assets/screenshots/desktop-home.png)

## 你将获得

- **双击启动：** 从开始菜单或桌面启动，无需 PowerShell、pnpm 或浏览器标签页。
- **原生生命周期：** 一个 WinForms/WebView2 窗口拥有一套私有 Harness 后端，并在窗口关闭时停止后端。
- **沿用 Harness 功能：** 会话、工作区、插件、工具、权限、模型设置和 `$DSH_HOME` 数据均通过上游 Web UI 工作。
- **Windows 发行资产：** 每个 Release 提供安装器、便携 ZIP 和 `SHA256SUMS.txt`。
- **经过验证的打包：** 发行构建会测试部署后端、真实 WebView 页面加载、优雅关闭、静默安装及卸载。

<a id="run"></a>

## 安装

1. 打开[最新 Release](https://github.com/evanmormmm/deepseek-harness-desktop/releases/latest)。
2. 下载 `DeepSeek-Harness-Desktop-<version>-win-x64-Setup.exe`。
3. 使用 `SHA256SUMS.txt` 校验文件，运行安装器，然后从开始菜单打开 **DeepSeek Harness**。
4. 打开**设置 → 模型**，添加你的 DeepSeek API Key，再选择工作区并创建会话。

当前社区二进制尚未使用 Authenticode 签名，因此 Windows SmartScreen 可能显示发布者未知。带截图的完整步骤、便携版、前提条件、架构、卸载、排错和源码构建方式见 [Windows 图文指南](apps/desktop/README.md)。

## 架构

原生宿主嵌入现有 Harness Web 客户端，而不是另写一套聊天界面。它在精确 IPv4 loopback 的随机端口上启动内置 Node 运行时，验证子进程和 HTML 端点，只允许 WebView2 访问该源，并在关闭时等待有界 profile 释放完成。

![桌面生命周期架构](apps/desktop/assets/diagrams/desktop-architecture.svg)

<a id="run-from-source"></a>

## 从源码构建

```powershell
pnpm install --frozen-lockfile
pnpm run desktop:build
pnpm run desktop:package
```

发行资产会写入 `.artifacts/desktop-release/`。开发环境要求 Windows x64、Node `^22.19 || >=24`、pnpm、.NET 8 SDK、WebView2 Runtime 和 Inno Setup 6；安装版用户不需要这些开发工具。

## 上游与维护

本仓库保留完整上游 Git 历史，并让 `upstream` 指向 [`deepseek-ai/deepseek-harness`](https://github.com/deepseek-ai/deepseek-harness)。桌面宿主有意保持为 `apps/desktop` 下的轻量产品层；除启动已组装 Web profile 所需的桌面进程入口外，上游 Harness packages 保持原有结构。

Harness 架构和插件开发见 [AGENTS.md](AGENTS.md)、[开发指南](docs/development.md)与[架构文档](docs/architecture.md)。

## 许可证

[MIT](LICENSE)。第三方依赖及其许可证见 [THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)和每个桌面 Release。

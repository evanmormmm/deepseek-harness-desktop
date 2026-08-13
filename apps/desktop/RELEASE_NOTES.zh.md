# DeepSeek Harness Desktop 0.1.0

[English](RELEASE_NOTES.md) | 中文

这是首个社区 Windows 桌面发行版，基于 DeepSeek Harness `0.1.0-rc.5` 和上游提交 `47f943859bef60e4160492346772ded9b24f765a` 构建。

> 本二进制发行版由 `evanmormmm` 维护，不是 DeepSeek 官方发布。

## 下载

- **推荐：** `DeepSeek-Harness-Desktop-0.1.0-win-x64-Setup.exe` 会为当前 Windows 用户安装，并创建开始菜单快捷方式。
- **便携版：** `DeepSeek-Harness-Desktop-0.1.0-win-x64.zip` 需要完整解压目录后运行。
- **校验：** `SHA256SUMS.txt` 包含两个资产的 SHA-256 哈希。

二进制尚未使用 Authenticode 签名。Windows SmartScreen 可能提示未知发布者。

## 亮点

- 在现有 Harness Web 客户端外提供原生 WinForms/WebView2 窗口。
- 内置私有 Node.js 和生产 Harness 运行时；安装后无需终端或开发工具链。
- 每个 Windows 用户只有一个实例，再次启动会激活已有窗口。
- 使用随机精确 loopback 端口和严格外部导航策略。
- 关闭、更新和卸载时优雅释放 profile，只有超时才兜底终止所属进程树。
- 现有 `$DSH_HOME` 凭据、设置、会话、插件和工作区保持兼容。

## 验证

发行构建会运行 C# 单元测试、真实已构建桌面适配器、部署后端 HTTP 生命周期、便携版 WebView 加载及优雅退出、静默安装器部署、安装版 WebView 生命周期、卸载、TypeScript 检查、lint、仓库约束和文档检查。

## 要求与限制

- x64 的 Windows 10 1809 或更新版本。
- Microsoft Edge WebView2 Evergreen Runtime。
- 模型请求需要用户自己的 DeepSeek API Key。
- 本版本没有应用内自动更新器，也没有 Authenticode 签名。

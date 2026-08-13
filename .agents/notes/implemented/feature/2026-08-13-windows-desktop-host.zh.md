# Agent Note: Windows desktop host

Status: implemented

[English](2026-08-13-windows-desktop-host.md) | 中文

## Problem

Web profile 需要通过终端命令启动，并在浏览器标签页中使用。这套生命周期让 Harness 更像开发服务器，而不是日常桌面 agent：用户必须保留终端、管理固定端口、寻找浏览器标签页，并记住应关闭哪个进程。如果在另一套客户端中重新实现聊天、设置、会话或工具，就会让行为脱离已组装的 Web 产品，并重复其无密钥浏览器覆盖。

## Decision

`apps/desktop` 是基于 Microsoft Edge WebView2 的 Windows x64 WinForms 宿主。它嵌入现有 Web UI，不增加第二套客户端协议，也不改变 profile 组合。宿主显示原生启动／错误状态，恢复窗口位置，每个 Windows 用户只允许一个实例，并在再次启动时激活已有窗口。

原生宿主拥有一个由 `runtime/harness/node_modules/@deepseek-ai/dsh/lib/desktop-bin.js` 启动的子进程。该应用层 Node 适配器通过现有 `runProfile()` 入口运行随附 `web` profile、精确 loopback host 和端口 `0`。它只在 profile 激活后输出一行 JSON 就绪记录；宿主会在 WebView2 导航前验证子进程 pid、精确 `http://127.0.0.1:<port>` 源、HTTP 状态和 HTML 内容。适配器只通过继承的 stdin 接受 `shutdown`，并调用现有有界 profile 关闭。窗口关闭会等待该释放；八秒后，宿主只终止自己拥有的进程树。

WebView2 只向 Harness 应用授予每次运行的一个精确 loopback 源。指向同源和 `about:blank` 的顶层导航保留在嵌入窗口中。普通非 loopback HTTP(S) 和邮件链接交给操作系统打开，其他 loopback 端口、`file`、`data` 和脚本 scheme 均被阻止。除非设置 `DSH_DESKTOP_DEVTOOLS=1`，否则开发者工具保持关闭。

Windows 构建器会输出自包含 .NET 宿主、`runtime/node/node.exe`，以及 `runtime/harness` 下的生产 `pnpm deploy` 闭包。发行目录不含文件系统符号链接或 junction，携带已构建 Web 前端和 Windows PTY 资产，并可在源码树之外运行。发行打包器会把该已验证目录装入一个便携 ZIP，并把同一 ZIP 嵌入当前用户范围的 Inno Setup 安装器。安装过程会将其解压到 `%LOCALAPPDATA%\Programs`，并创建开始菜单及可选桌面快捷方式。更新和卸载会通知运行中的主实例，等待其所属进程停止，然后删除打包的文件和快捷方式，不会递归删除用户另行放入应用目录的文件。

Harness 状态继续保存在现有 `$DSH_HOME`：桌面端更新不会迁移或删除凭据、设置、会话、插件或工作区记录。除非 `--workspace` 或 `DSH_DESKTOP_WORKSPACE` 选择其他路径，子进程会以用户主目录作为回退 cwd。新会话仍使用所选工作区 cwd 作为实际 `workspace-write` 根，因此在其他盘符注册的项目不会被限制在回退目录中。

## Verification

.NET 测试固定了就绪记录解析、精确源导航决策、运行时布局解析、桌面参数验证、单实例关闭和运行时清理范围。已构建 Node 适配器冒烟会用隔离 Harness home 启动真实 Web 组合，请求 HTML 首页，写入 `shutdown`，要求退出码为零且出现停止记录，并验证随机端口不再接受连接。Windows 构建器会针对部署后的运行时重复这些检查，并要求便携版 EXE 完成 WebView 加载和后端优雅退出。发行打包器会静默安装生成的安装器，从安装路径重复 WebView 生命周期，再启动普通应用，要求第二次启动不关闭主实例，并在其运行时执行卸载。随后它会要求进程和打包运行时都消失，同时保留一个外来夹具文件。标签触发的 GitHub Actions 会运行源码和文档门禁，重新构建安装器、便携 ZIP 和校验文件，然后发布 Release。

## Alternatives considered

**安装一个运行 PowerShell 并打开浏览器的快捷方式。** 不予采用：它仍会保留可见终端、固定端口归属、浏览器标签页生命周期和 shell／工具链依赖，不能形成桌面应用。

**使用 WinUI、WPF 或原生控件重写界面。** 不予采用：每一种 Web 会话节点、设置表单、审批流程、插件 UI 和回放行为都会产生第二套实现与第二条兼容时间线。

**在 WebView 进程内运行 Harness。** 不予采用：WebView2 不承载 Node，把插件运行时嵌入原生进程还会混合浏览器与 Harness 的故障域。私有子进程可保留已交付 Node 运行时，并让关闭拥有一个明确归属方。

**使用 Electron。** 本次 Windows 专用首个宿主未采用该方案，因为机器已经提供 Evergreen WebView2 Runtime。WinForms 加 WebView2 能保持原生壳较小，并让 Node 专用于 Harness，而不是在 UI 宿主中再携带一套 Chromium 和 Node 运行时。

**把桌面服务器绑定到 3080 端口。** 不予采用：残留进程和其他应用可能占用该端口。端口 `0` 为每次单实例运行提供无冲突地址，而精确选中的源会成为 WebView 导航权限来源。

## Consequences

用户可以像普通 Windows 应用一样启动、更新和删除 Harness，同时现有模型、会话、工具、权限与插件行为保持不变。便携目录和安装器大于原生 EXE，因为它们有意携带 Node 和完整生产 Harness 闭包。Windows x64、.NET 8 构建工具、Inno Setup 和 WebView2 是明确的打包前提；已安装应用自身携带 .NET 与 Node，但依赖 Evergreen WebView2。Authenticode 签名、应用内自动更新和非 Windows 原生宿主仍是独立的发布能力。

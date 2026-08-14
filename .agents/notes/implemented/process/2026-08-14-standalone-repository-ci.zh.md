# Agent Note: 让独立仓库脱离上游 CI 基础设施运行

Status: implemented

[English](2026-08-14-standalone-repository-ci.md) | 中文

## Problem

上游工作流依赖组织级 GitHub App 凭据、Project 配置、具名企业 runner 和自托管备用池。独立公共仓库不具备这些资源，因此 Issue 自动化会立即失败，必需的拉取请求任务则会无限排队。便携 Python runtime 还会在 manylinux 容器内重新构建宿主机生成的 `node-pty` Makefile；pnpm 完成安装后，该 Makefile 可能仍引用一个已不存在的 `node-addon-api` 生成目标片段。Windows 安装器冒烟测试也可能在卸载器进程退出后、Windows 释放日志文件句柄前立即开始清理。

## Decision

阻塞拉取请求的任务使用标准 GitHub 托管 Linux 和 Windows runner，并把 worker 数限制在这些机器适合的范围。组织专用的备用任务和基准测试仅在 `github.repository` 为 `deepseek-ai/deepseek-harness` 时运行。Issue policy 和 lifecycle 自动化采用相同的仓库条件，因为其配置和 GitHub App 凭据属于上游组织。

真实 API E2E 保留强制 preflight，并读取仓库 secret `DEEPSEEK_API_KEY_EXTERNAL`；secret 缺失仍然失败，不会把全部跳过误报为绿色。Dependabot 和分叉拉取请求继续不接触 secret，并在访问 secret 前跳过任务。

manylinux 重建会在安装完成后重新运行 npm 内置的 `node-gyp` configure 命令，让 Makefile 与所有外部目标片段一起重新生成，然后在 manylinux 容器内编译这些文件。Windows 打包冒烟测试使用有界重试删除 fixture，以处理短暂的 `EBUSY` 及相关递归删除错误。

## Alternatives considered

**复制上游基础设施。** 个人仓库不拥有上游 GitHub App、组织 Project、企业 runner 名称或备用主机。重建这些资源只会增加运维依赖，不会改善桌面发行版。

**在上游之外跳过全部 CI。** 这样虽会清空队列，却也会丢失源码、文档、打包和平台证据。标准托管 runner 能以较低并行度保留这些检查。

**在没有密钥时让真实 API E2E 自行跳过。** 工作流会在没有发送 DeepSeek 请求的情况下报告成功，从而隐藏仓库配置缺失。

**为 Windows 清理增加固定等待。** 文件句柄释放时间随宿主负载变化。只重试实际失败的文件系统操作既能响应已观察到的锁，也能限制总等待时间。

## Consequences

独立仓库的拉取请求检查可能比上游企业 runner 更慢，上游专用 runner 基准测试和 Issue 治理也不会在派生仓库运行。保留的检查使用可用的 GitHub 托管容量，真实 API E2E 会明确报告配置缺失，Linux runtime 继续执行 manylinux GLIBC 校验，而 Windows 打包能够容忍卸载器释放句柄时的有界竞态，同时仍严格校验残留文件。

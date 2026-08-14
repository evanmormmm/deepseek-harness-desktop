# Agent Note: Run standalone repositories without upstream CI infrastructure

Status: implemented

English | [中文](2026-08-14-standalone-repository-ci.zh.md)

## Problem

The upstream workflows assume organization-scoped GitHub App credentials, Project configuration, named enterprise runners, and self-hosted standby pools. A standalone public repository has none of those resources, so issue automation fails immediately and required pull-request jobs remain queued indefinitely. The portable Python runtime also rebuilds a host-generated `node-pty` Makefile inside a manylinux container; after pnpm finishes installing, that Makefile can reference a generated `node-addon-api` target fragment that is no longer present. The Windows installer smoke can observe the uninstaller process exiting before Windows releases its log file handle.

## Decision

Blocking pull-request jobs run on standard GitHub-hosted Linux and Windows runners with worker counts bounded for those machines. Organization-only standby and benchmark jobs run only when `github.repository` is `deepseek-ai/deepseek-harness`. Issue policy and lifecycle automation use the same repository guard because their configuration and GitHub App credentials belong to the upstream organization.

Real-API E2E keeps its hard preflight and reads the repository secret `DEEPSEEK_API_KEY_EXTERNAL`; a missing secret remains a failure rather than an all-skipped false green. Dependabot and fork pull requests remain keyless and skip the job before secret access.

The manylinux rebuild reruns the npm-bundled `node-gyp` configure command after installation completes so the Makefile and every external target fragment are regenerated together, mounts the npm package read-only, then compiles those files inside the manylinux container. The Windows packaging smoke removes its fixture with bounded retries for transient `EBUSY` and related recursive-removal errors.

## Alternatives considered

**Copy the upstream infrastructure.** A personal repository does not own the upstream GitHub App, organization Project, enterprise runner names, or standby hosts. Recreating them would add operational dependencies without improving the desktop distribution.

**Skip all CI outside upstream.** That would clear the queue by discarding source, documentation, packaging, and platform evidence. Standard hosted runners preserve those checks with lower parallelism.

**Let real-API E2E self-skip without a key.** The workflow would report success without making a DeepSeek request, which would hide missing repository configuration.

**Use a fixed delay for Windows cleanup.** File-handle release time varies by host load. Retrying only the filesystem operation responds to the observed lock and keeps the wait bounded.

## Consequences

Standalone pull-request checks can take longer than the upstream enterprise lanes, and upstream-only runner benchmarks and issue governance do not run in derivative repositories. The retained checks use available GitHub-hosted capacity, real-API E2E reports missing configuration explicitly, the Linux runtime keeps its manylinux GLIBC verification, and Windows packaging tolerates the uninstaller's bounded handle-release race without weakening residual-file assertions.

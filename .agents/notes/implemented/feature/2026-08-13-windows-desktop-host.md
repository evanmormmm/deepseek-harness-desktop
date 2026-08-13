# Agent Note: Windows desktop host

Status: implemented

English | [中文](2026-08-13-windows-desktop-host.zh.md)

## Problem

The Web profile required a terminal command to start and a browser tab to use. That lifecycle made Harness feel like a development server rather than a daily desktop agent: users had to keep a terminal open, manage a fixed port, find the browser tab, and remember which process to close. Reimplementing chat, settings, sessions, or tools in another client would split behavior from the assembled Web product and duplicate its keyless browser coverage.

## Decision

`apps/desktop` is a Windows x64 WinForms host over Microsoft Edge WebView2. It embeds the existing Web UI without adding a second client protocol or changing the profile composition. The host shows a native startup/error state, restores window placement, admits one instance per Windows user, and activates the existing window on a second launch.

The native host owns one child process launched from `runtime/harness/node_modules/@deepseek-ai/dsh/lib/desktop-bin.js`. That app-layer Node adapter invokes the existing `runProfile()` entry with the shipped `web` profile, exact loopback host, and port `0`. It emits one JSON readiness line only after profile activation; the host validates the child pid, exact `http://127.0.0.1:<port>` origin, HTTP status, and HTML content before navigating WebView2. The adapter accepts `shutdown` only on its inherited stdin and calls the existing bounded profile shutdown. Window close joins that disposal; after eight seconds the host kills only its owned process tree.

WebView2 grants the Harness application one exact per-run loopback origin. Top-level navigation to the same origin and `about:blank` stays embedded. Ordinary non-loopback HTTP(S) and mail links open through the operating system, while other loopback ports, `file`, `data`, and script schemes are blocked. Developer tools are disabled unless `DSH_DESKTOP_DEVTOOLS=1` is present.

The Windows builder emits a self-contained .NET host beside `runtime/node/node.exe` and a production `pnpm deploy` closure under `runtime/harness`. The distribution contains no filesystem symlink or junction, carries the built Web frontend and Windows PTY assets, and runs from outside the source tree. The release packager places that verified directory in one portable ZIP and embeds the same ZIP in a per-user Inno Setup installer. Installation extracts it under `%LOCALAPPDATA%\Programs` and creates Start-menu plus optional desktop shortcuts. Update and uninstall signal a running primary instance, wait for its owned process to stop, and remove the packaged files and shortcuts without recursively deleting foreign files placed in the application directory.

Harness state remains in the existing `$DSH_HOME`: desktop updates do not migrate or delete credentials, settings, sessions, plugins, or workspace records. The child starts with the user profile as the fallback cwd unless `--workspace` or `DSH_DESKTOP_WORKSPACE` selects another path. New sessions still use their selected workspace cwd as the actual `workspace-write` root, so a registered project on another drive is not confined to the fallback directory.

## Verification

The .NET suite pins readiness parsing, exact-origin navigation decisions, runtime layout resolution, desktop argument validation, single-instance shutdown, and runtime cleanup containment. The built Node adapter smoke boots the real Web composition with an isolated Harness home, requests the HTML root, writes `shutdown`, requires exit zero and the stopped record, and verifies that the random port no longer accepts connections. The Windows builder repeats those checks against the deployed runtime and requires the portable EXE to complete a WebView load plus graceful backend exit. The release packager silently installs the generated installer, repeats the WebView lifecycle from the installed path, starts the ordinary app, requires a second launch to leave the primary instance alive, and uninstalls it while running. It then requires the process and packaged runtime to disappear while a foreign fixture file remains. Tag-triggered GitHub Actions run source and documentation gates, rebuild the installer, portable ZIP, and checksum manifest, then publish a Release.

## Alternatives considered

**Install a shortcut that runs PowerShell and opens the browser.** Rejected because it retains a visible terminal, fixed-port ownership, browser-tab lifecycle, and shell/toolchain dependency; it does not produce a desktop application.

**Rebuild the interface in WinUI, WPF, or native controls.** Rejected because every Web conversation node, settings form, approval flow, plugin UI, and replay behavior would gain a second implementation and a second compatibility timeline.

**Run Harness in the WebView process.** Rejected because WebView2 does not host Node, and embedding the plugin runtime into the native process would mix browser and Harness failure domains. A private child preserves the shipped Node runtime and gives shutdown one explicit owner.

**Use Electron.** Rejected for this Windows-specific first host because the machine already supplies the Evergreen WebView2 runtime. WinForms plus WebView2 keeps the native shell small and leaves Node dedicated to Harness rather than carrying a second Chromium and Node runtime in the UI host.

**Bind the desktop server to port 3080.** Rejected because stale processes and other applications can own that port. Port `0` gives each single-instance run an uncontended address, and the exact selected origin becomes the WebView navigation authority.

## Consequences

Users launch, update, and remove Harness as an ordinary Windows application while the existing model, session, tool, permission, and plugin behavior remains unchanged. The portable directory and installer are larger than the native EXE because they deliberately carry Node and the full production Harness closure. Windows x64, .NET 8 build tooling, Inno Setup, and WebView2 are explicit packaging requirements; the installed application itself carries .NET and Node but relies on Evergreen WebView2. Authenticode signing, in-app auto-update, and non-Windows native hosts remain separate release capabilities.

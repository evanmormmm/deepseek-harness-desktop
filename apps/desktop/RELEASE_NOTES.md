# DeepSeek Harness Desktop 0.1.0

English | [中文](RELEASE_NOTES.zh.md)

This is the first community Windows desktop release built from DeepSeek Harness `0.1.0-rc.5` at upstream commit `47f943859bef60e4160492346772ded9b24f765a`.

> This binary distribution is maintained by `evanmormmm`; it is not an official DeepSeek release.

## Download

- **Recommended:** `DeepSeek-Harness-Desktop-0.1.0-win-x64-Setup.exe` installs for the current Windows user and creates a Start-menu shortcut.
- **Portable:** `DeepSeek-Harness-Desktop-0.1.0-win-x64.zip` runs after the entire directory is extracted.
- **Verification:** `SHA256SUMS.txt` contains SHA-256 hashes for both assets.

The binaries are not Authenticode-signed. Windows SmartScreen may report an unknown publisher.

## Highlights

- Native WinForms/WebView2 window over the existing Harness Web client.
- Private bundled Node.js and production Harness runtime; no terminal or development toolchain required after installation.
- One instance per Windows user, with second-launch activation.
- Random exact-loopback port and strict external-navigation policy.
- Graceful profile disposal on close, update, and uninstall, with bounded owned-tree termination only as fallback.
- Existing `$DSH_HOME` credentials, settings, sessions, plugins, and workspaces remain compatible.

## Verification

The release build runs C# unit tests, the real built desktop adapter, the deployed backend HTTP lifecycle, portable WebView load and graceful exit, silent installer deployment, installed WebView lifecycle, uninstallation, TypeScript checks, lint, repository constraints, and documentation checks.

## Requirements and limits

- Windows 10 version 1809 or later, x64.
- Microsoft Edge WebView2 Evergreen Runtime.
- A user-provided DeepSeek API key for model requests.
- No in-app auto-updater and no Authenticode signature in this release.

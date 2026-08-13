param(
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\DeepSeek Harness')
)

$ErrorActionPreference = 'Stop'
$programsRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
$target = [System.IO.Path]::GetFullPath($InstallDirectory)
$expectedPrefix = $programsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar
if (-not $target.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "uninstall-desktop-windows: install directory must stay under $programsRoot, got $target"
}

$running = @(Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue)
if ($running.Count -gt 0) {
    foreach ($process in $running) { [void]$process.CloseMainWindow() }
    $deadline = [DateTimeOffset]::Now.AddSeconds(15)
    while ([DateTimeOffset]::Now -lt $deadline) {
        if (@(Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue).Count -eq 0) { break }
        Start-Sleep -Milliseconds 250
    }
}
Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue | Stop-Process -Force
$shortcuts = @(
    (Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSeek Harness.lnk'),
    (Join-Path ([Environment]::GetFolderPath('Programs')) 'DeepSeek Harness\DeepSeek Harness.lnk')
)
foreach ($shortcut in $shortcuts) {
    Remove-Item -LiteralPath $shortcut -Force -ErrorAction SilentlyContinue
}
$startMenuDirectory = Split-Path -Parent $shortcuts[1]
if (Test-Path -LiteralPath $startMenuDirectory) {
    Remove-Item -LiteralPath $startMenuDirectory -Force -ErrorAction SilentlyContinue
}
if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
Write-Output "REMOVED_INSTALL_DIRECTORY=$target"
Write-Output 'UNINSTALL_EXIT=0'

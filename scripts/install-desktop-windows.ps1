param(
    [Parameter(Mandatory = $true)]
    [string]$SourceDirectory,
    [string]$InstallDirectory = (Join-Path $env:LOCALAPPDATA 'Programs\DeepSeek Harness')
)

$ErrorActionPreference = 'Stop'
$source = [System.IO.Path]::GetFullPath($SourceDirectory)
$programsRoot = [System.IO.Path]::GetFullPath((Join-Path $env:LOCALAPPDATA 'Programs'))
$target = [System.IO.Path]::GetFullPath($InstallDirectory)
$expectedPrefix = $programsRoot.TrimEnd([System.IO.Path]::DirectorySeparatorChar) + [System.IO.Path]::DirectorySeparatorChar

if (-not $target.StartsWith($expectedPrefix, [System.StringComparison]::OrdinalIgnoreCase)) {
    throw "install-desktop-windows: install directory must stay under $programsRoot, got $target"
}

$required = @(
    (Join-Path $source 'DeepSeek Harness.exe'),
    (Join-Path $source 'runtime\node\node.exe'),
    (Join-Path $source 'runtime\harness\node_modules\@deepseek-ai\dsh\lib\desktop-bin.js'),
    (Join-Path $source 'runtime\harness\node_modules\@deepseek-ai\dsh-web-frontend\dist\index.html')
)
$missing = @($required | Where-Object { -not (Test-Path -LiteralPath $_ -PathType Leaf) })
if ($missing.Count -gt 0) {
    throw "install-desktop-windows: source distribution is incomplete:`n$($missing -join "`n")"
}

function Stop-InstalledApplication {
    $running = @(Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue)
    if ($running.Count -eq 0) { return }

    foreach ($process in $running) { [void]$process.CloseMainWindow() }
    $deadline = [DateTimeOffset]::Now.AddSeconds(15)
    while ([DateTimeOffset]::Now -lt $deadline) {
        if (@(Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue).Count -eq 0) { return }
        Start-Sleep -Milliseconds 250
    }

    Get-Process -Name 'DeepSeek Harness' -ErrorAction SilentlyContinue | Stop-Process -Force
}

Stop-InstalledApplication
New-Item -ItemType Directory -Path $programsRoot -Force | Out-Null
if (Test-Path -LiteralPath $target) {
    Remove-Item -LiteralPath $target -Recurse -Force
}
Copy-Item -LiteralPath $source -Destination $target -Recurse -Force

$executable = Join-Path $target 'DeepSeek Harness.exe'
$shell = New-Object -ComObject WScript.Shell
$desktopShortcut = Join-Path ([Environment]::GetFolderPath('Desktop')) 'DeepSeek Harness.lnk'
$startMenuDirectory = Join-Path ([Environment]::GetFolderPath('Programs')) 'DeepSeek Harness'
New-Item -ItemType Directory -Path $startMenuDirectory -Force | Out-Null
$startMenuShortcut = Join-Path $startMenuDirectory 'DeepSeek Harness.lnk'

foreach ($shortcutPath in @($desktopShortcut, $startMenuShortcut)) {
    $shortcut = $shell.CreateShortcut($shortcutPath)
    $shortcut.TargetPath = $executable
    $shortcut.WorkingDirectory = [Environment]::GetFolderPath('UserProfile')
    $shortcut.IconLocation = "$executable,0"
    $shortcut.Description = 'DeepSeek Harness desktop application'
    $shortcut.Save()
}

Write-Output "INSTALL_DIRECTORY=$target"
Write-Output "EXECUTABLE=$executable"
Write-Output "DESKTOP_SHORTCUT=$desktopShortcut"
Write-Output "START_MENU_SHORTCUT=$startMenuShortcut"
Write-Output 'INSTALL_EXIT=0'

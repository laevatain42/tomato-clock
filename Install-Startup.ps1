$ErrorActionPreference = "Stop"

$appDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$target = Join-Path $appDir "Start-TomatoClock.vbs"
$startupDir = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startupDir "Tomato Clock.lnk"

if (-not (Test-Path -LiteralPath $target)) {
    throw "Launcher not found: $target"
}

$shell = New-Object -ComObject WScript.Shell
$shortcut = $shell.CreateShortcut($shortcutPath)
$shortcut.TargetPath = $target
$shortcut.WorkingDirectory = $appDir
$shortcut.WindowStyle = 7
$shortcut.Description = "Start Tomato Clock on login"
$shortcut.Save()

Write-Host "Startup shortcut created: $shortcutPath"

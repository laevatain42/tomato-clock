$ErrorActionPreference = "Stop"

$startupDir = [Environment]::GetFolderPath("Startup")
$shortcutPath = Join-Path $startupDir "Tomato Clock.lnk"

if (Test-Path -LiteralPath $shortcutPath) {
    Remove-Item -LiteralPath $shortcutPath
    Write-Host "Startup shortcut removed: $shortcutPath"
} else {
    Write-Host "Startup shortcut was not found."
}

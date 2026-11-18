<#
.SYNOPSIS
    Removes MacKeys. Optionally restores your previous PowerToys Keyboard Manager config.

.PARAMETER RestorePowerToys
    Restore the Keyboard Manager config you had before install.ps1 (from its backup).
    Without it, the Mac-style shortcuts stay in PowerToys (always on).
#>
param([switch]$RestorePowerToys)

$ErrorActionPreference = 'Stop'
$installDir = Join-Path $env:LOCALAPPDATA 'MacKeys'
$kmConfig = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\Keyboard Manager\default.json'
$ptExe = Join-Path $env:LOCALAPPDATA 'PowerToys\PowerToys.exe'

Write-Host '==> Stopping MacKeys'
Get-Process MacKeys -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500

Write-Host '==> Turning Win+L back on (if MacKeys had turned it off)'
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -Name 'DisableLockWorkstation' -ErrorAction SilentlyContinue

Write-Host '==> Removing start with Windows and settings'
Remove-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MacKeys' -ErrorAction SilentlyContinue
Remove-Item 'HKCU:\Software\MacKeys' -Recurse -ErrorAction SilentlyContinue

$restore = $null
if ($RestorePowerToys) {
    $restore = Get-Item "$kmConfig.before-mackeys" -ErrorAction SilentlyContinue
    if (-not $restore) { Write-Warning 'No Keyboard Manager backup found.' }
} elseif ((Test-Path $kmConfig) -and -not (Select-String -Path $kmConfig -Pattern 'originalKeys' -Quiet)) {
    # MacKeys had paused the shortcuts (empty config): put them back so PowerToys keeps working.
    $stash = Join-Path $installDir 'keyboard-manager.json'
    if (Test-Path $stash) { $restore = Get-Item $stash }
}
if ($restore) {
    Get-Process PowerToys* -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Seconds 2
    Copy-Item $restore.FullName $kmConfig -Force
    Write-Host "    Restored PowerToys Keyboard Manager config from $($restore.Name)"
    if (Test-Path $ptExe) { Start-Process $ptExe }
}

Write-Host "==> Deleting $installDir"
if (Test-Path $installDir) { Remove-Item $installDir -Recurse -Force }

if (-not $RestorePowerToys) {
    Write-Host 'The Mac-style shortcuts were left in PowerToys. Remove them in PowerToys > Keyboard Manager, or run with -RestorePowerToys.'
}
Write-Host 'MacKeys removed.'

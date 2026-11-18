<#
.SYNOPSIS
    Installs MacKeys: Mac-style keyboard shortcuts and brightness keys for an Apple Magic Keyboard on Windows.

.DESCRIPTION
    1. Builds MacKeys.exe (brightness keys + overlay) and installs it to %LOCALAPPDATA%\MacKeys.
    2. Registers it to start with Windows (current user only) and starts it.
    3. Applies the Mac-style shortcut config to PowerToys Keyboard Manager
       (your current Keyboard Manager config is backed up first).

.PARAMETER SkipPowerToys
    Only install the brightness app; leave PowerToys untouched.

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\install.ps1
#>
param([switch]$SkipPowerToys)

$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$installDir = Join-Path $env:LOCALAPPDATA 'MacKeys'
$exe = Join-Path $installDir 'MacKeys.exe'

Write-Host '==> Building MacKeys.exe'
& (Join-Path $root 'build.ps1')

Write-Host "==> Installing to $installDir"
Get-Process MacKeys -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Milliseconds 500
New-Item -ItemType Directory -Force $installDir | Out-Null
Copy-Item (Join-Path $root 'bin\MacKeys.exe') $exe -Force

Write-Host '==> Enabling start with Windows'
Set-ItemProperty -Path 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -Name 'MacKeys' -Value "`"$exe`""

if ($SkipPowerToys) { Start-Process $exe; Write-Host 'Done (PowerToys skipped).'; return }

# ---------------- PowerToys Keyboard Manager ----------------
$ptExe = @(
    (Join-Path $env:LOCALAPPDATA 'PowerToys\PowerToys.exe'),
    (Join-Path $env:ProgramFiles 'PowerToys\PowerToys.exe')
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $ptExe) {
    Write-Warning 'PowerToys is not installed. Install it, then run this script again:'
    Write-Host '    winget install Microsoft.PowerToys'
    Start-Process $exe
    return
}

$ptData = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys'
$kmDir = Join-Path $ptData 'Keyboard Manager'
$kmConfig = Join-Path $kmDir 'default.json'
$settings = Join-Path $ptData 'settings.json'

Write-Host '==> Applying Mac-style shortcuts to PowerToys Keyboard Manager'
Get-Process PowerToys* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2
New-Item -ItemType Directory -Force $kmDir | Out-Null
$backup = "$kmConfig.before-mackeys"
if ((Test-Path $kmConfig) -and -not (Test-Path $backup)) {   # keep the original only (re-installs don't overwrite it)
    Copy-Item $kmConfig $backup
    Write-Host "    Backed up your previous config to $backup"
}
$json = Get-Content (Join-Path $root 'config\keyboard-manager.json') -Raw | ConvertFrom-Json
foreach ($s in $json.remapShortcuts.global) {
    if ($s.runProgramFilePath -eq '%MACKEYS_EXE%') { $s.runProgramFilePath = $exe }   # Ctrl+Cmd+Q -> MacKeys.exe --lock
}
$compact = $json | ConvertTo-Json -Depth 10 -Compress
[IO.File]::WriteAllText($kmConfig, $compact)
# MacKeys switches PowerToys between this config and an empty one (Apple keyboard connected / not).
[IO.File]::WriteAllText((Join-Path $installDir 'keyboard-manager.json'), $compact)

# Turn on Keyboard Manager and "Run at startup" in PowerToys' general settings.
if (Test-Path $settings) {
    $s = Get-Content $settings -Raw | ConvertFrom-Json
    if ($s.enabled.PSObject.Properties.Name -contains 'Keyboard Manager') { $s.enabled.'Keyboard Manager' = $true }
    else { $s.enabled | Add-Member -NotePropertyName 'Keyboard Manager' -NotePropertyValue $true }
    $s.startup = $true
    [IO.File]::WriteAllText($settings, ($s | ConvertTo-Json -Depth 10 -Compress))
} else {
    Write-Warning 'PowerToys settings not found (has PowerToys been started once?). Enable Keyboard Manager manually in PowerToys.'
}

Start-Process $ptExe
Start-Sleep -Seconds 3
Start-Process $exe
Write-Host ''
Write-Host 'Done! MacKeys is running (look for the sun icon in the tray) and PowerToys has the Mac-style shortcuts.'

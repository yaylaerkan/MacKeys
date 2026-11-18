# Restarts PowerToys and MacKeys from their own folders (so neither keeps another folder locked as its
# working directory). Optional: -RemoveFolder <path> deletes an (empty) folder while both are stopped.
param([string]$RemoveFolder)

Set-Location $env:USERPROFILE
$mk = Join-Path $env:LOCALAPPDATA 'MacKeys\MacKeys.exe'
$pt = @((Join-Path $env:LOCALAPPDATA 'PowerToys\PowerToys.exe'), (Join-Path $env:ProgramFiles 'PowerToys\PowerToys.exe')) |
    Where-Object { Test-Path $_ } | Select-Object -First 1

Write-Host '==> Stopping MacKeys and PowerToys'
Get-Process MacKeys -ErrorAction SilentlyContinue | Stop-Process -Force
Get-Process PowerToys* -ErrorAction SilentlyContinue | Stop-Process -Force
Start-Sleep -Seconds 2

if ($RemoveFolder) {
    if (Test-Path -LiteralPath $RemoveFolder) {
        if (Get-ChildItem -LiteralPath $RemoveFolder -Force) { Write-Warning "$RemoveFolder is not empty; leaving it." }
        else {
            try { Remove-Item -LiteralPath $RemoveFolder -Force -ErrorAction Stop; Write-Host "    Deleted $RemoveFolder" }
            catch { Write-Warning "Still locked: $($_.Exception.Message)" }
        }
    } else { Write-Host "    $RemoveFolder is already gone" }
}

Write-Host '==> Starting PowerToys and MacKeys'
if ($pt) { Start-Process $pt -WorkingDirectory (Split-Path $pt) }
Start-Sleep -Seconds 3
if (Test-Path $mk) { Start-Process $mk -WorkingDirectory (Split-Path $mk) }
Start-Sleep -Seconds 3
Get-Process MacKeys, PowerToys, PowerToys.KeyboardManagerEngine -ErrorAction SilentlyContinue | ForEach-Object { "    running: $($_.Name) ($($_.Id))" }

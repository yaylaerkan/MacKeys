# Prints what MacKeys has actually installed on this machine (read-only).
$install = Join-Path $env:LOCALAPPDATA 'MacKeys'
$km = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\Keyboard Manager\default.json'
"Install folder exists : $(Test-Path $install)"
if (Test-Path $install) { "  files: " + ((Get-ChildItem $install).Name -join ', ') }
"Repo build exists     : $(Test-Path (Join-Path $PSScriptRoot '..\bin\MacKeys.exe'))"
$run = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
"Run key MacKeys       : $($run.MacKeys)"
"Run key MacBrightness : $($run.MacBrightness)"
"Settings              : " + (((Get-ItemProperty 'HKCU:\Software\MacKeys' -ErrorAction SilentlyContinue).PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', ')
"PowerToys config      : exists=$(Test-Path $km), has mappings=$(if (Test-Path $km) { Select-String -Path $km -Pattern 'originalKeys' -Quiet }), points to MacKeys=$(if (Test-Path $km) { Select-String -Path $km -Pattern 'MacKeys' -Quiet })"
"Processes             :"
Get-Process MacKeys, MacBrightness, PowerToys, PowerToys.KeyboardManagerEngine -ErrorAction SilentlyContinue | ForEach-Object { "  $($_.Name) ($($_.Id)) $($_.Path)" }

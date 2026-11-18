# Prints what MacKeys has actually installed on this machine (read-only).
$install = Join-Path $env:LOCALAPPDATA 'MacKeys'
$km = Join-Path $env:LOCALAPPDATA 'Microsoft\PowerToys\Keyboard Manager\default.json'
"Install folder exists : $(Test-Path $install)"
if (Test-Path $install) { "  files: " + ((Get-ChildItem $install).Name -join ', ') }
"Repo build exists     : $(Test-Path (Join-Path $PSScriptRoot '..\bin\MacKeys.exe'))"
$run = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run' -ErrorAction SilentlyContinue
"Run key MacKeys       : $($run.MacKeys)"
"Settings              : " + (((Get-ItemProperty 'HKCU:\Software\MacKeys' -ErrorAction SilentlyContinue).PSObject.Properties | Where-Object { $_.Name -notlike 'PS*' } | ForEach-Object { "$($_.Name)=$($_.Value)" }) -join ', ')
$pol = Get-ItemProperty 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Policies\System' -ErrorAction SilentlyContinue
"Win+L disabled policy : $($pol.DisableLockWorkstation)"
"PowerToys config      : exists=$(Test-Path $km), has mappings=$(if (Test-Path $km) { Select-String -Path $km -Pattern 'originalKeys' -Quiet })"
"Processes             :"
Get-Process MacKeys, PowerToys, PowerToys.KeyboardManagerEngine -ErrorAction SilentlyContinue | ForEach-Object { "  $($_.Name) ($($_.Id)) responding=$($_.Responding) started=$($_.StartTime)" }
$log = Join-Path $install 'mackeys.log'
if (Test-Path $log) { "--- last log lines:"; Get-Content $log -Tail 25 }
"--- recent .NET / app errors:"
Get-WinEvent -FilterHashtable @{ LogName = 'Application'; StartTime = (Get-Date).AddHours(-2) } -ErrorAction SilentlyContinue |
    Where-Object { $_.ProviderName -in '.NET Runtime', 'Application Error' -and $_.Message -match 'MacKeys' } |
    Select-Object -First 3 | ForEach-Object { "[$($_.TimeCreated)] $($_.ProviderName)"; ($_.Message -split "`n" | Select-Object -First 12) -join "`n" }

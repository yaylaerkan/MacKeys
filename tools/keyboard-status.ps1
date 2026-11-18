# Shows MacKeys' recent log and how Windows currently sees the keyboards (read-only).
$log = Join-Path $env:LOCALAPPDATA 'MacKeys\mackeys.log'
if (Test-Path $log) { '--- last log lines:'; Get-Content $log -Tail 15 }
'--- keyboards (PnP):'
Get-CimInstance Win32_PnPEntity -Filter "PNPClass='Keyboard'" |
    ForEach-Object { '{0,-28} status={1,-8} err={2} {3}' -f $_.Name, $_.Status, $_.ConfigManagerErrorCode, $_.DeviceID }
'--- present-only (Get-PnpDevice -PresentOnly):'
Get-PnpDevice -Class Keyboard -PresentOnly -ErrorAction SilentlyContinue |
    ForEach-Object { '{0,-28} status={1,-8} {2}' -f $_.FriendlyName, $_.Status, $_.InstanceId }
'--- Bluetooth device:'
Get-PnpDevice -Class Bluetooth -ErrorAction SilentlyContinue | Where-Object FriendlyName -match 'Keyboard' | ForEach-Object {
    $c = (Get-PnpDeviceProperty -InstanceId $_.InstanceId -KeyName '{83DA6326-97A6-4088-9453-A1923F573B29} 15' -ErrorAction SilentlyContinue).Data
    '{0,-28} status={1,-8} connected={2}' -f $_.FriendlyName, $_.Status, $c
}

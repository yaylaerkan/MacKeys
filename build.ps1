# Builds bin\MacKeys.exe with the C# compiler that ships with Windows (.NET Framework 4.x).
# No Visual Studio or .NET SDK required.
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$csc  = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw 'C# compiler not found. .NET Framework 4.x is required.' }

$bin = Join-Path $root 'bin'
New-Item -ItemType Directory -Force $bin | Out-Null
& $csc /nologo /target:winexe /optimize+ /out:"$bin\MacKeys.exe" `
    /reference:System.dll /reference:System.Drawing.dll /reference:System.Management.dll /reference:System.Windows.Forms.dll `
    "$root\src\MacKeys.cs"
if ($LASTEXITCODE -ne 0) { throw "Build failed (csc exit code $LASTEXITCODE)." }
Write-Host "Built $bin\MacKeys.exe"

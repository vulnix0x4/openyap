# Builds Windows\build\OpenYap.exe with the C# compiler that ships with Windows (.NET Framework 4.8).
# No SDK, Visual Studio, NuGet packages, or downloads are needed.
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$build = Join-Path $root 'build'
New-Item -ItemType Directory -Force $build | Out-Null

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { throw "The .NET Framework 4.8 compiler was not found at $csc." }
$speech = Get-ChildItem (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech') -Recurse -Filter System.Speech.dll |
    Select-Object -First 1 -ExpandProperty FullName
if (-not $speech) { throw 'System.Speech.dll was not found. Enable .NET Framework 4.8 in Windows Features.' }

$icon = Join-Path $build 'OpenYap.ico'
& (Join-Path $PSScriptRoot 'icon.ps1') -Output $icon

# The legacy compiler cannot open paths longer than 260 characters, so compile relative to the folder.
Push-Location $root
try {
    $sources = Get-ChildItem Sources -Filter *.cs | ForEach-Object { 'Sources\' + $_.Name }
    & $csc /nologo /target:winexe /platform:x64 /optimize+ /langversion:5 /warnaserror+ `
        /out:build\OpenYap.exe /win32icon:build\OpenYap.ico /win32manifest:OpenYap.manifest `
        /r:System.dll /r:System.Core.dll /r:System.Drawing.dll /r:System.Windows.Forms.dll "/r:$speech" @sources
    if ($LASTEXITCODE -ne 0) { throw "Compilation failed ($LASTEXITCODE)." }
} finally { Pop-Location }
Write-Host "Built $(Join-Path $build 'OpenYap.exe')"

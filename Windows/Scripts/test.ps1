# Builds OpenYap.exe and runs the checks that need no special hardware:
# DSP self-test, import decoding of Windows' own sounds, and speech rendering (to memory, never played).
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
& (Join-Path $PSScriptRoot 'build.ps1')
$exe = Join-Path $root 'build\OpenYap.exe'
$failures = 0
& $exe --self-test | Out-String | Write-Host
if ($LASTEXITCODE -ne 0) { $failures++ }
$media = Get-ChildItem (Join-Path $env:WINDIR 'Media') -Filter *.wav | Select-Object -First 6 -ExpandProperty FullName
& $exe --decode @media | Out-String | Write-Host
if ($LASTEXITCODE -ne 0) { $failures++ }
& $exe --decode (Join-Path $env:WINDIR 'notepad.exe') | Out-String | Write-Host
if ($LASTEXITCODE -ne 1) { Write-Host 'FAIL a non-audio file was accepted'; $failures++ }
& $exe --speech-test | Out-String | Write-Host
if ($LASTEXITCODE -ne 0) { $failures++ }
Write-Host "Windows tests: $(if ($failures) { 'FAIL' } else { 'PASS' }) ($failures failures)"
exit $failures

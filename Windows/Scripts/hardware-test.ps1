# Real-device smoke test that leaves your own apps alone. A helper plays an inaudible -80 dBFS tone;
# OpenYap captures it as the "music app", sweeps "Music I hear" (the helper's own Windows volume),
# and reads its send back through endpoint loopback. The send level should stay ~1e-4 at every step.
#   -Virtual  render endpoint ID used as the virtual microphone (default: a VB-CABLE "CABLE Input").
#   Run OpenYap.exe --devices to list IDs. Use an output nobody listens to; the send is -80 dBFS.
param([string]$Virtual = '', [int]$Seconds = 15)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $csc /nologo /platform:x64 /optimize+ /out:build\tonehelper.exe Tests\ToneHelper.cs Sources\Wasapi.cs
    if ($LASTEXITCODE -ne 0) { throw 'Could not build the tone helper.' }
    $env:OPENYAP_DATA = Join-Path $root 'build\test-data'
    if (Test-Path $env:OPENYAP_DATA) { Remove-Item -Recurse -Force $env:OPENYAP_DATA }
    $helper = Start-Process -FilePath (Resolve-Path build\tonehelper.exe) -ArgumentList ($Seconds + 15) -PassThru -WindowStyle Hidden
    Start-Sleep -Milliseconds 1500
    $arguments = @('--hardware-test', '--music', 'tonehelper', '--no-monitor', '--gains', '1,1,0,0,1',
        '--hear-sweep', '0.5,0.1,0,1', '--seconds', $Seconds, '--return-loopback', '--expect-send', '1e-4')
    if ($Virtual) { $arguments += @('--virtual', $Virtual) }
    & .\build\OpenYap.exe @arguments | Out-String | Write-Host
    $code = $LASTEXITCODE
    if (-not $helper.HasExited) { Stop-Process -Id $helper.Id }
    & .\build\OpenYap.exe --restore | Out-String | Write-Host
    $journal = Join-Path $env:OPENYAP_DATA 'volume-journal.txt'
    Write-Host ("Journal left behind: " + (Test-Path $journal))
    exit $code
} finally { Pop-Location }

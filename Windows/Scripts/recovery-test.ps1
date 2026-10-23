# Forced-quit recovery with real Windows audio sessions, touching only a test helper's volume.
# 1. A helper plays an inaudible tone; OpenYap shares it and lowers its volume to 0.5.
# 2. OpenYap is killed (no cleanup). The helper must stay at 0.5 and the journal must exist.
# 3. OpenYap.exe --restore must put the helper back to 1.0 and delete the journal.
param([string]$Virtual = '')
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
$failures = 0
function Expect($ok, $what) { if ($ok) { Write-Host "PASS $what" } else { Write-Host "FAIL $what"; $script:failures++ } }
function HelperVolume { $line = (& .\build\OpenYap.exe --apps | Out-String) -split "`n" | Where-Object { $_ -like 'tonehelper |*' }; if ($line) { [double](($line -split 'volume ')[1]) } else { -2 } }
try {
    $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
    & $csc /nologo /platform:x64 /optimize+ /out:build\tonehelper.exe Tests\ToneHelper.cs Sources\Wasapi.cs
    $env:OPENYAP_DATA = Join-Path $root 'build\test-data'
    if (Test-Path $env:OPENYAP_DATA) { Remove-Item -Recurse -Force $env:OPENYAP_DATA }
    $journal = Join-Path $env:OPENYAP_DATA 'volume-journal.txt'
    $helper = Start-Process -FilePath (Resolve-Path build\tonehelper.exe) -ArgumentList 60 -PassThru -WindowStyle Hidden
    Start-Sleep -Seconds 2
    Expect ((HelperVolume) -eq 1) 'helper starts at full volume'
    $arguments = @('--hardware-test', '--music', 'tonehelper', '--no-monitor', '--gains', '0.5,0,0,0,1', '--seconds', '60')
    if ($Virtual) { $arguments += @('--virtual', $Virtual) }
    $app = Start-Process -FilePath (Resolve-Path build\OpenYap.exe) -ArgumentList $arguments -PassThru
    Start-Sleep -Seconds 6
    Expect ([Math]::Abs((HelperVolume) - 0.5) -lt 0.01) 'OpenYap set the helper to "Music I hear" = 50%'
    Stop-Process -Id $app.Id -Force
    Start-Sleep -Milliseconds 500
    Expect ([Math]::Abs((HelperVolume) - 0.5) -lt 0.01) 'a forced quit leaves the changed volume behind'
    Expect (Test-Path $journal) 'the journal survived the forced quit'
    & .\build\OpenYap.exe --restore | Out-String | Write-Host
    Expect ((HelperVolume) -eq 1) '--restore put the helper back to 100%'
    Expect (-not (Test-Path $journal)) 'the journal is removed after a complete restore'
    if (-not $helper.HasExited) { Stop-Process -Id $helper.Id }
} finally { Pop-Location }
Write-Host "Recovery test: $(if ($failures) { 'FAIL' } else { 'PASS' }) ($failures failures)"
exit $failures

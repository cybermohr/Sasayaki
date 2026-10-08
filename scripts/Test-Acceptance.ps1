param(
    [ValidateSet('Offline', 'LiveAzure', 'InteractiveWindows')][string]$Mode = 'Offline',
    [string]$FixturePcm
)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $env:LOCALAPPDATA 'Sasayaki\toolchain\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $repo
try {
    if ($Mode -eq 'Offline') {
        & $dotnet test Sasayaki.slnx -c Release --logger 'trx;LogFileName=offline.trx' --results-directory TestResults
        if ($LASTEXITCODE) { throw 'Offline tests failed.' }
    } elseif ($Mode -eq 'LiveAzure') {
        if (!(Test-Path -LiteralPath (Join-Path $env:LOCALAPPDATA 'Sasayaki\settings.json'))) {
            Write-Output 'BLOCKED: enter Azure deployments and keys in Sasayaki Settings first.'
            exit 2
        }
        if (!$FixturePcm -or !(Test-Path -LiteralPath $FixturePcm)) {
            Write-Output 'BLOCKED: provide -FixturePcm with a known 5–30 second utterance in raw PCM16 little-endian, mono, 16000 Hz. This fixture will be sent to your Azure deployments.'
            exit 2
        }
        $exe = Join-Path $repo 'artifacts\win-x64\Sasayaki.exe'
        if (!(Test-Path -LiteralPath $exe)) { throw 'Run scripts/Build.ps1 -Publish first.' }
        $fixture = (Resolve-Path -LiteralPath $FixturePcm).Path
        $result = Join-Path $repo 'TestResults\live-azure.json'
        New-Item -ItemType Directory -Force (Split-Path $result -Parent) | Out-Null
        $process = Start-Process -FilePath $exe -ArgumentList @('--live-test', ('"' + $fixture + '"'), ('"' + $result + '"')) -WindowStyle Hidden -Wait -PassThru
        Get-Content -LiteralPath $result
        if ($process.ExitCode) { exit $process.ExitCode }
    } else {
        Write-Output 'INTERACTIVE CHECKS REQUIRED: follow docs/ACCEPTANCE.md. This opens an instrumented local text target; it does not certify the other applications or physical gestures automatically.'
        & pwsh -NoProfile -STA -File (Join-Path $PSScriptRoot 'Input-Target.ps1')
        if ($LASTEXITCODE) { throw 'Input target failed.' }
    }
} finally { Pop-Location }

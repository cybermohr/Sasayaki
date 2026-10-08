param([switch]$Publish)
$ErrorActionPreference = 'Stop'
$repo = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $env:LOCALAPPDATA 'Sasayaki\toolchain\dotnet\dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { 'dotnet' }
Push-Location $repo
try {
    & $dotnet restore Sasayaki.slnx
    if ($LASTEXITCODE) { throw 'Restore failed.' }
    & $dotnet build Sasayaki.slnx -c Release --no-restore
    if ($LASTEXITCODE) { throw 'Build failed.' }
    if ($Publish) {
        $architecture = [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture.ToString().ToLowerInvariant()
        if ($architecture -ne 'x64') { throw "This release targets x64; detected $architecture. Review native dependencies before publishing another architecture." }
        & $dotnet publish src/Sasayaki.App/Sasayaki.App.csproj -c Release -r win-x64 --self-contained true -o artifacts/win-x64
        if ($LASTEXITCODE) { throw 'Publish failed.' }
    }
} finally { Pop-Location }

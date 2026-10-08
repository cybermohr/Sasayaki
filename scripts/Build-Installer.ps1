param([string]$Version = '1.0.0')
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

if ($Version -notmatch '^(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)\.(0|[1-9][0-9]*)$') {
    throw 'Version must have three numeric parts, for example 1.0.0.'
}
$parts = $Version.Split('.') | ForEach-Object { [int]$_ }
if ($parts[0] -gt 255 -or $parts[1] -gt 255 -or $parts[2] -gt 65534) {
    throw 'Version must fit both MSI and assembly metadata (major/minor <= 255, build <= 65534).'
}
if (![OperatingSystem]::IsWindows() -or [System.Runtime.InteropServices.RuntimeInformation]::OSArchitecture -ne 'X64') {
    throw 'Build the x64 installer on x64 Windows.'
}

$repo = Split-Path $PSScriptRoot -Parent
$localSdk = Join-Path $env:LOCALAPPDATA 'Sasayaki/toolchain/dotnet/dotnet.exe'
$dotnet = if (Test-Path -LiteralPath $localSdk) { $localSdk } else { (Get-Command dotnet -ErrorAction Stop).Source }
$stage = Join-Path $repo "artifacts/installer-staging/$Version-$([Guid]::NewGuid().ToString('N'))"
$publish = Join-Path $stage 'publish'
$extensions = Join-Path $repo 'artifacts/installer-toolchain/4.0.6'
$previousRollForward = $env:DOTNET_ROLL_FORWARD
Push-Location $repo
try {
    # WiX 4 runs on .NET 6; use the installed .NET runtime when 6 is absent.
    $env:DOTNET_ROLL_FORWARD = 'Major'
    New-Item -ItemType Directory -Force $publish, $extensions, 'artifacts/installer' | Out-Null
    & $dotnet tool restore --configfile NuGet.config
    if ($LASTEXITCODE) { throw 'WiX tool restore failed.' }

    # SDK package is not mirrored. The CLI and both extensions are pinned instead.
    $hashes = @{
        ui = 'E92C4DDAC5D17F5360291AB856C676E19FD92AF14DAFFB5D5D4B8E1E9C716B47'
        util = '541168D58C299DD8D62E92EA1877489756CFA73A27FD6EC855CD6341C8447562'
    }
    foreach ($name in @('ui', 'util')) {
        $package = "wixtoolset.$name.wixext"
        $archive = Join-Path $extensions "$package.4.0.6.nupkg"
        if (!(Test-Path -LiteralPath $archive)) {
            Invoke-WebRequest "https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-public/nuget/v3/flat2/$package/4.0.6/$package.4.0.6.nupkg" -OutFile $archive
        }
        if ((Get-FileHash -LiteralPath $archive -Algorithm SHA256).Hash -ne $hashes[$name]) {
            throw "Hash mismatch for $package. Remove the cached package and retry."
        }
        Expand-Archive -LiteralPath $archive -DestinationPath (Join-Path $extensions $name) -Force
    }

    & $dotnet publish src/Sasayaki.App/Sasayaki.App.csproj -c Release -r win-x64 --self-contained true `
        -o $publish "-p:Version=$Version" "-p:AssemblyVersion=$Version.0" "-p:FileVersion=$Version.0" `
        '-p:Company=Sasayaki' '-p:Product=Sasayaki' '-p:DebugType=None' '-p:DebugSymbols=false'
    if ($LASTEXITCODE) { throw 'Application publish failed.' }
    foreach ($required in @('Sasayaki.exe', 'Sasayaki.dll', 'coreclr.dll', 'hostfxr.dll', 'PresentationFramework.dll')) {
        if (!(Test-Path -LiteralPath (Join-Path $publish $required))) { throw "Self-contained publish is missing $required." }
    }

    # One file per component, with identities derived only from its relative path.
    # Never include the version or staging directory in component identity seeds.
    $document = [System.Xml.XmlDocument]::new()
    $ns = 'http://wixtoolset.org/schemas/v4/wxs'
    $root = $document.CreateElement('Wix', $ns)
    [void]$document.AppendChild($root)
    $fragment = $document.CreateElement('Fragment', $ns)
    [void]$root.AppendChild($fragment)
    $group = $document.CreateElement('ComponentGroup', $ns)
    $group.SetAttribute('Id', 'PublishedFiles')
    [void]$fragment.AppendChild($group)
    $directories = @{ '' = 'INSTALLFOLDER' }
    $files = Get-ChildItem -LiteralPath $publish -Recurse -File | Sort-Object FullName
    foreach ($file in $files) {
        $relative = [System.IO.Path]::GetRelativePath($publish, $file.FullName).Replace('\', '/')
        if ($relative -match '(^|/)(settings\.json|\.env.*|.*\.local\.(json|md|yaml|yml|config)|.*\.(pfx|pem|key))$') {
            throw "Unexpected configuration or credential file in publish: $relative"
        }
        $relativeDirectory = [System.IO.Path]::GetDirectoryName($relative).Replace('\', '/')
        $parent = ''
        foreach ($segment in ($relativeDirectory -split '/' | Where-Object { $_ })) {
            $path = if ($parent) { "$parent/$segment" } else { $segment }
            if (!$directories.ContainsKey($path)) {
                $digest = [Convert]::ToHexString([System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($path.ToLowerInvariant())))
                $id = 'D_' + $digest.Substring(0, 32)
                $directoryRef = $document.CreateElement('DirectoryRef', $ns)
                $directoryRef.SetAttribute('Id', $directories[$parent])
                $directory = $document.CreateElement('Directory', $ns)
                $directory.SetAttribute('Id', $id)
                $directory.SetAttribute('Name', $segment)
                [void]$directoryRef.AppendChild($directory)
                [void]$fragment.AppendChild($directoryRef)
                $directories[$path] = $id
            }
            $parent = $path
        }
        $bytes = [System.Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes("Sasayaki.Payload/$($relative.ToLowerInvariant())"))
        $digest = [Convert]::ToHexString($bytes)
        $component = $document.CreateElement('Component', $ns)
        $component.SetAttribute('Id', 'C_' + $digest.Substring(0, 32))
        $component.SetAttribute('Guid', [Guid]::new([byte[]]$bytes[0..15]).ToString('D'))
        $component.SetAttribute('Directory', $directories[$relativeDirectory])
        $component.SetAttribute('Bitness', 'always64')
        $payloadFile = $document.CreateElement('File', $ns)
        $payloadFile.SetAttribute('Id', 'F_' + $digest.Substring(0, 32))
        $payloadFile.SetAttribute('Source', $file.FullName)
        $payloadFile.SetAttribute('KeyPath', 'yes')
        [void]$component.AppendChild($payloadFile)
        [void]$group.AppendChild($component)
    }
    $document.Save((Join-Path $stage 'Payload.wxs'))

    # Brand the standard maintenance/progress/error pages with the same icon.
    Add-Type -AssemblyName System.Drawing
    $icon = [System.Drawing.Icon]::new((Join-Path $repo 'src/Sasayaki.App/Assets/Sasayaki.ico'), 48, 48)
    try {
        foreach ($image in @(@{Name='banner'; Width=493; Height=58; X=425; Y=5}, @{Name='dialog'; Width=493; Height=312; X=70; Y=90})) {
            $bitmap = [System.Drawing.Bitmap]::new($image.Width, $image.Height)
            $graphics = [System.Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.Clear([System.Drawing.Color]::White)
                $graphics.DrawIcon($icon, $image.X, $image.Y)
                $bitmap.Save((Join-Path $stage "$($image.Name).bmp"), [System.Drawing.Imaging.ImageFormat]::Bmp)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $icon.Dispose() }

    & $dotnet msbuild installer/Sasayaki.Installer.wixproj -t:Build "-p:InstallerVersion=$Version" `
        "-p:StagingPath=$stage" "-p:WixExtensionsPath=$extensions" "-p:DotnetPath=$dotnet"
    if ($LASTEXITCODE) { throw 'MSI build failed.' }
    $msi = Join-Path $repo "artifacts/installer/Sasayaki-$Version-x64.msi"
    Write-Output "Built unsigned installer: $msi"
    Write-Output "Fresh payload and build intermediates: $stage"
    & (Join-Path $PSScriptRoot 'Test-Installer.ps1') -Path $msi -Version $Version -PublishedPath $publish
} finally {
    $env:DOTNET_ROLL_FORWARD = $previousRollForward
    Pop-Location
}

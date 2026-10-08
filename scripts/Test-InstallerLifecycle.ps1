param(
    [Parameter(Mandatory)][string]$BaseMsi,
    [Parameter(Mandatory)][string]$UpgradeMsi,
    [switch]$IsolatedMachine
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
if (!$IsolatedMachine) { throw 'Run only in a disposable Windows VM; pass -IsolatedMachine to acknowledge this requirement.' }
$identity = [Security.Principal.WindowsIdentity]::GetCurrent()
$principal = [Security.Principal.WindowsPrincipal]::new($identity)
if (!$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    throw 'Use an elevated PowerShell terminal in the disposable VM for the machine-scope tests.'
}
$BaseMsi = (Resolve-Path -LiteralPath $BaseMsi).Path
$UpgradeMsi = (Resolve-Path -LiteralPath $UpgradeMsi).Path

function Get-MsiProperty([string]$Msi, [string]$Name) {
    $installer = New-Object -ComObject WindowsInstaller.Installer
    $database = $installer.OpenDatabase($Msi, 0)
    $view = $database.OpenView("SELECT ``Value`` FROM ``Property`` WHERE ``Property`` = '$Name'")
    try {
        [void]$view.Execute()
        $record = $view.Fetch()
        try { $record.StringData(1) }
        finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
    } finally {
        [void]$view.Close()
        foreach ($com in @($view, $database, $installer)) { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($com) }
    }
}
$baseVersion = Get-MsiProperty $BaseMsi 'ProductVersion'
$upgradeVersion = Get-MsiProperty $UpgradeMsi 'ProductVersion'
if ([version]$upgradeVersion -le [version]$baseVersion) { throw 'UpgradeMsi must have a strictly newer version.' }
foreach ($msi in @($BaseMsi, $UpgradeMsi)) {
    if ((Get-MsiProperty $msi 'UpgradeCode') -ne '{BAA81940-B8B2-49A7-912D-C8EBFC98A47C}') { throw 'Unexpected MSI upgrade identity.' }
}
$userFolder = Join-Path $env:LOCALAPPDATA 'Programs/Sasayaki'
$machineFolder = Join-Path $env:ProgramW6432 'Sasayaki'
$settingsDirectory = Join-Path $env:LOCALAPPDATA 'Sasayaki'
$settingsFile = Join-Path $settingsDirectory 'settings.json'
$userMenu = Join-Path ([Environment]::GetFolderPath('Programs')) 'Sasayaki'
$machineMenu = Join-Path ([Environment]::GetFolderPath('CommonPrograms')) 'Sasayaki'
$userMarker = 'HKCU:/Software/Sasayaki/Installer'
$machineMarker = 'HKLM:/Software/Sasayaki/Installer'
foreach ($path in @($userFolder, $machineFolder, $settingsDirectory, $userMenu, $machineMenu, $userMarker, $machineMarker)) {
    if (Test-Path -LiteralPath $path) { throw "Revert to a clean VM snapshot; existing data at $path." }
}
if (Get-Process Sasayaki -ErrorAction SilentlyContinue) { throw 'Quit Sasayaki before starting the isolated test.' }
$logs = Join-Path (Get-Location) "installer-lifecycle-$([Guid]::NewGuid().ToString('N'))"
New-Item -ItemType Directory -Path $logs, $settingsDirectory | Out-Null
Add-Type -AssemblyName System.Security.Cryptography.ProtectedData
$fakeKey = [Convert]::ToBase64String([Security.Cryptography.ProtectedData]::Protect(
    [Text.Encoding]::UTF8.GetBytes('installer-test-placeholder-not-an-Azure-key'), $null,
    [Security.Cryptography.DataProtectionScope]::CurrentUser))
@{ SpeechKey=$fakeKey; CleanupKey=$fakeKey; StartAtLogin=$false; InstallerTestFixture=$true } |
    ConvertTo-Json | Set-Content -LiteralPath $settingsFile -Encoding utf8
$settingsHash = (Get-FileHash -LiteralPath $settingsFile -Algorithm SHA256).Hash
function Assert-Lifecycle([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "Lifecycle check failed: $Message. Logs: $logs. Revert the VM snapshot." }
}
function Assert-Settings {
    Assert-Lifecycle (Test-Path -LiteralPath $settingsFile) 'settings remain present'
    Assert-Lifecycle ((Get-FileHash -LiteralPath $settingsFile -Algorithm SHA256).Hash -eq $settingsHash) 'settings and protected key blobs unchanged'
}
function Invoke-Msi([string]$Name, [string]$Operation, [string]$Msi, [string[]]$Properties, [int]$Expected = 0) {
    $log = Join-Path $logs "$Name.log"
    $arguments = @($Operation, ('"' + $Msi + '"'), '/qn', '/norestart', '/l*v', ('"' + $log + '"')) + $Properties
    $process = Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -Wait -PassThru
    Assert-Lifecycle ($process.ExitCode -eq $Expected) "$Name returned $($process.ExitCode); expected $Expected"
    Assert-Settings
    Write-Output "PASS: $Name"
}
foreach ($scope in @('user', 'machine')) {
    $properties = if ($scope -eq 'user') { @('ALLUSERS=""', 'MSIINSTALLPERUSER=1') } else { @('ALLUSERS=1', 'MSIINSTALLPERUSER=""') }
    $opposite = if ($scope -eq 'user') { @('ALLUSERS=1', 'MSIINSTALLPERUSER=""') } else { @('ALLUSERS=""', 'MSIINSTALLPERUSER=1') }
    $folder = if ($scope -eq 'user') { $userFolder } else { $machineFolder }
    $menu = if ($scope -eq 'user') { $userMenu } else { $machineMenu }
    $otherFolder = if ($scope -eq 'user') { $machineFolder } else { $userFolder }
    $otherMenu = if ($scope -eq 'user') { $machineMenu } else { $userMenu }
    $marker = if ($scope -eq 'user') { $userMarker } else { $machineMarker }
    Invoke-Msi "$scope-install" '/i' $BaseMsi $properties
    $exe = Join-Path $folder 'Sasayaki.exe'
    Assert-Lifecycle (Test-Path -LiteralPath $exe) "$scope executable installed"
    Assert-Lifecycle (Test-Path -LiteralPath (Join-Path $menu 'Sasayaki.lnk')) "$scope shortcut installed"
    Assert-Lifecycle (!(Test-Path -LiteralPath $otherFolder) -and !(Test-Path -LiteralPath $otherMenu)) 'opposite scope stays empty'
    $missingFile = Join-Path $folder 'Sasayaki.Core.dll'
    Remove-Item -LiteralPath $missingFile
    Invoke-Msi "$scope-repair" '/fa' $BaseMsi $properties
    Assert-Lifecycle (Test-Path -LiteralPath $missingFile) 'repair restores deleted DLL'
    Invoke-Msi "$scope-reject-switch" '/i' $UpgradeMsi $opposite 1603
    Assert-Lifecycle ((Get-ItemProperty -LiteralPath $marker).Version -eq $baseVersion) 'rejected scope switch leaves old version'
    # No scope properties: upgrade must discover and keep the existing scope.
    Invoke-Msi "$scope-upgrade" '/i' $UpgradeMsi @()
    Assert-Lifecycle ((Get-ItemProperty -LiteralPath $marker).Version -eq $upgradeVersion) 'upgrade registry version'
    Assert-Lifecycle ((Get-Item -LiteralPath $exe).VersionInfo.FileVersion -eq "$upgradeVersion.0") 'upgrade executable version'
    Assert-Lifecycle (!(Test-Path -LiteralPath $otherFolder)) 'upgrade keeps scope'
    Invoke-Msi "$scope-reject-downgrade" '/i' $BaseMsi $properties 1603
    Assert-Lifecycle ((Get-ItemProperty -LiteralPath $marker).Version -eq $upgradeVersion) 'rejected downgrade leaves newer version'
    Invoke-Msi "$scope-uninstall" '/x' $UpgradeMsi $properties
    Assert-Lifecycle (!(Test-Path -LiteralPath $folder) -and !(Test-Path -LiteralPath $menu)) 'uninstall removes files and shortcut directory'
    Assert-Lifecycle (!(Test-Path -LiteralPath $marker)) 'uninstall removes installer markers'
}
Write-Output "Both scope lifecycles passed. Settings fixture preserved. Verbose logs: $logs"
Write-Output 'Interactive UAC, cancellation, running recordings, and launch checks still require docs/INSTALLER-ACCEPTANCE.md.'

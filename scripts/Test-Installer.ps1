param(
    [Parameter(Mandatory)][string]$Path,
    [string]$Version = '1.0.0',
    [string]$PublishedPath,
    [switch]$CheckSession
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$msiPath = (Resolve-Path -LiteralPath $Path).Path
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath, 0)
function Read-MsiTable([string]$Query, [int]$Columns) {
    $view = $database.OpenView($Query)
    try {
        [void]$view.Execute()
        while ($record = $view.Fetch()) {
            try {
                $row = @()
                for ($i = 1; $i -le $Columns; $i++) { $row += $record.StringData($i) }
                ,$row
            } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($record) }
        }
    } finally { [void]$view.Close(); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($view) }
}
function Assert-Installer([bool]$Condition, [string]$Message) {
    if (!$Condition) { throw "MSI validation failed: $Message" }
}
try {
    $properties = @{}
    foreach ($row in (Read-MsiTable 'SELECT `Property`, `Value` FROM `Property`' 2)) { $properties[$row[0]] = $row[1] }
    Assert-Installer ($properties.ProductVersion -eq $Version) 'ProductVersion'
    Assert-Installer ($properties.Manufacturer -eq 'Sasayaki') 'publisher'
    Assert-Installer ($properties.UpgradeCode -eq '{BAA81940-B8B2-49A7-912D-C8EBFC98A47C}') 'stable upgrade identity'
    Assert-Installer ($properties.ALLUSERS -eq '2' -and $properties.MSIINSTALLPERUSER -eq '1') 'dual scope, default Just me'
    Assert-Installer ($properties.MSIRESTARTMANAGERCONTROL -eq 'Disable') 'Restart Manager must not close recordings'
    Assert-Installer (!$properties.ContainsKey('LAUNCHSASAYAKI')) 'launch must default unchecked'
    Assert-Installer ($properties.ARPPRODUCTICON -eq 'SasayakiIcon') 'Installed apps icon'
    $summary = $database.SummaryInformation(0)
    try {
        Assert-Installer ($summary.Property(7) -eq 'x64;1033') 'x64 summary template'
        Assert-Installer (([int]$summary.Property(15) -band 8) -eq 0) 'package must support elevation for all-user installation'
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($summary) }
    $files = @(Read-MsiTable 'SELECT `FileName`, `Version` FROM `File`' 2)
    $names = @($files | ForEach-Object { ($_[0] -split '\|')[-1] })
    foreach ($name in @('Sasayaki.exe','Sasayaki.dll','coreclr.dll','hostfxr.dll','PresentationFramework.dll')) {
        Assert-Installer ($names -contains $name) "bundled application/runtime file $name"
    }
    $app = $files | Where-Object { ($_[0] -split '\|')[-1] -eq 'Sasayaki.dll' }
    Assert-Installer ($app[1] -eq "$Version.0") 'application assembly version'
    Assert-Installer (@($names | Where-Object { $_ -match '(^settings\.json$|\.pfx$|\.pem$|^\.env)' }).Count -eq 0) 'no credentials/settings in payload'
    if ($PublishedPath) {
        Assert-Installer ($files.Count -eq @(Get-ChildItem -LiteralPath $PublishedPath -Recurse -File).Count) 'all freshly published files are packaged'
    }
    $media = @(Read-MsiTable 'SELECT `Cabinet` FROM `Media`' 1)
    Assert-Installer ($media.Count -gt 0 -and @($media | Where-Object { !$_[0].StartsWith('#') }).Count -eq 0) 'embedded cabinets only'
    $shortcuts = @(Read-MsiTable 'SELECT `Directory_`, `Target`, `Icon_` FROM `Shortcut`' 3)
    Assert-Installer ($shortcuts.Count -eq 1 -and $shortcuts[0][0] -eq 'SasayakiMenuFolder' -and $shortcuts[0][2] -eq 'SasayakiIcon') 'one branded Start menu shortcut, no desktop shortcut'
    $components = @(Read-MsiTable 'SELECT `Attributes` FROM `Component`' 1)
    Assert-Installer (@($components | Where-Object { ([int]$_[0] -band 256) -eq 0 }).Count -eq 0) 'all components use 64-bit registration'
    $actions = @{}
    foreach ($row in (Read-MsiTable 'SELECT `Action`, `Type`, `Target` FROM `CustomAction`' 3)) { $actions[$row[0]] = $row }
    Assert-Installer ($actions.ContainsKey('RejectScopeSwitch') -and $actions.ContainsKey('RejectRunningApplication')) 'scope and running-app guards'
    Assert-Installer ($actions.ClearRunningApplication[2] -eq '' -and $actions.DefaultMachineInstallPerUser[2] -eq '') 'type 51 actions clear properties with an empty string'
    Assert-Installer ($actions.ContainsKey('LaunchSasayaki') -and ([int]$actions.LaunchSasayaki[1] -band 3072) -eq 0) 'launch is immediate and impersonated'
    $sequence = @{}
    foreach ($row in (Read-MsiTable 'SELECT `Action`, `Sequence` FROM `InstallExecuteSequence`' 2)) { $sequence[$row[0]] = [int]$row[1] }
    Assert-Installer ($sequence.Wix4CloseApplications_X64 -lt $sequence.RejectRunningApplication -and $sequence.RejectRunningApplication -lt $sequence.InstallInitialize) 'running-app check precedes transaction'
    Assert-Installer ($sequence.RejectScopeSwitch -lt $sequence.InstallInitialize) 'scope guard precedes transaction'
    Assert-Installer ($sequence.AppSearch -lt $sequence.DefaultMachineScope -and $sequence.DefaultMachineScope -lt $sequence.FindRelatedProducts -and $sequence.FindRelatedProducts -lt $sequence.LaunchConditions) 'existing scope is resolved before upgrade/downgrade detection'
    Assert-Installer ($sequence.RemoveExistingProducts -gt $sequence.InstallInitialize -and $sequence.RemoveExistingProducts -lt $sequence.InstallFinalize) 'major upgrades are inside the rollback transaction'
    $upgrades = @(Read-MsiTable 'SELECT `VersionMin`, `VersionMax`, `Attributes`, `ActionProperty` FROM `Upgrade`' 4)
    Assert-Installer (@($upgrades | Where-Object { $_[0] -eq $Version -and ([int]$_[2] -band 2) -ne 0 -and $_[3] -eq 'WIX_DOWNGRADE_DETECTED' }).Count -eq 1) 'newer-version detection rejects downgrades'
    Assert-Installer (@($upgrades | Where-Object { $_[1] -eq $Version -and $_[3] -eq 'WIX_UPGRADE_DETECTED' }).Count -eq 1) 'older-version detection supports upgrades'
    $close = @(Read-MsiTable 'SELECT `Attributes` FROM `Wix4CloseApplication`' 1)
    # Only prompt-to-continue (0x40); no close, end-session, reboot, or terminate bits.
    Assert-Installer ($close.Count -eq 1 -and [int]$close[0][0] -eq 64) 'prompt only; no forced closing'
    Write-Output "MSI inspection passed: x64, version $Version, $($files.Count) files, embedded runtime/cabinets, both scopes, branded Start menu, safe guards, unchecked user-context launch."
    if ($CheckSession) {
        # Open costing sessions only. Never call ExecuteAction or InstallInitialize:
        # these checks do not install/uninstall files, shortcuts, or registry values.
        if (!('SasayakiInstallerChecks.Native' -as [type])) {
            Add-Type -TypeDefinition @'
namespace SasayakiInstallerChecks {
    public static class Native {
        [System.Runtime.InteropServices.DllImport("msi.dll")]
        public static extern uint MsiSetInternalUI(uint level, System.IntPtr window);
    }
}
'@
        }
        $previousUi = [SasayakiInstallerChecks.Native]::MsiSetInternalUI(2, [IntPtr]::Zero)
        try {
            foreach ($scope in @('user', 'machine')) {
                $session = $installer.OpenPackage($msiPath, 1)
                function Set-SessionProperty([string]$Name, [string]$Value) {
                    [void]$session.GetType().InvokeMember('Property', [Reflection.BindingFlags]::SetProperty, $null, $session, @($Name, $Value))
                }
                try {
                    Set-SessionProperty 'ALLUSERS' $(if ($scope -eq 'user') { '' } else { '1' })
                    Set-SessionProperty 'MSIINSTALLPERUSER' $(if ($scope -eq 'user') { '1' } else { '' })
                    Assert-Installer ($session.DoAction('AppSearch') -eq 1) 'Windows destination searches succeed'
                    Set-SessionProperty 'SASAYAKI_RUNNING' '1'
                    Assert-Installer ($session.DoAction('ClearRunningApplication') -eq 1) 'clear-running action succeeds'
                    Assert-Installer ($session.Property('SASAYAKI_RUNNING') -eq '') 'running flag actually clears'
                    foreach ($action in @('CostInitialize', 'FileCost')) {
                        Assert-Installer ($session.DoAction($action) -eq 1) "$scope $action succeeds"
                    }
                    if ($scope -eq 'machine') {
                        Assert-Installer ($session.DoAction('SetINSTALLFOLDER') -eq 1) 'machine path action succeeds'
                    }
                    $menuAction = if ($scope -eq 'machine') { 'SetMachineMenu' } else { 'SetUserMenu' }
                    Assert-Installer ($session.DoAction($menuAction) -eq 1) "$scope menu path action succeeds"
                    Assert-Installer ($session.DoAction('CostFinalize') -eq 1) "$scope costing succeeds"
                    $expectedFolder = if ($scope -eq 'user') { Join-Path $env:LOCALAPPDATA 'Programs/Sasayaki' } else { Join-Path $env:ProgramW6432 'Sasayaki' }
                    $actualFolder = $session.TargetPath('INSTALLFOLDER').TrimEnd('\')
                    Assert-Installer ($actualFolder -eq $expectedFolder.Replace('/', '\')) "$scope destination: got $actualFolder; expected $expectedFolder"
                    $expectedMenu = [Environment]::GetFolderPath($(if ($scope -eq 'user') { 'Programs' } else { 'CommonPrograms' }))
                    Assert-Installer ($session.TargetPath('ProgramMenuFolder').TrimEnd('\') -eq $expectedMenu) "$scope Start menu scope"
                    Assert-Installer ($session.DoAction('SetLaunchTarget') -eq 1) 'launch target action succeeds'
                    Assert-Installer ($session.Property('WixShellExecTarget') -eq (Join-Path $expectedFolder 'Sasayaki.exe').Replace('/', '\')) "$scope launch target"
                    Set-SessionProperty 'USERINSTALLPATH' $(if ($scope -eq 'machine') { 'C:\fixture\' } else { '' })
                    Set-SessionProperty 'MACHINEINSTALLPATH' $(if ($scope -eq 'user') { 'C:\fixture\' } else { '' })
                    $guard = 'NOT REMOVE AND ((USERINSTALLPATH AND ALLUSERS = 1) OR (MACHINEINSTALLPATH AND NOT ALLUSERS))'
                    Assert-Installer ($session.EvaluateCondition($guard) -eq 1) "$scope rejects opposite-scope marker"
                    if ($scope -eq 'machine') {
                        Set-SessionProperty 'ALLUSERS' '2'
                        Set-SessionProperty 'MSIINSTALLPERUSER' '1'
                        Assert-Installer ($session.DoAction('DefaultMachineScope') -eq 1) 'machine default action succeeds'
                        Assert-Installer ($session.DoAction('DefaultMachineInstallPerUser') -eq 1) 'machine per-user flag clearing succeeds'
                        Assert-Installer ($session.Property('ALLUSERS') -eq '1' -and $session.Property('MSIINSTALLPERUSER') -eq '') 'existing machine scope can be selected before upgrade detection'
                    }
                    Write-Output "MSI costing session passed: $scope destination, Start menu, property clearing, and scope rejection."
                } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($session) }
            }
        } finally { [void][SasayakiInstallerChecks.Native]::MsiSetInternalUI($previousUi, [IntPtr]::Zero) }
    }
} finally {
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database)
    [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer)
}

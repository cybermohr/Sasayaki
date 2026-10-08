# MSI acceptance

The installer is authored in `installer/Package.wxs` and `installer/Wizard.wxs`.
The stable UpgradeCode is `BAA81940-B8B2-49A7-912D-C8EBFC98A47C`. File component
identities are derived from relative payload paths; do not change those seeds or
the fixed shortcut/marker component identities between releases. Major upgrades
remove the previous version inside the rollback transaction. The installer owns
only published files, its Start menu shortcut, and `Software\Sasayaki\Installer`
registry markers. It never owns the app's settings file or Run registry value.

## Automated package checks

```powershell
pwsh -File scripts/Build-Installer.ps1 -Version 1.0.0
pwsh -File scripts/Test-Acceptance.ps1 -Mode Offline
```

The build checks MSI version, publisher, x64 architecture/components, dual scope
with Just me as default, embedded cabinets and runtime, application version,
the Start menu shortcut/icon, unchecked launch, and guards before the install
transaction. WiX runs Windows Installer ICE validation during the build.
These checks establish package contents and authoring; they do not establish
interactive behavior or successful installation.

`scripts/Test-Installer.ps1 -Path <msi> -CheckSession` also asks the Windows
Installer engine to cost both scopes and checks their actual destination and
Start menu paths, scope-rejection conditions, and property clearing. It never
runs the install transaction or writes installed files/shortcuts/registry values.

## Isolated Windows lifecycle checks

Use a disposable x64 Windows VM with snapshots, an ordinary test account, an
administrator account for elevation, and PowerShell 7. Do not install test
packages on the developer workstation. Build `1.0.0` and `1.0.1`, copy both MSIs
and `scripts/Test-InstallerLifecycle.ps1` to the VM, then run the following from
an elevated terminal under the test account on the disposable machine:

```powershell
pwsh -File scripts/Test-InstallerLifecycle.ps1 `
  -BaseMsi .\Sasayaki-1.0.0-x64.msi `
  -UpgradeMsi .\Sasayaki-1.0.1-x64.msi -IsolatedMachine
```

The script exercises silent installation in both scopes, missing-file repair,
upgrade, downgrade rejection, scope-switch rejection, uninstall, and byte-for-byte
settings preservation. It refuses existing MSI installations, settings, or
application directories and writes verbose MSI logs. If a failure interrupts
the run, revert the VM snapshot. It deliberately does not automate UAC or record
audio. An elevated test verifies the per-machine result, not the UAC experience.

## Interactive checks

Revert to a clean snapshot before each sequence. Open Setup normally, not from
an elevated terminal, so the completion launch retains the original user.

1. Confirm the welcome, scope, confirmation, and completion pages display the
   microphone artwork. Just me is selected initially. Check both destinations
   and go Back to change the selection before installing.
2. Install Just me as a standard user. Confirm no UAC request, files in that
   user's local Programs folder, a user Start menu shortcut, no desktop shortcut,
   and microphone artwork in Installed apps. Launch must be unchecked initially.
3. Select Launch Sasayaki and Finish. With no settings, Settings opens. Confirm
   Sasayaki runs as the original account at normal integrity. Save valid settings
   (and, for the live check, working Azure configuration), quit, then launch from
   Start: Settings stays closed and Sasayaki appears quietly in the tray.
4. Enable and disable Start at login through app Settings. Confirm Setup neither
   creates nor removes this preference. Disable it before each final uninstall.
5. Repair using Installed apps/original MSI after deleting one packaged DLL.
   Confirm it returns and the existing settings/key blobs are unchanged.
6. Upgrade with 1.0.1. Confirm existing scope is selected, one Installed apps entry
   remains, the application is 1.0.1, and settings and protected keys still load.
   Open 1.0.0: it must reject the downgrade. Choose the opposite scope on a newer
   package: it must require uninstall. Uninstall, then reinstall in the other
   scope: the saved per-user settings must load.
7. Repeat Everyone on this computer as a standard user with administrator
   credentials. Confirm UAC is requested only when installation proceeds, cancel
   UAC once, and check no files/shortcut/registration remain. Then approve UAC and
   check Program Files, the common Start menu, and original-user completion launch.
   Log in as another user and confirm they get their own first-launch Settings.
8. Keep Sasayaki running, including a recording. Start installation, upgrade,
   repair, and uninstall in turn. Check the finish-recording/quit prompt. Abort
   must leave installation intact; Retry proceeds only after tray Quit. Ignore
   must fail safely while it runs. No recording is force-closed and no reboot is
   used to work around an open Sasayaki process.
9. Cancel at Welcome, Scope, Confirmation, and during copying. Check files,
   shortcuts, and Installed apps registration; cancellation during an upgrade
   must roll back to the old installation. Settings/key blobs must stay intact.
10. Uninstall each scope. Confirm all packaged files, the Start menu shortcut,
    and Installed apps entry disappear. User settings remain and no Azure resource
    changes occur. App-created/unmanaged files may keep a nonempty directory alive.

## Evidence from implementation environment

See the recorded results below. No isolated Windows VM, Windows Sandbox launcher,
or Hyper-V management command was available in the implementation session.
The interactive lifecycle, UAC, repair, upgrade, downgrade, running-recording,
cancellation, and post-install launch scenarios above remain unexercised here.
First-launch Settings and quiet subsequent startup are confirmed by inspection
of `App.OnStartup`; installed-app UI behavior still needs the interactive checks.

Recorded on 2026-10-08:

- `Build-Installer.ps1 -Version 1.0.0`: succeeded, including MSI inspection;
  Windows Installer ICE validation reported no errors or warnings.
- Output: `artifacts/installer/Sasayaki-1.0.0-x64.msi`, 59,977,414 bytes, unsigned.
  SHA-256: `5E05856979D6F23B81087EEF05806748ECC0086234A3FEEACE6CCB131E33BD7F`.
- All 480 embedded files match the isolated fresh publish by SHA-256. Application
  executable and assemblies have file version `1.0.0.0`; runtime files are included.
- Wizard and shortcut/Installed apps icon bytes match `Assets/Sasayaki.ico`.
- Both MSI costing sessions passed: actual application and Start menu paths,
  clearing the running flag, launch target, and opposite-scope rejection. Existing
  machine-scope defaulting was checked before upgrade detection.
- All file component identities matched across two fresh staging directories.
- Offline tests: 56 passed, zero failed or skipped.
- The portable executable retained its pre-installer-build timestamp and folder.
- Detailed inspection, extracted package data, and payload verification are under
  the ignored `TestResults/installer` directory. The isolated lifecycle script is
  provided and syntax-checked, but was not run on this workstation.

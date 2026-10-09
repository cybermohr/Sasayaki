# Sasayaki

Native Windows dictation using Ctrl+Win, Azure streaming transcription, and light
text cleanup. Text is submitted at the current focused caret using Unicode input;
the app does not use the clipboard.

**Status:** unsigned x64 MSI available; the user reports the application works.
The two-second target is unmeasured; see the acceptance walkthrough for full validation.

## Before first run

Create the two deployments using [Azure setup](docs/AZURE-SETUP.md), then
have these six values ready to enter in the app's Settings:

| Setting | Where to find it / expected value |
| --- | --- |
| Speech resource root | Azure OpenAI resource's HTTPS endpoint, such as `https://YOUR-RESOURCE.openai.azure.com/` |
| Speech deployment | Your deployment name, normally `sasayaki-speech` |
| Speech API key | Key shown in **Build > Models > sasayaki-speech > Details** |
| Cleanup base URL | Cleanup resource's endpoint ending in `/openai/v1/` |
| Cleanup deployment | Your deployment name, normally `sasayaki-cleanup` |
| Cleanup API key | Key shown in **Build > Models > sasayaki-cleanup > Details** |

Keep the Azure endpoint/key pages available while configuring the app. Enter
the values directly in Settings; no separate Azure settings file is needed.

If both deployments share one Foundry resource, use the same resource key in both
key fields. The speech URL is `https://YOUR-RESOURCE.openai.azure.com/`;
the cleanup URL is `https://YOUR-RESOURCE.services.ai.azure.com/openai/v1/`.
Each deployment still has its own name in Settings.

The deployment Details pages may show complete API URLs. For speech, use the
Azure OpenAI resource endpoint root, for example
`https://YOUR-RESOURCE.openai.azure.com/`, without a deployment path or query.
For cleanup, remove `responses` from the displayed `/openai/v1/responses` URL,
leaving `/openai/v1/`. The app constructs the request routes itself.

Speech uses the `gpt-live-transcribe` realtime model. Its deployment name in the
app can remain `sasayaki-speech`; deploy the GPT Live Transcribe model with that
name (or enter the deployment name you choose).

## Run

Install [Sasayaki-1.0.0-x64.msi](installer/Sasayaki-1.0.0-x64.msi) and open Sasayaki from the
Start menu. The installer includes the .NET runtime.
Settings opens on first launch. Enter the
six values above, using the masked fields for keys, test the connections, and
save. Saved keys are protected for
your Windows account under `%LOCALAPPDATA%\Sasayaki`, outside this repository.
Once valid settings are saved, future launches start quietly in the system tray.
Open Settings from the tray whenever you need to make changes. Missing or invalid
settings, or an unavailable recording shortcut, open Settings automatically.

- Hold Ctrl+Win and speak; release to finish.
- Double-press Ctrl+Win to start hands-free recording; double-press again to finish.
- Escape cancels. Recording is capped at five minutes with a countdown.
- A blue-and-purple animated waveform responds to microphone volume while
  recording in a compact, rounded overlay without recording text, then settles
  when capture ends. Processing and error messages remain visible when needed.
  Windows reduced-animation settings
  disable continuous motion.
- Recording automatically mutes all currently active Windows playback devices.
  Audio returns when capture ends, including cancellation, errors, and tray Quit.
  Devices already muted stay muted; manually unmuting during recording overrides
  automatic restoration. Volume levels are unchanged. Devices connected after
  recording starts are not muted until the next recording.
- Right-click the system tray icon for Settings, recovery, discard, or Quit.
- The tray and application use the supplied microphone artwork. Double-click
  the tray icon to open Settings; Windows may place it in the hidden-icons menu.
- Settings lets you update both endpoint URLs, deployment names, and API keys.
  Leave a key field blank to retain its saved key; enter a new key to replace it.
- Change the recording shortcut in Settings. Keep Ctrl+Win, or choose two or
  more of Ctrl/Alt/Shift plus a letter, number, or F1–F11. Hold and double-press
  gestures use the selected shortcut; Escape always cancels. Save applies changes
  immediately and preserves the previous shortcut if the new one is unavailable.
  Custom shortcuts are checked and reserved with Windows while Sasayaki runs.
  This detects registered global conflicts, not shortcuts privately handled by
  other apps. The default modifier-only Ctrl+Win gesture cannot be reserved this
  way. A startup conflict opens Settings so you can select another shortcut.
- To recover text, review it, arm insertion, focus the destination, then press and
  release Ctrl+Win. Check for any partial previous insertion before retrying.

Insertion depends on the target's Unicode input support and Windows integrity
rules. The overlay reports submission to Windows, not verified rendering. Errors
retain available text in memory; quitting clears it. Retained text can be raw or
incomplete when transcription/cleanup fails.

## Build and verify

```powershell
pwsh -File scripts/Build-Installer.ps1 -Version 1.0.0
pwsh -File scripts/Test-Acceptance.ps1 -Mode Offline
```

Requires .NET 10 SDK. The build script also finds the SDK from the interrupted
session at `%LOCALAPPDATA%\Sasayaki\toolchain\dotnet`. The installed application
does not need that SDK. See [acceptance steps](docs/ACCEPTANCE.md) and
[installer validation](docs/INSTALLER-ACCEPTANCE.md) for actual evidence and open checks.

The installer build restores WiX CLI 4.0.6 from the checked-in tool manifest
and WiX UI/Util 4.0.6 extensions with SHA-256 verification, using Microsoft's
public package mirror. It does not need `api.nuget.org` or a separately installed
WiX toolset. The WiX project uses the CLI because the mirror lacks the WiX SDK
package. Each build publishes fresh application files into its own
`artifacts/installer-staging` directory, applies the supplied version to the app
and MSI, and produces one MSI with embedded cabinets under `artifacts/installer`.
MSI table inspection runs automatically after building. Run it separately with:

```powershell
pwsh -File scripts/Test-Installer.ps1 -Path installer/Sasayaki-1.0.0-x64.msi -Version 1.0.0
```

Add `-CheckSession` to verify both destination and Start menu scopes using MSI
costing sessions; this does not install the application.

Versions use `major.minor.build`; major/minor must be at most 255 and build at most
65534. Increment the version for every released MSI. The initial MSI is unsigned;
its publisher metadata is **Sasayaki**, but Windows cannot verify a signing
publisher. See [installer validation](docs/INSTALLER-ACCEPTANCE.md) for evidence
and the isolated Windows test procedure.

## Install, update, remove

Open the MSI normally from Explorer. Choose **Just me** (the default) to install
in `%LOCALAPPDATA%\Programs\Sasayaki`, or **Everyone on this computer** to install
in `%ProgramFiles%\Sasayaki` with administrator approval. Setup creates a Start
menu shortcut in the selected scope and no desktop shortcut. The completion
page offers an unchecked **Launch Sasayaki** option, which launches from the
original user's Setup session. Each Windows user keeps their own settings and
account-protected keys; the MSI contains no Azure credentials.

To upgrade, finish recording, quit Sasayaki from its tray menu, and open the newer
MSI. Setup selects the existing scope. Older versions are rejected. To change
scope, uninstall first, then reinstall; saved settings remain available. Use
Windows **Installed apps > Sasayaki** or reopen the original MSI to repair or
uninstall. Repair restores missing packaged files. Setup asks you to quit a
running Sasayaki and will not terminate a recording; choosing Ignore while it
still runs aborts the installation.

Before uninstalling, turn off **Start at login** in Sasayaki Settings, then quit
from the tray. The app owns this per-user startup preference; the installer does
not change it. Uninstall removes packaged files and its Start menu shortcut,
preserving `%LOCALAPPDATA%\Sasayaki\settings.json` and its protected keys. Delete
that file separately only if you also want to erase saved configuration. No
Azure resources are provisioned or deleted by installation or removal.

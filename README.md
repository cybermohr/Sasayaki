# Sasayaki

Native Windows dictation using Ctrl+Win, Azure streaming transcription, and light
text cleanup. Text is submitted at the current focused caret using Unicode input;
the app does not use the clipboard.

**Status:** desktop build available; the user reports the application works.
The two-second target is unmeasured; see the acceptance walkthrough for full validation.

## Before first run

Create the two deployments using [Azure setup](clone-run/AZURE-SETUP.md), then
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

Open `artifacts/win-x64/Sasayaki.exe`. Keep the entire published folder together;
the build includes the .NET runtime. Settings opens on first launch. Enter the
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
pwsh -File scripts/Build.ps1 -Publish
pwsh -File scripts/Test-Acceptance.ps1 -Mode Offline
```

Requires .NET 10 SDK. The build script also finds the SDK from the interrupted
session at `%LOCALAPPDATA%\Sasayaki\toolchain\dotnet`. The published executable
does not need that SDK. See [acceptance steps](docs/ACCEPTANCE.md) and
[build status](clone-run/BUILD-STATUS.md) for actual evidence and open checks.

## Install, update, remove

Copy the complete published folder to a permanent local folder, for example
`%LOCALAPPDATA%\Programs\Sasayaki`, and run the executable from there. Optional
start-at-login uses that executable path and defaults off. To update, quit via
the tray and replace the whole application folder. To remove, turn off
start-at-login in Settings, quit, and remove your installed folder. Remove
`%LOCALAPPDATA%\Sasayaki\settings.json` separately if you also want to remove saved
configuration. No Azure resources are provisioned or deleted by the app.

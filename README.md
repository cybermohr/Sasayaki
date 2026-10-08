# Sasayaki

Native Windows dictation using Ctrl+Win, Azure streaming transcription, and light
text cleanup. Text is submitted at the current focused caret using Unicode input;
the app does not use the clipboard.

**Status:** initial desktop build available. Offline checks and startup pass;
Azure and physical Windows acceptance are pending. The two-second target is unmeasured.

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
name (or enter the deployment name you choose). The former MAI deployment is not
used by this build.

## Run

Open `artifacts/win-x64/Sasayaki.exe`. Keep the entire published folder together;
the build includes the .NET runtime. Settings opens on first launch. Enter the
six values above, using the masked fields for keys, test the connections, and
save. Saved keys are protected for
your Windows account under `%LOCALAPPDATA%\Sasayaki`, outside this repository.

- Hold Ctrl+Win and speak; release to finish.
- Double-press Ctrl+Win to start hands-free recording; double-press again to finish.
- Escape cancels. Recording is capped at five minutes with a countdown.
- Right-click the system tray icon for Settings, recovery, discard, or Quit.
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

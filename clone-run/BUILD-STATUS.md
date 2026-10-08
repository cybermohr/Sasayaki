# Sasayaki build status

Current Windows build: `artifacts/win-x64/Sasayaki.exe`. Keep the complete
published folder together. The user reports the application is working.

## Current implementation

- C#/.NET 10 WPF desktop application with a single-instance guard.
- `gpt-live-transcribe` over the GPT Realtime WebSocket transcription API,
  using 24 kHz PCM16 mono audio and an explicit final commit.
- `gpt-5.4-mini` for English dictation cleanup with reasoning disabled.
- Hold/release and double-press recording gestures; configurable shortcuts with
  Windows global hotkey reservation for custom chords.
- Automatic system playback muting during capture and restoration on completion.
- Compact animated waveform, microphone artwork for the app/tray icon, Settings
  and Quit from the tray, and quiet startup with valid saved configuration.
- Editable endpoint URLs, deployment names, and API keys; keys protected with
  Windows DPAPI outside the repository.
- Unicode input at the focused caret without clipboard mutation, with retained
  text available for deliberate recovery after a failed insertion.

## Validation

The latest published build succeeded. The offline suite passed **56 tests**.
Windows checks verified rejection and release of a conflicting hotkey reservation,
Settings construction and shortcut selectors, and icon resource loading.
The user confirmed the application works in their workflow.

Comprehensive physical-key, cross-application insertion, clipboard-format, and
failure-path acceptance remains defined in [the acceptance walkthrough](../docs/ACCEPTANCE.md).
The two-second finish-to-visible-insertion target has not been measured against
its full fixture; build success and offline tests do not establish that result.

## Build and setup

Use `pwsh -File scripts/Build.ps1 -Publish` to publish to `artifacts/win-x64`.
The script finds the local .NET SDK under `%LOCALAPPDATA%\Sasayaki\toolchain\dotnet`
when it is not on PATH. Configure Azure using [Azure setup](AZURE-SETUP.md).

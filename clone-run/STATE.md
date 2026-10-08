# Sasayaki project state

- Runtime: native Windows desktop app, C#/.NET 10 WPF.
- Speech: Azure-hosted `gpt-live-transcribe`, GPT Realtime transcription API.
- Cleanup: Azure-hosted `gpt-5.4-mini`, reasoning disabled.
- Interaction: Ctrl+Win by default, configurable hold/release and double-press
  recording, compact waveform overlay, playback muting, and a tray menu.
- Credentials: endpoint URLs, deployment names, and protected keys in app Settings.
- Published app: `artifacts/win-x64/Sasayaki.exe`.
- User reports the current application works. Full acceptance and latency evidence
  are tracked in [BUILD-STATUS.md](BUILD-STATUS.md) and
  [the acceptance walkthrough](../docs/ACCEPTANCE.md).
- Azure instructions: [AZURE-SETUP.md](AZURE-SETUP.md).
- Planning documents: scope.md, PLAN.md, research.md, and the self-contained
  GOAL-PROMPT.md handoff.

# Sasayaki build status

Resumed: 2026-10-07, after reboot. This file supersedes the earlier planning-only
statement in STATE.md. Initial desktop implementation is built and packaged;
the complete acceptance condition has NOT been met.

## Plan review and implementation

Reviewed PLAN.md, PLAN-REVIEW-LOG.md, scope, and the existing interrupted code.
The plan still represents the requested scope. No independent reviewer was used.
Preserved the exact gesture, Azure provider choices, current-caret insertion,
clipboard constraint, and two-second target. No Azure resources were created.

Existing work: core gesture machine, configuration, Realtime transport, transcript
accumulation, cleanup client, PCM framing, DPAPI store, microphone scaffold.

Added: WPF host, single-instance guard, tray menu, masked settings, Azure connection
checks, nonactivating overlay, session coordination/cancellation, timeout/countdown,
dedicated keyboard hook, testable key routing/replay, focus/modifier-checked Unicode
insertion, retained-text review and deliberate retry, suspend/lock cancellation,
build scripts, offline tests, service smoke runner, and interactive input target.

Review corrections: stop the microphone during the short-tap ambiguity gap; keep
the resampler input alive across callback gaps rather than reporting temporary
buffer exhaustion as EOF; gate new sessions while old capture drains; preserve
retained text when Escape disarms recovery. Audio and transcripts are not logged.

Speech protocol initially implemented against MAI guidance; replaced on 2026-10-08
with GPT Realtime `gpt-live-transcribe`, using Microsoft's current 24 kHz WebSocket
transcription sample: https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-websockets
NAudio streaming behavior reviewed against upstream MediaFoundationTransform.cs
at tag v2.2.1. No cloud parameter or model accuracy claims were inferred from mocks.

## Recorded checks

### 2026-10-08 live speech investigation

Azure settings are now saved, and the user reports that the Settings connection
test succeeds. Live dictation fails. Reproduced twice using the saved settings:
synthetic silence through the speech client, and generated speech using the
minimal documented session configuration. Both configured the session, then Azure
returned `server_error`, code `unimplemented`, message `Input transcription failed.`
No microphone audio or credentials were printed. A working live transcription
path remains blocked; the Azure backend cause has not been established.

Added safe, specific messages for known speech errors without exposing arbitrary
service payloads. Offline tests now pass 44/44, including error classification,
malformed errors, retained partial text, and suppression of echoed private data.
The entries below preserve the earlier build's historical checks.

| Command/check | Actual result |
| --- | --- |
| `pwsh -File scripts/Build.ps1 -Publish` | Restore, Release build, and self-contained win-x64 publish succeeded; 0 warnings/errors |
| `pwsh -File scripts/Test-Acceptance.ps1 -Mode Offline` | 34 passed, 0 failed, 0 skipped; TRX in ignored TestResults/offline.trx |
| Published `Sasayaki.exe --smoke-test` | Process launched with hook/tray/overlay and exited 0 |
| `pwsh -File scripts/Test-Acceptance.ps1 -Mode LiveAzure` | BLOCKED: no saved Azure settings; user confirmed deployments not set up yet |
| Physical keyboard / cross-app insertion / clipboard formats and history | NOT RUN |
| Live microphone conversion and speech fidelity | NOT RUN |
| 30-utterance visible-insertion latency fixture | NOT RUN; target unmeasured |

Machine architecture: x64. SDK: 10.0.401 under
`%LOCALAPPDATA%\Sasayaki\toolchain\dotnet` (not on the normal dotnet PATH).
Portable output: `artifacts/win-x64/Sasayaki.exe` plus the rest of that directory.

Offline coverage includes hold/tap/double-press boundaries, cancellation, modifier
orders/sides, repeated/injected events, unrelated shortcut replay ordering, bounded
audio, empty audio, drain/one commit, provisional cancellation without upload,
transcript deduplication, endpoint restrictions, and cleanup rejection. These do
not yet provide automated coverage of the entire native coordinator or every
failure mode listed in the plan. The Windows target is an observation aid, not
an automated certificate of complete native-input acceptance.

## Next steps and blockers

1. User sets up the two Azure deployments using AZURE-SETUP.md and enters keys in
   app Settings; never paste credentials in chat or the repository.
2. Run connection checks, known-utterance service fixture, and actual microphone
   dictation. Confirm deployed model parameter support and cleanup fidelity.
3. Run docs/ACCEPTANCE.md for physical-key balancing/Start-menu leakage, Unicode
   insertion in representative applications, focus changes, recovery, lock/suspend,
   clipboard fixtures/history, and microphone/network failures.
4. Measure the full 30-utterance latency fixture. Optimize any failures; do not
   label the two-second requirement met from submission timing or averages.

## 2026-10-08 GPT Live Transcribe rebuild

Changed the speech endpoint to `/openai/v1/realtime?intent=transcription`, configured
24 kHz PCM16 mono with minimal transcription delay, and send microphone frames while
recording. The finish gesture drains the send queue and commits the audio. Updated
the Azure setup guide and app field label for the Azure OpenAI resource root. The
existing deployment name `sasayaki-speech` remains valid if assigned to the new
`gpt-live-transcribe` model deployment. This build has not yet been tested against
that Azure deployment or with a microphone.

| Command/check | Result |
| --- | --- |
| `pwsh -File scripts/Build.ps1 -Publish` | Succeeded; Release build and self-contained win-x64 publish, 0 warnings/errors |
| Offline tests | Not run for this rebuild |
| Live Azure transcription / microphone acceptance | Not run; requires a deployed `gpt-live-transcribe` model |

Known technical risk: Ctrl+Win interception uses suppression of the completing
modifier and an unassigned-key menu mask. Offline routing tests pass; actual shell
behavior and replay fidelity remain unproven on this machine. Native injection
cannot atomically bind focus and text across arbitrary applications.

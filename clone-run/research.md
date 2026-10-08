# Sasayaki research and decisions

Scope: personal Windows app, English-only Azure dictation,
Ctrl+Win hold/double-press, faithful light cleanup, two-second finish-to-insertion
target, and no mutation of the normal clipboard. Current implementation and
validation are tracked in [BUILD-STATUS.md](BUILD-STATUS.md).

## Recommended architecture

Use C#/.NET 10 WPF with Win32 input integration, direct personal Azure connections,
Azure-hosted gpt-live-transcribe for recognition, and gpt-5.4-mini with reasoning disabled
for cleanup. Store resource keys under the user's local profile protected by DPAPI.
Keep audio/text in bounded process memory. This is an engineering recommendation
based on the requirements and documentation, not a benchmark result.

## Product reference

[Wispr Flow](https://wisprflow.ai/) describes filler removal, punctuation, spoken
corrections, and insertion into the current typing context. Sasayaki intentionally
omits its broader personalization, snippets, meeting notes, team, and sync features.
Nothing in that product page establishes Wispr's internal architecture or provides
a latency guarantee for our implementation.

## Speech model selection

Use `gpt-live-transcribe` for live English transcription through the GPT Realtime
API. Deploy it in Microsoft Foundry as `sasayaki-speech`. Confirm model availability
and quota in the intended subscription before creating the resource; see
[Azure setup](AZURE-SETUP.md) for the current deployment workflow.

Microsoft's [GPT Realtime guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio)
lists `gpt-live-transcribe` for real-time transcription and duration-based billing.
The [WebSocket guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/realtime-audio-websockets)
describes the resource endpoint and session configuration. Sasayaki uses 24 kHz
PCM16 mono, streams ordered frames after gesture confirmation, drains captured
audio on finish, commits once, and waits for the completed transcript.
A commit acknowledgment alone is not a final transcription result.

## Cleanup model selection

Start with gpt-5.4-mini, reasoning_effort=none, on Azure v1 Chat Completions. The
[model catalog](https://learn.microsoft.com/en-us/azure/foundry/foundry-models/concepts/models-sold-directly-by-azure)
lists its chat support; the [reasoning guide](https://learn.microsoft.com/en-us/azure/foundry/openai/how-to/reasoning)
documents the effort controls. Test the deployed version's accepted parameters.
Use a short editing instruction, one transcript, no tools, no history, and complete
output before insertion. This is a cleanup candidate, not a claim that newest or
largest means best for this limited operation.

The [retirement schedule](https://learn.microsoft.com/en-us/azure/foundry/openai/concepts/model-retirement-schedule)
currently lists gpt-5.4-mini as GA with a September 21, 2027 retirement date, while
gpt-4.1-mini is deprecated. That favors starting with the newer small model rather
than baking an older default into a new app. If latency or fidelity fails, benchmark
gpt-5.4-nano or another available small model with the same fixtures; never silently
change the selected deployment. Large reasoning models add no demonstrated value
to this narrow edit task.

[Chat API reference](https://learn.microsoft.com/en-us/rest/api/microsoft-foundry/azureopenai/chat):
use the actual deployment name, the correct v1 base URL, resource-key authentication,
and validate finish_reason/content. Output truncation, refusal, errors, and empty
text are failure cases, not valid cleaned transcripts. No parameter or heuristic
can replace semantic quality checks on representative utterances.

## Native Windows client

[WPF on .NET 10](https://learn.microsoft.com/en-us/dotnet/desktop/wpf/whats-new/net100)
provides a maintained Windows desktop stack. WPF is selected for straightforward
Win32 interaction, compact tray/overlay UI, and no need for a browser runtime.
WinUI 3 is a possible native alternative, but adds no required capability here;
framework choice is reversible before the first native spike.

Use [NAudio](https://github.com/naudio/NAudio) for microphone/WASAPI integration;
its [resampling guidance](https://github.com/naudio/NAudio/blob/main/Docs/Resampling.md)
supports explicit conversion instead of assuming the mic already produces 24 kHz
mono PCM. Pin a stable release verified against .NET 10 during implementation.
Do not send loopback/system audio.

A [nonactivating window style](https://learn.microsoft.com/en-us/windows/win32/winmsg/extended-window-styles)
supports the overlay design. Settings/recovery can be ordinary interactive windows,
but the recording overlay must not steal focus from the typing destination.

## Ctrl+Win is an early technical risk

[RegisterHotKey](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-registerhotkey)
uses a virtual key with modifiers and reserves Windows-key combinations for the OS.
It is not sufficient evidence that the requested modifier-only hold/double-tap UX
can be handled reliably with one registration.

Proposed approach: a [low-level keyboard hook](https://learn.microsoft.com/en-us/windows/win32/winmsg/lowlevelkeyboardproc)
on a dedicated message-loop thread feeding a pure state machine. Track physical
transitions, ignore auto-repeat/injected events, and keep callbacks minimal. Test
Ctrl-first/Win-first and left/right variants, Start behavior, modifier balancing,
and unrelated shortcuts. The initial tap/hold ambiguity must be timed and included
in the latency budget. Native interception behavior is still to be demonstrated.

## Clipboard-free insertion and limits

Ordinary [WM_PASTE](https://learn.microsoft.com/en-us/windows/win32/dataxchg/wm-paste)
reads the shared clipboard, so a private buffer alone cannot make ordinary paste
use a different source. The user requires the shared clipboard to remain unchanged.

[KEYEVENTF_UNICODE](https://learn.microsoft.com/en-us/windows/win32/api/winuser/ns-winuser-keybdinput)
supports text input from speech recognition. Our proposal sends Unicode input from
private memory; no clipboard save/restore fallback. [SendInput](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
is restricted by integrity levels and existing keyboard state. Its returned count
confirms queued events, not that arbitrary target controls visibly accepted text.
Universal delivery cannot honestly be guaranteed. Verify representative controls,
retain the result on failures, and never automatically resend partial output.

Follow current focus just before insertion, matching the user's latest requirement;
do not force the original window to the front. The focus race cannot be eliminated
for every arbitrary external control. Check before submission and exercise races
in the acceptance suite. Do not emit Enter as a command or execute terminal text.

## Existing implementation comparison

[Sotto](https://github.com/smirk-dev/sotto) is a Windows dictation reference using
local recognition and simulated typing with a clipboard fallback. Its documented
behavior is useful prior art, but the local model path and clipboard fallback do
not meet this Azure-only, clipboard-preserving scope. No source code was copied,
and its README compatibility claims were not independently tested.

## Personal authentication and setup

Use API keys for the first personal version, protected with
[DPAPI](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.protecteddata)
and [CurrentUser scope](https://learn.microsoft.com/en-us/dotnet/api/system.security.cryptography.dataprotectionscope).
A same-user process can still access that user's secrets; this is at-rest protection,
not isolation from other software running as the user. No extra backend server is
needed. If the subscription prohibits key authentication, plan an Entra variant
instead of changing its policies. [AZURE-SETUP.md](AZURE-SETUP.md) documents the
resource, deployment, and application configuration steps.

## Five main implementation risks and proof steps

1. Shortcut interception: prove the exact physical gestures and ordinary key behavior
   with native tests before adding cloud services.
2. Text delivery: demonstrate clipboard-free insertion across representative controls,
   preserve data on failures, and distinguish submitted from visibly inserted.
3. Model availability/accuracy: deploy in the user's subscription and test real English
   fixtures. Documentation alone does not establish access or quality for this voice.
4. Two-second latency: streaming reduces work after finish, but finalization, cleanup,
   network, and target rendering all count. Measure cold and warm full-pipeline runs.
5. Cleanup fidelity: test names, numbers, negation, corrections, intended repetition,
   questions, and instruction-like text; never silently answer or rewrite the speaker.

## Validation

See [BUILD-STATUS.md](BUILD-STATUS.md) for current build evidence and
[the acceptance walkthrough](../docs/ACCEPTANCE.md) for validation steps.

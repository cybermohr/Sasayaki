# Sasayaki: core Wispr Flow dictation

Status: user requirements captured; research and implementation specification prepared.
Technical choices are recommendations pending the plan's live validation milestones.

## Target and workspace

- Reference product: Wispr Flow, https://wisprflow.ai/.
- Build root: current Sasayaki repository.
- Confirmed platform: native Windows 11 application.
- Confirmed backend: Azure AI for speech recognition and text cleanup.
- Confirmed usage: personal use with the user's own Azure subscription.
- Confirmed language scope: English-only dictation and cleanup for v1.
- Deliverable: instructions for setting up the Azure backend, including a current
  Microsoft speech-to-text model recommendation and a working verification procedure.

## Must-have workflow

1. Focus a text field in another application.
2. Use the default Ctrl+Win chord to show a small recording interface and begin speaking.
3. Support both hold-to-dictate (release to finish) and hands-free mode
   (double-press the chord to start; double-press again to finish).
4. Transcribe and clean up the speech extremely quickly.
5. Stage the resulting text in private temporary app memory and insert it at the
   currently focused text caret, without changing the normal Windows clipboard.

## Constraints and acceptance criteria

- User prioritizes very low delay between finishing speech and text insertion.
- Confirmed latency target: cleaned text inserted within two seconds after the
  user finishes recording (release in hold mode; stop double-press in hands-free mode).
  This includes final transcription, cleanup, and insertion; it is not yet benchmarked.
- Representative dictation lengths, network conditions, and latency measurement
  distribution remain to be defined in the verification plan.
- The recording UI must not steal typing focus. Follow the user's current focused
  text field, including an intentional focus change during recording.
- App-independent insertion is a core requirement; no per-app integrations or
  user-selected list of supported applications. Validate a representative range
  of controls and document actual Windows/application limitations honestly.
- Keep existing Windows clipboard data and history untouched. Use a private
  temporary text buffer; do not silently substitute clipboard overwrite-and-restore.
- If insertion is blocked, retain the result for recovery and report the failure
  without mutating the clipboard or claiming successful insertion.
- Confirmed cleanup: remove fillers and repetitions, add punctuation, and resolve
  spoken corrections while preserving the user's wording, meaning, and tone.
- Do not summarize, embellish, change formality, or rewrite into polished prose.
- Cleanup acceptance examples should cover fillers, accidental repetitions,
  explicit self-corrections, and preservation of names, numbers, and negation.
- Both hotkey modes must finalize and insert exactly once per recording.
- Treat a double-press as two presses of the Ctrl+Win chord. Recognition timing,
  modifier release order, and cancellation are implementation details to resolve
  and test, not permission to alter the requested interaction.

## Implementation decisions and validation risks

- Azure subscription/region availability and preview-model suitability.
- Clipboard-free insertion compatibility and focus changes during finalization.
- Selected stack: C#/.NET 10 WPF with Win32 input integration; direct Azure access.
- Cleanup candidate: Azure-hosted gpt-5.4-mini with reasoning disabled; verify
  deployment parameter support, editing fidelity, and end-to-end latency.
- Leading speech candidate: MAI-Transcribe-2-Streaming (public preview), subject to
  actual availability and a dictation accuracy/latency spike. See research.md.

## Proposed non-goals

The user requested only the base dictation workflow. Meeting notes, accounts,
team features, sync, personalization, and snippets are outside the proposed v1.
Distribution, multi-user authentication, and a shared billing backend are also
outside the personal-use scope. Plan direct access to the user's Azure resources
unless a verified technical requirement calls for an intermediary.

## Reference observation

The official product page, accessed 2026-10-07, describes filler removal,
punctuation/formatting, handling spoken corrections, and insertion where the
cursor is. This establishes a product reference, not an implementation architecture
or a verified latency guarantee. See research.md for the completed technical research.

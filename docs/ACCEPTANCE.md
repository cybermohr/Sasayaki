# Acceptance walkthrough

The portable build and offline tests are available. Physical keyboard behavior,
microphone conversion, Azure accuracy, representative application insertion, and
the two-second finish-to-visible target still require evidence. Do not treat a
successful SendInput count or startup smoke test as that evidence.

## Local checks

```powershell
pwsh -File scripts/Build.ps1 -Publish
pwsh -File scripts/Test-Acceptance.ps1 -Mode Offline
pwsh -File scripts/Test-Acceptance.ps1 -Mode InteractiveWindows
```

The interactive command opens an ordinary text field in a separate process. It
shows delivered key transitions, text-change observation times, foreground status,
and clipboard sequence/owner. It never reads clipboard contents or saves text.
The displayed clipboard metadata is a useful check, not proof of all format/history
requirements. Text-change timestamps are observations, not automated latency certification.

1. Launch `artifacts/win-x64/Sasayaki.exe`. Configure Azure in Settings using
   [the setup guide](../clone-run/AZURE-SETUP.md). Use Test Azure connections.
2. Focus the instrumented target. Hold Ctrl+Win, speak a known short phrase, and
   release. Check actual complete output, selection replacement, punctuation,
   no auto-submission, and no foreground change from the recording overlay.
3. Repeat with Win before Ctrl, both left/right variants, both release orders,
   a short single tap, and double-press start/stop. An isolated press during
   hands-free recording must not finish. Escape must cancel.
4. Check ordinary Ctrl+C and unrelated Ctrl+Win+another-key combinations. Before
   the 250 ms hold decision, an unrelated shortcut must cancel provisional audio.
   Check for Start menu popups and stuck Ctrl/Win state after each sequence.
5. Repeat in Notepad, a browser textarea/contenteditable, an editor, and a terminal
   with harmless text. Never use a command that would matter if submitted.
6. Change focus while speaking/processing. The currently focused target should
   receive text. Missing focus, app-owned focus, held modifiers, or a partial
   SendInput result must retain text and report a recoverable error.
7. Open tray → Review / retry retained text. Check the destination for a partial
   previous insertion first. Arm, focus a new field, then press/release Ctrl+Win.
   Escape while armed disarms without deleting retained text. Closing the review
   window without arming discards it. Recovery never retries automatically.
8. Check disconnect, invalid key, throttling, microphone removal, lock/suspend,
   and elevated target behavior. Late callbacks must never insert after cancellation.

## Clipboard and performance evidence

Prepare text, image, HTML, and file-list clipboard fixtures outside Sasayaki.
Compare format/content fingerprints before and after dictation using an external
fixture harness. Also verify clipboard history contains no dictated text and test
delayed-render ownership without materializing those formats. Deliberately copy
new data elsewhere during dictation and confirm it survives. These fuller fixture
checks are still manual; the target only samples sequence/owner metadata.

For performance, use the plan's 30 known utterances, each 5–30 seconds, including
cold/warm connections and both gesture modes. Capture the physical finish gesture
and actual rendered target with timestamped video or an instrumented target.
Record all failures and durations, p50/p95/max, region, and deployment. Every
measured typical-workload result must finish within 2,000 ms to satisfy the plan.
The overlay's reported time stops at Windows input submission and cannot certify
visible insertion.

## Live service smoke check

After saving settings, supply a nonpersonal, known utterance as raw PCM16
little-endian mono 24 kHz (no WAV header):

```powershell
pwsh -File scripts/Test-Acceptance.ps1 -Mode LiveAzure -FixturePcm C:\fixtures\known-utterance.pcm
```

The supplied fixture is sent to the configured Azure resources and consumes their
quota. Results go to ignored `TestResults/live-azure.json` without transcript or
audio content. Missing settings/fixture returns BLOCKED. A successful service check
does not establish microphone capture, editing fidelity, or visible insertion.
Use the four semantic fixtures in PLAN.md plus intentional emphasis, names, and
numbers during interactive testing. Do not save real personal speech as evidence.

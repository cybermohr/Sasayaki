# Sasayaki plan review log

Date: 2026-10-07. Reviewer: current Codex session, **self-review**.
No independent agent, second model, or external provider reviewed this plan.
Maximum rounds: 3. Completed rounds: 2.
This evaluates the specification's readiness for implementation, not live product
correctness, Azure access, model quality, or satisfaction of the latency target.

## Round 1

Plan SHA256: 7FB2A3FAB2E24D352CDD3CCF58CD1187C998BD42F76D2A282D2A9CC99D5929BB
Verdict: REVISE.

| Finding | Evidence / consequence | Disposition |
| --- | --- | --- |
| Provisional hotkey audio could be sent before an unrelated shortcut is recognized | Capture begins at chord-down, but modifier-only gestures are ambiguous | Keep candidate audio local until gesture classification; abort without upload |
| Recoverable insertion had no focus-safe retry interaction | Clicking the recovery UI moves focus into Sasayaki | Add explicit armed-retained-text mode; user refocuses target and invokes Ctrl+Win to insert |
| Queue bounds and network timeouts were vague | Unbounded or indefinite waits would undermine responsiveness | Specify a five-second converted-audio cap, connection timeout, and ten-second finish deadline; two-second target remains unchanged |
| Delayed clipboard rendering could invalidate the test itself | Reading delayed formats can make their owner render data | Test delayed ownership without forced rendering; distinguish intentional user clipboard changes |
| SendInput timing alone could falsely satisfy performance acceptance | API submission is not necessarily visible target insertion | Require instrumented target observations or timestamped capture and report all durations/errors |
| Tap-to-hands-free audio continuity needed an explicit rule | First tap audio could be repeated or lost | Preserve one logical session and append first-press audio once before resumed capture |

Rejected alternative: overwrite-and-restore the clipboard for compatibility.
Reason: violates the user's explicit normal-clipboard preservation requirement.
Rejected alternative: choose a different default hotkey to simplify implementation.
Reason: changes the requested interaction before attempting the native spike.

## Round 2

Plan SHA256: EA3BF3A38447E10B9787D020133B9A59C221302378EF8618E5099AEB6BD2F2DE
Verdict: APPROVED for implementation with the stated validation milestones.
Review mode: self-review, not independent approval.

Rechecked original requirements against the revised plan: both exact hotkey modes,
English-only Azure pipeline, faithful light cleanup, two-second complete insertion
target, focus-safe native interface, clipboard preservation, personal subscription,
and reproducible backend setup are represented with corresponding acceptance evidence.
Revisions address the material specification gaps found in Round 1.

Remaining engineering risks are explicit and must be tested before the app is
called complete: modifier suppression/replay on Windows, Unicode delivery across
controls, focus races, preview access, model fidelity, and full-pipeline latency.
None has been resolved by a live prototype during this documentation task.
The review does not waive these requirements or accept a reduced latency target.

## Documentation verification

- Both PowerShell code blocks in AZURE-SETUP.md parsed successfully with the local
  PowerShell AST parser. No network requests in those examples were executed.
- dotnet host is present but `dotnet --list-sdks` produced no SDK entries.
- Azure CLI was not found on PATH; the guide uses portal setup.
- Azure deployment, microphone streaming, cleanup inference, native-key tests,
  text insertion, application build, and latency tests: NOT RUN.

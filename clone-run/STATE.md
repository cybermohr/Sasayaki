# Cloneify run: Sasayaki

- Target: Wispr Flow core hotkey dictation workflow.
- Run root: C:/Users/bmohr/OneDrive - Microsoft/Documents/code2/Sasayaki
- Updated: 2026-10-07
- Phase 0 scope: complete; user requirements in scope.md.
- Phase 1 research: complete as documentation; live feasibility unverified.
- Phase 2 plan/review: complete; self-review approved in two rounds with explicit
  native-input and Azure validation milestones. No independent review claimed.
- Phase 3 handoff: complete; GOAL-PROMPT.md contains the scope, final plan, research,
  and backend setup instructions inline.
- Build: resumed after reboot; initial desktop build and portable package created.
  See BUILD-STATUS.md for authoritative build results and remaining acceptance.
- Authorization: user said 'go to work' after the research checkpoint; completed
  the requested research, planning, and reviewable handoff without further routine
  permission questions. This planning run does not imply Azure provisioning.
- Confirmed: native Windows 11, personal Azure subscription, English-only.
- Confirmed hotkey: Ctrl+Win hold/release, or double-press start/stop.
- Confirmed cleanup: remove fillers/repetitions, punctuate, resolve spoken
  corrections, preserve wording/meaning/tone.
- Confirmed latency: two seconds from finish gesture to visible cleaned insertion.
- Confirmed insertion: app-independent, private memory, current caret, no clipboard mutation.
- Selected implementation: C#/.NET 10 WPF; MAI-Transcribe-2-Streaming preview;
  Azure gpt-5.4-mini cleanup without reasoning; personal keys protected by DPAPI.
- Setup instructions: AZURE-SETUP.md; commands syntax-checked, live tests unrun.
- Next action: configure Azure deployments (user confirmed not set up yet), then
  perform live and physical Windows checks from docs/ACCEPTANCE.md. SDK exists in
  the local Sasayaki toolchain; build scripts locate it automatically.
- Remaining evidence: Windows gesture/insertion behavior, Azure subscription/model
  access, speech/cleanup accuracy, and measured latency. Never mark these passed
  based on documentation or mock results.

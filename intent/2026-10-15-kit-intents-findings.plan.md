# Plan: the intents spec and build step catch up with Optimer's adoption (APS-18)

Intent: [`2026-10-15-kit-intents-findings.md`](2026-10-15-kit-intents-findings.md) (intent approved and plan accepted by the owner 2026-10-15). Branch `aps-18-intents-findings`; one commit per group; PR with `## What` / `## Proof`. Check `gh api user --jq .login` = `ambysto` first.

## Groups
### 1. Spec (main session, HARD)
- `docs/spec/intents.md`, macOS notes only:
  - **Lifetime:**
    - Replace "30 s after the last intent" with what was observed: about 30 s in the 2026-10-14 spike; on 2026-10-15 only after the Shortcuts editor was closed (5 min 20 s with it open, 2 min after a run in a closed editor).
    - Add the senders table from Optimer's probe and the refusal that held (alive 2 min later).
    - Activity Monitor's Quit never reaches the delegate.
    - The Dock's Quit was not probed (Optimer has no Dock item).
    - Replace "proven in the first adopter, not here" with the proven rule: a Quit Apple event with no quit reason from `/usr/libexec/linkd` signed `com.apple.linkd`. Identify the sender by its audit token, falling back to its pid; an unknown sender is accepted.
  - **Launch role (new):** a launch by `linkd` is background-only; the menu bar item after a read intent does not respond (a known limit); the `.accessory` switch Optimer keeps, and what it did and did not fix.
  - **Build step:** the stale const values after an incremental build, and the fix. **Signing:** listing needs the signed app opened once; an older copy with the same bundle id and version may need `lsregister -f`.
- Proof: diff touches only the macOS notes; spec still names no Swift types outside them.

### 2. Script (main session, MEDIUM, same review)
- `scripts/app-intents-metadata.sh`: the stale-build message becomes "remove `<package>/.build/out/Intermediates.noindex/<Module>.build/<Config>` and rebuild (an incremental build leaves the const values behind)".
- Proof:
  - `bash -n`;
  - `scripts/check-app-intents-lint.sh` passes;
  - in a scratch copy, touching a source file shows the new message.

### 3. Records (EASY)
- Kit CLAUDE.md: the App Intents line and the APS-14 "Open" item (Optimer adopted: AO-21, `ambysto-optimer#29`). Intent status.

### 4. Independent review (reviewer)
- Groups 1–2 against Optimer's plan (group 5 table and owner-run results) for over-claims and drift.

## Risks
| Risk | How it is caught |
|---|---|
| A one-machine observation is written as a rule | Each note carries its date, macOS and Xcode version; review criterion 1 |
| The rule text drifts from the macOS notes | Criterion 2: the rules are unchanged |

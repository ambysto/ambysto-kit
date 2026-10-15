# Plan: the suite popover closes like a menu bar popover should (APS-19)

Intent: [`2026-10-15-kit-suite-popover-dismiss.md`](2026-10-15-kit-suite-popover-dismiss.md). Branch `aps-19-suite-popover`; one commit per group; PR with `## What` / `## Proof`. Check `gh api user --jq .login` = `ambysto` first.

## Groups
### 1. Popover behaviour (builder, then reviewer)
- `Sources/AmbystoUI/SuiteStatusItem.swift`, `SystemSuiteStatusItem`:
  - On open: `NSApp.activate()` (macOS 14 API), show the popover, then make its window key (`popover.contentViewController?.view.window?.makeKey()`), so transient closing and Esc work.
  - The item's toggle: set `popover.delegate = self`. `popoverDidClose` records the close time. `togglePopover` does not reopen when the popover closed less than 0.3 s ago, because that close came from the same click's mouse-down.
  - The 0.3 s rule lives in a small pure type, for example `PopoverToggle` with `shouldOpen(now:lastClose:)`, so it is tested with an injected time.
- No other file changes, except a test file in `Tests/AmbystoUITests`.
- Proof: `swift build -c release` 0 warnings, `swift test` green (+ the toggle tests), the suite bar grep list at zero hits.

### 2. Owner run (owner)
- Build DevBox and Optimer against this branch's commit, locally (`swift package edit` or a temporary pin in a scratch clone; no app PR needed for the check).
- Criteria 1–3 with DevBox hosting, then with Optimer hosting (quit DevBox and reopen it, so Optimer is elected).

### 3. Records (EASY)
- Kit CLAUDE.md "Open": the fix and that adopters pick it up with their next pin move. Intent status and results.

## Risks
| Risk | How it is caught |
|---|---|
| Activating the app steals focus from the user's frontmost app | It happens only on the user's click on the suite item, as any menu bar app does; owner run |
| The 0.3 s window swallows a real double click | A reopen needs a new click after 0.3 s; owner run criterion 3 |
| `makeKey` fails because the popover window is not created yet | Called after `show`; owner run criterion 2 |

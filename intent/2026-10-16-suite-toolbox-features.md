---
status: draft   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Pro Suite: the toolbox jobs, placed in the seven apps

Ticket: APS-21 (Plane). Parent: the suite strategy (`intent/2026-10-09-ambysto-pro-suite.md`), decision 8: **the suite grows by features, not apps**. Benchmark: the toolbox of IObit Advanced SystemCare 19 (owner screenshot, 2026-10-16), the IObit model the suite intent already names.

## Problem

A buyer who comes from a cleaner suite checks its toolbox against ours: 31 tools in one grid (uninstaller, RAM, internet booster, registry cleaner, shredder, context menu manager, disk doctor, auto shutdown and so on). Some of those jobs are real and fit the DNA (**measure first, change safely, always reversible**). Many do not: they promise gains nobody can measure, cannot be undone, or sit outside what the suite does.

Nothing written says which app owns which job. Without it, each app decides alone: a job lands in two apps (a process list in Optimer and DevBox), or a refused job comes back as a "small extra".

## Outcome

One placement table. Each toolbox job is exactly one of:

- **A. Covered:** an app already does it; no new work.
- **B. New feature of one app:** written later as that app's own intent, in that app's repo and Plane project (suite decision 7).
- **C. Later:** not decided now.
- **D. Refused:** with the reason, in the voice of the landing's "What we won't do".

### A. Covered (8)

| Toolbox job | Owner | Feature that already does it |
|---|---|---|
| Uninstaller | Lean | Uninstall with leftovers; every move journaled and restorable |
| Large file finder | Lean | Large and old files |
| Duplicate file finder | Lean | Byte-identical duplicates |
| Disk cleaner | Optimer | Cleanup of regenerable caches only; personal files unticked until ticked |
| Process manager | Optimer, DevBox | Optimer: per-app memory, CPU, disk. DevBox: listeners, port, project. Neither force-quits; DevBox stops only after confirmation |
| RAM "booster" | Optimer | Measures memory pressure and the apps behind it. No "Free RAM" button (landing refusal) |
| Internet booster | Steady | Network tweaks as switches, original value captured, restore |
| DNS protector | Steady | DNS benchmark (diagnostic 6), `dns_fastest` measured tweak with restore (ADR-0015, ADR-0016), `dnswatch` alerts on DNS changes |

### B. New features (8), one owner app each

Every mechanism below is a **candidate**: the owner app's plan proves it on the target OS versions, and a mechanism that does not behave as documented is dropped, not guessed (Tune v1's rule).

| # | Toolbox job | Owner | The feature | Kind |
|---|---|---|---|---|
| B1 | Context menu manager | Tune | Every right-click entry: who added it and what it runs. Switch off one entry for this user; the original is captured and restored | reversible change |
| B2 | Program deactivator (startup items, services) | Tune, Windows | Tune's login-items module on Windows: Run and RunOnce keys, the Startup folder, logon tasks in Task Scheduler, services' start type | reversible change; read-only where admin is needed (decision 2) |
| B3 | Default program | Tune | Which app opens each file type and URL scheme. Mac: change it as a tweak, previous handler captured. Windows: read-only, plus a button to the Settings page | Mac: reversible change. Windows: read |
| B4 | Empty folder scanner | Lean | Empty folders inside Lean's allowed roots (a folder holding only `.DS_Store` or `desktop.ini` counts as empty). A Lean row like any other: tick, Trash, journal | reversible (Trash + journal) |
| B5 | Shortcut fixer | Lean | Aliases, symlinks and `.lnk` shortcuts whose target is gone; Dock and Start menu entries for removed apps. Tick, Trash, journal. A login item that points at a removed app is shown with a link to Tune | reversible (Trash + journal) |
| B6 | Disk doctor | Lean | Disk health, read-only: SMART status, NVMe wear (percent used), temperature, error counts, the last file-system check. Repair is explained and opens the system's own tool (Disk Utility First Aid, `chkdsk` through Windows' tool); Lean never runs it | read |
| B7 | Auto shutdown | Power | A one-shot the user starts: sleep, shut down or restart after a duration, at a time, or when a keep-awake session ends. Visible countdown, one-click cancel, nothing recurring by default. Shutdown asks apps to quit normally, so an app with unsaved work can stop it | user-started change |
| B8 | System information | Optimer, or the suite hub (decision 1) | A read-only machine profile: model, CPU, memory, disks, GPU, OS version and build, uptime; battery summary when Power is installed. "Copy as text" leaves out the serial number and user name unless the user ticks them | read |

Platform notes for the plans:
- **B1, Mac:** Finder Sync and action extensions through the per-user extension election (`pluginkit`); the Services menu through the user domain `com.apple.ServicesMenu.Services`. **B1, Windows:** the per-user shell-extension block list and `LegacyDisable` on static verbs. Entries registered machine-wide are listed read-only (decision 2).
- **B2:** per-user entries need no admin. Services and machine-wide Run keys and tasks need one.
- **B3, Mac:** LaunchServices' public API, not the `com.apple.LaunchServices` preference domain that Tune's never-touch list protects (decision 3). **B3, Windows:** `UserChoice` is hash-protected, so Ambysto does not write it.
- **B4, B5:** every deletion goes through the Kit's `AmbystoSafety` gate (never-touch, identity by device + inode, in-use check) and Lean's allowed roots. Folders that apps keep empty on purpose (inside `.git`, app containers, `~/Library` app folders) are never-touch suffixes in Lean's spec.
- **B6, Mac:** `diskutil` SMART status and IOKit, without root. NVMe detail that needs root is not shown and the spec says so. **B6, Windows:** `Get-PhysicalDisk` and `Get-StorageReliabilityCounter`; any counter that needs admin is marked.
- **B7:** the same model as keep-awake (Power's intent): a session the user starts, with guard rails, never a change Power makes on its own. Mac: a sleep request needs no root; shutdown and restart go through the system's normal quit flow. Windows: `shutdown /s|/r /t`, no admin.

### C. Later (3, not decided now)

- **Win Fix** (repairing common OS problems): only as guided steps, needs admin, high risk. Revisit after B2's elevation decision.
- **Portable version:** Steady's unzip-and-run desktop mode (ADR-0019) is the precedent; decide per app when its Windows build exists.
- **System control** (one place for hidden OS settings): Tune already names the setting behind every switch and opens the exact system pane for privacy. A launcher of OS panes on its own measures nothing; revisit if Tune's users ask for it.

Not a toolbox job, noted for the plans: Steady already lists network drivers and the versions available for rollback. Updating drivers stays refused (D).

### D. Refused (12)

The "Free RAM" button is not a row here: Smart RAM's job is covered by Optimer's measuring (A), and the button itself is a landing refusal.

| Toolbox job | Why not | Landing line it falls under |
|---|---|---|
| Malware scanning | Suite decision 6 | "Scan for malware" |
| Driver updates | Suite decision 6: updaters are what Defender flags and users distrust | "Scan for malware … Nor do we update your drivers" |
| Software updates | Suite decision 6 | same |
| Registry cleaner | No gain anyone can measure, real risk of breaking an app | "Sell a boost" |
| Registry defrag | Same, and it needs a reboot and admin | "Sell a boost" |
| Disk defragmenter | No effect on SSDs and adds wear; Windows already optimizes drives on a schedule | "Sell a boost" |
| File shredder | Cannot be undone, the opposite of the DNA. On SSDs (TRIM) and APFS (copy-on-write), overwriting is not guaranteed to erase, so the promise cannot be measured. Lean's intent already excludes secure erase | "Make a change you cannot undo" |
| VPN | A network service, not a utility on the machine; needs servers and an account | outside the suite |
| Data recovery (undelete) | Results cannot be promised. Lean's journal and the Trash are the suite's way back | outside the suite |
| Password manager | Outside the suite | outside the suite |
| Face login, protected folder | Security products, outside the suite; they would hold user secrets | outside the suite |

### Success criteria (measurable)

| # | Criterion | How it is checked |
|---|---|---|
| 1 | Every one of the 31 jobs in the benchmark toolbox maps to exactly one row of A to D (8 + 8 + 3 + 12), and no job sits in two owner apps (except A's process list, where the two apps show different facts) | review against the screenshot's list |
| 2 | Each B row has its own intent in its owner repo and a ticket in that app's Plane project (ATN Tune, ASP Lean, APW Power, AO Optimer), linked to this ticket | Plane links |
| 3 | Each B change (B1, B2, B3, B7) and each Trash feature (B4, B5) has a restore proof in its app's plan: after apply and restore, a read equals the captured original (value-equal for preferences and registry values; path and identity for a Trash restore) | the app's proof log |
| 4 | No B feature needs root on macOS. Any Windows admin need is written in its app intent, with the elevation model, before building | review of each app intent |
| 5 | Every B feature is Free for its job; Pro adds only conveniences (history, export, presets), per the landing's Free and Pro lines | review of each app intent |

## Constraints

- Suite DNA and the landing's refusals. Safety is never behind the key.
- macOS first per app, Windows after, from each app's `docs/spec/` (suite decision 4).
- No new app and no new Kit library for these jobs. A shared need that shows up in two app plans goes to the Kit through its own intent.
- B4 and B5 are deletions: the Kit's safety gate is HARD (`CLAUDE.md`), so their plans get an independent review.

## Out of scope

- Building any feature: each is its own app intent.
- Prices and tiers beyond criterion 5.
- The suite hub window itself (decision 1).
- Changing the landing (decision 5 only says whether to).

## Open decisions (owner)

1. **Suite hub.** On 2026-10-16 a preview window "Ambysto Pro Suite" was mocked outside this repo (a local HTML preview, sample data only): a sidebar with the seven apps, and one read-only scan across them. The suite strategy says "no single mega-app". Is a hub allowed that only launches the apps and reads their glances, and never changes anything itself? If yes, it gets its own intent and B8 lives there. If no, B8 goes to Optimer and the mock stays a demo. **Recommendation:** allow it as launcher plus glances, read only. Every change happens in the owner app.
2. **Windows admin in Tune** (B2, part of B1). Tune macOS v1 refuses anything that needs an administrator. On Windows, services and machine-wide entries need one. The options are: list them read-only (the same rule as v1), or use Steady's per-machine install with an elevated helper (ADR-0019). **Recommendation:** read-only in Tune's first Windows version.
3. **Default apps on Mac** (B3). Confirm that LaunchServices' API is allowed, and that macOS's own confirmation prompt (for the default browser) is acceptable.
4. **Shutdown in Power** (B7). Power's position is that it never changes how the Mac sleeps on its own. A timer the user starts follows the keep-awake model. Confirm, and confirm that shutdown and restart are in, not only sleep.
5. **Landing.** Add a "Clean the registry, shred files" line to "What we won't do"?
6. **Order.** Suggested, cheapest reversible work first: Lean B4 + B5 → Tune B1 (Mac) → Lean B6 → Power B7 → Tune B3 → B8 (after decision 1) → Tune B2 (with Tune's Windows version).

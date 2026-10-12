# The merged suite bar

Platform-neutral. The macOS implementation lives in this repository (see the macOS notes). Contract version **1**. Test vectors: `suite-bar-vectors.json`.

When two or more Ambysto apps run, they show **one** readout for the whole machine instead of one icon each. Every running app publishes a small glance record into a shared folder; one of them, the **host**, shows the readout: a menu bar item (notification-area icon on Windows) with a popover, and an optional floating bar. With one app running nothing changes.

## The suite catalog

| Order | App id | macOS bundle id | Name | Participates (contract 1) | Can host |
|---|---|---|---|---|---|
| 1 | `optimer` | `com.ambysto.optimer` | Ambysto Optimer | yes | yes |
| 2 | `steady` | `com.ambysto.steady` | Ambysto Steady | only with the shared group folder | no (a window app) |
| 3 | `space` | `com.ambysto.space` | Ambysto Space | no (runs only while open) | no |
| 4 | `devbox` | `com.ambysto.devbox` | Ambysto DevBox | yes | yes |
| 5 | `power` | `com.ambysto.power` | Ambysto Power | yes | yes |
| 6 | `tune` | `com.ambysto.tune` | Ambysto Tune | no (runs only while open) | no |
| 7 | `guard` | `com.ambysto.guard` | Ambysto Guard | yes | yes |

The order is the display order everywhere. A new app is added at the end in a minor contract release.

## The shared folders

| Folder | Who uses it |
|---|---|
| **Plain folder**: the user's application-support folder + `Ambysto/Suite/` | every app that is not sandboxed (contract 1: all participants) |
| **Group folder**: the shared container of the publisher's team, `<team id>.com.ambysto.suite` | sandboxed apps, once a team id exists |

An app **publishes into every suite folder it can reach** and **hosts only while it holds the host lock in every one of them**, so apps on different folders can never elect two hosts. Each folder holds:

```
glances/<app id>.json     one record per app, written only by that app
host.lock                 an empty file; the host-elect holds an exclusive lock on it
settings.json             the readout's choices, written only by the current host
```

Files are written atomically (write a temporary file, then replace). **No file is ever deleted**: an app that quits writes `"state": "stopped"`.

## The glance record

JSON, UTF-8, an object, **at most 2,048 bytes**.

| Field | Rule |
|---|---|
| `contract` | integer, the major version: `1`. A reader skips a record with a higher major. Later minors only add fields; a reader ignores fields it does not know. |
| `app` | an app id from the catalog; it must equal the file name without `.json` |
| `hosting` | boolean: `true` while this app holds the lock and its readout is shown |
| `state` | `running` or `stopped` |
| `at` | number, seconds since 1970-01-01 UTC, when written |
| `ttl` | integer seconds, 3 to 60 |
| `level` | `ok`, `notice` or `alert`: the app's overall state |
| `metrics` | array of 0 to 4 metrics, **in display order** |

A metric:

| Field | Rule |
|---|---|
| `id` | 1–24 characters from `a–z`, `0–9`, `-`; unique within the record |
| `label` | 1–16 Unicode scalars |
| `value` | a finite number with at most 2 decimals, **or** a state word from the closed list: `on`, `off`, `active`, `idle`, `paused`, `blocked`, `allowed`, `ok`, `warning`, `error`, `none` |
| `unit` | 0–6 Unicode scalars (`%`, `ms`, `GB`, …); may be empty |
| `level` | `ok`, `notice` or `alert` |

**Characters** in `label` and `unit`: no control characters (Unicode category Cc), no bidirectional formatting characters (U+061C, U+200E, U+200F, U+202A–U+202E, U+2066–U+2069), no `/` and no `\`.

A record that breaks any rule is **invalid**: the host skips it, and a publisher refuses to write it (and logs that once).

**Numbers and states only.** A glance never carries a path, an app or process name, a host name, a port owner, an account or anything else a user would consider private: every local process of the user can read the folder. The closed state-word list and the character rules limit what fits, but no validator can recognise a name inside a label, so labels are fixed strings in each app's code, in the app's language, and every app's adoption is reviewed against this rule. The host formats numbers in the user's locale (decimal separator, grouping); the publisher sends raw numbers, rounded to 2 decimals before they are written (so `0.1 + 0.2` is written `0.3`), and a unit.

## Reading the folder safely

A reader:
1. accepts only files named `<catalog app id>.json` (temporary files and anything else are ignored);
2. opens without following links and without blocking, and reads only a **regular file owned by the current user**;
3. reads at most 2,049 bytes; a longer file is invalid.

## Freshness

A `running` record is **fresh** while `at − 2 s ≤ now ≤ at + ttl`. A record from the future (more than 2 s ahead: the clock was set back) is **stale**, so a crashed app's last numbers can never stay on screen.

A publisher rewrites its record when a value changes, at least every `ttl / 2` seconds even when nothing changed (heartbeat), and immediately after the computer wakes from sleep.

A stale or `stopped` record means that app is **not running** for the suite: its row shows "not running" with no numbers for 60 s, then disappears.

**Expired numbers.** The heartbeat proves the app runs, not that its numbers are current. When an app has not published (even unchanged values) for **`staleAfter`** seconds (default 3 × ttl, never below the ttl; an app with a slow refresh sets its own, e.g. 150 s for a 60 s refresh), its record is written **without metrics**, keeping its last level, until it publishes again. The row stays (the app runs) but shows no numbers. After a sleep longer than `staleAfter`, the first heartbeat on wake has no numbers until the app publishes. A value the validator refuses is never the source of a later record. An app whose interval between two publishes is longer than its `staleAfter` would blink between numbers and none; implementations log that once (a sleep or a stalled refresh alone does not trigger the log).

## Active

The readout is **active** when at least **two participants** have a fresh `running` record. It becomes inactive only after fewer than two have been fresh for **10 s** without a break, and for 10 s after waking from sleep it never becomes inactive. Space and Tune never count in contract 1.

## The host

- Every participant that can host waits for the exclusive lock on `host.lock` of every suite folder it can reach, **one folder after the other in sorted path order** (so two apps can never each hold one lock and wait for the other), for as long as it runs (one blocking wait, no polling). If a lock file cannot be opened, it tries again every 5 s. The lock file is opened without following links, created with owner-only permissions, and never inherited by child processes.
- After getting the lock, the app checks that the file it locked is still the file at that path (same device and file number); if it was replaced it lets go and waits again.
- The lock holder is the **host-elect**. It shows the readout only while the readout is active, and publishes `"hosting": true` only while its menu bar item is actually shown.
- The operating system releases the lock when the holder quits or crashes, so the next app takes over at once.
- A clean quit, in this order: remove the menu bar item, publish `stopped`, release the lock. Two items never show at once.

## Each app's own icon and floating bar

An app shows **its own menu bar icon** when:
- the readout is not active; or
- the user turned on "Show my own menu bar icon" in that app; or
- the readout is active but no fresh record has said `"hosting": true` for 5 s (something that is not a participant holds the lock, or the host's item cannot be shown).

The host's own icon follows the same rule. When the user removes an app's own icon from the menu bar by hand, that counts as turning "Show my own menu bar icon" off, and the icon stays away until the app is started again (the app is not put back against the user's hand, even while the readout is not active).

While the readout is active, every app hides **its own floating bar** and releases its own floating-bar shortcut; the merged floating bar and the suite shortcut replace them.

## The readout

- **Menu bar item:** the Ambysto mark plus up to two values the user picks. Default: Optimer's first metric when Optimer runs, else the first metric of the first participant in catalog order, else the mark alone. Tinted by the worst level among fresh records; `notice` and `alert` also add a glyph, so colour is never the only signal.
- **Popover:** one row per participant in catalog order (fresh rows with their metrics; "not running" rows without numbers), an **Open** button per running app, the choice of the two values, "Show floating bar", and the "Ambysto apps" list (installed apps can be opened; others appear there only when a link exists, and never in the readout).
- **Merged floating bar:** the same rows, compact (one line per app) or expanded; off by default. The suite shortcut **Control-Option-M** toggles it; only the host registers it.
- **settings.json** keeps the two chosen values, whether the floating bar is shown and expanded, its top-left position, and the menu bar item's position. Unknown keys, and known keys whose value has an unexpected type, are kept unchanged (integers exactly) when the file is rewritten.

## Adopting the suite bar

An app that joins the readout must:
- publish fixed labels and numbers or state words only (reviewed for names on adoption);
- choose a `staleAfter` that fits its refresh rhythm;
- bind its own menu bar icon to the participant's "show own icon" state;
- open its main window on **Open**;
- **show its main window when it is opened again while running and its own icon is hidden** (on macOS, `applicationShouldHandleReopen`), so it is always reachable;
- hide its own floating bar and release its own shortcut while the readout is active.

## Open

**Open** sends the system-wide notification `com.ambysto.suite.open` whose only content is the target app id (no other data), delivered immediately even to background apps. The target app opens its main window. Any local process can send it; the worst it can do is bring a window forward.

## Windows notes

- Plain folder: `%LOCALAPPDATA%\Ambysto\Suite\`; no group folder.
- Host lock: an exclusive `LockFileEx` on `host.lock`, held by a waiting thread; the system releases it when the process ends.
- Watching for changes: `ReadDirectoryChangesW` on `glances\`.
- Open: a named event per app id (`Local\Ambysto.Suite.Open.<app id>`).
- The readout is one notification-area icon; the same clutter problem exists there.

## macOS notes

- Plain folder: `~/Library/Application Support/Ambysto/Suite/`. Group folder: `~/Library/Group Containers/<team id>.com.ambysto.suite/`.
- Lock: `open(O_RDWR | O_CREAT | O_NOFOLLOW | O_CLOEXEC, 0600)`, then `flock(LOCK_EX)` in a dedicated thread; replaced-file check with `fstat` against `lstat`. Reading: `open(O_RDONLY | O_NOFOLLOW | O_NONBLOCK | O_CLOEXEC)` and `fstat` (`S_ISREG`, owner uid).
- Watching: a vnode dispatch source on `glances/` (an atomic write is a rename into the directory), plus a re-read every 5 s to expire stale records.
- Open: `DistributedNotificationCenter`, object = app id, no user info, `deliverImmediately: true`; observers use `suspensionBehavior: .deliverImmediately`. Wake: `NSWorkspace.didWakeNotification`.
- Menu bar: the host's item is an `NSStatusItem` with autosave name `AmbystoSuite`; an app's own icon is its `MenuBarExtra(isInserted:)`.
- "Shown" (for `hosting`): the item's `isVisible` flag is set (the user has not taken it out of the bar) and its button has a non-empty window on a screen. Occlusion does not count: an auto-hidden menu bar, a full-screen app, a locked screen or a sleeping display still leave the item in the bar. **Not detected and not tested:** an item hidden by macOS's own menu bar settings, or clipped by the notch, while its window still exists. The 5 s no-host fallback covers only what makes "shown" false.
- **Spikes (2026-10-11, macOS 26.6.2, Apple Silicon):**
  1. A distributed notification posted with `deliverImmediately: true` reached two accessory (menu bar) apps that were not active within 1 ms, including one observing with the `.drop` behaviour.
  2. `MenuBarExtra(isInserted:)` removes and re-inserts a `.window`-style item every time. SwiftUI writes the binding three times per change with the **same** value, so only a write that differs from the current value is a user action.
  3. A second process blocked in `flock(LOCK_EX)` got the lock 16 ms after the holder received `SIGKILL`.
  4. On macOS 26 every menu bar item's window belongs to Control Center, so `CGWindowListCopyWindowInfo` cannot tell which app owns an item. The Accessibility attribute `AXExtrasMenuBar` of an app (its children are its items) can, and is what the proof script counts; it needs Accessibility permission for the terminal that runs it.
- **The suite shortcut and other apps:** Carbon hot keys are non-exclusive across processes (`CarbonEvents.h`): another app that registers Control-Option-M receives the same press, and registration still succeeds. While another app holds the key *exclusively* (`kEventHotKeyExclusive`), a non-exclusive registration is still accepted but receives no presses, and this cannot be detected. Registration fails only for a duplicate in the same process (for example an app's own Control-Option-M not yet released); the host then logs it once per run. The suite does not register exclusively, so a frozen host can never block the key.


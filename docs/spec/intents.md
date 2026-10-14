# Intents: answers and actions outside the app

Platform-neutral. Each Ambysto app exposes a few of its answers and actions to the system, so a user can reach them from Shortcuts and Spotlight (and Raycast through Shortcuts, not yet tried) without opening the app. Each app defines its own intents in its own code; the Kit holds this spec and will hold the build step and the metadata lint (plan group 3, not built yet). Decisions: `intent/2026-10-12-kit-app-intents.md` and its plan (spike records 2026-10-12 and 2026-10-14).

## Kinds

Every intent is one of two kinds, fixed in its declaration:

| Kind | What it may do | What it must not do |
|---|---|---|
| **Read** | Measure and answer. | Change anything; open a window. |
| **Opens the app** | Bring the app forward on the step where the user confirms the action. | Perform the action itself. |

The intent document allowed a read intent to start something the app itself runs without a confirmation; this spec lists none, and adding one is a change to this spec.

Anything that deletes, moves, stops a process or changes a setting is an **opens the app** intent. The action it leads to runs only through the app's own confirmation step:
- The call that performs the action takes a token that only that confirmation step creates. The token is bound to the exact items shown and is used once.
- A new request never changes a confirmation already on screen; it waits until that one is confirmed or cancelled.
- Intent parameters only pre-select. The app measures again before it shows the step and acts only on what it measured itself (for example, never on a process id passed in).
- A confirmation dialog shown by the system (Shortcuts or Spotlight) never stands in for the app's own step.

So the suite's rule (measure first, change safely, always reversible) holds from every entry point.

Each app keeps an allow-list of its **read** intents in its own repository. Adding an intent to the list is reviewed like code, which means reviewing that intent's code. The build fails when an intent that is not on the list does not open the app (the metadata lint).

## Naming

- Title: what the user asks or does, then `(Ambysto <App>)`. An action starts with a verb ("Clean regenerable caches (Ambysto Optimer)"); an answer names what it measures ("Memory in use (Ambysto Optimer)"). `<App>` is the name from the suite catalog (`suite-bar.md`).
- At most one spoken phrase per intent, containing the app's name.
- English only, like the apps.

## Answers

- A short sentence the user can read ("Memory in use: 63%").
- Where a shortcut can use it, also the value: a number with its unit, or a list of names.
- A refusal is a sentence the user can act on ("Optimer needs Full Disk Access to measure this."), never an error code.

## Privacy

- Answers are computed on the Mac when the intent runs. No network, no telemetry.
- The app donates nothing and indexes nothing of its own. The system itself records each run of an intent (on macOS, a per-app activity stream), so the spec does not promise that a run leaves no trace on the Mac.
- A local answer may name apps (owner decision 3, 2026-10-12). It is never written to the suite folders. The owner confirmed it on 2026-10-14, knowing the system keeps its own record of each run on the same Mac.

## Launch and lifetime

- An intent may launch the app. The app starts as on any launch (its own icon or the suite readout).
- The system may ask an app it launched for an intent to quit once its intents are done. The app **accepts** only when no window of it is open and no confirmation is pending. Otherwise it **refuses** and keeps running as if the user had launched it. The app's own menu bar icon does not count as an open window (owner decision, 2026-10-14).
- The app never refuses to quit for a log-out, restart or shut-down, nor when the user chooses Quit. When the app cannot tell where a quit came from, it accepts.
- A request to open the app on a confirmation step is held in the app's own process until the app can show it, so a cold launch does not lose it. It is never passed through a channel another process can send on.

## Windows notes

Not built. The equivalents are jump-list tasks or command-line verbs; the kinds, naming, answers and privacy rules above apply unchanged.

## macOS notes

- **Framework:** App Intents. Shortcuts from macOS 14 and Spotlight from macOS 26 by Apple's documentation; the spikes ran on macOS 26.6 and 27.2 only.
- **Declaration:** every intent conforms to `AppIntent` **directly**, with a literal `static let title` and, for an **opens the app** intent, a literal `static let openAppWhenRun = true`. The metadata is extracted from literals in the app's own source: a value supplied by a protocol extension is not seen, and in the 2026-10-14 spike an intent declared through such a protocol failed at run time (its type was not found in the release binary). The Kit therefore defines no intent types.
- **Build step:** `swift build` (Swift Build) emits the const values with no extra flags; `appintentsmetadataprocessor` runs once with the arm64 triple for a universal binary; `appintentsnltrainingprocessor` needs all its flags to archive the phrase assets; the output goes to `Contents/Resources/Metadata.appintents`. The app registers its intents when it is launched once.
- **Signing:** intents run only in a build signed with a team identifier. An ad-hoc build lists its actions in Shortcuts, but every run fails (`linkd` refuses to register the process: "Unable to get teamId"). An app ships intents only in its Developer ID build. Not yet checked: a `.dmg` install with quarantine and App Translocation (waits for a Developer ID, notarized build).
- **Lifetime:** `linkd` tracks an app it launched for an intent. 30 s after the last intent it logs "Idle grace period elapsed" and sends a Quit Apple event, which reaches `applicationShouldTerminate`. An app launched by Spotlight was reused and not tracked. A Quit from the Dock or Activity Monitor is also a Quit Apple event. How the app tells the system's quit apart (the event's quit reason or sender) is proven in the first adopter, not here; until it can, the app accepts. Until then the app never refuses, so a cold-launched app can lose an open confirmation to the system's quit 30 s after the intent; nothing is deleted (the action still needs the token), and the user runs the intent again.
- **Confirmation router:** an in-process object registered when the app starts. It holds a request until the app's window scene exists, then opens the confirmation step. No URL scheme and no distributed notification: both can be sent by any local process.
- **Activity stream:** each run is written by `linkd` to a Biome stream named after the bundle id. Whether it holds the answer text is not known.
- **Spotlight donation:** each run in the 2026-10-14 spike logged a failed Core Spotlight donation (`CSIndexErrorDomain -1000`) from the framework, not from the app. Not investigated.

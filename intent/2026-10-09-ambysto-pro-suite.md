---
status: approved   # draft → approved → planned → building → verified → shipped (PR #n)
---
# Ambysto Pro Suite: bundle strategy

## Problem
A single narrow utility is hard to sell on its own; a suite of focused tools with a deep bundle discount is how utility vendors grow average order value and retention. Ambysto already has two products with the same DNA (**measure first, change safely, always reversible**):
- **Ambysto Steady:** network stability (Windows + Apple).
- **Ambysto Optimer:** system resources and safe cleanup (macOS, Windows planned).

They are built and licensed separately, with no shared foundation, no cross-sell and no bundle.

## Benchmarks to follow (owner, 2026-10-09)
| Vendor | Model to learn from |
|---|---|
| **CleanMyMac** | One flagship app with many modules; polished brand; free scan → paid clean |
| **MacPaw** | Portfolio of separate apps (CleanMyMac, Gemini, ClearVPN, …) plus Setapp as a subscription channel |
| **IObit** | Many single-purpose Windows utilities, sold alone and in heavily discounted packs, with in-app cross-promotion |
| **Parallels Toolbox** | Dozens of small tools in one app, subscription |

Ambysto's choice: **separate native apps that share one license and one kit**, like IObit / MacPaw. Each app also keeps a "toolbox" depth of features like CleanMyMac. No single mega-app.

## Outcome
1. **Ambysto Pro Suite:** a bundle in which one license key unlocks Pro in every app listed in its `products` field. Each app also sells alone.
   - **Suite now:** Optimer, Steady and **Space** (the next app: disk map, large/old files, duplicates, uninstall with leftovers; reuses Optimer's safety gate).
   - **Later candidates:** DevBox (developer ports, servers, AI agents), Tune (reversible system tweaks), Power (battery / thermal), Clip (clipboard), Guard (per-app network, needs Developer ID).
2. **Ambysto Kit:** a shared Swift package (and later a matching .NET library for Windows) with:
   - license verification (format in AO-5);
   - brand tokens (violet `#6B5CE8`, axolotl coral `#ED708F`, ink, rounded numerals);
   - the floating mini bar framework;
   - "Ambysto apps" discovery and cross-launch;
   - shared Settings sections.
3. **Suite-only perk (a reason to own several apps beyond the discount):** when two or more Ambysto apps are installed, their floating mini bars **merge into one machine-health bar** (memory/CPU from Optimer, ping/loss from Steady, free space from Space).
4. **Cross-sell at the right moment:** the app suggests the sibling that solves what it just measured, for example Optimer showing low disk → "Open Ambysto Space". Never an ad and never a nag beyond the Free reminder rules.

**Success criteria:**
- One key with `products: [optimer, steady, space]` unlocks all three apps offline.
- Installing two apps shows a single merged mini bar.
- Every app ships its design tokens and license verification from Ambysto Kit, not copies.

## Constraints
- Native per platform (SwiftUI on Apple, WinUI on Windows). Offline license check; no telemetry or phone-home.
- Every app follows the safety DNA: no root unless unavoidable and explicit; reversible changes; measure before change.
- macOS first; the Windows Kit follows the spec, not the Swift code.

## Out of scope (separate tickets)
- Prices and discount levels (AO-11 market research).
- Payment (AO-14).
- Policies (AO-13).
- The landing page (AO-12). It will present the suite.

## Decisions (owner, 2026-10-09)
1. **Ambysto Kit is its own repo:** `ambysto/ambysto-kit` (this repo). It also hosts suite-level docs.
2. **Plane is split by project:** **APS** (Ambysto Pro Suite: strategy, Kit, research, policies, payment), **ASP** (Ambysto Space), **AO** (Ambysto Optimer). A Steady project will be added when its work is tracked here.
3. **Steady has no license yet**, so it is not migrated now; it adopts the shared key format when its Pro tier is defined.
4. **macOS first for every utility**, then Windows. This applies to Space and to all future apps.
5. Next: run the `bto-researchmarket` playbook on suite and bundle vendors (CleanMyMac, MacPaw, IObit, Parallels Toolbox first), including a **value × functionality** positioning chart.
6. **Suite lineup and order** (owner, after the APS-3 research): **Space → DevBox → Power → Tune → Guard**, joining Optimer and Steady.
   - Guard waits for Developer ID (AO-6), because it needs Network Extension.
   - **Dropped:** malware scanning and app/driver updaters. They fall outside the suite's DNA, and updaters are what Defender flags and users distrust.
   - **Deferred:** Clip, because macOS Tahoe's clipboard history makes it likely Apple ships the same feature.
   - With all seven apps, suite functionality reaches about 11.5 of 15, above CleanMyMac's 10.5.
7. **One repository per app:** `ambysto/ambysto-<app>`, with `macos/` first and `windows/` later in the same repo, sharing `docs/spec/`.
   - Each app gets its own Plane project.
   - A repo and its Plane project are created only when work on that app starts.
   - Apps depend on **Ambysto Kit by semver tag** (SwiftPM `from:`). Cross-app contracts (license format, merged mini bar protocol) live in the Kit's spec and change only with a Kit version bump.


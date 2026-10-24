---
status: approved   # approved by owner on 2026-10-18; building follows
---

# Fair pricing and honest copy: pay only when you are happy

Ticket: APS-22 (Plane). Related: APS-3 (market research on competitor auto-renewal complaints), APS-21 (toolbox features), AO-12 (suite landing).

Owner directive 2026-10-18:
> *"Stress the message: we only charge when you feel happy enough to pay. It appears on the website and in the app."* (translated from the owner's Vietnamese)

## Problem

Utility suites (IObit, CCleaner, MacKeeper, CleanMyMac) rely heavily on anxiety-driven marketing:
- Countdown timers and fabricated "85% discounts".
- Hostage features: locking essential cleanup/safety tools behind a paywall.
- Auto-renewals that bill unexpectedly at higher rates (the #1 paying user complaint in APS-3, ~87/275 1-star reviews).
- In-app spam: paying users continue to be nagged with cross-sell popups and upsells (~45/275 reviews).

Ambysto's brand philosophy is built on honesty, verifiable metrics, and mutual respect. Users must never feel tricked, coerced, or rushed into buying.

## Outcome

Establish the core brand promise across all public web landings and app interfaces:

> **"We only charge when you are happy to pay."**
> Everything needed to do the job safely is free forever. Pro adds conveniences and peace of mind. No subscriptions, no auto-renewals, no forced paywalls, no guilt trips.

## Scope of changes

1. **Suite Landing (`ambysto-kit/site/index.html`):**
   - Add to "What we won’t do": No forced paywalls, no auto-renewal, no guilt trips.
   - Update Pricing section (`#get`): Headline and lede emphasizing *"We only charge when you are happy to pay"*.
   - Clarify the Free vs Pro boundary: Free cleans, measures, and restores forever. Pro is for conveniences and supporting independent development.
2. **Ambysto Lean Landing (`ambysto-space/site/index.html`):**
   - Update `#price-title` section: *"Reclaiming disk space is never held hostage. Free cleans everything safely. Only pay for Pro if Lean brought you peace of mind and you're happy to support us."*
3. **Ambysto Optimer Landing (`ambysto-optimer/site/index.html`):**
   - Update `.receipts` table: Emphasize perpetual license, no nagging, pay once only when genuinely happy with performance gains.
4. **Ambysto Steady Landing (`steady/site/index.html`):**
   - Emphasize transparent network diagnostics: No snake-oil speed boosts, no hidden recurring fees.
5. **In-App Copy (`AmbystoKit` & App specs):**
   - `ReminderPolicy.message`: Update the quiet Free reminder banner to state:
     *"Ambysto is free to use forever. Only upgrade to Pro if you're happy with what it does and want to support our work."*

## Done when

- [ ] Intent committed on `ambysto-kit` and linked in plan.
- [ ] `ambysto-kit/site/index.html` updated and copy checked.
- [ ] `ambysto-space/site/index.html` updated.
- [ ] `ambysto-optimer/site/index.html` updated.
- [ ] `steady/site/index.html` updated.
- [ ] No banned marketing words (copy-lint passes).

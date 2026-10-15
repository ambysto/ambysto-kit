---
status: superseded   # 2026-10-15, see the note under the title; the old text stays below
---

# Suite landing (AO-12)

> **Superseded 2026-10-15.** This spec describes the first plan: a waitlist page with a price question, no Buy action and no screenshots. Do not build from it. The owner replaced it with two decisions:
> - **Brand record, 2026-10-12:** the page is written as if all seven apps are finished and can be bought. It shows a price placeholder and a Buy action that only says checkout is not connected, never an invented price or a working payment (ambysto-kit#40).
> - **Owner rule, 2026-10-15, for every Ambysto landing:** the page is a preview. Download actions only say the download is not public yet, "Apple Silicon and Intel" stays, and real download links (Developer ID, then the Mac App Store) come after the Developer ID exists. The "Why not the Mac App Store?" question was removed (ambysto-kit#46).
>
> The page in `site/` is the reference now. The old text below is kept as the record.

The public front door for the Ambysto Pro Suite. It explains the suite, links to the app pages that already exist, and asks what a visitor would pay. It does not sell anything.

This file is the product spec. The owner asked for the page on 2026-10-11, then asked that it present the finished product and its specified behavior, not the build status. The page in `site/` does that and is live at https://ambysto-eta.vercel.app. There is no checkout on the page.

The page is a CLI deploy of `site/`, not a Git deploy. A later edit goes live only after another production deploy, or after the repo is connected with root directory `site/`.

## Problem

Each app is hard to understand as a product on its own, and two of them already speak only for themselves:

- Optimer has a page at `ambysto-optimer.vercel.app`.
- Space has a page at `ambysto-space.vercel.app` and says it is not released.
- Steady, DevBox, Power, Tune and Guard have no public page.
- Nothing tells a visitor that the apps share one license, one safety rule, and a price.

The market note (`docs/research/2026-10-09-pro-suite-market.md`, section 8) says the cheapest check before a price is final is a landing page plus a waitlist with a price question. That check does not exist. The suite intent left it as AO-12.

## Outcome

One static page, in English, that a stranger can read in a few minutes and then answer one question: what they would pay once for Optimer, Steady and Space together.

Success, measured:

1. Every sentence on the page is in the claims ledger below. A sentence with no source is a failed page.
2. The page has no checkout, no "Buy" button, and no price shown as a fact. The only prices are choices in the form.
3. Submitting the form stores an email, the price choice, and the time. The store is not this git repo.
4. Optimer and Space cards link to their existing pages. This page does not add a second download URL.
5. At 375px and at 1200px, in light and in dark, the form can be completed with the keyboard only.
6. The deployed site serves this page and nothing from `docs/`, `intent/`, `Sources/` or `macos/`.

## Who it is for

A Mac owner who already dislikes cleaner apps and auto-renewing utility suites. The first readers are developers and creators, including the Build to Own cohort. The page is still written for someone who has never heard of Ambysto.

Windows is named as later. The page does not pretend a Windows build exists.

## Job of the page

Answer, in order:

1. What Ambysto refuses to do.
2. What the first three apps actually do.
3. What is free, and what Pro adds, without a price tag.
4. Which later apps exist as work, and that they are not in the price being asked about.
5. What the visitor would pay, if anything.

The page does not teach people how to use an app. That stays on the app's own page.

## Voice

Match the Optimer and Space pages. Short sentences. Name the limit. No fear, no countdown, no struck-through price, no "was $224".

The suite line, already written in the research note, is the only slogan:

> Measure first. Change safely. Always reversible.

On this page, add only what the same note already ties to complaints: no auto-renew, no telemetry, Pro is not an upsell inside the job the app does.

UI copy is English. The apps are English-only in v1.

## What the page may say the product is

| App | Public line (only this) | Public status | Link |
|---|---|---|---|
| Optimer | See which app is using memory, CPU and disk. Clean only caches that grow back. It never force-quits. | Its own page offers a download. This page does not. | `https://ambysto-optimer.vercel.app` |
| Space | See where the disk went. Map it, find large, old and duplicate files, and uninstall an app with its leftovers. Only what you tick goes to the Trash. | Not released. Say "Not released", the same as its page. | `https://ambysto-space.vercel.app` |
| Steady | Watches the connection: monitor, diagnose, recover. | No page in this workspace. No screenshot. | None, until a Steady page exists |
| DevBox | See which process is listening, which project it belongs to, what containers keep on disk, and which coding agents are busy. Stopping asks you first. | Not released. | None |
| Power | Battery health, which apps use power, heat, and keep-awake. It measures. It does not force a mode. | Not released. | None |
| Tune | System tweaks as switches that remember the old value and put it back in one click. | Not released. | None |
| Guard | See which app connects where, and allow or block that app. | Not released. The filter cannot ship until a Developer ID exists. Do not say it blocks traffic today. | None |

First suite, the only set the form asks a price for: **Optimer, Steady, Space.** That is the set in the suite intent and in the research note. DevBox, Power, Tune and Guard are a later list. They are not included in the $39 / $49 question.

Free and Pro, one table for the suite, not seven tables:

| | Free, forever | Pro |
|---|---|---|
| The job of each app (measure, clean what regrows, move to Trash, stop with confirmation, tweak and restore) | Yes | Yes |
| History beyond the free window, export, alerts, presets | No | Yes |
| A reminder to pay | Only after a real purchase page exists. It does not exist, so the apps show none. | Never |

Do not list Dock badges, Steady's quality signal, or Guard's lists as Pro features on this page. Those details live on each app's spec. One wrong row here is worse than a short table.

License, as far as the license spec allows the page to go:

- One key, checked on the Mac, no account and no network call for the check.
- The key names the apps it covers.
- Launch copies of the apps are given Pro keys. The page may say that. It may not say the key is payment for a review.

## Information architecture

One column, these sections, in this order. Nav anchors: Apps, Price, Waitlist.

1. **Header.** Wordmark "Ambysto". Links: Apps, Price, and a text button "Join the waitlist". No app icon collage.
2. **Hero.** Eyebrow: `Mac utilities`. H1: `Measure first.` Second line, emphasis: `Then it waits.` Lead: `Ambysto is a set of small Mac apps. Each one shows the number and stops. You decide what changes. Nothing auto-renews. Nothing is sent off the Mac.` Primary button: `Join the waitlist` (scrolls to the form). Secondary text link: `See the apps`. Fine print: `macOS first. Windows later. No mobile.`
3. **The refusal.** H2: `What it will not do.` Six items, and no others:
   - No "Free RAM" button and no force-quit.
   - No delete, block, or tweak without an action you take.
   - No malware scanner and no driver or app updater.
   - No subscription and no auto-renew.
   - No telemetry in the apps.
   - No countdown and no fake discount.
4. **The first three.** H2: `Three apps. One rule.` Cards for Optimer, Space, Steady using the table above. Optimer and Space cards are links. Steady is not a link.
5. **Free and Pro.** H2: `Free does the job.` The table above. One line under it: `Pro is a key, not a subscription. There is no purchase page yet.`
6. **Later.** H2: `Not in this price.` One sentence: `DevBox, Power, Tune and Guard are separate apps, still unreleased. A price for them is not being asked today.` Then four short cards from the table. No dates.
7. **Price question.** H2: `What would you pay once?` Body: `For Optimer, Steady and Space together. Not a checkout. Pick one.` The four radios below. No option is pre-selected.
8. **Form.** Fields in the next section. Thank-you state replaces the form on the same page: `You're on the list. One email when there is a price. Nothing else.`
9. **Questions.** The FAQ below, using `<details>`.
10. **Footer.** `© 2026 Ambysto. Not affiliated with Apple.` and `Named after Ambystoma, the axolotl.`

No hero product shot. There is no suite screenshot, and this page does not invent one. Pictures stay on the Optimer and Space pages.

## Price question

The research note treats these as hypotheses, not decisions. The page asks. It does not announce.

Radios, required, no default:

| Value stored | Label |
|---|---|
| `39` | `$39 once` |
| `49` | `$49 once` |
| `less` | `Less than $39` |
| `single` | `I would only buy one app` |

Under the radios, in small type: `Optimer, Steady and Space only. DevBox, Power, Tune and Guard are not included. A single app, sold alone, is a different question and is not on sale.`

Do not print on the page, until a later intent decides them: seat counts, a family pack, a 30-day refund, one year of updates, an education discount, or `$19.99` as a single-app price. Those sentences exist only in the research note.

## Form

Fields:

| Field | Required | Notes |
|---|---|---|
| Email | Yes | One address. No name. |
| Price | Yes | The four radios. Submit stays disabled until one is chosen. |
| Apps | No | Checkboxes: Optimer, Steady, Space. Stored as ids `optimer`, `steady`, `space`. |
| Platform | No | `Mac`, `Windows`, `Both`. |
| Company website | Hidden | Honeypot. A non-empty value is dropped and still shows the thank-you state. |

Consent line, next to the button, not a pre-ticked box: `One email when a price exists. No newsletter. No account.`

The button says `Join the waitlist`.

After a successful submit, remember that state in `localStorage` on this origin so a reload still shows the thank-you line. That is the only client storage.

The request is `POST` of `application/x-www-form-urlencoded` or JSON, to a URL that is not committed with a secret in it. The server stores: time (UTC), email, price, apps, platform. It returns 204 or 200. The page then shows the thank-you state without navigating away.

Emails, CSV exports, and provider keys never go in this repository. The kit repo is public.

If the store is not chosen yet, the page still ships with the form visible and the button disabled, plus the line `The list is not open yet.` A disabled form is acceptable. A form that pretends to save and drops the address is not.

## FAQ (the only answers)

**Why several apps instead of one?**
Each app does one job and stays free at that job. One key can cover more than one app. The apps are not modules of a single cleaner.

**Why not the Mac App Store?**
The sandbox blocks work these apps do, such as reading another app's memory or cleaning caches. They are offered direct. Source: the Optimer page.

**Does Pro lock the cleanup, the delete, or the restore?**
No. Free keeps the job. Pro adds conveniences such as a longer history, export, and alerts. Source: each app's Free/Pro spec. Do not name a convenience the suite table does not list.

**Windows?**
Later. A native app, built from the same written spec. Mac first. No phone app.

**Will you email me?**
Once, when there is a price. No other mail.

**Is anything for sale?**
No. Early copies are given a Pro key. This page only asks what a price should be.

## Claims ledger

A builder may not add a sentence that is not in this table or in the draft copy above.

| Claim | Source |
|---|---|
| Measure first, change safely, always reversible | Suite intent, kit README |
| Optimer: per-app memory, CPU, disk; clean caches that regrow; never force-quit | Optimer README, Optimer site |
| Space: map, large/old, duplicates, uninstall leftovers; Trash; not released | Space README, Space site |
| Steady: monitor, diagnose, recover a connection | Kit README. No further Steady claims. |
| DevBox: ports and project, containers, agents; stop asks first | DevBox README |
| Power: battery, power by app, thermal, keep-awake; measured, never forced | Power README |
| Tune: tweaks that save the previous value and restore in one click | Tune README |
| Guard: per-app outbound allow or block; not shippable without Developer ID | Guard README |
| First suite is Optimer, Steady, Space | Suite intent; research note section 8 |
| Later order was Space, then DevBox, Power, Tune, Guard | Suite intent. The page does not need to show the order, only the names. |
| Dropped: malware scanning, driver and app updaters | Suite intent |
| Free is usable forever. Pro does not paywall the job. | Each app's Free/Pro spec |
| No purchase page at launch, so no pay reminder. Gifted keys, not in exchange for a review. | Optimer spec 03, Space spec 06, kit CLAUDE |
| One offline key, product mask, no network in the check | `docs/spec/license.md` |
| No telemetry, no account, direct sales, no auto-renew | Suite intent constraints; research note section 8 |
| $39 and $49 are questions about Optimer + Steady + Space | Research note section 8. Hypotheses. |
| macOS first, Windows later, no mobile | Kit README; Optimer and Space FAQs |
| Optimer is not on the Mac App Store because of the sandbox | Optimer site FAQ |
| Violet `#6B5CE8`, coral `#ED708F` | Suite intent |

## Do not claim

- That the floating bars already merge. The suite intent describes that perk. The kit does not ship it yet.
- That Intel is tested. The Optimer page says it. Optimer's `CLAUDE.md` says that line is untested. This page does not repeat it.
- A download file that 404s. Optimer's page points at GitHub releases that do not exist while that repo is private. Link to the Optimer page only.
- Any Steady feature beyond the one sentence. Steady's repo is not in this workspace, and it has no Pro tier yet.
- That Guard blocks connections in the build a visitor could run.
- A ship date, a version number, a user count, or a benchmark.
- Seats, refund window, update term, education price, or a single-app dollar price.
- Competitor logos, or a chart from the research note.
- "Lifetime", "unlimited", "military grade", "AI-powered".
- A screenshot, mock, or generated picture of an app. Use none in v1.

## Visual

Use the Optimer page's tokens and type, not a new brand.

- Violet `#6B5CE8`, violet ink `#5849d6` (dark: `#9a8dff`), coral `#ED708F`.
- Page `#F6F5FB`, card white, ink `#1C1A33`, muted `#5F5C78`. Dark page `#12111F`, card `#1C1A2E`, ink `#F2F0FF`.
- Type: `ui-rounded, "SF Pro Rounded", system-ui, sans-serif`. No web font.
- Radius 14px. Hairline borders. The Optimer shadow is allowed. No gradients behind the hero.
- Follows the system light or dark appearance. No theme toggle in v1. A toggle is a second state to test, and the Space page already trusts the system.
- Body 17px. One H1. Sections stack. Width `min(1120px, 100% - 32px)`.
- Buttons: violet fill, white label, for the waitlist only. Links are underlined or otherwise not color-only.
- Price radios are a stack of labelled rows, at least 44px tall. The chosen row uses a violet border.
- Focus ring: 3px violet, offset 3px.
- `prefers-reduced-motion`: no animation. Do not copy Optimer's ticking numbers. This page has no live data.

## Build constraints

- One file, `site/index.html`, in the ambysto-kit repo. CSS and the small script live in that file. No framework, no package, no image assets in v1.
- The script does three things only: require a price before submit, POST the form, switch to the thank-you state. No analytics script. No cookie.
- Deploy the `site/` directory alone, the way Space does (`ambysto-space`, root directory `site`). A merge to `main` may deploy only after this spec is approved and a plan names the project. Do not run a production deploy from an agent.
- `site/vercel.json` sets a long cache only if a later change adds hashed assets. v1 has no assets.
- Confirm on the live host that `/`, and only the waitlist endpoint if it is on the same host, are reachable, and that `/docs/spec/license.md`, `/intent/`, and `/Sources/` return 404.
- The kit repo is public. This page's HTML will be public. Do not put a private URL, a key, or an email inbox password in the file. The form's action URL, if it contains a public form id, is fine.

## Out of scope

- Taking payment, Paddle, a checkout, or a license issuer on the page.
- Redesigning the Optimer or Space pages.
- New screenshots, an app icon, or an axolotl illustration.
- PostHog or any other analytics.
- A blog, an account, a docs site, or a locale switch.
- Per-app landings for DevBox, Power, Tune, or Guard.
- Copying the research chart onto the page.
- i18n. English only.

## Open questions

Empty means this draft can be approved as written.

1. Confirm the page is the suite front door, not a new landing for a single app.
2. Domain, and whether the Vercel project is a new one with root directory `site/` in ambysto-kit.
3. Where the waitlist is stored. Until that exists, the form ships disabled.
4. Steady: is the one sentence enough, or is there a repo to read before the Steady card is published?
5. Are the four price answers (`$39`, `$49`, less than `$39`, single app only) the question to ask?

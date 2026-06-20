# Ben's Magic Forge — Brand Identity Guide v1.0
*MTG Deck Management & Competitive Intelligence*
*Where strategy is forged, one card at a time.*

---

## 01 · Foundation

### Mission
Give Commander players a living forge for their deckbuilding craft — tracking cards, synergies, and competitive intelligence in one place, so strategy gets sharper with every session.

### Audience
Competitive and semi-competitive MTG Commander players who care about winning, not just playing. Brewers who track meta shifts, price windows, and deck performance.

### Personality
Methodical and arcane. The app feels like a master artificer's workshop — disciplined, rich with lore, and always working toward something sharper. Never casual, never corporate.

### Positioning
The competitive layer that sits above Moxfield and Archidekt. Where those tools store decks, the Forge *sharpens* them — pulling intelligence from Scryfall, tracking the meta, and surfacing what matters.

---

## 02 · Color Palette

Rooted in forge-fire and deep iron. Heat ascends from resting ember to brilliant gold; cool rune-violet provides the counter-weight for interactive and data states.

| Color Name | Hex Code | Role / System Application |
| :--- | :--- | :--- |
| **Obsidian** | `#0D0B0F` | Page background |
| **Iron** | `#1C1820` | Surface / cards |
| **Ember** | `#C4541A` | Primary accent / CTA |
| **Forge Gold** | `#C9922B` | Gradient anchor / rarity |
| **Rune Violet**| `#7B4EA6` | Interactive / links |
| **Steel** | `#8A9BAD` | Secondary text |
| **Parchment** | `#E8DFC8` | Primary text / headings |
| **Ash** | `#4A4050` | Muted / disabled |

> ⚠️ **Gradient Rule**
> The Ember→Gold gradient (`135deg, #C4541A → #C9922B`) is reserved for wordmarks, primary CTAs, and hero data highlights only. Overuse dilutes the heat.

---

## 03 · Typography System

Three typefaces in strict roles.

1. **Cinzel Decorative (Weight 900)**
   * **Role:** Wordmark and major hero moments only.
   * *Specimen:* `Ben's Magic Forge`

2. **Cinzel (Weights 400, 600, 700)**
   * **Role:** Page headings, section headers, subheadings, and navigation elements.
   * *Specimen (700):* `Deck Intelligence Dashboard`
   * *Specimen (400):* `Commander · Competitive · 99-Card Format`

3. **Source Sans 3 (Weight 300 / 600)**
   * **Role:** Body copy and standard descriptive prose.
   * *Specimen:* `Ben's Magic Forge pulls live data from Scryfall and MTGJSON to give you a competitive edge in every Commander game.`

4. **JetBrains Mono (Weight 400 / 500)**
   * **Role:** Data matrices, labels, code snippets, and strict system markers.
   * *Specimen:* `DECK_ID: bens-forge-001 · UPDATED: 2026-06-18 · SYNERGY_SCORE: 94.2%`

---

## 04 · Logo Lockups & Marks

### Primary Configurations
* **The Wordmark:** Must always use *Cinzel Decorative*.
* **The Subtitle Line:** "MTG Deck Management" must always use *Cinzel* at `0.22em` letter-tracking.

### Color & Context Treatment
* **On Obsidian (Dark Background):** Text uses a linear gradient from Parchment to Forge Gold. Sub-display text uses Ember.
* **On Iron (Surface Background):** Text uses a linear gradient from Parchment to Forge Gold. Sub-display text uses Rune Violet.
* **On Parchment (Light Background):** Text uses a dark gradient from Obsidian to Ash. Sub-display text uses Ember.
* **On Ember (Promotional Accent):** Text is flat Parchment. Sub-display text is solid Obsidian.

### Icon Mark
* **The "F" Mark:** A standalone capital letter "F" set in *Cinzel Decorative* over a gold-to-ember radial background with an ember drop-shadow box.
* **Usage Rule:** Serves as the app icon, favicon, or in any layout context restricted under `160px` in width where full text lockups fail to fit.

---

## 05 · Signature Runic Circle System

The brand's single most distinctive element. Concentric geometric circles embedded with traditional runework represent "ritual preparation" — indicating a methodical, intentional engineering space.

### System Usage Rules
* **Hero Backgrounds:** 90s continuous ambient spin layout, opacity dialed down to `6%–10%`.
* **Loading States:** 8s active spin layout, full visual opacity, locked at a native width of `120px`.
* **Achievement / Modal Screens:** Static presentation, gold-dominant palette line, set at `200px`.
* **Restrictions:**
  * ❌ Never use on every screen — it completely dilutes its thematic power.
  * ❌ Never fill or stroke with a flat color outside of the defined core palette tokens.

---

## 06 · UI Components

### Buttons
* **Primary Button:** Gradient fill (`Ember → Gold`), typography color is `Obsidian`. Used for critical paths (e.g., `⚒ Build Deck`).
* **Secondary Button:** Transparent background with a `50%` opacity `Forge Gold` stroke outline. Text uses `Parchment`.
* **Ghost Button:** Transparent background with a `40%` opacity `Rune Violet` stroke outline. Text uses `Rune Violet`.
* **Danger Button:** `15%` opacity `Ember` tinted background with a `40%` opacity `Ember` border. Text uses pure `Ember` (e.g., `Remove Card`).

### Badges & Tags
* **Commander:** `badge-commander` — Background: `rgba(123,78,166,0.2)`, Text: `#B08DE0`, Border: `rgba(123,78,166,0.35)`
* **Rare:** `badge-rare` — Background: `rgba(196,84,26,0.15)`, Text: `Forge Gold`, Border: `rgba(201,146,43,0.3)`
* **Common:** `badge-common` — Background: `rgba(138,155,173,0.12)`, Text: `Steel`, Border: `rgba(138,155,173,0.2)`
* **New:** `badge-new` — Background: `rgba(196,84,26,0.25)`, Text: `#F4A27A`, Border: `rgba(196,84,26,0.4)`
* **Synergy Chips:** Rounded pills (`2rem`), background `rgba(123,78,166,0.12)`, border `rgba(123,78,166,0.25)`, text color `#C4AAED`. Prefixed with a native hexagon symbol (`⬡`).

---

## 07 · Motion & Animation Principles

Motion is highly deliberate and functional — never gratuitous. It flags critical system state updates and evokes forge-fire energy.

### Core Transitions
1. **Ember Pulse:** Continuous active loop for live/active signals. Uses a `2s` ease-in-out box-shadow pulse pattern.
2. **Gold Shimmer:** Skeleton evaluation/loading bars. Relies on a `2s` linear background-position layout loop over a gold gradient.
3. **Rune Spin:** Explicit content/data loading. Loops concentric rings in opposite directions at an `8s` linear interval.

### System Easing Matrix
* **Appear States:** `ease-out 200ms`
* **Disappear States:** `ease-in 150ms`
* **Hover Interactivity:** `ease 120ms`
* **Page Transitions:** `ease-in-out 320ms`
* **Ambient Animation Loops:** `linear ∞`

> ⚠️ **Accessibility Override:** Always explicitly listen to `prefers-reduced-motion`. In activated reduced environments, kill all ambient loops and force functional state changes to `≤ 100ms`.

---

## 08 · Writing, Voice & Tone

The Forge speaks like a master artificer who is also a "Spike" (competitive player): precise, highly confident, and deeply technical. It respects user expertise and refuses to over-explain core MTG loop systems.

### Content Design Matrix

| ✓ Do | ✕ Don't |
| :--- | :--- |
| Use MTG terminology natively (e.g., "commander", "tutor", "stax", "goodstuff") without glossaries. | Explain fundamental mechanics like what a "planeswalker" or "mana" is. This is a pro environment. |
| Lead with strong actions: *"Save deck"*, *"Run analysis"*, *"Compare lists"*. | Inject low-value filler lines like: *"Great job!"*, *"You're all set!"*, or *"Looks like..."* |
| Quantify explicitly: *"14 cards match this synergy"* instead of *"several cards"*. | Present ambiguous errors like: *"Something went wrong. Please try again."* — identify the breakdown. |
| Keep errors blunt and actionable: *"No Scryfall connection — check your network, then retry."* | Include soft apologies in code states: completely eliminate *"Sorry,"* or *"Unfortunately."* |
| Fill empty screens with clear vectors: *"No decks forged yet. Start with your commander."* | Mix metaphors — never combine technical forge nomenclature with generic startup buzzwords. |

#### Comparative Example
* **Good Artificer Copy:** `"Deck synergy score dropped 8 points after removing Sol Ring. Consider a replacement mana rock."`
* **Bad Startup Copy:** `"Oops! It looks like your deck score has changed. Don't worry, you can always update your cards to make things better!"`

---

## 09 · Spacing & Grid System

### Space Scale
A rigid 5-step spacing scale derived systematically from a `0.5rem` base. Do not introduce arbitrary numbers outside this scale.

* **xs:** `0.5rem` (8px) — For inner input elements, tight label groupings, badges.
* **sm:** `1.0rem` (16px) — For structural component padding, small card item grids.
* **md:** `2.0rem` (32px) — For larger layout gaps, inner panel sections, container margins.
* **lg:** `4.0rem` (64px) — For structural module separators, large component flows.
* **xl:** `7.0rem` (112px) — Reserved exclusively for hero section blocks and major page dividers.

### Grid Framework
* **Layout:** 12-column standard layout bounded at a maximum width of `1100px`, centered.
* **Fluid Gutter:** Clamped dynamic range mapping from `1.5rem` on mobile layouts up to `5.0rem` on desktop fields.
* **Card Display Blocks:** Always use `auto-fill, minmax(280px, 1fr)`.

### Border Radius
* **4px (`sm`):** System interactive fields, data input frames, state badges.
* **8px (`md`):** Main interface card layouts, modular layout blocks, floating dashboard grids.
* **9999px (`pill`):** Action chips, user toggles, status pills.

---

## 10 · SPA Navigation, Contrast & Backgrounds

To ensure the master artificer's workspace feels both atmospheric and highly practical, the interface must strictly balance immersive background aesthetics with uncompromising readability and seamless browser integration.

### Background Art & Contrast Control
* **Subtle Ambience:** The global card-art background grid (`#app-bg-cards`) must be deeply dimmed to prevent visual fatigue. It is strictly capped at **`12%` opacity** (`--bg-grid-opacity: 0.12`) with a heavy sat/brightness filter mapping of `saturate(0.35) brightness(0.3)`. The art must never compete with foreground text.
* **Secondary Text Legibility:** Critical card-level descriptions (such as `.card-role-text` detailing card functions in a category list) must be styled in **Steel** (`#8A9BAD` / `var(--text-secondary)`) instead of dark muted tones, ensuring a high-contrast ratio on the dark Obsidian background.
* **Muted Ash Contrast Variable:** The dark-theme muted text variable `--text-muted` is set to **`#8E7F96`** (originally `#4A4050`). This provides a solid **4.5:1 WCAG AA contrast ratio** on Obsidian, making secondary metadata labels, placeholders, and tooltips easily legible for older or visually-strained readers.
* **Readable Column Metrics:** Quantities (like `.card-qty` "1x" / "4x") are mapped to **Steel** (`var(--text-secondary)`) rather than muted ash so that quantity metadata stands out instantly.
* **Vibrant Pricing Indicators:** Financial metrics and system prices (`.card-price`) must use high-luminance **Forge Gold** (`#C9922B` / `var(--gold)`) instead of dark gold, making price summaries easily scannable.

### Micro-Typography & Readability Thresholds
* **No Micro-Text:** Never use font sizes below **`0.78rem` (12.5px)**. Micro-text causes severe strain.
* **Scales & Spacing for Chips:**
  - Subtitle wordmark and small header labels: increased from `0.68rem` to **`0.82rem`** at `0.22em` letter-spacing.
  - Interactive badges, owned status, and budget chips (`.budget-chip`, `.owned-chip`): scaled from `0.7rem` to **`0.85rem`** with a generous padding increase of `0.2rem 0.55rem` to create comfortable reading boundaries.
  - Table headers and timeline logs (`.collection-table th`, `.history-meta`): scaled from `0.7rem` to **`0.85rem`** using **Steel** for immediate scanning.

### SPA Navigation & Browser State Preservation
* **URL-State Sync:** Any view transitions driven by `switchView(view)` must maintain a stateful URL in the address bar using hash-routing (e.g., `/#library`, `/#forge`, `/#collection`).
* **History Stack Integration:** Direct DOM navigation updates must push to the browser's history stack via `history.pushState` on active clicks, but bypass pushing when restoring historical states.
* **Popstate Handlers:** A global window listener on the `popstate` event must intercept browser Back and Forward clicks, parse the incoming hash, and route the DOM accordingly to ensure the user never "loses their place" when navigating.

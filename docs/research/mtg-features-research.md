# MTG Deck Management Platform — Feature Research Report

> **Purpose:** Competitive feature analysis of major Magic: The Gathering deck management websites to inform product roadmap decisions for the forge-app platform.
>
> **Scope:** Moxfield, Archidekt, EDHREC, TappedOut, Deckbox, MTGGoldfish, Scryfall, and 8 additional notable platforms.
>
> **Sources:** Direct site research, GitHub repositories (CubeCobra, moxfield-public), Reddit communities (r/EDH, r/magicTCG, r/spikes), Draftsim reviews, EDHREC articles, community blogs — 2024/2025.

---

## Table of Contents

1. [Executive Summary](#1-executive-summary)
2. [Feature Comparison Matrix](#2-feature-comparison-matrix)
3. [Site-by-Site Feature Deep-Dives](#3-site-by-site-feature-deep-dives)
   - [Moxfield](#31-moxfield)
   - [Archidekt](#32-archidekt)
   - [EDHREC](#33-edhrec)
   - [TappedOut](#34-tappedout)
   - [Deckbox](#35-deckbox)
   - [MTGGoldfish](#36-mtggoldfish)
   - [Scryfall](#37-scryfall)
   - [ManaBox](#38-manabox)
   - [Cube Cobra](#39-cube-cobra)
   - [17Lands](#310-17lands)
   - [MTGStocks](#311-mtgstocks)
   - [Deckstats.net](#312-deckstatsnet)
   - [Commander Spellbook](#313-commander-spellbook)
   - [Untapped.gg](#314-untappedgg)
   - [EchoMTG](#315-echomtg)
4. [Emerging AI-Powered MTG Tools](#4-emerging-ai-powered-mtg-tools)
5. [Community Consensus Rankings](#5-community-consensus-rankings)
6. [Prioritized Recommendations for forge-app](#6-prioritized-recommendations-for-forge-app)
7. [AI/ML Feature Opportunities](#7-aiml-feature-opportunities)
8. [Quick Wins](#8-quick-wins)

---

## 1. Executive Summary

This report surveys **15 major MTG digital platforms** across deck building, collection management, metagame analysis, price tracking, social community, and AI/recommendation features. The goal is to identify high-value feature opportunities for **forge-app** — a full-stack ASP.NET Core + MongoDB deck management and AI generation platform already ahead of the curve on AI capabilities.

### Key Themes

**1. The AI gap is real and widening.** As of 2024, zero major incumbent platforms (Moxfield, Archidekt, MTGGoldfish, Deckbox) offer native AI deck generation or AI-powered card recommendations. A cluster of smaller AI-focused tools is emerging to fill this vacuum (ManaForge, ManaTap AI, AI Deck Tutor), but none is integrated with a full-featured deck management platform. **forge-app's existing DeepInfra RAG pipeline is a genuine competitive differentiator** that can be extended significantly.

**2. Users juggle 3–5 separate sites.** A typical Commander player uses Moxfield to build, EDHREC to research, Commander Spellbook to check combos, MTGGoldfish for metagame context, and Deckbox or ManaBox to track their physical collection. No single platform integrates this full workflow. Consolidation is a major opportunity.

**3. Mobile is an open frontier.** Moxfield and Archidekt — the two dominant deck builders — have **no native mobile apps**. ManaBox (iOS + Android) is the best mobile MTG tool, but it lacks AI and full deck-building features. A mobile-capable or PWA version of forge-app addresses a gap at the top of the market.

**4. Social features drive retention.** Platforms with active community features (follows, deck primers, comments, activity feeds) consistently generate more return visits. Moxfield's social features are responsible for a significant portion of its adoption over older tools.

**5. Collection-to-deck workflow is underserved.** Most platforms treat collection management and deck building as separate concerns. Tight integration — showing which cards you own in a deck, generating a shopping list for missing cards, or suggesting buildable decks from your collection — adds substantial perceived value.

---

## 2. Feature Comparison Matrix

| Feature | Moxfield | Archidekt | EDHREC | TappedOut | Deckbox | MTGGoldfish | Scryfall | ManaBox | Cube Cobra |
|---|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|:---:|
| **Deck Building** | ✅ Best UX | ✅ Visual | — | ✅ Legacy | ✅ Basic | ✅ Browse | — | ✅ Mobile | ✅ Cube |
| **Card Search (Scryfall-powered)** | ✅ | ✅ | ✅ | Basic | Basic | ✅ | ✅ **Best** | ✅ Offline | ✅ |
| **Collection Management** | ✅ Good | ✅ Good | — | ✅ Good | ✅ **Best web** | ✅ Premium | — | ✅ **Best mobile** | — |
| **Price Tracking** | ✅ | ✅ | Basic | Basic | ✅ | ✅ **Best** | Basic | ✅ Multi-vendor | Basic |
| **Social / Community** | ✅ Good | ✅ Good | ✅ Articles | ✅ Large archive | ✅ **Trade** | ✅ **Content** | — | — | ✅ Cube community |
| **Commander Recommendations** | ✅ EDHREC panel | ✅ EDHREC+Spellbook | ✅ **Best** | — | — | ✅ Meta | — | — | — |
| **Combo Detection** | ✅ Spellbook | ✅ **In-editor** | — | Basic | — | — | — | — | — |
| **Metagame Analysis** | — | — | ✅ EDH only | — | — | ✅ **Best** | — | — | — |
| **Win Rate Tracking** | — | ✅ Beta | — | — | — | ✅ Tournament | — | — | ✅ Draft |
| **Playtesting / Goldfishing** | ✅ Good | ✅ **Best** | — | Basic | — | — | — | ✅ Simulator | ✅ Draft sim |
| **AI / ML Features** | — | — | ✅ Synergy algo | — | — | — | ✅ Tagger | — | ✅ Draft bots |
| **Salt Score / Social Impact** | — | ✅ EDHREC data | ✅ **Best** | — | — | — | — | — | — |
| **Card Packages (reusable)** | ✅ **Best** | ✅ | — | — | — | — | — | — | — |
| **Trade Tools** | — | — | — | Basic | ✅ **Best** | — | — | ✅ | — |
| **Card Scanning (camera)** | — | — | — | ✅ Android | — | — | — | ✅ **Best** | — |
| **Proxy Printing** | ✅ | — | — | — | — | — | — | — | — |
| **Tournament / Event Tools** | — | ✅ Brackets | — | — | — | ✅ **Best** | — | — | — |
| **Cube Support** | — | ✅ | — | — | — | — | — | — | ✅ **Best** |
| **Deck Version History** | ✅ | — | — | — | — | — | — | — | ✅ Cube |
| **Deck Collaboration** | — | ✅ | — | — | — | — | — | — | ✅ |
| **Deck Primers (rich text)** | ✅ Markdown | — | ✅ Articles | ✅ **Best** | — | ✅ Articles | — | — | ✅ |
| **Budget Replacement Suggestions** | — | — | ✅ Filter | — | — | ✅ **Series** | — | — | — |
| **Public REST API** | — | — | — | — | — | — | ✅ **Best** | — | ✅ |
| **Native Mobile App** | — | — | — | ✅ Android | — | — | — | ✅ **iOS+Android** | — |
| **Offline Use** | — | — | — | — | — | — | — | ✅ | — |
| **Deck Comparison / Diff** | — | ✅ **Best** | — | — | — | — | — | — | — |
| **Custom Cards / Art** | — | ✅ | — | — | — | — | — | — | ✅ |
| **Power Level Indicator** | ✅ cEDH bracket | ✅ Bracket | — | Basic | — | — | — | — | — |
| **CSV Import / Export** | ✅ | ✅ | — | ✅ | ✅ | ✅ | — | ✅ | ✅ |
| **Deck Tagging / Categories** | ✅ | ✅ | — | ✅ | — | — | — | — | — |
| **Sample Hand / Playtest** | ✅ | ✅ **Best** | — | Basic | — | — | — | ✅ | ✅ Draft |
| **Deck EV / Price Trends** | — | — | — | — | — | ✅ **Best** | — | ✅ | — |

---

## 3. Site-by-Site Feature Deep-Dives

### 3.1 Moxfield

**Category:** Deck Building & Management — Current community gold standard for competitive and cEDH players.

#### Core Deck Building
- Full multi-format support: Standard, Pioneer, Modern, Legacy, Vintage, Commander/EDH, Brawl, Pauper, Oathbreaker, Duel Commander, Canadian Highlander, and many more
- Mainboard, Sideboard, Maybeboard, and Scratchpad sections
- Card grouping by type, color, CMC, rarity, or custom tags
- **Deck privacy controls**: Public, private, or unlisted
- **Deck duplication**: Copy any public deck to start your own version
- **Full deck version history**: Every change tracked with a timestamped changelog
- **Proxy printing**: Generates printable proxy sheets for playtesting
- Format and legality enforcement including Commander color identity
- Token requirements display: Lists all tokens your deck generates
- **Card Packages**: Saved, reusable groups of cards (e.g., "Mana Base," "Ramp Suite") — the fastest way to build from tested building blocks

#### Card Search
- Full Scryfall search syntax supported natively
- Card legality and rulings viewable inline while building
- All printings visible with price comparison

#### Analytics
- Mana curve visualization
- Color pie breakdown
- Spell/permanent/land split
- **Color ratio tool**: Compares mana requirements vs. land counts
- Average CMC with and without lands; special Ad Nauseam metric for cEDH

#### Social & Community
- Deck sharing with likes and comments
- **User following and activity feed**
- Notification system
- **Markdown Primer Editor**: Rich strategy documentation with syntax highlighting and auto-generated table of contents

#### Integrations
- **EDHREC recommendations panel** embedded directly in the deck builder
- **Commander Spellbook integration**: Detects known combos in the current decklist
- cEDH bracket tier tagging and power level indicators

#### Export / Import
- Import from URL (Archidekt, TappedOut), file upload, or pasted text
- Export to MTG Arena, MTGO, plain text, CSV

---

### 3.2 Archidekt

**Category:** Deck Building & Management — Best visual deck-building experience.

#### Core Deck Building
- **Visual drag-and-drop builder** — simulate physically arranging cards
- Deck views: visual image grid, text list, spreadsheet/table
- Supports all formats including custom/house rules
- **Cube support** (200+ card lists)
- **Integrated playtester**: Shuffle, draw, play cards, add counters, goldfish in-browser with full game-state simulation
- **Custom Tags / Subcategories**: Fine-grained category assignment within a deck
- Non-English card editions and serialized card number tracking
- **Version Priority**: When building, prioritize art versions you already own

#### Unique Standout Features
- **Deck Comparison Tool**: True side-by-side diff between two decklists with highlighted differences
- **Salt score display** from EDHREC visible on cards during deck building
- **Custom Card Art**: Upload custom images for proxy-ready cards
- **Custom Cards**: Add fictional/custom cards for casual theorycrafting
- **Custom Format & Legality**: Apply house-rule banlists to legality checks
- **Commander Spellbook combo highlighting** directly in the editor — see every known combo as you build
- **Community feature voting / public roadmap**: Users vote on features transparently at archidekt.com/features
- **Bracket system integration** for Commander power level

#### Social
- Deck sharing, embedding on external sites
- **Game tracking (beta)**: Log games played, win rates, opponents
- Community feature voting with transparent roadmap

---

### 3.3 EDHREC

**Category:** Commander/EDH Recommendation Engine — The definitive Commander research tool.

#### Core Innovation
EDHREC is not a deck builder — it is a **pure recommendation and discovery engine** for Commander. It aggregates decklists from Moxfield, Archidekt, TappedOut, MTGGoldfish, and others, processing tens of thousands of lists continuously.

#### Recommendation Algorithm
- **Synergy Score**: Measures how often a card appears with a specific commander *relative to its general play rate* — surfaces cards uniquely good with your commander
- **High Synergy Cards** section: Cards that over-index for your commander
- **Nonbo Warning**: Flags cards appearing *less* often with your commander than baseline — negative synergy indicators
- **Decklist Analyzer**: Upload a decklist → get upgrade suggestions, synergy analysis, "missing staples," and comparison vs. the community average
- **Average Deck Generator**: Statistical 99-card midpoint of all community builds for any commander
- **Precon Upgrade Recommendations**: Select a precon → see the most-played synergistic upgrades with budget filtering

#### Salt Score System
- **Community-voted rating (1–4 scale)** of how frustrating each card is to play against in Commander
- **Salt Calculator**: Run your entire decklist to get a total deck salt score — assess social impact and table friendliness
- This data is exposed via EDHREC and consumed by Archidekt's deck builder

#### Discovery
- **Theme pages**: Dedicated pages for every major archetype (tokens, lifegain, graveyard, etc.) with top commanders, key cards, and strategy guides
- **Set analysis**: New commanders featured immediately after each set release
- **Commander popularity trends** (weekly/monthly historical data)
- **Budget Commander** recommendations with price ceilings

---

### 3.4 TappedOut

**Category:** Legacy Deck Management & Community — Historically important but declining.

#### Historically Unique Features
- One of the oldest MTG deck builders — an enormous archive of historical decklists going back many years
- **Acquireboard**: A separate section for cards needed to complete a deck, syncable with want lists
- **Deck Cycling**: Community feature to promote decks for visibility
- **Deck Primers**: Rich-text strategy documentation tied to decklists (this was a TappedOut innovation before Moxfield adopted it)

#### Collection / Trade Features
- **"Find Decks from Collection"**: Suggests complete deck builds from your owned cards, with completion percentages and acquisition cost estimates
- Inventory management with wishlist and **trade binder** (flag cards available for trade)
- **Owned/missing cards toggle** on any deck view
- **Deck-to-inventory sync**: Automatically add a deck's card requirements to the collection tracker

#### Notes on Current State
- Website design is dated; mobile experience is poor compared to modern competitors
- Active development has slowed significantly
- Retains value primarily for its large archive of older decklists

---

### 3.5 Deckbox

**Category:** Collection Management & Trading — Best-in-class for physical card trading.

#### Collection Management (Primary Focus)
- Catalog by set, printing, condition, language, foil status, quantity
- **Privacy controls** for collections and trades (added 2024)
- Pricing integration via TCGPlayer for real-time collection valuation
- Bulk import/export via text files or CSV
- **Total collection value calculation** with live market data
- Separate "Inventory," "Tradelist," and "Wishlist" sections for precise tracking

#### Trading System (Flagship Feature)
- **Trade matching**: Automatically matches your want list with other users' inventories globally
- **Full trade interface**: Negotiate, make offers, and confirm trades entirely within Deckbox
- **Feedback/reputation system**: User reliability scores for safe peer-to-peer trading (similar to eBay seller feedback)
- **Location-based trader search**: Find geographically nearby traders for in-person trades
- Buy/sell/trade forums

#### Deck Building
- **"Build from Collection" flow**: Construct decks directly from your owned inventory
- Basic deck builder with format legality checking

---

### 3.6 MTGGoldfish

**Category:** Metagame Analysis, Price Tracking & MTG Content — The definitive finance and meta resource.

#### Price Tracking (Industry Best)
- **Real-time card prices** for paper (TCGPlayer, Card Kingdom), MTGO, and MTG Arena in one place
- **Historical price charts** — short-term and long-term trend analysis for any card
- **Price watchlist and alerts**: Notifications for price spikes/drops on tracked cards
- **Market insights**: Reports price movements from meta shifts, reprints, and bannings
- **EV (Expected Value) calculators** for sealed product (booster boxes, precons, collector boosters)
- **Deck price trend tracking** — watch how a deck's total cost changes over time
- Premium: price history CSV downloads, unlimited price alerts

#### Metagame Analysis
- **Tier rankings** (S/1/2/3) for competitive decks in every format
- **Weekly/monthly meta trend charts** — metagame share percentages by archetype
- **Win rates** for tournament decks and archetypes
- **Banning/unbanning impact analysis** — immediate data update when bans occur
- **Tournament results tracker**: Updated in real-time with full decklists from GPs, Pro Tours, SCG Opens, MTGO competitive leagues
- Commander/EDH analytics: rising commanders, combo piece price tracking

#### Content
- **Commander Clash** video series (weekly Commander content)
- "This Week in Legacy/Modern/Pioneer" format articles
- Budget deck brewing articles with premium-to-budget upgrade paths
- **SuperBrew** (Premium): Automated deck idea generator based on user-specified synergies and cards

---

### 3.7 Scryfall

**Category:** Card Search & Database — The gold standard MTG card database; backbone of the ecosystem.

#### Card Search (Definitive Best-in-Class)
Scryfall's query language is the most powerful card search syntax in all of MTG. Key operators include:
- `c:`, `ci:` — color, color identity
- `cmc:`, `mv:` — mana value with comparison operators (`=`, `<`, `>`, `>=`, `<=`)
- `t:` — card type/subtype
- `o:` — oracle text (rules text) search
- `pow:`, `tou:` — power and toughness with comparisons
- `e:` / `s:` — specific set/expansion
- `lang:` — card language
- `f:` — format legality
- `is:` — special status (commander, reprint, promo, unique, etc.)
- `r:` — rarity
- `a:` — artist
- `usd:`, `eur:`, `tix:` — price filters with comparison operators
- `year:`, `date:` — printing year
- Boolean operators: `AND`, `OR`, `NOT`, parentheses
- `order:` — sort results (price, cmc, name, edhrec, released, etc.)

#### API (Backbone of the Ecosystem)
- Free REST API used by virtually every MTG app, site, and tool
- `GET /cards/search?q=` — full query language support
- `GET /cards/named?fuzzy=` — fuzzy name search
- `GET /cards/random` — random card with optional filter
- `POST /cards/collection` — batch card lookup (up to 75 cards)
- `GET /bulk-data` — bulk download URLs (all cards, oracle cards, unique artworks, rulings)
- Up to 175 results per page with pagination

#### Other Features
- **Scryfall Tagger**: Community-driven semantic card tagging enhancing search discoverability
- Every printing shown with high-quality card images (including Secret Lairs, serialized, promos)
- Rules text, rulings, set symbol, artist credit, and format legality all on a single card page
- **No ads, no paywalls, no account required** for full search functionality
- Card pages show all formats' legality, all printings, EDHREC play rates, and prices simultaneously

---

### 3.8 ManaBox

**Category:** Mobile-First Collection Management — Best mobile MTG app.

#### Core Mobile Features
- **iOS and Android native apps** — mobile-first; web version in development
- **Camera-based card scanning**: Best-in-class scanning experience using device camera
- **Batch editing after scan**: Edit quantity, foil status, language, condition, purchase price, altered/misprint flags in bulk
- **Full MTG card database available offline**: Works without internet connection
- **Multi-language and multi-currency support**
- **Built-in MTG rulebook**: Always up-to-date comprehensive rules accessible offline

#### Price & Value Tracking
- Real-time prices from TCGPlayer, Card Kingdom, Star City Games, and Cardmarket
- Collection value monitoring over time
- Historical price changes per card
- Trade valuation with live multi-vendor price comparison

#### Additional Features
- **Google Drive and iCloud backup/sync**
- **MTG News Feed**: Aggregated community news within the app
- **Deck Simulator**: Test decks, simulate draws, analyze mana curve
- **"Unpacked" annual collection video**: Spotify Wrapped–style collection year-in-review

---

### 3.9 Cube Cobra

**Category:** Cube Management & Draft Simulation — The definitive cube tool.

#### Cube Management
- Create, edit, and organize custom cubes of any size
- **Version control / change logs**: Every edit tracked with full history
- **Collaboration**: Cubes can be co-owned; comment and feedback system per cube
- CSV, .txt, or direct entry import

#### Draft Simulation
- **Draft Bots**: AI bots with varying skill levels for solo practice drafts
- **Real Player Live Drafts**: Synchronous and asynchronous online drafts with friends
- **Multiple formats**: Classic booster draft, sealed, Grid draft, Winchester draft, Rochester draft, and more
- **Mock draft simulation** using community pick data statistics
- Export draft decks to MTG Arena, MTGO, Cockatrice, Tabletop Simulator

#### Analytics
- **Cube statistics dashboard**: Color balance, curve analysis, tag breakdowns, rarity distribution
- **Card pick analytics**: How frequently each card is picked across all drafts
- **Draft performance tracking**: Per-player statistics

#### Open Source
- Fully open source on GitHub (dekkerglen/CubeCobra)

---

### 3.10 17Lands

**Category:** Limited/Draft Analytics — The definitive data source for MTG draft.

#### Analytics
- **Individual card win rates**: WR% when drawn, WR% in opening hand, game improvement when drawn
- **ALSA (Average Last Seen At)**: Draft pick order analytics — when cards are typically taken
- **Deck color win rates**: Win rates by color combination/archetype per set
- **Personal draft logs**: Complete draft logs, gameplay replays, win rates by seat, archetype, and opponent
- **Open data sets**: Downloadable CSV/JSON data — completely free and publicly available
- **AI Draft Overlay**: Real-time pick suggestions displayed during MTG Arena drafts

---

### 3.11 MTGStocks

**Category:** Finance & Price Speculation.

#### Price Tracking
- **Market Movers**: Daily/weekly/monthly cards with highest price change (% and absolute)
- **Historical price charts** with long-term trend and volatility analysis
- **Buyout detection**: Identifies sudden mass purchases suggesting artificial price spikes or speculation
- **Custom price alerts**: Notifications when specific cards spike or drop to target thresholds
- Market mover daily email digests
- Sealed product EV calculations for investment decisions

---

### 3.12 Deckstats.net

**Category:** Deck Building + Collection Tracking + Win Recording — Popular with European players.

#### Notable Features
- **Win/loss record tracking per deck**: Log game outcomes over time
- **"Deck Tutor"**: Find complete decks buildable from your existing collection
- **Card draw probability calculator**: Opening hand probability analysis
- Cardmarket price data integration (especially useful for European users — EUR pricing)
- Import/export to Cockatrice, XMage, Arena, MTGO
- Interactive deck builder supporting all formats

---

### 3.13 Commander Spellbook

**Category:** Infinite Combo Database — The definitive Commander combo reference.

#### Features
- **The definitive combo database for Commander** — community-maintained, exhaustive
- Search combos by card name, commander, or color identity
- Step-by-step instructions, prerequisite listings, required cards for every combo
- Combo difficulty ratings
- **Deck scan feature**: Input a decklist to find combos already present, or combos 1–2 cards away from completion
- **"Almost" combos**: Shows combos you can enable by adding just one more card
- Filter by number of cards required, color identity, legality
- Integrated directly into Archidekt's editor; referenced by Moxfield

---

### 3.14 Untapped.gg

**Category:** MTG Arena Companion App & Analytics.

#### Features
- MTG Arena companion app (desktop install required)
- **Live deck tracker overlay**: Tracks hand, graveyard, remaining deck contents in real-time
- Collection sync with Arena for optimal wildcard crafting recommendations
- Meta tier lists with win rates from millions of Arena games
- Personal mulligan guidance based on historical hand data
- Matchup win rates and rank progress tracking
- **AI-powered pick suggestions for MTG Arena drafts** using 17Lands data
- Automatic match logging and statistics

---

### 3.15 EchoMTG

**Category:** Finance-Focused Collection Tracker.

#### Features
- **Financial focus**: Real-time pricing with automated daily value updates
- **Weekly email reports** with collection value changes and market trends
- **7-day gain/loss analysis** per card and collection
- Real-time price graphs for every card
- Public tradelist
- Deck management from tracked collection
- Import/export to CSV, MythicHub, TCGPlayer

---

## 4. Emerging AI-Powered MTG Tools

As of 2024, **zero major incumbent platforms** (Moxfield, Archidekt, MTGGoldfish, Deckbox) offer native AI deck generation or intelligent card recommendations. A cluster of smaller AI-focused tools is starting to fill this gap:

| Platform | AI Features | Focus |
|---|---|---|
| **ManaForge** (mtgmanaforge.io) | AI deck generator, power level analyzer, ban tracker, upgrade suggestions | All formats |
| **ManaTap AI** (manatap.ai) | Conversational AI deck coach, synergy analysis, mana base optimization | Commander |
| **MTG Agents** (mtg-agents.com) | "Karn" AI — natural language deck construction and card search | All formats |
| **AI Deck Tutor** (aidecktutor.com) | 99-card Commander AI optimization engine | Commander |
| **Commander AI** (mtgcommander.ai) | EDHREC + Scryfall-backed synergy stats, bracket identification | Commander |
| **ManaBrain** (manabrain.space) | LLM-grade semantic search, combo insights, collection manager | All + Collection |

**Critical observation:** None of these emerging AI tools has the full-featured deck management platform that forge-app already offers. **forge-app has a first-mover advantage** in combining robust deck management with a production-grade AI pipeline (DeepInfra LLM + RAG via Qdrant). Extending that AI pipeline to new features is lower effort than for any competitor building from scratch.

---

## 5. Community Consensus Rankings

Based on r/EDH, r/magicTCG, and r/spikes discussions (2024):

| "Best for…" | Winner | Runner-Up |
|---|---|---|
| All-around deck builder | **Moxfield** | Archidekt |
| Visual/casual builder | **Archidekt** | Moxfield |
| Commander research & recommendations | **EDHREC** | — |
| Metagame & finance | **MTGGoldfish** | MTGStocks |
| Physical card trading | **Deckbox** | TappedOut |
| Mobile collection management | **ManaBox** | Dragon Shield |
| Card database & advanced search | **Scryfall** | — |
| European collection tracking | **Deckstats.net** | Deckbox |
| Limited / Draft analytics | **17Lands** | Draftsim |
| Cube management | **Cube Cobra** | — |
| Commander combo lookup | **Commander Spellbook** | — |
| MTG Arena companion | **Untapped.gg** | — |

---

## 6. Prioritized Recommendations for forge-app

Features are ranked by estimated user impact × differentiation value, with implementation complexity assessed against forge-app's existing stack (ASP.NET Core 10, MongoDB, PostgreSQL, DeepInfra AI, vanilla JS frontend).

### 🔴 Tier 1 — High Impact, High Differentiation

#### 1. Deck Playtest / Sample Hand Simulator
**What it is:** In-browser simulation — draw a sample opening hand, take mulligans, and draw cards turn-by-turn to goldfish with a deck.

**Best implementation (Archidekt):** Full game-state simulation with counters, graveyard zone, exile zone, mana tracking, and token creation.

**MVP implementation:** Draw 7 cards, support mulligan (London Mulligan), draw next card. Display cards as images from the Scryfall CDN.

**Complexity:** Low-medium. Pure frontend logic on the vanilla JS side — no backend changes required. Deck data is already available client-side on the deck detail page. Scryfall card images are already in use.

**Value:** Extremely high — almost every active deck builder uses a playtest sandbox regularly. Archidekt's is considered the best. Adding even a basic version matches Moxfield's and closes a major feature gap.

---

#### 2. Mana Curve & Statistics Dashboard
**What it is:** Visual analytics panel showing mana curve histogram, color pip distribution, card type breakdown (creatures, instants, sorceries, enchantments, artifacts, lands), average CMC, and spell/land ratio.

**Best implementation (Moxfield):** Inline in deck view. Updates in real time as cards are added/removed. Includes a "color ratio tool" comparing mana requirements vs. land counts.

**Complexity:** Low. Card data (CMC, type, color) is already stored in MongoDB. Chart generation can be done purely in JS (Chart.js — a single lightweight CDN dependency). No AI or external API calls needed.

**Value:** High — this is a basic expectation of any deck builder. Currently absent from forge-app's frontend based on the current architecture.

---

#### 3. AI-Powered Card Recommendation Panel
**What it is:** While viewing or building a deck, show a panel of suggested cards to add — ranked by synergy with the current decklist and commander.

**Best implementation (EDHREC):** Synergy-scored recommendations based on statistical co-occurrence across thousands of community decklists.

**forge-app's advantage:** The existing RAG pipeline (Qdrant vector search + DeepInfra LLM) can be extended to generate contextual card suggestions without calling EDHREC's API. Specifically:
- Store card-to-card co-occurrence vectors or semantic card embeddings in Qdrant
- Given the current decklist, query for semantically similar/synergistic cards
- Re-rank by commander color identity, format legality, and budget ceiling

**Complexity:** Medium. Requires extending the RAG pipeline with card embedding data. The LLM inference infrastructure already exists. Qdrant already stores vectors. The main work is ingesting a card embedding corpus and building the suggestion endpoint.

**Value:** Very high — this is the most-used feature on EDHREC and the most-requested feature in AI MTG tools. No full-featured deck management platform offers this natively.

---

#### 4. Deck Version History & Changelog
**What it is:** Track every change made to a deck (cards added/removed, quantities changed, categories updated) with timestamps. Allow users to view the deck at any historical state.

**Best implementation (Moxfield):** Full timestamped changelog on every deck's history page, with the ability to view past versions.

**Complexity:** Low-medium. MongoDB's document model is well-suited for append-only event sourcing. Each deck mutation becomes an event stored in a `DeckHistory` collection, with `deckId`, `timestamp`, `userId`, and a diff of changes. The frontend then renders this as a readable changelog.

**Value:** High — power users manage multiple versions of Commander decks as the meta shifts. This is one of Moxfield's most-praised features and a common complaint when it's absent.

---

#### 5. In-Deck Collection Ownership Overlay
**What it is:** When viewing a deck, cards the user already owns are highlighted/marked. A "missing cards" list is generated, optionally with a "copy shopping list" button showing the cost to complete the deck.

**Best implementation (Archidekt / TappedOut):** Owned cards are visually distinct in the deck view (green border, checkmark overlay). A "missing" tab shows cards still needed with their current prices and a total acquisition cost.

**Complexity:** Medium. Requires a collection management system (see #8 below). Once collection data exists in MongoDB, querying the intersection with a decklist is a simple aggregation. The frontend overlay is CSS + JS.

**Value:** Very high — this bridges deck building and collection management, which is one of the biggest workflow pain points for paper MTG players.

---

### 🟠 Tier 2 — Meaningful Differentiators

#### 6. Deck Comparison / Diff Tool
**What it is:** Side-by-side comparison of two decklists showing cards unique to each deck, cards in common, and a percentage similarity score.

**Best implementation (Archidekt):** True diff view with cards highlighted by presence in deck A only, deck B only, or both.

**Complexity:** Low-medium. Backend: simple set operations on two card lists — no AI or external calls. Frontend: a two-column diff-style layout in vanilla JS/CSS.

**Value:** Medium-high — frequently requested by players who maintain multiple versions of the same deck, or who want to compare their build to a popular community list.

---

#### 7. Combo Detection Integration
**What it is:** Automatically detect known infinite and synergistic combos present in the current decklist, with step-by-step instructions.

**Best implementation (Archidekt / Commander Spellbook):** Embedded directly in the editor — as cards are added, combos involving those cards are flagged in a sidebar. Clicking shows full combo walkthrough.

**Implementation approach:** Commander Spellbook offers a **free public API** (`commanderspellbook.com/api/`) that accepts a card list and returns matching combos. This is a simple backend proxy call — no AI infrastructure needed.

**Complexity:** Low. Add a backend endpoint that proxies a request to the Commander Spellbook API with the current decklist. Display results in the deck view panel. The API is free and well-documented.

**Value:** High for Commander players — one of the most-used research tools in the format.

---

#### 8. Personal Card Collection Management
**What it is:** Users maintain a personal card inventory (quantities, printings, conditions, foil status). This data powers the ownership overlay (#5) and the "buildable decks" feature (#9).

**Best implementation (Deckbox):** Full catalog by set, printing, condition, language, foil, quantity. Separate Inventory / Tradelist / Wishlist sections. Real-time TCGPlayer pricing on the collection.

**MVP implementation:** Quantity tracking per card (name + set). Bulk CSV import from Moxfield/Deckbox/ManaBox formats (forge-app already has CSV parsing infrastructure for deck imports).

**Complexity:** Medium. MongoDB collection with `userId`, `cardName`, `setCode`, `quantity`, `foil`, `condition`. The existing CSV import infrastructure lowers the barrier for bulk collection import.

**Value:** High — collection management is a core need for paper players and is the foundation for features #5 and #9.

---

#### 9. "Decks You Can Build" From Collection
**What it is:** Given a user's collection, suggest complete decks they can build (or nearly build) — ranked by completion percentage and sorted by format, color identity, or commander.

**Best implementation (TappedOut "Find Decks from Collection"):** Shows decks sorted by % complete, with acquisition cost for missing cards.

**Complexity:** Medium-high. Requires collection management (#8) plus a corpus of community decklists or AI-generated archetypes to match against. The LLM pipeline could generate viable deck templates for common commanders, which are then matched against the collection.

**Value:** High — frequently cited as one of TappedOut's most-loved features before its decline. No modern platform does this well.

---

#### 10. Deck Primer / Strategy Guide Editor
**What it is:** A rich-text editor attached to each deck where owners can write strategy documentation — card explanations, win conditions, budget upgrade paths, matchup notes.

**Best implementation (Moxfield):** Markdown editor with syntax highlighting, live preview, and auto-generated table of contents. TappedOut pioneered this feature for the broader MTG community.

**Complexity:** Low. A Markdown editor in the frontend (e.g., `marked.js` or `SimpleMDE` — both lightweight single CDN includes) writing to a `primer` field in the MongoDB deck document. No backend AI or API calls required.

**Value:** Medium-high — primers are a powerful community and SEO feature, encouraging richer deck descriptions that drive organic discovery.

---

#### 11. Salt Score Display & Deck Salt Calculator
**What it is:** Show EDHREC's community-sourced "salt score" for each card (1–4 scale indicating how frustrating it is to play against in Commander). Calculate a total deck salt score.

**Best implementation (EDHREC / Archidekt):** EDHREC's salt page and Archidekt show salt scores during card search. EDHREC's Salt Calculator totals a full decklist.

**Implementation approach:** EDHREC exposes salt scores through their public API/JSON data. Alternatively, Scryfall's Tagger community data contains similar sentiment annotations.

**Complexity:** Low — data fetching from EDHREC's JSON endpoints, cached in MongoDB or Redis. Display as a badge on each card in deck view plus a total.

**Value:** Medium-high — Commander is an inherently social format; playgroup-friendliness assessment is a genuine decision-making tool.

---

#### 12. Multi-Format Export (MTG Arena / MTGO / Cockatrice)
**What it is:** Export any deck in the specific text format required by MTG Arena, MTGO, or Cockatrice — each has slightly different formatting rules.

**Best implementation (Moxfield / Archidekt):** Format-specific export buttons that generate correctly formatted output in one click.

**Complexity:** Very low. forge-app already has CSV export infrastructure. Adding format-specific text serializers is straightforward string formatting.

**Value:** Medium — reduces friction for users who play on multiple platforms.

---

### 🟡 Tier 3 — Quality-of-Life Improvements

#### 13. Card Packages (Reusable Deck Building Blocks)
**What it is:** Save a named group of cards (e.g., "40-Land Commander Base," "Blue Counterspell Suite," "Aristocrats Engine") that can be added to any deck in one click.

**Best implementation (Moxfield):** Named packages stored per-user. Any public package can be browsed and imported by other users.

**Complexity:** Low-medium. A `CardPackage` collection in MongoDB with `userId`, `name`, `cards[]`. Add an endpoint to apply a package to a deck. Basic frontend modal to browse and import packages.

**Value:** High for power users who build many decks and maintain consistent land bases or core packages.

---

#### 14. Power Level / Bracket Assessment Tool
**What it is:** An objective, data-driven deck power level indicator beyond a self-reported 1–10 slider. The Commander community has recently adopted an official **4-bracket system** (1 = precon, 2 = upgraded, 3 = optimized, 4 = cEDH).

**Best implementation (Moxfield / Archidekt):** Bracket tagging with the new official 4-tier system. Some tools also flag presence of tutors, fast mana, and infinite combos as objective bracket indicators.

**forge-app AI advantage:** The existing LLM pipeline can analyze a full decklist and output a structured power level assessment: identifying infinite combos, fast mana (Mana Crypt, Sol Ring, Chrome Mox), tutors, and stax pieces — then mapping these to the 4-bracket system with an explanation. No other platform currently generates AI-reasoned power level explanations.

**Complexity:** Low for bracket tagging; Medium for AI-driven assessment. The latter extends the existing deck analysis prompt in `RagPipelineService`.

**Value:** High — bracket compatibility is now a primary topic of conversation when sitting down to play Commander.

---

#### 15. Budget Upgrade Path Generator (Enhanced)
**What it is:** Given a deck, suggest card-by-card budget swap recommendations — not just "this card is over budget" but "this $40 card can be replaced by these 3 alternatives ranked by synergy and price."

**Current state in forge-app:** Budget replacement suggestions already exist via `RagPipelineService`. This recommendation is about surfacing and improving that feature in the UI.

**Enhancement:** Present suggestions as a ranked, visual list with Scryfall card images, price delta, and a one-click "apply swap" action. Add a "target budget" slider that triggers a full deck budget optimization pass.

**Complexity:** Low — the AI infrastructure for budget suggestions already exists. This is primarily a frontend enhancement to the existing API response.

**Value:** High — budget accessibility is one of the most-discussed topics in Commander communities.

---

#### 16. Proxy Sheet Generator
**What it is:** Generate a printable PDF or PNG sheet of proxy cards for playtesting — typically laid out in a grid matching standard card dimensions for cutting and sleeving.

**Best implementation (Moxfield):** One-click proxy sheet generation with high-resolution card images from Scryfall.

**Complexity:** Low-medium. Backend: fetch card images from Scryfall, stitch into a printable grid layout using a PDF library (e.g., `QuestPDF` for .NET — free, well-documented). Frontend: a print view trigger.

**Value:** Medium — proxy printing is a common need for cEDH and competitive players who test before purchasing expensive cards.

---

#### 17. Deck Tagging & Discovery
**What it is:** Apply structured tags to decks (format, archetype, tribe, theme, power level, color identity) that make decks browsable and discoverable via a public deck directory.

**Best implementation (Moxfield / Archidekt):** Free-form and structured tags. Public deck browsing by tag, format, color identity, and commander.

**Complexity:** Low. MongoDB already stores deck metadata. Adding a `tags[]` array field and an index on it, plus a browsing endpoint with tag filters, is straightforward.

**Value:** Medium-high — public deck discovery drives community engagement and SEO traffic.

---

## 7. AI/ML Feature Opportunities

forge-app's existing AI pipeline (DeepInfra `meta-llama/Llama-3.3-70B-Instruct` + Qdrant RAG + `RagPipelineService`) provides a foundation that competitors lack. The following features leverage this infrastructure for meaningful differentiation.

### 7.1 Natural Language Deck Building
**What it is:** "Build me a Gishath Dinosaur tribal deck focused on aggressive combat with a budget under $150" → AI generates a full 100-card Commander list.

**Current state:** Already implemented via the async AI generation job with budget enforcement loop.

**Enhancement opportunities:**
- Add **iterative refinement** — "Make it more focused on ramp" or "Replace the most expensive cards" as follow-up prompts that modify the generated deck
- Support **theme prompts** beyond commander name — tribes (Elves, Dragons, Vampires), archetypes (stax, combo, aggro), and power level targets as input parameters
- **Explain each card choice** — after generation, provide a brief rationale per card group explaining why each card was included

### 7.2 AI Deck Analysis & Improvement Coach
**What it is:** Upload any deck → receive a detailed AI analysis covering: win conditions, key synergies, identified weaknesses, categories needing improvement, and specific cut/add recommendations ranked by impact and budget.

**Current state:** Deck analysis with category breakdown exists. Enhancement: make the feedback actionable with specific card names, prices, and one-click apply.

### 7.3 "What Does This Deck Do?" Summary Generation
**What it is:** For any deck (including community decks), generate a 2–3 paragraph natural language description: the game plan, key synergies, how it wins, and who would enjoy playing it.

**Value:** Dramatically improves the experience of browsing public decks — users can instantly understand any deck without reading every card. Also serves as an SEO-rich deck description.

**Complexity:** Low. A single LLM call with the decklist as context. Prompt already exists in similar form for the AI generation pipeline.

### 7.4 AI-Powered Commander Recommendation
**What it is:** "I want to build a graveyard combo deck that wins around turn 7-8 at a mid-power table" → AI suggests 3–5 commanders that fit, with brief explanations of why each fits the criteria.

**Complexity:** Low. Prompt engineering task using the existing LLM call infrastructure. Commander data is already part of the RAG corpus.

### 7.5 Contextual Card Search (Semantic Search)
**What it is:** Search cards by what they *do* rather than their name or rules text keywords. "Cards that let me draw cards when creatures die" → surfaces relevant cards ranked by relevance, even if those cards don't contain those exact words in their oracle text.

**Best parallel:** Scryfall Tagger (community semantic tags), ManaBrain (LLM-grade semantic search).

**Implementation:** Store Qdrant card embeddings with oracle text as the embedded content. Query with a natural language input. Return top-N results ranked by cosine similarity, filtered by format legality and color identity.

**Complexity:** Medium. Requires populating Qdrant with card oracle text embeddings (one-time batch job; embeddings generated via DeepInfra embedding model). Query path reuses existing Qdrant client.

**Value:** High — a genuinely novel card search experience that no incumbent platform offers.

### 7.6 AI Mana Base Optimizer
**What it is:** Given the deck's color pip requirements and CMC curve, analyze the mana base and suggest land count, color ratios, and specific dual land recommendations optimized for the budget.

**Current state:** forge-app's analysis includes basic category breakdowns. Enhancement: focus specifically on mana base optimization as a dedicated operation.

**Complexity:** Low. Extension of the existing analysis prompt with mana base–specific instructions and structured JSON output.

### 7.7 "Explain This Card" for New Players
**What it is:** Click any card in a deck → AI generates a plain-English explanation of what the card does, why it's in this specific deck, and how to use it effectively.

**Complexity:** Very low. A single LLM call with card oracle text + deck context as input. Results can be cached by card + deck archetype to minimize API calls.

**Value:** Medium — lowers the barrier for newer players exploring decks built by experienced users.

### 7.8 Playstyle / Power Level Matching
**What it is:** "Find decks that play like [deck X] but at a lower power level" or "Find a commander that suits an aggressive midrange playstyle for a casual table."

**Complexity:** Medium. Requires deck embeddings in Qdrant (generated from AI analysis summaries or deck features). Cosine similarity search over deck vectors with filter criteria.

---

## 8. Quick Wins

These are features with **low implementation complexity** relative to their user value, achievable without significant new infrastructure or AI pipeline changes.

| # | Feature | Why It's a Quick Win | Estimated Complexity |
|---|---|---|---|
| 1 | **Multi-format text export** (Arena, MTGO, Cockatrice) | Pure string formatting — forge-app already has CSV export infrastructure | 1–2 days |
| 2 | **Mana curve histogram** | Chart.js (CDN), card data already in MongoDB, no backend changes needed | 1–2 days |
| 3 | **Commander Spellbook combo detection** | Free public API proxy, results displayed in deck view panel | 2–3 days |
| 4 | **Sample hand / draw simulator** | Pure frontend JS, uses existing card image loading via Scryfall CDN | 2–4 days |
| 5 | **"Explain this deck" AI summary** | Single LLM call, minimal prompt engineering, reuses existing DeepInfra integration | 1–2 days |
| 6 | **Deck tagging & tag-based browsing** | `tags[]` array on MongoDB deck document, basic filter endpoint, simple UI | 2–3 days |
| 7 | **Deck primer / Markdown editor** | Single Markdown editor library (SimpleMDE, ~10KB), `primer` field on deck document | 1–2 days |
| 8 | **Salt score badges per card** | Fetch and cache EDHREC salt data in MongoDB, display as badge in card list view | 2–3 days |
| 9 | **Color identity mana pip breakdown** | Count mana symbols in oracle costs across the deck; Chart.js pie chart | 1 day |
| 10 | **Card type / category breakdown** | Parse card types already stored in deck documents; simple bar chart or table | 1 day |
| 11 | **Deck duplication ("fork" deck)** | Copy deck document in MongoDB with new `userId` and `sourceId` reference | < 1 day |
| 12 | **Budget slider for AI generation** | Budget parameter already exists in the AI generation job; expose as a UI slider | < 1 day |
| 13 | **Public deck discovery / browse page** | Filter/sort endpoint over public decks by format, color, commander; vanilla JS list view | 2–3 days |
| 14 | **"Share deck" link with preview** | Public deck URL already works; add Open Graph meta tags for rich social link previews | < 1 day |

---

*Report compiled from direct site research, GitHub repositories, Reddit community discussions, and published reviews — 2024/2025.*

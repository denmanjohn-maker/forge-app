# Potential Features Report for forge-app

## Objective
Investigate features from popular Magic the Gathering (MTG) deck management and deck builder websites (like Moxfield, Archidekt, EDHREC, and TappedOut) and identify potential features that could be added to `forge-app`.

## Feature Breakdown by Platform

### 1. Moxfield
- **Clean, Intuitive Deck Building**: Support for all formats with custom tags and sorting categories.
- **Auto-Card Suggestions & EDHREC Integration**: Smart recommendations during deck construction.
- **Playtesting**: Digital "goldfishing" with simulated hands, draws, and board states.
- **Collection Management**: Collection tracking allowing users to filter decks by owned cards.
- **Collaboration & Social**: Deck sharing, comments, upvotes, and public/private/unlisted visibility options.
- **Price Tracking**: Live integration with multiple vendors (TCGplayer, Card Kingdom).

### 2. Archidekt
- **Drag-and-Drop Interface**: Highly visual deck construction process.
- **Advanced Playtesting Tools**: Built-in hand simulator, draw simulator, and sample hand testing.
- **Statistics & Analytics**: In-depth mana curve analysis, color distribution, and custom stats.
- **Tagging & Organization**: Deep custom folders, tags, and sorting systems.
- **Import/Export**: Easy bulk import from text and CSV, integration with MTGO/Arena.

### 3. EDHREC
- **Commander Meta Data & Analytics**: Aggregates deck data to highlight popular cards for specific commanders.
- **Synergy Scores**: Highlights cards with the highest synergy with a given commander.
- **Advanced Search & Discovery**: Finding card themes, strategies, or tribal recommendations.
- **Sample Decks & Archetypes**: Sample decklists based on data analytics.

### 4. TappedOut
- **Community Interaction**: Deep social environment with comments, ratings, and discussions.
- **Competitive Ranking**: Decks ranked by upvotes and tournament performance.
- **Event Hosting**: Tools to run tournaments or events within the community.

## Potential Features for `forge-app`

Based on the investigation, here are potential high-value features for `forge-app`, categorized by impact:

### Tier 1: Core Deck Management & AI Integration
1. **AI-Powered Card Suggestions (Smart Recommendations)**
   - Leverage the existing `forge-ai-api` to suggest cards that fit the current deck's theme, mana curve, and synergy, similar to EDHREC but dynamically personalized.
2. **Visual Playtesting (Goldfishing)**
   - A digital playtesting sandbox to draw sample hands, simulate turns, and test deck flow.
3. **Advanced Deck Analytics & Visualizations**
   - Mana curve charts, color distribution pie charts, and probability calculators (e.g., chance to draw a land by turn 3).
4. **Collection Tracking & Ownership Integration**
   - Allow users to input their collection and see which cards they are missing when viewing or building a deck.

### Tier 2: Community & Social Features
1. **Deck Versioning & History**
   - Ability to save revisions of a deck, compare diffs between versions, and fork other users' decks.
2. **Community Ratings & Comments**
   - Allow users to publish their AI-generated or manually tweaked decks for public feedback and upvotes.
3. **Price Tracking & Vendor Integration**
   - Display live pricing for decks and provide one-click buying options through TCGplayer or Card Kingdom APIs.

### Tier 3: Advanced Organizational Tools
1. **Custom Tags and Grouping**
   - Let users group cards within a deck by custom categories (e.g., "Ramp", "Targeted Removal", "Board Wipes") rather than just card type.
2. **Reusable Packages / Modules**
   - Users can save "packages" (e.g., a 10-card green ramp package) to quickly insert into new decks.
3. **Export/Import Compatibility**
   - Robust parsing to easily import decks from MTG Arena, MTGO, or text lists.

## Conclusion
`forge-app` has a unique opportunity to combine standard deck-building features (like playtesting, tagging, and collection tracking) with its AI-driven capabilities (`forge-ai-api`). Focusing on **AI-Powered Suggestions**, **Advanced Analytics**, and **Visual Playtesting** would bring the app closer to feature parity with leading platforms like Moxfield and Archidekt while maintaining its AI-native edge.
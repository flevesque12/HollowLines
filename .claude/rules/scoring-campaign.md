---
paths:
  - "**/ScoreSystem*"
  - "**/CampaignManager*"
---

## 8. Scoring summary

| Action | Points | Formula |
|---|---|---|
| Drill | 10 × streak | 🔄v3.1: streak tracks **downward drills only**; lateral/up are neutral |
| Chunk Burst | cells × 25 × fall_bonus | fall_bonus = floor(fall_distance / 2) |
| Bomb | blocks × 25 × chain_mult | chain_mult = # bombs in sympathetic chain |
| Depth | 50 | Per new deepest row |
| Perfect Clear | 500 **flat** | Rare bonus. The cascade multiplier was removed — see §15.2 |
| Diamond | 150 | 🔄v3.1: bonus in ALL modes (campaign gate removed) |
| Enemy Kill | 100 (Crawler) / 150 (Boomer) | ✅R5.11: 2 types only. Kill-by-burst × fall_bonus, kill-by-bomb × chain_mult |
| Boomer explosion | blocks × 25 × parent_bonus | ✅R5.11: Boomer death blast scores like a bomb. Parent bonus carries through |

**Air restore** (revised in §15.1 — the draft values were ~20× oversupplied):

| Source | Amount |
|---|---|
| **Every drill** | **+0.5%** 🆕v3.1 — the core arcade survival loop: drill to breathe |
| Air capsule (drill or liberate) | +6% |
| Chunk Burst | +0.5% |
| Bomb chain (per bomb) | +1% |
| Perfect Clear | +6% |

**Campaign drain:** 4 %/s on levels 1-3, 7 %/s from level 4 (`CampaignManager.DrainRateForLevel`).

---

## 9. Campaign — 10 levels

Win = reach the bottom + **score minimum** (🔄v3.1 — replaces diamond gate).

Row counts and drain were revised in §15.1 — the v2.1 depths were far too short for the air clock
to function once the line gate was removed.

| Level | Rows | New mechanic | Drain | Wobble | Diamonds | Score min | Enemies |
|---|---|---|---|---|---|---|---|
| 1 | 24 | Movement + drilling | 4%/s | 0.8 s | 0 | 0 | — |
| 2 | 30 | Color Streak (tutorial vein) | 4%/s | 0.8 s | 0 | 0 | — |
| 3 | 32 | Chunks + Chunk Burst (tutorial setup) | 4%/s | 0.8 s | 0 | 0 | — |
| 4 | 40 | Air capsules + drain + **Diamonds** | 7%/s | 0.6 s | 2 | 500 | — |
| 5 | 44 | Hard blocks | 7%/s | 0.6 s | 3 | 500 | — |
| 6 | 50 | Bombs (single) + **Crawler** (🔄R5) | 7%/s | 0.6 s | 3 | 1,500 | 3 Crawlers |
| 7 | 54 | Bomb chains + **Boomer** (🆕v3.1) | 7%/s | 0.6 s | 4 | 1,500 | 3 Crawlers + 2 Boomers |
| 8 | 60 | Steel blocks | 7%/s | 0.6 s | 4 | 3,000 | 4 Crawlers + 3 Boomers |
| 9 | 68 | Full mix | 7%/s | 0.6 s | 5 | 3,000 | 5 Crawlers + 4 Boomers |
| 10 | 76 | Dense final board | 7%/s | 0.6 s | 5 | 5,000 | 6 Crawlers + 5 Boomers |

> **🔄v3.1 Score gate (replaces diamond gate).** The diamond gate was removed because it
> conflicted with design rule 6 (depth is always forward) — players could miss a diamond and
> have no way to backtrack, making the level impossible. The score gate forces engagement with
> the scoring mechanics (bursts, bombs, enemy kills) without requiring lateral routing.
> Levels 1-3 have no gate (tutorial). A pure tunnel-bot at 50 pts/row earns ~2,000 on a 40-row
> level, so the 500-pt gate at level 4 is trivially passed by descending. The 3,000+ gates on
> levels 8-9 require at least a few bursts or bomb chains — which happen naturally for a player
> who is engaging with the game.
>
> **Diamonds are still placed** (same counts, same placement logic) — they're just not required.
> Each one is +150 pts toward the score gate, making them a helpful bonus on the path.
>
> **🔄v3.1 Enemies simplified.** Digger and Tank removed. Crawlers appear from level 6 (alongside
> bombs, since the player already understands burst kills). Boomers appear from level 7 (the
> player has seen bomb chains and can appreciate the amplification effect).

> The burst tutorial band sits **directly on the bedrock floor** (`BurstTutorialTop()`), not below
> the spawn zone. An authored solid row placed mid-board is not anchored: with porous terrain
> beneath it the whole band settles, falls 2+ rows and bursts on load, destroying the setup before
> the player sees it. Only the bedrock never moves.

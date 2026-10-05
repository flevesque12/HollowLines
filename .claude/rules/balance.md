---
paths:
  - "**/AirSystem*"
  - "**/ScoreSystem*"
  - "**/StrateGenerator*"
---

## 15. ✅ Balance findings — ALL RESOLVED (R2.8 complete)

First real v3 playtest run (`cd tools/playtest && dotnet run`, R2.7) found the systems all worked
but the **balance contradicted three of the seven non-negotiable design rules.** All five findings
below are now resolved (R2.8a-f, 2026-07-21): color veins (§15.3), Perfect Clear nerf (§15.2),
settle-on-load + pacing (§15.1 / R2.8d/c), bomb economy proven (§15.4), wobble closed (§15.5).
Endless (R3) can proceed — it inherits a scoring/pacing model that now holds. Kept as a record of
*why* each constant is where it is; **re-read the relevant subsection before re-tuning any of them.**

### 15.1 Pacing — ✅ RESOLVED (R2.8c): cascade fixed, re-measured, pacing satisfies the design rules

Original symptom: the tunnel bot won all 10 levels in **1.3–4.5 s**, never dropping below **95 %
air**. Removing the line gate (§5.7) left depth as the only win condition, and drilling straight
down is both the fastest path and the natural instinct.

Two of my original three "directions" were wrong and are retracted:
- ~~"Make boards wider"~~ — the win is vertical; a tunnel is a tunnel at any width.
- ~~"Raise drain"~~ *alone* — making a 2 s run costly needs ~20 %/s, which kills exploration.
  Drain only works **after** runs are long enough for it to act. The two levers are multiplicative.

#### What landed

1. **Depths tripled** (§9 table rewritten: 14-26 → 24-76 rows). The v2.1 line gate was the run's
   time sink; nothing replaced it. Even a human-paced player was spending only 9-25 % of their air.
   → descent went from 1.3-2.3 s to **2.4-7.1 s** (fast bot), 4.6-8.9 s (human-paced).
2. **Drain raised** 3/4 %/s → **4/7 %/s**, now that runs are long enough for it to bite.
3. **Restore economy corrected** — see below. The draft values assumed rare events.
   `+25 capsule / +5 burst / +3 bomb / +15 PC` → `+6 / +0.5 / +1 / +6`.

The harness now prints an air ledger (`drain -X caps +Y burst +Z`) so this is measurable, and
`RestoreEconomy_IsSmallRelativeToDrain` guards against the values drifting back up.

#### ⛔ Root cause — the burst cascade (NOT yet addressed)

Air *income* still outruns drain ~4× on level 10 even after the nerf, and the ledger says why:

```
lvl 10  drain -62.1   caps +204.0(34)   burst +39.0(78)
```

**78 bursts and 34 capsules in a 8.9 s run.** The board only *has* ~35 capsules — the run collects
nearly all of them, including ones nowhere near the player's tunnel.

The mechanism: a burst clears its chunk **plus a 1-cell shockwave ring** (§5.2). That much removal
destabilises the neighbours, which burst, which destabilise more — positive feedback. One tunnel
sets off a chain reaction that demolishes the board, and along the way it:

- liberates nearly every air capsule → air becomes free (§15.1)
- generates 78 × burst restore → air becomes free again
- **destroys the blocks before the player can drill them** → which is very likely why streaks
  stayed at 1-8 even after the vein fix (§15.3)

8.7 bursts per second is not "spectacle" (design rule 2), it is continuous demolition. **Further
tuning of restore values is treating a symptom** — the cascade needs a design decision first:

- shrink or remove the shockwave (it is what propagates the cascade),
- raise `BurstFallThreshold` from 2 to 3,
- or require the board to settle between bursts.

Each changes game feel materially, so this is a call for the designer, not a constant to twiddle.

#### ✅ Resolution (R2.8c / R2.8d, 2026-07-21)

The designer chose **silent settle-on-load** (`GravitySystem.Settle()`, §5.2 / R2.8d) over shrinking
the shockwave. The board now rests before play; only player-caused falls burst. Re-measured on the
settled board (the harness now calls `Settle()` too, matching GameBootstrap):

```
                           BEFORE (self-destructing)     AFTER (settled)
tunnel bot, lvl 10 bursts  78                            24
                 capsules  34                            17
air income vs drain        ~4×                           ~2×
```

With the cascade gone the pacing measures honestly, and it **satisfies design rules 5 and 6**:

- **Aggressive descent** (tunnel bot): WINS levels 1-9 with air to spare, crushed on 10 by gravity
  (fair, telegraphed). A perfect straight-down line is *meant* to be survivable — that is the skill.
- **Passive / greedy play** (row-clear bot, sweeps every row): dies to air or crush almost every
  level. Rule 5 ("passive = death") holds.
- The insight: the air clock is effectively a **tax on score-farming** (streaks / bursts / bombs all
  slow you down and cost air), not a tax on descending. That matches the arcade design, so the three
  landed fixes (depths ×3, drain 4/7 %/s, restore +6/+0.5/+1/+6) are kept as-is — **no further
  air/drain tuning.**

**One generator fix landed here:** level 7's `bombRate` (0.12/0.14 — the densest level) **walled the
player in** (bombs are not drillable) and produced *fewer* chains than levels 8-10. Lowered to
0.09/0.10; re-measured, level 7 went from **1 detonation / 0 chains** to **8 detonations / 4 chains
(×3), 13 % bomb points** — it finally teaches what it is themed on (§15.4).

> **Residual, not blocking:** a pure speedrunner still ends levels 1-9 at 73-99 % air — air is nearly
> decorative for the optimal line. Left as-is on purpose (see the "tax on score-farming" insight); if
> Endless (R3) wants more descent pressure, that is the depth-based drain ramp's job (R3.2), not a
> campaign retune.

### 15.2 Perfect Clear pays for the whole game — ✅ CASCADE REMOVED, judgment call remains

**The diagnosis.** The v3 draft specified `500 × cascade`, contradicting design rule 7's flat
"+500". Measured, cascades were averaging **×4–×7**: level 7 scored 45 427 points from 13 Perfect
Clears — ~3 494 each. The base was never the problem; the multiplier was.

**The fix.** `AwardPerfectClear` is now **flat `PerfectClearBase`**. Rule 7 is listed
non-negotiable, so it wins over the §8 table. The cascade step is still reported in
`ScoreEvent.Detail` (the HUD popup and chain SFX read it) — it just no longer scales the points.

#### Measured effect — row-clear bot, points mix (`% streak / burst / bomb / depth / perfect`)

| lvl | before | after |
|---|---|---|
| 7 | 0/ 2/ 4/ 1/**91** | 7/14/14/ 9/**53** |
| 8 | 0/ 6/ 0/ 8/**84** | 1/13/ 0/21/**64** |
| 9 | 0/10/ 0/ 8/**80** | 1/20/ 0/20/**57** |
| 10 | 0/10/ 2/ 6/**79** | 1/25/ 0/16/**55** |

The runaway is gone. **Burst share roughly tripled** on late levels (8–10 % → 20–26 %) and **bombs
became visible for the first time** (0 % → 12–18 % on levels 6-7) — both simply by removing what
was drowning them out. Run totals fell ~60 % (lvl 7: 27 330 → 9 330), which is the point.

#### ⚠️ Remaining judgment call — NOT a data question

Perfect Clear is still **44–68 %** for the row-clear bot. But look at the two bots separately:

- **Tunnel bot (natural descent):** Perfect Clear is **0 %** on most levels, 37–42 % at worst.
- **Row-clear bot (deliberately farms void rows):** 44–68 %.

So PC is already rare for a player who just descends; it is only dominant for someone
*optimising* for it. Whether that gap violates rule 7 is a **design decision, not a measurement**:

- Cutting `PerfectClearBase` further (500 → ~250) would fix the farmer's mix, but it also devalues
  the rare jackpot the normal player earns maybe twice a run — punishing the intended experience
  to correct the degenerate one.
- The alternative is to make Perfect Clear *harder to farm* (a level-design / generator lever)
  rather than cheaper.

**Left at flat 500 pending that call.** Note this interacts with §15.1: the row-clear bot only gets
to farm because runs are slow and unpressured. Fixing pacing may resolve this on its own — do
§15.1 before touching the base again.

### 15.3 Drilling is not the reward — ✅ ROOT CAUSE FIXED (color veins), points mix still blocked

Design rule 1: "Drilling IS the reward. Every drill tap gives points (×streak)."

**Original measurement:** best streak 1–10 (usually 2–4), streak share of points falling to **0 %**
on later levels.

**Diagnosed cause:** `PickCell` rolled every cell's color independently, so a same-color run of 4+
essentially never occurred outside the authored level-2 vein. The streak system had no material.

#### The fix (implemented)

Color choice is now split out of the type roll:

- `PickCellType()` decides the **type** only (empty / steel / bomb / hard / air / *color slot*).
- `PickColor()` resolves the color by **looking at its neighbors**, with
  `VerticalColorCohesion = 0.75` and `HorizontalColorCohesion = 0.55`.
  Vertical is weighted higher because drilling straight down is the natural instinct.
  Expected vein length ≈ `1/(1-p)` ≈ 4, i.e. a routine ×4 streak.
- Inheritance respects the strate's `MaxColors`, so a 2-color band never inherits a `C`.

> **⚠️ The non-obvious part — `Build` threads a COLUMN MEMORY, not the previous row.**
> The first implementation passed the literal row above and **did not work**: with `EmptyRate` at
> 0.36 the inheritance chain was shredded by gaps, and level 8 still capped at a 3-run.
> `Build` now carries `char[] columnColors` — the last color placed in *each column*, however many
> empty / Hard / Steel cells sit in between. A vein survives holes **exactly as the player's streak
> survives streak-neutral cells** (§6.1). That symmetry is the point; do not "simplify" this back
> to the previous row.

#### Measured effect

| Metric (tunnel bot) | Before | After |
|---|---|---|
| Best streak | 1–5 (median 2) | 2–8 (median 4) |
| Biggest burst | 1–6 cells | up to **12** |
| Best bomb chain | rare | up to **8** |

Bigger chunks were the predicted side benefit, and they materialised — cohesion feeds Chunk Burst
and bomb chains for free.

**Still open:** the streak *share of points* barely moved (0–29 %) and Perfect Clear still takes
79–91 %. That is **not** a streak problem any more — it is §15.2 alone. This fix was necessary but
not sufficient; it makes the Perfect Clear nerf measurable instead of trading one imbalance for a
vacuum. **Do §15.2 next.**

Pinned by `GenerateRow_InheritsColorFromTheRowAbove`, `GenerateRow_CohesionNeverViolatesMaxColors`,
and `CampaignBoard_ProducesVerticalVeins_LongEnoughForStreaks` (which scores a column the way
StreakTracker would: only a *different* color breaks the run).

#### Two side effects to watch

1. **Levels 1 and 3 now end "crushed" at the 0.25 s cadence** (they used to be won). More mass per
   chunk = gravity is a real threat again. Healthy, or too punishing for onboarding? Undecided.
2. **Level 2 burst opportunity dropped to 4.6 %** (from 1.5 %, vs 10–15 % elsewhere) — veins
   redistribute the gaps. Consistent with bursts being taught at level 3, so not acted on.

### 15.4 Bursts are working; bombs are proven (R2.8e ✅)

Bursts fire well and the raised porosity does its job — **11–15 % of solid cells sit over a 2+ gap**
on levels 3-10 (levels 1-2 sit lower, which is fine: bursts are taught at level 3).
Since the §15.3 fix, burst *size* is up substantially (biggest burst 1–6 → up to 12).

**Bombs: verdict = the economy works; the old "0 %" was a bot artifact, not a design flaw.**
R2.8e added a `BombBot` to `tools/playtest` that deliberately digs at buried bombs to light fuses
(the tunnel/row bots avoid them, so they could never answer this). Measured, hunting bombs:

| lvl | bombs on board | detonations | chain events | best chain | bomb % of points |
|---|---|---|---|---|---|
| 6 | 11 | 6 | 0 | ×1 | 3 % |
| 7 | **41** | **1** | 0 | ×1 | 1 % |
| 8 | 25 | 8 | 3 | ×3 | **22 %** |
| 9 | 30 | 20 | 7 | ×3 | 3 % |
| 10 | 45 | 5 | 3 | ×4 | **33 %** |

So bombs detonate, **chain up to ×4**, and can be **22–33 % of a run's points** when actively
pursued. Design rule 3 ("bombs are friends") holds. The bot dies every run (it does not dodge its
own blasts — deliberate; the point was to measure payout, not survival), which caps the sample but
the chain data is real.

> **✅ Level 7 anomaly — FIXED (R2.8c).** Level 7 authored **41 bombs** (vs 11 on lvl 6, 25 on lvl 8)
> yet the hunter triggered only **1** detonation before air-death: bombs are not drillable, so the
> `bombRate` 0.12/0.14 band (the densest of any level) **walled the player in** and produced *fewer*
> chains than the lower-rate levels 8-10 — the opposite of the "bomb chains" intent. Lowered to
> 0.09/0.10; re-measured, level 7 went from **1 detonation / 0 chains** to **8 / 4 (×3), 13 % bomb
> points**. The table above is the pre-fix run.

**Side benefit measured here:** the R2.8d settle-on-load also tamed the burst cascade the old
§15.1 numbers were taken on. Tunnel bot, level 10: **78 bursts / 34 capsules → 24 / 17**, and air
income fell from ~4× drain to ~2×. The board no longer demolishes itself, so the remaining air
oversupply is now a straight pacing question (§15.1 / R2.8c), not a cascade artifact.

### 15.5 Wobble tuning — ✅ CLOSED (R2.8f): not answerable by non-dodging bots; keep the defaults

Re-ran on settled boards with the R2.8c pacing (runs are now 12-31 s, no longer "too short"). The
0.6 / 0.8 / 0.9 s comparison **still shows no self-crush reduction from a longer telegraph** — if
anything the longer wobble slightly *worsens* bot outcomes (row-clear bot, 0.15 s: lvl 1 goes
2♥ WIN → 2♥ WIN → 3♥ crushed as wobble rises; levels 2-3 the same shape).

**Why it can't be measured this way:** neither bot dodges on the telegraph. The row-clear bot keeps
drilling and stepping under chunks regardless of the wobble, so it never cashes in the extra dodge
window; a longer wobble just holds the chunk aloft longer and shifts the run's timing (swapping
crush deaths for air deaths). The telegraph's value is a **human-reaction** question (can a player
read and sidestep in 0.6 vs 0.8 s?), which a wobble-blind bot cannot answer.

**Decision:** keep the shipped values — **0.8 s on campaign 1-3, 0.6 s from level 4** (design rule 4).
A real answer needs a dodging bot or a human playtest; that is out of scope for the R2.8 balance
pass and belongs with playtesting, not the harness. §15.5 is closed as "measurement not applicable",
not as a tuning change.

### 15.6 Endless generator — `emptyRate = 0` bug (fixed 2026-07-23, ahead of R3.1)

Found while auditing the code against the GDD: `StrateGenerator.EndlessStrates` passed `0f` in the
`emptyRate` constructor slot, so **endless boards generated with zero porosity** — a solid wall.
With no gaps a chunk can never fall 2+ rows, so Chunk Burst would have been dead on arrival in
endless, and the board would be one undrillable-through mass.

Fixed by ramping the two v3-calibrated campaign constants with difficulty:
`empty = EarlyEmptyRate + (LateEmptyRate - EarlyEmptyRate) × difficulty` (0.28 → 0.36). **Not** the
GDD §7 "empty rate" column (25 %→12 %): those are the pre-balance numbers §15.3 already rejected as
too low for bursts (opportunity scales ~`EmptyRate²`).

Pinned by `EndlessBoard_ContentRows_HavePorosity` (asserts >10 % empty in the content rows — a loose
floor that survives seed variance but fails hard if `emptyRate` regresses toward 0). **The value was
provisional until R3.1 measured it — see §15.7.**

### 15.7 Endless steady state — ✅ MEASURED AND RETUNED (R3.1, 2026-07-26)

Endless generated its own rates (`EndlessStrates`), and every one of them was set by **estimate**:
the mode was not playable, so nothing had ever been measured. R3.1 taught `tools/playtest` to run
endless (a chain of segments sharing one score / air tank / hearts — the harness mirror of §5.15)
and compared the generated terrain against **the campaign, the only balance yardstick this project
has evidence for**.

#### The finding: deep endless was worse than any authored board

| Board | color cells | hard+steel | burst-ready¹ |
|---|---|---|---|
| campaign 10 (hardest authored) | **32 %** | 13 % | **13 %** |
| endless steady state (before) | **22 %** | **27 %** | 11 % |

¹ share of solid cells with 2+ empty cells stacked beneath — a *genuine* burst opportunity, not just any gap.

Two design rules were breaking at once:

- **Rule 1 ("drilling IS the reward").** Color blocks are the streak's raw material. At 22 %, two
  thirds of every solid cell was hazard — the streak starved exactly where a run is supposed to
  peak, and steel isn't even drillable, so it also forced ever more air-costly detours.
- **Rule 2 ("big chunks burst").** Burst opportunity *fell below* the campaign band and then flatlined,
  so the deepest boards offered the FEWEST bursts. Backwards for the mode whose entire payoff is spectacle.

The cause was simply that the estimated caps (hard 0.15 / steel 0.12) were higher than anything the
campaign ships, and porosity ramped to the campaign's 0.36 while hazards ramped past it.

#### The fix (endless-only constants — campaign strates untouched)

| Constant | Before | After |
|---|---|---|
| `EndlessMaxHardRate` | 0.15 | **0.10** |
| `EndlessMaxSteelRate` | 0.12 | **0.08** |
| `EndlessMaxBombRate` | 0.08 | **0.08** (kept — campaign 10 ships ~9 % bomb cells) |
| `EndlessLateEmptyRate` | 0.36 (`LateEmptyRate`) | **0.40** |

Endless should **end harder than the campaign, not stop being the same game** — late difficulty is
the *drain ramp's* job (§15.1: the air clock is a tax on score-farming, not on descending).

#### Measured effect

| | color | hard+steel | burst-ready |
|---|---|---|---|
| endless steady state (after) | 27.5 % | 18.6 % | **13.7 %** |

And behaviourally, tunnel bot over 3 seeds:

| | before | after |
|---|---|---|
| depth reached | 45 / 99 / 85 | **157 / 162 / 109** |
| seams crossed | 0 / 1 / 1 | **2 / 2 / 1** |
| bursts | 18 / 40 / 8 | **60 / 57 / 30** |
| score | 9.8k / 13.3k / 8.5k | **27.6k / 31k / 15k** |

Pinned by `EndlessSegment_AtSteadyState_KeepsColorMaterialForStreaks` (color > 24 %) and
`EndlessSegment_AtSteadyState_KeepsBurstOpportunity` (burst-ready > 11 %) — both averaged over 8
seeds so they survive seed variance but fail hard if the rates drift back.

> **Two honest limits on this pass.** (1) Every endless run now ends **"crushed"**, because neither
> bot dodges the wobble (§15.5) — more bursts means more falling mass, so that is expected and says
> nothing about human play. (2) The tunnel bot still ends deep runs at 63-77 % air even with drain
> ramped to ~9 %/s: for a pure speedrunner the air clock stays soft, the same residual §15.1 accepted
> for the campaign. If endless wants real descent pressure, the lever is the drain ramp's slope
> (R3.2 constants), not the terrain — but that needs a *dodging* bot or a human to judge.

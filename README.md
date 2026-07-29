# Hollow Lines

**A tiny pixel-art driller descends into a well that never ends.**

![Unity](https://img.shields.io/badge/Unity-6000.3-black?logo=unity&logoColor=white)
![C#](https://img.shields.io/badge/C%23-.NET-8A2BE2?logo=csharp&logoColor=white)
![Tests](https://img.shields.io/badge/NUnit%20tests-260%20passing-brightgreen)
![Platform](https://img.shields.io/badge/platform-PC%20(mobile%20stretch)-blue)
![Status](https://img.shields.io/badge/status-solo%20dev%2C%20in%20progress-yellow)

Drill streaks of the same color for rising combos, undermine huge chunks to make them
shatter on impact, chain bombs together for massive bursts, collect buried diamonds on
the way down — and find air pockets before you suffocate. Campaign teaches you the
ropes; Endless is the real game: how deep can you go?

<p align="center">
  <img src="Assets/Screenshots/Thumbnails/thumb_01_title.png" width="49%" alt="Hollow Lines title screen" />
  <img src="Assets/Screenshots/Thumbnails/thumb_04_burst.png" width="49%" alt="A chunk burst scoring +175" />
  <br/>
  <img src="Assets/Screenshots/Thumbnails/thumb_02_streak.png" width="49%" alt="A ×6 color streak" />
  <img src="Assets/Screenshots/Thumbnails/thumb_03_bombs.png" width="49%" alt="Three bombs armed for a sympathetic chain" />
</p>

---

## Table of contents

- [What is this](#what-is-this)
- [Core mechanics](#core-mechanics)
- [Game modes](#game-modes)
- [Engineering highlights](#engineering-highlights)
- [Project structure](#project-structure)
- [Getting started](#getting-started)
- [Running the tests](#running-the-tests)
- [Status / roadmap](#status--roadmap)
- [Credits](#credits)
- [License](#license)

---

## What is this

Hollow Lines is a casual arcade descent game — think **Mr. Driller**'s avatar-in-a-well
physics crossed with **Downwell**'s score-chasing depth run. You dig, blocks fall, chunks
of the same color fuse into rigid slabs that crash down and shatter, bombs chain into each
other, and the well never stops going down.

The design went through a full mechanical redesign (v2.1 → v3, documented in
[`hollow-lines-gdd-v3.md`](hollow-lines-gdd-v3.md)) after early prototyping showed that
the original "clear a full row of empty cells" loop was too cerebral for the arcade feel
the game was chasing. v3 rebuilt scoring around three things that reward *every* drill
tap instead of an abstract goal on top of it:

> Every drill tap is the reward, not a means to some other goal. Spectacle and score are
> aligned — the biggest, loudest plays are also the highest-scoring ones.

## Core mechanics

| System | What it does |
|---|---|
| 🎨 **Color Streak** | Drilling the same color back-to-back multiplies your points. Colors generate in *veins*, not random noise, so a straight-down dig routinely builds a ×4–×8 streak. |
| 💥 **Chunk Burst** | Same-color blocks fuse into rigid chunks. Undermine one and it falls — 2+ rows of fall distance and it shatters on impact for a big payout, plus a shockwave that frees capsules, diamonds, and arms nearby bombs. |
| 🧨 **Bomb Bonanza** | Buried bombs arm when you drill next to them. Chain several together for a rising multiplier — bombs liberate air and diamonds instead of destroying them, so they're a tool, not a hazard. |
| 💎 **Diamonds** | Scattered on the way down, never behind an unbreakable wall, never requiring backtracking. From campaign level 4 on, collecting all of them is required to clear the level — routing tension between speed and thoroughness. |
| 🕳️ **Depth** | Every new deepest row banks points on its own. The well only ever pulls you forward. |
| ⭐ **Perfect Clear** | Clearing a full row of empties is a rare secret jackpot (+500 flat) — not the core loop, just a bonus for players who go looking for it. |
| 💨 **Air** | Constantly draining, restored by capsules, bursts, and bomb chains. Passive play is a slow death; aggression keeps you breathing. |

## Game modes

- **Tutorial showcase** — a hand-authored, deterministic intro board that teaches every
  mechanic above in its own isolated chamber before you ever see a procedural level.
- **Campaign** — 10 hand-tuned procedural levels, each introducing one new mechanic
  (streaks → chunk bursts → air & diamonds → hard blocks → bombs → bomb chains → steel → …),
  ramping toward a dense final descent.
- **Endless** — no levels, no win condition: you play until the air runs out or the hearts
  do. Depth is the leaderboard metric, difficulty ramps continuously across a chain of
  generated segments, and the drain rate climbs the deeper you go.
- **Daily Dig** — an Endless run seeded from the calendar date (UTC), so every player gets
  the *same* well on the same day. Local best-of-day tracking today; a shared leaderboard
  is the last planned piece.

## Engineering highlights

This project doubles as an exercise in keeping a game's rules honest and measurable —
a few things worth calling out for anyone reading the code:

- **Pure C# core, zero `UnityEngine` dependency.** Every rule of the game — grid physics,
  gravity, scoring, air, bombs, diamonds, endless progression — lives under
  `HollowLines.Core` with no `MonoBehaviour`, no `Update()`, nothing Unity-specific. It
  compiles and runs standalone with plain `dotnet run`. Unity's `View/` layer is a thin,
  swappable shell on top: input in, sprites/SFX out.
- **260 NUnit tests**, run in-editor via PlayMode, covering every scoring path, every
  physics edge case (crush-vs-burst ordering, coyote-time falls, chain cascades), and
  full board generators — including deterministic tutorial boards run end-to-end against
  the *real* gravity and bomb systems, not mocks.
- **A headless bot playtest harness** (`tools/playtest/`) that compiles the actual game
  logic and drives it with several bot strategies (a straight-down tunneler, a row-clear
  farmer, a bomb hunter) to measure real balance data — air income vs. drain, points-by-
  source attribution, burst/bomb frequency. Several real imbalances were *found and fixed*
  this way (a burst shockwave cascade that demolished boards before the player arrived, a
  scoring path that quietly ate 80%+ of a run's points, a generator bug that made Endless
  terrain worse than the hardest campaign level) — see §15 of `CLAUDE.md` for the full
  paper trail of what was measured and why each number is where it is.
- **Procedural audio and VFX, not a big asset budget.** Every sound effect is synthesized
  at runtime (`SfxSynth` — sine tones, sweeps, layered noise, arpeggios); every particle
  effect reuses one generated sprite; a couple of small hand-written shaders (`FuseGlow`,
  `DiamondShine`) do additive glow and per-tile shimmer without a single texture. Block
  *sprites* are the one AI-generated exception, with a procedural fallback if the art is
  missing.
- **AI-assisted development workflow.** This project's `CLAUDE.md` is a living
  spec-and-decision-log written for AI coding assistants (and future-me) — every deviation
  from the original design gets a dated note explaining *why*, not just *what*.

## Project structure

```
Assets/_Project/
  Scripts/
    Core/     — pure C# game rules (see above), unit-testable outside Unity
    View/     — Unity MonoBehaviours: input, rendering, VFX, audio, UI
    Tests/    — NUnit suite (PlayMode)
  Art/Resources/
    Tiles/    — AI-generated block sprites (+ procedural fallback)
    Shaders/  — FuseGlow, DiamondShine
tools/
  playtest/   — headless .NET 8 console harness, compiles Core/*.cs directly
hollow-lines-gdd-v3.md   — full game design document
CLAUDE.md                — architecture, decision log, balance findings, refactor plan
```

See `CLAUDE.md` for the complete architecture breakdown, every system's API, and the
full history of design decisions and balance passes.

## Getting started

1. Install **Unity 6000.3.x** (or newer Unity 6.x) via Unity Hub.
2. Open this folder as a Unity project.
3. Open the `PrototypeGym` scene (`Assets/_Project/Scenes/`).
4. Press **Play**. The tutorial showcase runs first by default; the main menu also offers
   Campaign, Endless ("Sans fin") and the Daily Dig ("Défi du jour").

**Controls:** A/D to walk left/right (Q/D on AZERTY), arrow keys to drill in any of the
four directions — or an Xbox controller, where the face buttons drill toward their
physical position (Y↑ A↓ X← B→) and the left stick walks. Esc or Start pauses.

## Running the tests

- **NUnit suite (primary):** `Window ▸ General ▸ Test Runner` in the Unity Editor, switch
  to the **PlayMode** tab, Run All. 260 tests, a few seconds.
- **Headless playtest harness (behavioral/balance):**
  ```bash
  cd tools/playtest
  dotnet run
  ```
  Compiles the real `Core/*.cs` sources directly (no Unity required) and runs several bot
  strategies through campaign, endless, and bomb-hunting scenarios, printing score/air/
  balance breakdowns.

## Status / roadmap

- ✅ Core loop — drilling, gravity, chunk fusion, color streaks, chunk bursts, bomb chains
- ✅ Campaign (10 levels + tutorial showcase)
- ✅ Endless mode + Daily Dig (date-seeded)
- ✅ Diamonds — collection, campaign win gate, Endless bonus
- 🔲 Enemies (Crawler / Digger / Tank) — killed by physics (bursts, bombs), not combat
- 🔲 Online leaderboard for Endless / Daily Dig (separate ASP.NET Core + PostgreSQL project)
- 🔲 Mobile port (stretch goal)

## Credits

Solo development by **Frédéric Lévesque** (design, code, and — with AI-assisted
generation for placeholder art — the tile sprites). Design DNA: *Mr. Driller*'s
avatar-in-well physics × *Downwell*'s score-chasing depth run.

## License

No license has been chosen for this project yet — all rights reserved by default until
one is added. If you're seeing this on GitHub and want to use or reference the code,
reach out first.

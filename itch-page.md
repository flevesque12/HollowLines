<!-- PAGE METADATA — for itch.io's form fields, not the description body. Delete before pasting. -->

- **Title:** Hollow Lines
- **Short description (tagline):** A tiny pixel-art driller falls down a well that never ends. Drill, shatter, chain, survive.
- **Genre:** Action / Arcade
- **Tags:** arcade, score-attack, pixel-art, destruction, endless, physics, singleplayer, unity, driller, 2d
- **Platform:** Windows (PC)
- **Input:** Keyboard, Xbox / XInput gamepad
- **Release status:** In development
- **Interface language:** French

---

# Hollow Lines

**An arcade action-descent: drill down, never stop.**

You're a tiny pixel-art driller descending a seven-column well that keeps going. Drill
straight down for rising color combos. Undermine huge slabs so they shatter on impact.
Chain buried bombs into each other. Blow up Boomers to amplify the wreckage. Grab diamonds
on the way past — all while your air runs out.

This is a score-chaser, not a puzzle game. **Nothing ever asks you to stop descending.**
The biggest, loudest plays are also the highest-scoring ones. Campaign teaches you the
ropes; Endless is the real game: how deep can you go?

---

## Controls

Everything is playable on keyboard or on an Xbox / XInput pad. The four face buttons drill
toward their **physical position** on the diamond, so you don't need a legend: Y is up,
A is down, X is left, B is right.

### Playing

| Action | Keyboard | Gamepad |
|---|---|---|
| Walk left / right | `A` / `D` (or `Q` / `D`) | Left stick or D-pad |
| Drill **up** | `↑` | **Y** |
| Drill **down** | `↓` | **A** |
| Drill **left** | `←` | **X** |
| Drill **right** | `→` | **B** |
| Pause / resume | `Esc` | **Start** |

### Menus

| Action | Keyboard | Gamepad |
|---|---|---|
| Navigate | Arrow keys | D-pad / left stick |
| Select | `Enter` | **A** |
| Back / cancel | `Esc` | **B** |

You drill the adjacent cell in whichever direction you press, one tap at a time. Falling is committed — but there's a
short grace window at the start of a drop where you can still catch an adjacent ledge, so
walking off an edge is never an instant death sentence.

---

## Game modes

**Tutorial** — a hand-built intro well that teaches every mechanic in its own isolated
chamber before you ever meet a procedural level. Short, and it hides one secret worth +500.

**Campaign** — 10 tuned levels, each introducing one new idea: color streaks, chunk bursts,
air capsules and diamonds, hard blocks, bombs and Crawlers, bomb chains and Boomers, steel,
then a dense final descent. To clear a level you need to reach the bottom **and** meet a
score minimum — enough to make you use the scoring systems, never enough to make you
backtrack for them.

**Endless** — no levels, no win condition. You play until the air runs out or the hearts do.
Depth is the score that matters, the terrain keeps getting meaner, and the air drains faster
the deeper you dig.

**Daily Dig** — an Endless run seeded from the calendar date, so every player in the world
gets the *same* well on the same day. Your best depth and best score for the day are kept
locally.

---

## How you score

Every drill tap pays. Nothing here is a chore you do to unlock the fun part.

- **Color Streak** — drilling **downward** on the same color back-to-back multiplies your
  points. Colors generate in veins, not random noise, so a straight dig routinely builds a
  ×4–×8 streak. Sideways and upward drills are neutral: they never break it, and they never
  build it. The streak rewards descending; it never asks you to detour.
- **Chunk Burst** — same-color blocks fuse into rigid slabs. Undermine one and it falls; if
  it drops two or more rows it **shatters on impact** for a big payout and sends out a
  shockwave that frees capsules and diamonds, lights nearby fuses, and flattens anything
  living in the blast. Bigger slab, longer fall, bigger number.
- **Bomb chains** — buried bombs arm when you drill next to them, then burn a 1.5 s fuse
  with an accelerating beep and flash. Set off several at once for a rising multiplier.
  Bombs *liberate* air capsules and diamonds instead of destroying them, so they're a tool,
  not a trap. Arming one by hand gives it a tight blast; one set off by a chain reaction
  hits much wider.
- **Depth** — every new deepest row banks points on its own. The well only pulls you forward.
- **Diamonds** — free candy on the way down. Never behind an unbreakable wall, never
  required. Grab one if it's on your path, ignore it if it isn't.
- **Enemies** — you never fight them; you drop things on them. See below.
- **Perfect Clear** — wipe out a full row and you get a rare flat jackpot. It's a secret for
  players who go looking, not the loop.

### Scoring at a glance

| Action | Points |
|---|---|
| Drill a block | 10 × your current streak |
| Chunk Burst | blocks in the slab × 25 × fall bonus |
| Bomb blast | blocks destroyed × 25 × chain multiplier |
| New deepest row | 50 |
| Diamond | 150 |
| Crawler killed | 100 × bonus |
| Boomer killed | 150 × bonus, plus whatever its explosion destroys |
| Perfect Clear | 500 |

A kill's *bonus* is inherited from whatever did the killing — the fall bonus of the slab
that flattened it, or the multiplier of the bomb chain that caught it. Kill a Boomer with a
five-bomb chain and every part of that payout multiplies.

### Air at a glance

Air drains constantly — gently on the first campaign levels, faster from level 4 on, and in
Endless it keeps climbing the deeper you get. You put it back by playing:

| Source | Air restored |
|---|---|
| Every drill | +0.5% |
| Air capsule | +6% |
| Chunk Burst | +0.5% |
| Bomb chain | +1% per bomb |
| Perfect Clear | +6% |

---

## The well

**Blocks**

- **Colored blocks** — one drill tap. Fuse with their neighbours into rigid falling slabs.
- **Hard blocks** — two taps. They crack first, then break. Never fuse.
- **Steel** — your drill can't touch it. Only a blast will soften it.
- **Air capsules** — a big gulp of air. Drill them, or free them with a bomb or a shockwave.
- **Bombs** — not drillable. Drill *beside* one to light it, or drop a slab on it.
- **Diamonds** — one tap, pure bonus, and physics can never destroy one. A falling slab
  lands on top of it and the diamond survives underneath, waiting for you.

**Air** is always draining, and **every single drill puts some back** — on top of capsules,
bursts and bomb chains. Standing still to plan is what kills you. Drilling *is* survival.

**Hearts** — you get three, plus a moment of invulnerability after each hit. A slab about to
fall wobbles first, so a crush is always telegraphed and always dodgeable. Deaths are earned,
not sprung on you.

---

## Enemies

Two kinds, both buried and completely inert until you drill nearby. Neither one blocks the
way down, and neither is a puzzle to solve.

- **Crawler** — wakes up and shuffles along its row, bouncing off walls. It'll cost you a
  heart if it reaches you. One hit from anything — a landing slab, a burst, a blast — kills it.
- **Boomer** — never moves and never touches you. But kill one and it **explodes**, wrecking
  more blocks and potentially setting off other Boomers nearby. A chain reaction is capped so
  it can't run away with the level.

Boomers amplify whatever you were already doing. Landing a slab on a cluster of them is the
single most spectacular thing in the game.

---

## Tips

- Drill **straight down** whenever you can. It builds the streak, it banks depth, and it
  keeps you breathing. Sidestep only when something is in the way.
- Don't dig *around* a big slab — dig *under* it. A slab that falls two rows is worth far
  more than the blocks you'd have drilled by hand.
- Bombs are friends. Light one and step one cell off its row or column — that's all the
  dodge you need.
- Never stop to plan. The air clock is a tax on standing still, not on descending.

---

## Good to know

- **The game's interface is in French.** The controls are the same either way and the game
  is almost entirely visual, so it plays fine without the language — but you've been warned.
- Made solo in Unity 6.
- Every sound effect is synthesized at runtime — there isn't a single audio file in the
  project. The glow, shimmer and exit-light effects are hand-written shaders.
- In development. A shared online leaderboard for Endless and the Daily Dig is the next
  planned piece.

---

*Made by Frédéric Lévesque.*
*Inspired by Mr. Driller and Downwell.*

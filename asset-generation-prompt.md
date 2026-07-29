# HOLLOW LINES — Asset Generation Prompt

> Paste **Section 1** into any asset / concept-art generator as the game brief.
> Section 2 is a shopping list of individual asset prompts you can send one at a time.

---

## 1. Master brief (paste this)

**Game:** *Hollow Lines* — a casual pixel-art arcade descent game for PC (Unity 6).

**Pitch:** You play a tiny pixel-art driller descending through layers of colored blocks in a
narrow vertical well. You drill streaks of the same color for rising combos, undermine huge
chunks of rock so they shatter on impact, chain bombs together for massive bursts, and hunt
air capsules before you suffocate. It's a score-chasing dig: how deep can you go before your
air or your three hearts run out?

**Tone & feel:** visceral, punchy, arcade. Every drill tap is rewarded with points, particles
and a rising pitch. Spectacle and score are aligned — big falling slabs, shockwaves, chained
explosions. Not cerebral, not puzzle-like. Claustrophobic underground pressure with bright,
readable, candy-colored terrain against near-black voids.

**References:** *Mr. Driller* (avatar in a well, chunk gravity, air meter), *Downwell*
(score-chasing vertical descent, high-contrast readability), classic 16-bit arcade pixel art.

**Art style:**
- Pixel art, low resolution per tile (target 16×16 or 32×32 px per grid cell), crisp, no
  anti-aliasing, hard pixel edges, limited palette per sprite.
- Readability first: every block type must be identifiable at a glance, at small size, in
  motion, on a dark background.
- Chunky black or very dark outlines, strong internal shading (2–3 shades per material).
- Side-view / straight-on orthographic perspective — no isometric, no perspective distortion.

**Playfield layout:** a **7-column-wide vertical shaft**, seen from the side, extending
downward for 24 to 76 rows. The camera follows the driller down the shaft, showing about 16
rows at a time. Left and right of the shaft are solid rock walls (currently undecorated —
decorative wall/border art is wanted). Empty cells are near-black (#0D0A08).

**Terrain block types and their established colors:**

| Block | Color (hex) | Notes |
|---|---|---|
| Color A block | amber / orange `#F09E26` | ~40% of terrain, 1 drill tap |
| Color B block | teal / green `#1C9E75` | ~30% of terrain, 1 drill tap |
| Color C block | pink / magenta `#D4547D` | ~20% of terrain, 1 drill tap |
| Hard block | brown `#8C7359` | 2 taps; cracked state is darker `#664D38` |
| Steel block | blue-grey `#B3BFCC` | indestructible by drill; only softened by explosions |
| Air capsule | cyan `#59D9F2` | glowing pickup, refills the air meter |
| Bomb | red-orange `#F24019` | armed by drilling nearby; lit fuse telegraph |

Same-color adjacent blocks visually fuse into large rigid **chunks** that fall as one slab.
Terrain generates in **veins** — long runs of one color snaking down through the shaft — so
the player reads the board as colored routes to follow.

**The avatar:** a tiny pixel-art driller occupying exactly one grid cell (1 cell tall).
Chunky helmet, headlamp, visible drill. Reads clearly as a person even at 16×16. Needs to
face left/right and drill in four directions (up, down, left, right). At high color streaks
the avatar glows in the current streak color and trails particles.

**Key visual moments to illustrate:**
1. **Wobble telegraph** — a huge multi-block slab shaking in place for 0.6–0.8 s before it drops.
2. **Chunk Burst** — a slab that fell 2+ rows shattering on impact into a spray of debris in
   its own color, plus a 1-cell shockwave ring erasing neighboring blocks.
3. **Color Streak** — the driller cutting straight down a long vein of one color, glowing,
   with a "×8" combo popup.
4. **Bomb chain** — several bombs detonating sympathetically, screen flash, liberated cyan
   air capsules floating free.
5. **Perfect Clear** — a rare full-width empty row collapsing, everything above shifting down.

**HUD elements needed:** score counter, depth readout ("DEPTH: 248"), a draining air bar,
three hearts, a color-tinted streak multiplier ("×12"), and centered burst / chain / perfect-clear
popups. Style: pixel bitmap font, chunky, arcade, high contrast.

**Palette direction overall:** near-black voids and dark browns for rock, with saturated amber /
teal / pink terrain and cyan glow accents. Warm underground lighting, cool cyan for air and
safety, red-orange for danger.

**Deliverables I'm looking for:** concept art, key art / title screen illustration, tilesets,
character sprite sheets, VFX sheets, UI/HUD kit, and store-page capsule art.

---

## 2. Individual asset prompts

Send these one at a time; each already assumes the style block above.

### 2.1 Key art / title screen
> Pixel-art key art for an arcade digging game called *Hollow Lines*. A tiny helmeted driller
> with a headlamp descends a narrow vertical shaft of glowing amber, teal and pink blocks. A
> massive slab of rock shatters below him in a spray of colored debris; a cyan air capsule glows
> in the dark. Dramatic vertical composition, deep perspective downward into blackness, warm
> underground lighting, high contrast, 16-bit arcade pixel art, chunky outlines, limited palette.

### 2.2 Terrain tileset
> Pixel-art tileset, 32×32 tiles, side-view underground blocks for a digging game. Include:
> amber block (#F09E26), teal block (#1C9E75), pink block (#D4547D), brown hard block (#8C7359)
> and its cracked variant (#664D38), blue-grey indestructible steel block (#B3BFCC), glowing cyan
> air capsule (#59D9F2), red-orange bomb with a fuse (#F24019), and an empty dark cavity tile.
> Each tile must be seamlessly tileable with itself, readable at small size, chunky dark outline,
> 2–3 shades of internal shading. Present as a labeled sheet on a dark background.

### 2.3 Driller character sprite sheet
> Pixel-art character sprite sheet, 16×16 (and a 32×32 variant), of a tiny driller: chunky helmet
> with a glowing headlamp, goggles, jumpsuit, handheld drill. Frames needed: idle (2), walk
> left/right (4 each), drill up, drill down, drill left, drill right (3 frames each), falling,
> hurt/flash, death. Readable silhouette at 16 px, chunky dark outline, limited palette,
> side-view orthographic. Transparent background, labeled grid layout.

### 2.4 VFX sheets
> Pixel-art VFX sprite sheets on transparent background, arcade style:
> (a) block shatter burst — colored debris chunks radiating outward, 6 frames, in amber, teal and
> pink variants; (b) expanding shockwave ring, 5 frames, 1-cell radius; (c) drill impact spark,
> 4 frames; (d) bomb explosion, 8 frames, red-orange to white; (e) pulsing bomb fuse halo ramping
> calm red → hot yellow → white, 6 frames; (f) cyan air capsule pickup sparkle, 5 frames;
> (g) dust puff on landing, 5 frames.

### 2.5 HUD / UI kit
> Pixel-art arcade HUD kit on a dark background: a vertical or horizontal air meter that drains
> (cyan fill, dark chunky frame), three heart icons (full / empty), a score panel, a "DEPTH: 000"
> readout, a color-tintable streak multiplier badge ("×12"), and celebratory popup banners reading
> "BURST!", "CHAIN ×3!" and "PERFECT CLEAR!". Chunky bitmap font, thick borders, high contrast,
> 16-bit arcade feel.

### 2.6 Shaft walls / background
> Pixel-art vertical side-wall borders and parallax background for a 7-column-wide mining shaft.
> Layered rock strata, embedded pipes, rusted support beams, dangling cables, faint glowing
> minerals. Must tile seamlessly vertically, stay visually quiet so foreground blocks pop, dark
> desaturated browns and greys, subtle warm lamp glow. Provide 3–4 depth variants that get darker
> and more ominous as they go deeper.

### 2.7 Logo / capsule art
> Pixel-art game logo for "HOLLOW LINES": chunky arcade lettering carved out of rock, drill bit
> or crack motif cutting through the letters, amber and cyan accents, dark background. Also
> provide a 1:1 store capsule composition using the same logo over the descending-driller scene.

### 2.8 Mockup screenshot (concept)

> Concept mockup of a gameplay screenshot: a 7-column vertical shaft filled with amber, teal and
> pink pixel blocks in snaking veins, a tiny driller mid-shaft glowing amber with a "×8" combo
> popup, a huge teal slab shaking above him, a cyan air capsule two rows down, a red bomb with a
> lit fuse to the side. HUD along the top: score, DEPTH: 148, draining cyan air bar, three hearts.
> 16-bit arcade pixel art, dark background, high contrast, 16:9.

---

## 3. Android / mobile addendum

Mobile is a stretch goal (GDD §1), but the 7-column shaft was chosen to be portrait-friendly
(GDD §3.1) — so the art above ports well. **Append this block to the master brief when
generating for mobile**, and add the extra assets in §3.2.

### 3.1 Extra constraints to paste alongside Section 1

> **Also targeting Android phones in portrait orientation (9:16 to 9:21, 1080×1920 and up).**
> The 7-column shaft fills the screen width edge-to-edge in portrait, so side-wall decoration is
> minimal or absent. Constraints:
> - Deliver every sprite at **4× the base tile size** (64×64 source for a 16×16 design, 128×128
>   for a 32×32 design), authored on the pixel grid so it downscales cleanly by integer factors.
>   No anti-aliasing, no soft edges — assets are rendered with point/nearest filtering.
> - Readability at arm's length on a 6" screen is the hard requirement: fewer, larger shapes;
>   no 1-pixel details that vanish; silhouettes distinguishable without color (colorblind-safe
>   shape or pattern differences between the amber, teal and pink blocks).
> - **The bottom ~25% of the screen is covered by the player's thumbs.** Nothing gameplay-critical
>   is drawn there. HUD lives at the top, under a safe-area inset for notches and punch-holes.
> - HUD re-stacks vertically for a narrow screen: score and depth on one top row, air bar as a
>   full-width horizontal bar beneath it, hearts and streak badge on the next row.
> - Touch controls needed (see below) — there is no hover state, and every tap target must be at
>   least 48×48 dp.

### 3.2 Mobile-only assets to request

**Touch control kit**
> Pixel-art touch control overlay for a portrait mobile digging game. A four-direction drill pad
> (up / down / left / right arrows arranged in a diamond, each a chunky drill-bit icon) sized for
> a thumb, plus separate left/right walk buttons. Provide normal, pressed and disabled states.
> Semi-transparent chunky frames so the terrain stays visible underneath, thick dark outlines,
> arcade pixel style, designed to sit in the bottom corners of the screen. Also provide swipe-gesture
> tutorial overlays: a pixel-art hand with a directional arrow showing "swipe down to drill".

**App icon**
> Android adaptive app icon for a pixel-art digging game called *Hollow Lines*. 432×432 px with
> all key content inside the central 264 px safe circle. Foreground: the tiny helmeted driller with
> a glowing headlamp, drill raised, seen head-on. Background layer: dark rock strata in amber and
> deep brown. Bold, readable at 48 px, high contrast, chunky pixel art. Deliver foreground and
> background as separate layers with transparency.

**Play Store graphics**
> (a) Google Play feature graphic, 1024×500, landscape: the *Hollow Lines* pixel logo on the left,
> the driller descending a shaft of amber / teal / pink blocks with a shattering slab on the right,
> dark background, room for the logo to stay legible when cropped.
> (b) Four portrait store screenshots, 1080×1920, showing gameplay mockups: a long color streak
> with a "×8" popup, a chunk burst mid-shatter, a bomb chain with a screen flash, and the run-summary
> screen with DEPTH as the headline.

**Portrait mockup**
> Concept mockup of a portrait mobile gameplay screenshot, 1080×1920. A 7-column vertical shaft of
> amber, teal and pink pixel blocks filling the full screen width, a tiny glowing driller mid-screen,
> a huge slab shaking above him, a cyan air capsule below. Top HUD: score and "DEPTH: 148" on one row,
> a full-width draining cyan air bar, then three hearts and an amber "×8" badge. Bottom corners: a
> semi-transparent four-way drill pad on the right, walk buttons on the left. 16-bit arcade pixel art,
> dark background, high contrast.

### 3.3 What this does NOT solve

The touch **control scheme itself is undesigned** — GDD §3.1 only says the grid is portrait-friendly,
and §16 covers keyboard + gamepad only. Generating a drill-pad asset does not answer whether drilling
should be buttons, swipes, or tap-the-target-cell. Decide the scheme before commissioning the final
control art, or you'll pay for it twice.

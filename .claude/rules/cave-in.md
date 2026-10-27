---
paths:
  - "**/CeilingSystem*"
  - "**/AirSystem*"
  - "**/CellType*"
  - "**/GravitySystem*"
  - "**/BombSystem*"
  - "**/EnemySystem*"
  - "**/ScoreSystem*"
  - "**/DeathTracker*"
  - "**/DeathCause*"
  - "**/CampaignManager*"
  - "**/EndlessManager*"
  - "**/StrateGenerator*"
  - "**/GameBootstrap*"
  - "**/HUDView*"
  - "**/VfxManager*"
  - "**/AudioManager*"
---

# Hollow Lines — L'Effondrement (v3.3, design)

> **Statut : DESIGN ACCEPTÉ (2026-10-27) — implémentation R8 en cours** (plan : `refactoring-plan.md` §R8).
> Remplace l'air (le chronomètre de suffocation) par une pression spatiale : le puits s'effondre derrière
> toi. Les capsules d'air restent et **font reculer le front**. Les décisions du §9 sont tranchées.
>
> **🔄 2026-10-27 — le bloc de gaz est abandonné** (dev : « la mécanique de gaz ne m'intéresse pas »).
> `GasSystem` (R8.3) et les events `GasReleased` / `NotifyGas` (R8.4) sont retirés du code ; la capsule
> d'air garde sa place et devient un **recul** du front (§4). L'Effondrement seul porte la pression.

---

## 1. Pourquoi

L'air faisait deux jobs : **forcer la descente** (règles 5, 6, 10) et **courber la difficulté de l'Endless**
(drain 5 → 10 %/s). Mais il était invisible : une barre en bas de l'écran. Et le harness l'a mesuré
« décoratif » pour tout joueur plus rapide qu'un débutant (§15.1, §15.8, §15.9) — il ne taxait que l'hésitation.

L'Effondrement garde **exactement la même équation** (s'arrêter = mourir, descendre = survivre), mais
la rend **visible dans le monde** : la menace est au-dessus de toi, elle descend, tu la vois arriver.
C'est la règle 6 (« depth is always forward ») transformée en mécanique.

---

## 2. Le modèle : la marge, c'est la nouvelle air

```
        ▓▓▓▓▓▓▓  ← EFFONDREMENT (rangées avalées = débris, rien ne bouge plus)
        ▓▓▓▓▓▓▓  ← front : CeilingRow (float, en rangées ABSOLUES)
          ...      ┐
          ...      │  MARGE = AvatarRow − CeilingRow
          ...      │  plafonnée à MaxLead (12 rangées)
           @       ┘  ← l'avatar
```

| Air (aujourd'hui) | Effondrement |
|---|---|
| `Air` 0-100 % | `Lead` = marge en rangées, 0-`MaxLead` → affiché en % (`Lead / MaxLead`) |
| `MaxAir` = 100 | **`MaxLead` = 12 rangées** (≈ les ¾ de la vue de 16 rangées) |
| Drain %/s | **Vitesse** rangées/s |
| Restore (capsule, burst…) | **Recul** du front (rangées) |
| Air à 0 → suffocation | Front atteint l'avatar → **−1 cœur** |
| Danger Zone : air < 15 % | Danger Zone : **marge < 15 % ≈ 2 rangées** |
| Start buffer R6.2 (3-5 s sans drain) | Le front **attend** 3-5 s avant de bouger |

### 2.1 La laisse (`MaxLead`) — pourquoi c'est essentiel

Sans plafond, un joueur rapide accumule 40 rangées d'avance et l'effondrement devient décoratif —
**exactement** le problème mesuré sur l'air. Donc : si la marge dépasse `MaxLead`, le front est
**rattrapé instantanément** à `AvatarRow − MaxLead`. C'est l'équivalent de `MaxAir = 100 %` : on ne
« banque » pas d'avance. Résultat : le front est toujours **à l'écran** (12 rangées sur 16 visibles),
la menace se voit en permanence, même pour le speedrunner.

### 2.2 Calibrage de départ — traduit directement de l'air mesuré

Temps pour mourir à l'arrêt = `MaxLead / vitesse` = `100 % / drain`. Donc **`vitesse = drain × MaxLead / 100`**,
et toute la balance §15 se transpose sans repartir de zéro :

| Contexte | Drain air | **Vitesse du front** (MaxLead 12) | Mort à l'arrêt |
|---|---|---|---|
| Campagne 1-3 | 4 %/s | **0,48 rangée/s** | 25 s |
| Campagne 4-10 | 7 %/s | **0,84 rangée/s** | 14 s |
| Endless départ | 5 %/s | **0,60 rangée/s** | 20 s |
| Endless plafond (prof. 200+) | 10 %/s | **1,20 rangée/s** | 10 s |

Formule Endless : `CeilingSpeedForDepth(d) = (5 + d/20 × 0,5) × 0,12`, plafonnée à 1,2. ✅ **R8.7 : mesurées au
harness et gardées telles quelles** (dev, 2026-10-27 — `balance.md` §15.11) : même pression que l'air, le front taxe
l'hésitation et le farming, jamais la descente.

### 2.3 Ce qui fait reculer le front (règle 5 : l'agression récompense)

Chaque ancienne source d'air devient un **recul** du front, aux mêmes proportions (`recul = restore × MaxLead / 100`) :

| Source | Restore air | **Recul** |
|---|---|---|
| Forage (n'importe lequel) | +0,5 % | **aucun** — descendre EST le gain (forer vers le bas = +1 rangée de marge) |
| Chunk Burst | +0,5 % | **+0,06** → arrondi à **+0,1 rangée** |
| Bombe en chaîne (par bombe) | +1 % | **+0,12 rangée** |
| Perfect Clear | +6 % | **+0,75 rangée** (et compense le décalage du §6.4) |
| Capsule d'air (forée ou libérée) | +6 % | **+0,75 rangée** (`CapsuleRecoil` — §4) |
| 🆕 Power Drill | — | **+0,5 rangée** (nouveau : le ×6 « repousse » le puits) |

> Le forage latéral ne recule rien : il coûte du temps et la marge fond. C'est voulu (règle 6) — mais
> c'est **plus dur** que l'air, où chaque forage latéral rendait +0,5 %. À surveiller au harness.

---

## 3. L'Effondrement dans le monde

### 3.1 Les rangées avalées deviennent des débris (Core)

Quand le front franchit une rangée (`floor(CeilingRow)` augmente), **chaque cellule de cette rangée devient
`CellType.Rubble`** — solide, non forable, ancrée (comme le bedrock : ne tombe jamais, ne fusionne avec rien).

Pourquoi le transformer en vraie cellule plutôt qu'un overlay visuel :
- **Rien ne peut tomber à travers le front.** Sans ça, un chunk au-dessus du front pourrait chuter
  dans la zone de jeu — absurde à l'écran, et un écrasement « injuste » (règle 4).
- Ce qui est avalé est **perdu, proprement** : bombes désarmées (jamais d'explosion hors-jeu), ennemis
  retirés sans points, diamants et capsules perdus (règle 8 : bonus, jamais requis), fissures effacées.
- Les chunks **sous** le front avec du vide dessous tombent normalement : les débris ne « tiennent »
  rien par le haut (le support vient d'en bas, §4).

**✅ R8.2 — ce que la conversion appelle** (GameBootstrap, R8.6, à chaque `RowsSwallowed` mappé sur le board) :

| Appel | Effet |
|---|---|
| `GridModel.FillRows(from, to, CellType.Rubble)` | Rangées → `Rubble` (capsules, diamants, bombes compris — perdus sans event) |
| `BombSystem.DisarmRows(from, to)` | Mèches éteintes en silence — jamais d'explosion dans les débris |
| `EnemySystem.BuryRows(from, to)` | Ennemis retirés **sans** `EnemyKilled` (ni points, ni VFX) ; un Boomer n'explose pas |
| *(rien pour les fissures)* | Une fissure sur une cellule devenue `Rubble` lit déjà 0 (`CanFuse` faux) et est élaguée |

Et les règles que les systèmes appliquent d'eux-mêmes : `GravitySystem` ancre `Rubble` comme le plancher
(jamais de chute, `Settle` compris) ; shockwave, bombe et Boomer le laissent intact (cas `default` des
tables) ; **`CollapseSystem` ne compte pas `Rubble` comme « du solide au-dessus »** — sinon chaque rangée
de spawn vide sous les débris devenait un Perfect Clear gratuit (+500) qui faisait descendre les débris.

### 3.2 Contact avec l'avatar

Quand `CeilingRow ≥ AvatarRow` (le front touche la rangée de l'avatar) :
1. **−1 cœur** (i-frames 1,5 s comme tout dégât), `DeathCause.CaveIn` *(§9, décision F — `Collapse`
   est déjà pris par le Perfect Clear qui écrase)*.
2. Le front **recule de 4 rangées** (`HitRecoil`). Sinon il retouche l'avatar dès la fin des i-frames →
   mort garantie en 4,5 s, injuste (règle 4).
3. **Pas de téléport au spawn.** Le spawn est en haut du board, donc **dans les débris**.

> ⚠️ **Ça touche TOUS les dégâts, pas juste le front.** Aujourd'hui `OnAvatarCrushed` téléporte au `_spawn`
> après chaque coup (chunk, burst, bombe, Crawler). Avec l'effondrement, le spawn peut être avalé. Nouvelle
> règle de respawn : **l'avatar reste où il est** ; s'il est DANS un chunk (écrasé), il remonte à la
> première cellule vide au-dessus, mais jamais au-dessus de `CeilingRow + 2`. Décision B.

### 3.3 Spec Core — `CeilingSystem` (R8.1)

Pur C#, `HollowLines.Core`. Ne connaît ni la grille ni l'avatar : il reçoit la rangée **absolue** de
l'avatar à chaque tick, et annonce ce qui doit arriver (rangées avalées, contact) par events — c'est
GameBootstrap qui convertit en débris (R8.2) et qui retire le cœur (R8.6). Même contrat que `AirSystem`.

```
Constantes:
  DefaultMaxLead      = 12f    // rangées (décision D) — la laisse
  DefaultSpeed        = 0.60f  // rangées/s (= drain 5 %/s × 12 / 100, Endless départ)
  HitRecoil           = 4f     // rangées (décision C)
  DangerZoneThreshold = 15f    // % de la marge, comme l'air
  DefaultStartGrace   = 3f     // s — même start buffer que R6.2 (grace, puis rampe ×0.5 → ×1 sur 12 s)
  Reculs : BurstRecoil 0.1 / BombChainRecoilPerBomb 0.12 / PerfectClearRecoil 0.75 / PowerDrillRecoil 0.5
          / CapsuleRecoil 0.75

État:
  CeilingRow (float)   // front, rangée ABSOLUE (le bas des débris = floor(CeilingRow) − 1)
  SwallowedRows (int)  // nombre de rangées absolues déjà converties = toutes celles < floor(CeilingRow)
  Lead                 // AvatarRow − CeilingRow, clampée [0, MaxLead]
  LeadPercent          // Lead / MaxLead × 100  (la barre du HUD)
  IsDangerZone         // LeadPercent < 15

API:
  Reset(float avatarRow)        // front à avatarRow − MaxLead, marge pleine, pas d'event de rangée
  BeginStartBuffer(float grace) // identique à AirSystem
  Tick(float dt, int avatarRow) // avance (vitesse × buffer), laisse, contact, rangées avalées
  NotifyAvatarRow(int row)      // laisse sans avancer (chute libre / téléport / couture)
  Recoil(float rows)            // recule le front (peut remonter dans les débris — du temps, pas de l'espace)
  RestoreBurst() / RestoreBombChain(n) / RestorePerfectClear() / RestorePowerDrill() / RestoreCapsule()
  ShiftDown(int rows = 1)       // Perfect Clear : les débris descendent d'un cran (§5.2)
  Speed { get; set; }           // campagne/endless l'assignent (comme DrainRate)

Events:
  RowsSwallowed(int fromRow, int toRow)  // [from, to) absolues — à convertir en Rubble
  AvatarReached()                         // le front a touché l'avatar ; il a DÉJÀ reculé de HitRecoil quand ça fire
  LeadChanged(float percent)              // HUD
  DangerZoneChanged(bool)                 // comme AirSystem
```

**Le recul gagne du temps, jamais de l'espace.** Une rangée convertie en débris l'est pour de bon (la grille
a changé) : `SwallowedRows` ne redescend jamais. Mais `CeilingRow` (le front) **peut** reculer au-dessus du
bas des débris — il « remonte dans les gravats » et doit les retraverser avant d'avaler une nouvelle rangée.
La marge (et la barre du HUD) se lit sur `CeilingRow` : c'est le **temps** avant le prochain contact, pas la
distance aux débris. Les débris eux-mêmes sont inoffensifs (solides, comme du Steel) ; seul le front frappe.

> Pourquoi pas « le recul est borné par les débris » (premier jet) : au moment où le front touche l'avatar,
> les débris sont forcément juste au-dessus de lui. Un recul borné vaudrait donc toujours ~0 et la décision C
> (recul de 4 rangées) ne servirait à rien — le joueur reprendrait un coup dès la fin de ses i-frames.

**Le contact n'avale jamais la rangée de l'avatar.** Ordre dans `Tick` : avancer → laisse → **contact**
(`CeilingRow ≥ AvatarRow` → `AvatarReached`, recul de `HitRecoil`) → **puis** avaler les rangées
`< min(floor(CeilingRow), AvatarRow)`. Le front frappe à nouveau s'il rattrape l'avatar ; les i-frames de
`HealthSystem` (côté GameBootstrap) absorbent les contacts répétés, comme pour un Crawler.

---

## 4. La capsule repousse le front (remplace « le bloc de gaz », abandonné)

La capsule d'air (`CellType.AirCapsule`, map char `P`, même tuile, même densité `airRate`) **reste**. Ce
qu'elle donnait en air, elle le donne en marge : **`CeilingSystem.RestoreCapsule()` → recul de
`CapsuleRecoil` = 0,75 rangée** (l'ancien +6 % × 12 / 100 = 0,72, arrondi comme le Perfect Clear).

- **Forée** (Drilled handler, et le 2ᵉ bloc percé par le Power Drill) **ou libérée** (shockwave de burst / Power
  Drill, explosion de bombe, explosion de Boomer — les events `AirCapsuleLiberated` existants) : même recul.
- Comme tout recul : borné par la laisse (à pleine marge, la capsule ne donne rien de plus) et peut remonter
  dans les débris (§3.3).
- Aucun nouveau système Core, aucun nouvel event : une constante et une méthode sur `CeilingSystem`.

**Pourquoi c'est conforme aux règles.** Règles 3 (« bombs liberate air ») et 5 (« capsules restore air »)
gardent leur sens tel quel avec « marge » au lieu d'« air ». Une capsule reste un bonus qu'on prend en passant :
elle ne demande jamais de détour (règle 6). 0,75 rangée ≈ 1,25 s de front à 0,6 r/s (l'ancienne capsule valait
1,2 s de drain à 5 %/s) — même poids qu'avant. ✅ Gardé en R8.7.

> **Retiré avec le gaz** : `Core/GasSystem.cs`, l'alias `CellType.Gas`, les events `GasReleased`
> (Gravity / Bomb / Enemy), `EnemySystem.NotifyGas`, `KillMethod.Gas`, `DeathCause.Gas` et son texte de récap,
> `GasSystemTests` (15) + `GasInteractionTests` (8). Le design du gaz reste dans l'historique git
> (commits b041c16 → a6a27f1) si l'idée revient un jour.

---

## 5. Ce qui change partout ailleurs

| Système | Aujourd'hui | Après |
|---|---|---|
| `AirSystem` | Chronomètre | **Supprimé** → `CeilingSystem` (Core, pur C#) |
| `ScoreSystem.DangerZone` | air < 15 % | **marge < 15 %** (même ×2, même règle ⚡D3) |
| `DeathCause.Suffocation` | | **Supprimé** → `CaveIn` (R8.9 — gardé tant que l'air tue, jusqu'à R8.6) |
| `DeathTracker` | « Coup fatal à 1:16 · air restant 40 % » | ✅R8.5 `DeathReport.Lead` → « Coup fatal à 1:16 · marge 3,5 rangées » ; mort `CaveIn` → « Dernier forage il y a 2,4 s » (s'arrêter, comme la suffocation) |
| `CampaignManager` | `DrainRateForLevel`, `AirStartGraceForLevel` | ✅R8.5 `CeilingSpeedForLevel` (0,48 / 0,84), `CeilingStartGraceForLevel` (5 / 3 s) |
| `EndlessManager` | `DrainRateForDepth`, `DrainRateChanged` | ✅R8.5 `CeilingSpeedForDepth`, `CeilingSpeed`, `CeilingSpeedChanged` (0,6 + 0,06 / 20 rangées, max 1,2) |
| `GravitySystem`, `BombSystem`, `EnemySystem` | `AirCapsuleLiberated` → air | `AirCapsuleLiberated` → `RestoreCapsule` (§4) |
| `StrateGenerator` | `airRate` | inchangé (la capsule reste) |
| HUD | Barre d'air + « Fore pour respirer ! » (R6.3) | ✅R8.8 **Même barre** = marge, libellée **« ÉBOULEMENT »** (choix dev), couleur pierre ; hint **« DESCENDS ! »** sous 30 % |
| VFX | Vignette danger | ✅R8.8 vignette sur la marge ; `CaveInView` (dents de roche + lueur, poussière, cailloux) ; débris = tuile pavés procédurale ; capsule = **étai de mine** (choix dev) |
| Audio | Heartbeat danger | ✅R8.8 heartbeat sur la marge + **grondement** en boucle, volume = proximité² (muet pendant la grâce et en pause) |
| Debug toggle | `disableAir` | ✅R8.6 `disableCeiling` (`FormerlySerializedAs("disableAir")`) |
| Aide en jeu | Page air | Page « Le puits s'effondre » |
| Harness | Ledger d'air | Ledger de marge (vitesse, reculs, rattrapages par la laisse) |
| CLAUDE.md | Règles 1, 5, 10 parlent d'air | Réécrites (§7) |

### 5.1 L'Endless et la couture des segments

Le front vit en **rangées absolues** (comme `EndlessManager.Depth`), donc il traverse la couture sans rien
de spécial : au chargement d'un nouveau segment, toute rangée dont la profondeur absolue est au-dessus du
front est **convertie en débris d'office**. ⚠️ À vérifier en R8.6 : les rangées de spawn du nouveau
segment sont mappées **au-dessus** de `SegmentStartDepth` (§6.3) — elles seront probablement avalées dès
le chargement, ce qui est correct (on arrive « par le haut ») mais doit être testé.

### 5.2 Perfect Clear

`CollapseSystem` décale toutes les rangées au-dessus d'une rangée vide **d'un cran vers le bas** — débris
compris. Le front doit donc **descendre d'une rangée** avec eux (sinon il y a une rangée de débris
« sous » le front). Effet net : −1 rangée de marge… compensé par le recul de +0,75 du Perfect Clear.
Quasi neutre. Acceptable pour un bonus rare (règle 7) ; à confirmer au harness.

---

## 6. Ce que ça ne change PAS

Momentum, fissures, Power Drill, graze, freefall, cascade, bombes, ennemis, diamants, Perfect Clear,
cœurs/i-frames, wobble, score gates de la campagne : **intacts**. ⚡D4 (« pas de drain en chute libre »)
devient inutile : en chute libre tu descends, donc la marge monte toute seule.

---

## 7. Règles non négociables — réécriture proposée

1. **Drilling IS the reward.** Every drill tap gives points (×momentum) — and drilling **down** buys distance
   from the collapse. ~~air (+0.5%)~~
4. **Wobble telegraphs everything.** 0.6 s warning. The collapse is always on screen.
5. **The collapse rewards aggression.** ~~Air rewards aggression.~~ Descending is the escape; capsules, bursts,
   bomb chains, Perfect Clears and Power Drills push the collapse back. Passive = buried.
10. **Scoring and surviving are the same gesture.** Drill fast = stay ahead of the collapse = momentum = score.

Règles 2, 3, 6, 7, 8, 9, 11 : inchangées. La règle 6 devient littéralement une mécanique.

---

## 8. Plan R8 (une étape = une session)

| Étape | Contenu | Tests |
|---|---|---|
| R8.1 | `CeilingSystem` (Core) : front, vitesse, laisse, grace, reculs, contact, `HitRecoil`, Danger Zone, `RowsSwallowed` | NUnit |
| R8.2 | `CellType.Rubble` + conversion des rangées avalées (grille, bombes, ennemis, fissures) | NUnit |
| ~~R8.3~~ | ~~`CellType.Gas` + `GasSystem`~~ — livré puis **retiré** (gaz abandonné) | — |
| ~~R8.4~~ | ~~`GasReleased`, Crawlers dans le gaz~~ — livré puis **retiré** ; remplacé par R8.4c (capsule → recul) | NUnit |
| R8.5 ✅ | `DeathTracker` (marge à la mort) / `CampaignManager` / `EndlessManager` : vitesses + grace ; `ScoreSystem` inchangé | NUnit |
| R8.6 ✅ | `GameBootstrap` : câblage, ordre du tick, nouvelle règle de respawn, couture Endless, l'air ne tue plus (passerelle `Mirror` jusqu'à R8.8) | Intégration + play mode |
| R8.7 ✅ | Harness : parité, puis calibrage — mesuré (×1 / ×1,5 / ×2, `MaxLead` 12 / 8), **rien de changé** (`balance.md` §15.11) | Harness |
| R8.8 ✅ | View : débris + front, barre de marge, hint, audio, tuile capsule (étai) | Visuel |
| R8.9 | Pages d'aide, GDD, CLAUDE.md, suppression d'`AirSystem` et des tests d'air | NUnit + play mode |

> L'air reste fonctionnel jusqu'à R8.6 : R8.1-R8.5 ajoutent à côté, R8.6 bascule, R8.9 nettoie
> (même stratégie que StreakTracker → MomentumTracker en R7).

---

## 9. Décisions — ✅ toutes acceptées telles que recommandées (dev, 2026-10-27)

| # | Question | Décision |
|---|---|---|
| A | Les rangées avalées deviennent de vraies cellules `Rubble`, ou juste un overlay visuel ? | **`Rubble` (Core)** — sinon des chunks tombent à travers le front (§3.1) |
| B | Après un dégât (n'importe lequel), l'avatar reste sur place au lieu de retourner au spawn ? | **Oui, partout** — le spawn peut être avalé (§3.2) |
| C | Le front qui touche l'avatar : −1 cœur + recul de 4 rangées ? | **Oui** — sans recul, c'est une mort garantie après les i-frames |
| D | `MaxLead` = 12 rangées (front toujours visible) ? | **Oui** pour commencer ; le harness ajuste |
| E | ~~Gaz absent des niveaux 1-3 ?~~ | **Caduque** — le gaz est abandonné (2026-10-27) |
| F | Nom de la cause de mort : `Collapse` existe déjà (Perfect Clear qui écrase) | Garder `Collapse` tel quel, ajouter **`DeathCause.CaveIn`** (le front), retirer `Suffocation` (`Gas` retiré avec le gaz) |

---

## 10. Aperçu jouable — `previewCaveIn` (2026-10-27) → **retiré en R8.6**

> **✅ R8.6 : l'éboulement EST le jeu.** Le toggle `previewCaveIn` est supprimé ; tout ce qui suit est
> maintenant câblé pour de vrai dans `GameBootstrap` (§11). `CaveInPreviewView` reste affiché en
> permanence (ligne du front + texte de marge, masqué quand le jeu est figé) jusqu'aux vrais visuels de R8.8.
> Historique de l'aperçu ci-dessous.

Toggle debug sur `GameBootstrap` (section « Debug / Testing ») pour jouer la mécanique **avant** R8.6.

Ce qu'il branche — le vrai Core, rien de simulé :
- L'air ne draine plus ; `CeilingSystem` tourne à la vitesse traduite du niveau (`SpeedForDrainRate`) avec le
  même start buffer ; `RowsSwallowed` → `FillRows(Rubble)` + `DisarmRows` + `BuryRows` ; contact → `CaveIn`.
- Les reculs : burst, chaîne de bombes, Perfect Clear (+ `ShiftDown`), Power Drill.
- La capsule : forer un bloc **AIR** (aussi le 2ᵉ bloc du Power Drill) ou le libérer (burst / bombe / Boomer /
  Power Drill) → `RestoreCapsule` (+0,75 rangée).
- Respawn décision B (`CaveInRespawnCell`) : on reste sur place, on remonte seulement si on est dans du solide.
- Danger Zone = celle de la marge.

Limites assumées (corrigées en R8.6 / R8.8) : rangées **locales au board** (le front repart à pleine laisse à
chaque couture Endless) ; la barre d'air du HUD est figée (la marge = la ligne orange + le texte en bas à gauche) ;
(R8.5 : le récap lit maintenant la marge).

**Vérifié en play mode (tutoriel, via UnityMCP, version avec gaz)** : le front entre dans le board, la rangée 0
devient des débris gris, avatar immobile → 3 coups `CaveIn` (≈ 2,5 s d'écart à 3 r/s forcés) → récap « Rattrapé
par l'éboulement » ; aucune exception. **La version capsule → recul n'est pas encore revue en play mode** (MCP
Unity déconnecté au moment du retrait du gaz) — Core seulement, 503/503 via `dotnet test`.

---

## 11. Câblage livré — R8.6 (2026-10-27)

- **`CeilingSystem` persistant**, créé dans `Awake` comme l'air l'était. Ses events sont branchés une seule fois :
  `RowsSwallowed` → `SwallowBoardRows` (rubble + `DisarmRows` + `BuryRows`, jamais la rangée de l'avatar),
  `AvatarReached` → `OnAvatarCrushed(CaveIn)`, `DangerZoneChanged` → `ScoreSystem.DangerZone`.
- **Rangées absolues** : rangée du board + `_rowOffset` (Endless : `SegmentStartDepth` ; sinon 0). Le spawn du
  nouveau segment tombe pile sur la rangée absolue de la sortie de l'ancien (62 + S dans les deux cas), donc
  l'avatar et le front ne bougent pas à la couture.
- **Board neuf** (`freshBoard`) : `Reset(spawn + offset)` + start buffer (`CeilingStartGraceForLevel`). **Couture** :
  rien n'est remis à zéro ; après la construction du board, les rangées que le front a déjà passées sont
  converties d'office (§5.1 — vérifié en play mode), puis `NotifyAvatarRow`.
- **Vitesse** : `CeilingSpeedForLevel` / `_endless.CeilingSpeed` au chargement, `CeilingSpeedChanged` en Endless.
- **Reculs** : capsule forée / percée par le Power Drill / libérée (gravité, bombe, Boomer), burst, chaîne de
  bombes, Perfect Clear (`ShiftDown` + recul), Power Drill. Aucun appel `AirSystem.Restore*` ne reste.
- **Tick** : étape 9 = `_ceiling.Tick(dt, avatar + offset)`, figé pendant la fin de niveau (R6.14), **pas** figé en
  chute libre (⚡D4 retiré). `disableCeiling` remplace `disableAir`.
- **Respawn décision B** pour tous les dégâts (`RespawnCell`) — plus de téléport au spawn.
- **L'air ne tue plus** (plus de `AirDepleted` → `Suffocation`). Passerelle jusqu'à R8.8 : `DrainRate` 0, son
  `Tick` ne fait tourner que l'horloge du start buffer (le décompte de grâce du HUD), et
  **`AirSystem.Mirror(LeadPercent)`** chaque frame — la barre, le hint « Fore pour respirer », la vignette et le
  heartbeat lisent donc la marge sans avoir été touchés. Le libellé « AIR » / le texte du hint changent en R8.8.

---

## 12. Visuels livrés — R8.8 (2026-10-27)

**Plus rien dans le jeu ne lit `AirSystem`** (sauf la page d'aide, R8.9). Le HUD, la vignette (VfxManager) et le
heartbeat (AudioManager) prennent `CeilingSystem` directement ; la passerelle `AirSystem.Mirror` de R8.6 n'est plus
appelée — elle part avec `AirSystem` en R8.9. `CeilingSystem.StartGraceDuration` ajouté pour la mèche du HUD.

- **Barre « ÉBOULEMENT »** (choix dev) : même barre en bas, couleur pierre au lieu du cyan de l'air ; pendant la grâce
  « ÉBOULEMENT · 3 » + la mèche dorée (R6.2) ; rouge sous 25 %. Hint **« DESCENDS ! »** sous 30 %, avec le « pop »
  à chaque gain de marge (une rangée descendue, une capsule, un burst).
- **`CaveInView`** (remplace `CaveInPreviewView`, plus de texte OnGUI) : une dent de roche pixel-art par colonne
  pendue au front (3 profils procéduraux), une lueur orange derrière (rouge pulsé en Danger Zone), la bande de
  poussière sur la rangée en train de tomber, des cailloux qui tombent du front (pluie fine à pleine laisse, grêle
  quand il est proche), une bouffée par rangée avalée et une gerbe quand le front frappe l'avatar.
- **Débris** : tuile pavés 16×16 procédurale (Voronoi, mortier sombre), teintée par `BoardView.rubbleColor`.
- **Capsule = étai de mine** (choix dev) : `Editor/CapsuleTileGenerator` réécrit (menu *Hollow Lines → Regenerate
  Capsule Tile (timber prop)*) — deux poteaux, un chapeau boulonné, une croix de contreventement, sur la roche ; même
  chemin et taille, donc import et `Resources.Load` inchangés. Le reflet du shader passe du cyan au doré.
- **Grondement** : `SfxSynth.Rumble` (bruit brun + corps à 80 Hz, boucle de 3 s fondue sur elle-même — saut à la
  jointure mesuré 0,0003, sans clic) ; volume = proximité², muet pendant la grâce et en pause.

**Vérifié en play mode (captures, tutoriel)** : grâce (« ÉBOULEMENT · 2 », mèche, étai lisible dans la grille), marge
à 25 % (barre rouge, « DESCENDS ! », cailloux), marge à 2 % (rangée 0 en pavés, dents + lueur, vignette, ×2 DANGER).

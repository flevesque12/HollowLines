---
paths:
  - "**/CeilingSystem*"
  - "**/GasSystem*"
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

# Hollow Lines — L'Effondrement + le bloc de gaz (v3.3, design)

> **Statut : DESIGN ACCEPTÉ (2026-10-27) — implémentation R8 en cours** (plan : `refactoring-plan.md` §R8).
> Remplace l'air (le chronomètre de suffocation) par une pression spatiale : le puits s'effondre derrière
> toi. Les capsules d'air deviennent des **blocs de gaz**. Les 6 décisions du §9 sont tranchées.

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

Formule Endless : `CeilingSpeedForDepth(d) = (5 + d/20 × 0,5) × 0,12`, plafonnée à 1,2. Valeurs **provisoires** —
le harness les mesure en R8.7 (§8), comme R7.15 l'a fait pour le drain.

### 2.3 Ce qui fait reculer le front (règle 5 : l'agression récompense)

Chaque ancienne source d'air devient un **recul** du front, aux mêmes proportions (`recul = restore × MaxLead / 100`) :

| Source | Restore air | **Recul** |
|---|---|---|
| Forage (n'importe lequel) | +0,5 % | **aucun** — descendre EST le gain (forer vers le bas = +1 rangée de marge) |
| Chunk Burst | +0,5 % | **+0,06** → arrondi à **+0,1 rangée** |
| Bombe en chaîne (par bombe) | +1 % | **+0,12 rangée** |
| Perfect Clear | +6 % | **+0,75 rangée** (et compense le décalage du §6.4) |
| Capsule d'air | +6 % | — supprimée (devient le gaz) |
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
  retirés sans points, diamants perdus (règle 8 : bonus, jamais requis), fissures effacées, gaz dissipé.
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
  RestoreBurst() / RestoreBombChain(n) / RestorePerfectClear() / RestorePowerDrill()
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

## 4. Le bloc de gaz (remplace la capsule d'air)

### 4.1 Ce que c'est

`CellType.Gas` prend la place de `AirCapsule` (même valeur d'enum, même slot dans le générateur, map char `P`
inchangé → les cartes authored restent valides). Une poche de gaz coincée dans la roche.

- **Forable en 1 coup**, compte comme un forage normal (points × momentum, +1 momentum).
- Forer (ou libérer par burst / bombe / Boomer) **arme la poche** : elle siffle **0,6 s** (= le wobble,
  règle 4), puis **libère un nuage**.
- Le nuage **monte** : toutes les 0,25 s, chaque cellule de gaz essaie de monter d'une case (si vide),
  sinon de s'étaler à gauche/droite. Volume : **4 cellules**. Durée de vie : **3 s**, puis il se dissipe.
- **Toucher le gaz = −1 cœur** (`DeathCause.Gas`, i-frames normales). Pas de téléport (§3.2).
- Le gaz qui atteint le front de l'effondrement est **absorbé** par les débris.

**✅ R8.3 — implémenté (`Core/GasSystem.cs`, pur C#)** :

```
GasSystem(GridModel grid)
  HissDuration 0.6 · StepInterval 0.25 · CloudVolume 4 · CloudLifetime 3   (provisoires → R8.7)
  Release(GridPos vent) → bool     // foré ou libéré ; l'appelant a déjà vidé la cellule
  Tick(float dt, GridPos avatar)   // nuages, puis hisses, puis contact
  IsGas / IsHissing / HasActiveGas / CopyGasCells / CopyHissingCells / Clear
  events : PocketHissing(pos) · CloudReleased(vent) · CloudDissipated(vent) · AvatarHitByGas(pos)
```

- **`CellType.Gas` est un alias d'`AirCapsule`** (même valeur 7, même `P`), pas une nouvelle cellule : la poche
  hérite de toutes les règles de la capsule (forable, ne fusionne pas, libérée par burst/bombe/Boomer), et
  l'air marche tel quel jusqu'à la bascule R8.6. R8.9 supprime le nom `AirCapsule`.
- **Le gaz ne touche jamais la grille** : il vit dans des cellules vides. Une bouffée dont la cellule redevient
  solide (chunk, débris) disparaît — c'est ainsi que l'effondrement « absorbe » le gaz, sans code dédié.
  Une poche rebouchée pendant son sifflement ne s'ouvre jamais.
- Pas : les bouffées les plus hautes bougent d'abord (une colonne monte d'un bloc) ; bloquée en haut, une
  bouffée glisse sur le côté, préférence gauche/droite **alternée à chaque pas** (étalement symétrique).
- Le Perfect Clear ne décale pas le gaz (contrairement aux bombes armées) : une bouffée qui se retrouve dans
  du solide disparaît, les autres continuent de monter. Assez bon pour 3 s de vie.

### 4.2 Pourquoi ça respecte les règles

**Le seul refuge, c'est en bas** (règle 6). Tu fores une poche → tu tombes dans sa case → elle siffle
0,6 s → si tu continues à forer (fenêtre de momentum 0,8 s, joueur réaliste 0,3 s/coup), tu es **une ou
deux rangées plus bas** quand le nuage sort… et il monte, **loin de toi**. Si tu t'arrêtes, il te prend.
C'est le même geste qui fait scorer (momentum), survivre (marge) et esquiver (gaz) — **règle 10 au carré**.

Contourner une poche est toujours possible et jamais obligatoire : elle est optionnelle comme danger,
comme la capsule l'était comme récompense.

### 4.3 Interactions

| Avec | Effet | Statut |
|---|---|---|
| **Graze** | Forer adjacent à une **bouffée** = graze (+50, +0,3 s) — une ligne dans `CollectDangerCells()`. **Pas** la poche qui siffle : on la fore soi-même, le forage suivant serait un graze automatique à chaque poche | 🟡 R8.6 (câblage View) |
| **Crawler** | Un Crawler (dormant ou actif) dans une bouffée meurt : `EnemySystem.NotifyGas(cells)`, `KillMethod.Gas`, ×1 (100 pts). **Les Boomers sont épargnés** : un kill au gaz n'a pas de bonus parent, donc pas d'explosion — un Boomer qui s'éteindrait sans exploser gaspillerait l'amplificateur (règle 9) | ✅ R8.4 |
| **Libération** | Burst / bombe / Boomer qui touche une poche → `GasReleased(pos)` (`GravitySystem`, `BombSystem`, `EnemySystem`), tiré juste après `AirCapsuleLiberated` (même cellule) | ✅ R8.4 |
| **Effondrement** | Le front absorbe le gaz | ✅ |
| **Bombe** | Une explosion qui touche un nuage l'**enflamme** : rayon 1, points de bombe | 🅿️ parqué (après playtests) |
| **Fissures** | Le gaz ne se fissure pas (comme la capsule aujourd'hui : `CanFuse` seulement) | ✅ inchangé |
| **Chunks** | Un chunk qui tombe dans un nuage le traverse ; le gaz ne supporte rien | ✅ |

### 4.4 Densité

Les capsules sortent à 5-10 % des cellules par strate (`airRate`). C'était correct pour une **récompense** —
pour un **danger**, c'est beaucoup trop. Proposition : `gasRate = airRate × 0,4` (2-4 %), **absent des
niveaux 1-3** (onboarding), présent dès le niveau 4. À mesurer au harness (R8.7).

---

## 5. Ce qui change partout ailleurs

| Système | Aujourd'hui | Après |
|---|---|---|
| `AirSystem` | Chronomètre | **Supprimé** → `CeilingSystem` (Core, pur C#) |
| `ScoreSystem.DangerZone` | air < 15 % | **marge < 15 %** (même ×2, même règle ⚡D3) |
| `DeathCause.Suffocation` | | **Supprimé** → cause effondrement + `Gas` |
| `DeathTracker` | « Dernier forage il y a 6,2 s » | « Le puits t'a rattrapé — marge 0 » |
| `CampaignManager` | `DrainRateForLevel`, `AirStartGraceForLevel` | `CeilingSpeedForLevel`, `CeilingStartGraceForLevel` |
| `EndlessManager` | `DrainRateForDepth`, `DrainRateChanged` | `CeilingSpeedForDepth`, `CeilingSpeedChanged` |
| `GravitySystem`, `BombSystem`, `EnemySystem` | `AirCapsuleLiberated` | `GasReleased(GridPos)` |
| `StrateGenerator` | `airRate` | `gasRate` (densité §4.4), chambre « Air » du tutoriel → chambre « Gaz » |
| HUD | Barre d'air + « Fore pour respirer ! » (R6.3) | **Même barre** = marge ; hint « DESCENDS ! » sous 30 % |
| VFX | Vignette danger | + rendu des débris, front qui s'effrite (poussière), nuage de gaz |
| Audio | Heartbeat danger | + grondement dont le volume suit la proximité, sifflement du gaz |
| Debug toggle | `disableAir` | `disableCeiling` |
| Aide en jeu | Page air | Page « Le puits s'effondre » + gaz |
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
4. **Wobble telegraphs everything.** 0.6 s warning — chunks, **gas pockets (hiss)**. The collapse is always on screen.
5. **The collapse rewards aggression.** ~~Air rewards aggression.~~ Descending is the escape; bursts, bomb
   chains, Perfect Clears and Power Drills push the collapse back. Passive = buried.
10. **Scoring and surviving are the same gesture.** Drill fast = stay ahead of the collapse = momentum = score.

Règles 2, 3, 6, 7, 8, 9, 11 : inchangées. La règle 6 devient littéralement une mécanique.

---

## 8. Plan R8 (une étape = une session)

| Étape | Contenu | Tests |
|---|---|---|
| R8.1 | `CeilingSystem` (Core) : front, vitesse, laisse, grace, reculs, contact, `HitRecoil`, Danger Zone, `RowsSwallowed` | NUnit |
| R8.2 | `CellType.Rubble` + conversion des rangées avalées (grille, bombes, ennemis, fissures) | NUnit |
| R8.3 | `CellType.Gas` (remplace AirCapsule) + `GasSystem` (armement 0,6 s, nuage, montée, durée de vie, contact) | NUnit |
| R8.4 | Événements `GasReleased` (gravity / bombes / Boomer), mort des Crawlers dans le gaz, graze sur le gaz | NUnit |
| R8.5 | `ScoreSystem` / `DeathTracker` / `CampaignManager` / `EndlessManager` : Danger Zone, causes de mort, vitesses | NUnit |
| R8.6 | `GameBootstrap` : câblage, ordre du tick, nouvelle règle de respawn, couture Endless, retrait d'`AirSystem` | Intégration + play mode |
| R8.7 | Harness : parité, puis **calibrage** (vitesses, `MaxLead`, `gasRate`, reculs) — même méthode que R7.15 | Harness |
| R8.8 | View : débris + front, nuage de gaz, barre de marge, hint, audio | Visuel |
| R8.9 | Générateur (densité gaz, chambre tutoriel), pages d'aide, GDD, CLAUDE.md, suppression des tests d'air | NUnit + play mode |

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
| E | Gaz absent des niveaux 1-3 ? | **Oui** — l'onboarding a déjà assez à apprendre |
| F | Nom de la cause de mort : `Collapse` existe déjà (Perfect Clear qui écrase) | Garder `Collapse` tel quel, ajouter **`DeathCause.CaveIn`** (le front) + **`DeathCause.Gas`**, retirer `Suffocation` |

---

## 10. Aperçu jouable — `previewCaveIn` (temporaire, 2026-10-27)

Toggle debug sur `GameBootstrap` (section « Debug / Testing ») pour jouer la mécanique **avant** R8.6.
**Coche-le avant Play** (il est lu à chaque `LoadLevel`). R8.6 le remplace par le vrai câblage, R8.8 par les
vrais visuels (`CaveInPreviewView` sera supprimé).

Ce qu'il branche — le vrai Core, rien de simulé :
- L'air ne draine plus ; `CeilingSystem` tourne à la vitesse traduite du niveau (`SpeedForDrainRate`) avec le
  même start buffer ; `RowsSwallowed` → `FillRows(Rubble)` + `DisarmRows` + `BuryRows` ; contact → `CaveIn`.
- Les reculs : burst, chaîne de bombes, Perfect Clear (+ `ShiftDown`), Power Drill.
- Le gaz : forer un bloc **AIR** (= poche de gaz, même cellule) ou le libérer (burst / bombe / Boomer / Power
  Drill) → `GasSystem.Release` ; contact → `Gas` ; `NotifyGas` chaque frame ; les bouffées comptent pour le graze.
- Respawn décision B (`CaveInRespawnCell`) : on reste sur place, on remonte seulement si on est dans du solide.
- Danger Zone = celle de la marge.

Limites assumées (corrigées en R8.6 / R8.8) : rangées **locales au board** (le front repart à pleine laisse à
chaque couture Endless) ; la barre d'air du HUD est figée (la marge = la ligne orange + le texte en bas à gauche) ;
le bloc garde son visuel « AIR » ; le récap affiche encore « air restant 100 % ».

**Vérifié en play mode (tutoriel, via UnityMCP)** : le front entre dans le board, la rangée 0 devient des débris
gris, avatar immobile → 3 coups `CaveIn` (≈ 2,5 s d'écart à 3 r/s forcés) → récap « Rattrapé par l'éboulement » ;
descente forée : Power Drill perce la poche (3,12) → sifflement → nuage qui monte pendant que l'avatar descend →
absorbé par les débris ; aucune exception. Le seul cœur perdu venait de la bombe du tutoriel (§5.14, accepté).

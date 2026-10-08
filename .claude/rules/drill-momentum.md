---
paths:
  - "**/MomentumTracker*"
  - "**/ScoreSystem*"
  - "**/AirSystem*"
  - "**/GrazeSystem*"
  - "**/FissureTracker*"
  - "**/HUDView*"
  - "**/VfxManager*"
  - "**/AudioManager*"
  - "**/GameBootstrap*"
---

## Drill Momentum — Directives de design (v3.2)

> **Principe fondamental:** Le geste qui garde le joueur en vie EST le geste qui fait
> scorer. Forer vite = survivre (air) = scorer (momentum) = spectacle (fissures, power drill).
> Tout pointe dans la même direction — comme Downwell, NecroDancer, Pac-Man CE.

> **Ce qui change:** `StreakTracker` (streak par couleur) est remplacé par `MomentumTracker`
> (streak temporel). Les couleurs deviennent un système de physique (chunk fusion/burst),
> pas de scoring actif — comme Mr. Driller.

### Décisions tranchées (2026-10-07)

| # | Décision | Impact |
|---|---|---|
| D1 | **×6 s'applique à tout le Power Drill burst** (2 blocs percés + shockwave), pas juste au drill déclencheur. Reset à Tier 1 après. | `PowerDrillActivated` event transmet le multiplier |
| D2 | **Float accumulator** interne (`_momentumProgress`) pour le bonus couleur ×1.5. API expose `DrillCount` (int) et `CurrentTier` (int). | `NotifyDrill(CellType)` inchangé |
| D3 | **Graze et freefall ne sont jamais multipliés par Danger Zone.** Le ×2 s'applique aux actions base seulement (drill, burst, bomb, enemy). | `AwardGraze`/`AwardFreefall` restent flat |
| D4 | **Air drain skippé pendant le freefall** (`_airSystem.Tick` aussi, pas juste le momentum timer). | Tick order §7 step 9 conditionné |
| D5 | **FissureTracker = système Core séparé** (`Dictionary<GridPos, int>`). 2ᵉ fissure → bloc se brise → event `FissureBroke`. | Nouveau fichier `Core/FissureTracker.cs` |

---

### M1. MomentumTracker — Spécification Core

**Fichier:** `Core/MomentumTracker.cs` (remplace `StreakTracker.cs`)

```
Classe: MomentumTracker (pure C#, pas de MonoBehaviour)

Constantes:
  MomentumWindow     = 0.8f     // secondes entre deux drills pour maintenir le momentum
  Tier1Threshold     = 3        // drills consécutifs pour Tier 1
  Tier2Threshold     = 7        // drills consécutifs pour Tier 2
  Tier3Threshold     = 10       // drills consécutifs pour Tier 3 (Power Drill)
  ColorBonusRate     = 1.5f     // multiplicateur de vitesse d'accumulation si même couleur

État:
  _drillCount        (int)      // nombre réel de drills dans la fenêtre (pour l'affichage)
  _momentumProgress  (float)    // ⚡D2: accumulateur interne — seuils de tier comparent contre ça
  _timer             (float)    // temps restant avant reset (décompte de MomentumWindow)
  _lastColor         (CellType) // dernière couleur forée (pour le bonus couleur optionnel)
  _colorChain        (int)      // drills consécutifs de même couleur dans le momentum actuel

Propriétés:
  CurrentTier        (int 0-3)  // palier actuel dérivé de _momentumProgress (⚡D2)
  DrillCount         (int)      // lecture seule — le vrai nombre de drills (int)
  ColorChain         (int)      // lecture seule — pour le bonus couleur optionnel
  Multiplier         (float)    // ×1 / ×2 / ×4 / ×6 selon le palier
  IsInPowerDrill     (bool)     // ⚡D1: true entre PowerDrillActivated et le reset

API:
  NotifyDrill(CellType drilled)
    → _timer = MomentumWindow (reset le compteur)
    → _drillCount++
    → si drilled == _lastColor: _colorChain++, _momentumProgress += ColorBonusRate (1.5f)
      sinon: _colorChain = 1, _momentumProgress += 1.0f
    → _lastColor = drilled
    → évaluer le tier (contre _momentumProgress), firer les events
    → SI _momentumProgress >= Tier3Threshold:
      IsInPowerDrill = true
      firer PowerDrillActivated
      ⚡D1: le ×6 s'applique à TOUT le Power Drill burst (2 blocs percés + shockwave).
      GameBootstrap orchestre le burst, puis appelle CompletePowerDrill().

  CompletePowerDrill()
    → ⚡D1: appelé par GameBootstrap APRÈS le burst complet du Power Drill
    → Reset partiel: _momentumProgress = Tier1Threshold, _drillCount = Tier1Threshold
    → IsInPowerDrill = false
    → Retombe en Tier 1 (pas Tier 0) — le cycle recommence

  ExtendTimer(float seconds)
    → _timer += seconds (utilisé par le graze: +0.3s)

  Tick(float dt)
    → _timer -= dt
    → si _timer <= 0: Reset total (_drillCount = 0, _momentumProgress = 0, _colorChain = 0, firer MomentumLost)

  Reset()
    → tout à zéro, firer MomentumLost

Events:
  TierChanged(int oldTier, int newTier)  // changement de palier (montée ou descente)
  MomentumLost()                          // retour à Tier 0
  PowerDrillActivated()                   // ⚡D1: Tier 3 atteint → le ×6 couvre tout le burst
```

---

### M2. Système de paliers

| Palier | Progress (seuil) | Multiplicateur | Effets visuels | Effets audio |
|---|---|---|---|---|
| Tier 0 | 0 – 2.9 | ×1 | Aucun | Son de drill normal |
| Tier 1 | 3 – 6.9 | ×2 | Trail derrière l'avatar | Pitch monte d'un demi-ton |
| Tier 2 | 7 – 9.9 | ×4 | Trail + **fissures** sur blocs adjacents | Pitch monte encore, grondement |
| Tier 3 | 10+ | ×6 | Flash + **Power Drill** | Impact basse, cri satisfaisant |

> ⚡D2: Les seuils comparent `_momentumProgress` (float). Chaque drill ajoute +1.0, ou +1.5
> si même couleur (`ColorBonusRate`). Un joueur qui alterne les couleurs atteint Tier 3 en
> 10 drills ; un joueur qui route la même couleur l'atteint en 7 (1.0 + 6×1.5 = 10.0).

**Tier 2 — Fissures (déstabilisation passive):**
- ⚡D5: `FissureTracker` — **système Core séparé** (`Core/FissureTracker.cs`)
  - État: `Dictionary<GridPos, int>` (position → nombre de fissures, 0-2)
  - `NotifyDrill(GridPos drillPos, int currentTier)` : si tier ≥ 2, les 4 blocs cardinalement
    adjacents non-vides reçoivent +1 fissure
  - Quand une cellule atteint 2 fissures → `FissureBroke(GridPos)` event, la cellule
    est vidée dans le GridModel, la fissure est retirée du dictionnaire
  - `Clear()` : reset total (changement de board / segment)
- Les fissures nourrissent les chunks : blocs brisés créent des trous →
  chunks au-dessus perdent leur support → chute → burst

**Tier 3 — Power Drill:**
- Le drill perce **2 blocs** au lieu d'un (le bloc ciblé + le bloc en dessous)
- Génère une **mini shockwave** (rayon 1) sur le 2e bloc percé
- Après le Power Drill: le momentum retombe à Tier 1 (pas Tier 0)
- Le cycle recommence — le joueur peut enchaîner les Power Drills

---

### M3. Mécaniques complémentaires

#### M3.1 Cascade visible — ChainTracker surfacé

Le `ChainTracker` existant (§4) devient visible au joueur :
- Afficher un compteur **×N** quand une cascade est en cours
- Chaque maillon de la chaîne multiplie les points des événements suivants
- Cascade = burst qui cause un autre burst, ou bomb qui cause un burst, etc.
- Le multiplicateur de cascade se combine avec le multiplicateur de momentum

**Aucun changement Core** — ChainTracker existe déjà. Changement View uniquement (HUDView popup).

#### M3.2 Graze bonus — Frôler le danger

**Fichier:** `Core/GrazeSystem.cs` (nouveau)

```
Conditions de graze (n'importe laquelle):
  - Forer un bloc cardinalement adjacent à un ennemi actif
  - Forer un bloc cardinalement adjacent à une bombe armée (fuse en cours)
  - Forer un bloc dans le rayon d'une shockwave en cours

Récompense:
  +50 points (flat, pas multiplié par momentum)
  +0.3s ajouté au timer de momentum (prolonge la fenêtre)

Cooldown: 0.5s entre deux grazes (éviter le spam)
```

Le graze encourage la prise de risque : rester près du danger au lieu de fuir.

#### M3.3 Freefall — Chute libre à travers le vide

**Tracking:** `AvatarModel` — ajouter un compteur de cellules vides traversées en chute libre.

```
Conditions:
  - L'avatar tombe à travers une cellule vide (pas de bloc à forer)
  - Chaque cellule vide traversée = +15 points
  - La chute libre MAINTIENT le momentum (le timer ne décompte pas pendant la chute)
  - ⚡D4: Pas de drain d'air pendant la chute libre — _airSystem.Tick(dt) AUSSI skippé,
    pas juste le momentum timer. Voir architecture.md §7 tick order step 9.

Fin de la chute libre:
  - L'avatar atterrit sur un bloc solide
  - L'avatar fore un bloc (transition directe chute → drill)
```

La chute libre récompense les zones poreuses : le joueur traverse le vide à toute
vitesse, maintient son momentum, et reprend le drilling à l'atterrissage.

#### M3.4 Danger Zone — Air critique

**Fichier:** `Core/AirSystem.cs` — ajouter un flag `IsDangerZone`

```
Condition: air < 15%
Effet: ⚡D3: les points BASE sont ×2 (drill, burst, bomb, enemy kill, boomer blast,
  perfect clear). Graze (+50) et freefall (+15) restent FLAT, pas multipliés.
Visuel: vignette rouge pulsante sur les bords de l'écran
Audio: heartbeat sourd

Le Danger Zone se combine avec les actions base:
  base × momentum_mult × cascade_mult × danger_zone_mult
  + graze_bonus (flat) + freefall_bonus (flat)
```

Le Danger Zone transforme une situation désespérée en opportunité de comeback.

---

### M4. Nouveau rôle des couleurs

Les couleurs **ne sont plus un système de scoring actif**. Elles deviennent un système
de physique (comme Mr. Driller) :

| Avant (streak couleur) | Après (momentum) |
|---|---|
| Forer la bonne couleur = scorer | Forer vite = scorer |
| Routing latéral obligatoire | Descente pure récompensée |
| Couleur = mécanique de scoring | Couleur = mécanique de physique |
| 33% chance de continuer en descendant | 100% — n'importe quelle couleur |

**Bonus couleur optionnel (skill expression):**
- Forer la même couleur consécutivement fait monter le momentum **×1.5 plus vite**
- Le joueur casual ignore complètement : il fonce et score
- Le joueur hardcore route pour le bonus couleur : il score encore plus
- Les deux sont viables — le bonus n'est jamais nécessaire

---

### M5. Formule de scoring complète

```
score = base_pts × momentum_mult × cascade_mult × danger_zone_mult
      + graze_bonus + freefall_bonus

Où:
  base_pts         = valeur de l'action (drill 10, burst cells×25×fall, bomb, etc.)
  momentum_mult    = ×1 / ×2 / ×4 / ×6 selon le palier
  cascade_mult     = ChainTracker.CurrentChain (×1 si pas de cascade)
  danger_zone_mult = ×2 si air < 15%, sinon ×1
  graze_bonus      = +50 pts flat (pas multiplié)
  freefall_bonus   = +15 pts/cellule vide (pas multiplié)
```

**Exemples de pics:**
- Tier 3 (×6) + Cascade ×3 + Danger Zone (×2) = **×36** sur les points de base
- Power Drill qui perce 2 blocs + shockwave qui déclenche un burst = cascade instantanée
- Graze near un ennemi en Danger Zone = +50 pts + prolonge le momentum de 0.3s

---

### M6. Impact sur les systèmes existants

| Système | Avant | Après momentum |
|---|---|---|
| **ScoreSystem** | `AwardDrill(streakStep, color)` | `AwardDrill(momentumMult)` — plus de couleur |
| **AirSystem** | Drill +0.5% (inchangé) | Idem + `IsDangerZone` flag (air < 15%) |
| **BombSystem** | Inchangé comme amplificateur | + source de graze (forer adjacent = +50) |
| **Crawler** | Speed bump | + source de graze (forer adjacent = +50) |
| **Boomer** | Cascade amplifier | Idem — explosion → cascade → momentum nourri |
| **FissureTracker** | — (nouveau) | ⚡D5 Tier 2+ fissure tracking (`Dictionary<GridPos, int>`), 2ᵉ fissure → bloc brisé |
| **ChunkSystem** | Fusion par couleur | Idem — nourri par les fissures de Tier 2 |
| **GravitySystem** | Burst existant | + mini shockwave du Power Drill |
| **DiamondSystem** | +150 pts | Inchangé |
| **HUDView** | Streak counter | Momentum counter + tier indicator + cascade ×N + graze popup |
| **VfxManager** | Streak glow | Trail (T1), fissures (T2), power drill flash (T3), danger vignette |
| **AudioManager** | Streak pitch | Pitch par tier, grondement T2, impact basse T3, heartbeat danger |

---

### M7. Directives d'implémentation Core

1. `MomentumTracker` est **pure C#** — pas de MonoBehaviour, pas de UnityEngine
2. ⚡D5: Les fissures en Tier 2 sont gérées par un **`FissureTracker`** Core séparé (`Dictionary<GridPos, int>`). 2ᵉ fissure → bloc brisé → event `FissureBroke`
3. Le Power Drill est un **signal** (`PowerDrillActivated` event) — GameBootstrap orchestre le double-drill + shockwave
4. `GrazeSystem` est un nouveau système Core pur — reçoit la position du drill et la liste des dangers
5. Le freefall tracking vit dans `AvatarModel` (compteur de cellules vides traversées)
6. `AirSystem.IsDangerZone` est un simple `bool` dérivé de `CurrentAir < 0.15f`

**Convention existante respectée:** Core émet des events → GameBootstrap wire → View réagit.

---

### M8. Directives d'implémentation View

1. **HUDView:** Momentum counter remplace le streak counter. Afficher le tier actuel (barre ou chiffre).
   Popup "×N" pour les cascades. Popup "+50 GRAZE" éphémère.
2. **VfxManager:**
   - Tier 1: trail de particules derrière l'avatar (3-4 particules, couleur du dernier bloc foré)
   - Tier 2: fissures visibles sur les blocs adjacents (hairline cracks, shader ou overlay sprite)
   - Tier 3: flash blanc + screen shake léger au moment du Power Drill
   - Danger Zone: vignette rouge pulsante (shader fullscreen, pulse à ~1 Hz)
3. **AudioManager:**
   - Tier 0→1: pitch du son de drill monte d'un demi-ton
   - Tier 1→2: pitch monte encore + ajout d'un grondement basse fréquence (rumble)
   - Tier 2→3: impact basse percussif au moment du Power Drill + son satisfaisant
   - Danger Zone: heartbeat sourd en boucle (60 bpm), se superpose au reste
   - Graze: petit "ting" aigu rapide
4. **BoardView:** Les fissures Tier 2 sont un overlay par cellule (pas un changement de sprite du bloc)

---

### M9. Résolution R6.11 — Drilling latéral

Le drilling latéral (gauche/droite) a **la même valeur** que le drilling vers le bas :
- Momentum +1 drill (comme un drill vers le bas)
- Pas de pénalité directionnelle
- Le timer se reset à 0.8s comme n'importe quel drill

**Différence avec l'ancien streak:** L'ancien `StreakTracker` (v3.1) comptait
uniquement les drills **vers le bas** (`DrillDirection.Down`). Le momentum compte
**tous les drills** — la direction n'a plus d'importance.

**Conséquence:** `MomentumTracker.NotifyDrill()` ne prend plus de `DrillDirection`.
Le paramètre est supprimé. La signature est `NotifyDrill(CellType drilled)`.

---

### M10. Migration StreakTracker → MomentumTracker

| Étape | Action |
|---|---|
| 1 | Créer `MomentumTracker.cs` avec l'API ci-dessus |
| 2 | Créer `GrazeSystem.cs` |
| 3 | Ajouter le freefall tracking dans `AvatarModel` |
| 4 | Ajouter `IsDangerZone` dans `AirSystem` |
| 5 | Modifier `ScoreSystem`: `AwardDrill(float momentumMult)` remplace `AwardDrill(int streakStep, CellType color)` |
| 6 | Modifier `GameBootstrap`: rewire tout (MomentumTracker, GrazeSystem, freefall, danger zone) |
| 7 | Modifier `HUDView`: momentum counter, cascade popup, graze popup |
| 8 | Modifier `VfxManager`: trail, fissures, power drill, danger vignette |
| 9 | Modifier `AudioManager`: pitch par tier, rumble, impact, heartbeat, graze ting |
| 10 | Supprimer `StreakTracker.cs` |
| 11 | Mettre à jour les tests: remplacer les tests streak par des tests momentum |

> **Attention:** La migration casse le streak system v3.1. C'est intentionnel —
> le momentum est le remplacement complet.

# Playtest harness

Headless bot playtest for Hollow Lines. Compiles the **real** `Assets/_Project/Scripts/Core/*.cs`
sources (pure C#, no UnityEngine) and replicates `GameBootstrap`'s exact wiring and tick order
(CLAUDE.md §7), so bot runs exercise the same rules as the shipped game loop.

```
cd tools/playtest
dotnet run        # .NET 8+
```

## What it runs

1. **Tunnel-rush bot** — drills straight down, ignores lines. Guards against depth-only
   degenerate wins (the reason the line gate exists).
2. **Row-clear bot** — excavates row by row at two input cadences (0.15 s ≈ skilled,
   0.25 s ≈ casual). Measures the air economy and line-clear accessibility.
3. **Board density stats** — % solid per campaign level, undermine-target rows.
4. **Keystone/undermine scripted scenario** — proves a line clear is possible, checks
   `LineSource` attribution, and shows the no-dodge crush.
5. **Porous-variant + wobble comparisons** — A/B sections used for the 2026-07-17 balancing
   pass (emptyRate, drain, wobble). Adapt these when testing new tuning values.

## Reading the output

Per run: outcome (WIN / air-death / crushed / timeout), sim time, air at end + minimum,
lines cleared, hearts lost, score, best chain, and line sources as Drill/Keystone/Demolition/Chain.

## Caveats

- Bots never dodge wobbles — crush counts are an absolute worst case, not a human prediction.
- Bots don't plan undermines — line counts are a floor, not a ceiling.
- Campaign boards are deterministic (seeded), so runs are reproducible; a bot change is the
  only thing that alters results for a given tuning.

// Hollow Lines — headless playtest harness.
// Compiles the real Core sources and replicates GameBootstrap's wiring (CLAUDE.md §7)
// so bot runs exercise the exact same rules as the shipped game loop.

using HollowLines.Core;

const float Dt = 1f / 60f;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length > 0 && args[0] == "dump")
{
    int lvl = args.Length > 1 ? int.Parse(args[1]) : 3;
    string[] b = StrateGenerator.CampaignBoard(lvl);
    var g = GridModel.FromStringMap(b);
    var gr = new GravitySystem(g);
    gr.Settle(); // same silent settle the game runs at board load (R2.8d)

    Console.WriteLine("row  authored | settled");
    for (int y = 0; y < b.Length; y++)
    {
        var s = new System.Text.StringBuilder();
        for (int x = 0; x < g.Width; x++)
            s.Append(g.Get(new GridPos(x, y)) switch
            {
                CellType.Empty => '.', CellType.ColorA => 'A', CellType.ColorB => 'B',
                CellType.ColorC => 'C', CellType.Hard => 'H', CellType.HardCracked => 'h',
                CellType.Steel => 'S', CellType.AirCapsule => 'P', _ => 'X',
            });
        Console.WriteLine($"{y,3}  {b[y]} | {s}");
    }
    return;
}

// ─────────────────────────────────────────────────────────────────────────────
// 1) Tunnel-rush bot: dig straight down, ignore lines. Tests the degenerate win.
// ─────────────────────────────────────────────────────────────────────────────
// 0.15 s is a speedrunner (≈11 rows/s with free-falls). 0.30 s is closer to a real player —
// tuning pacing against the fast bot alone would make the game lethal for humans (§15.1).
foreach (float cadence in new[] { 0.15f, 0.30f })
{
    Console.WriteLine($"=== TUNNEL-RUSH BOT (action every {cadence:0.00} s ≈ {1f / cadence:0.0} inputs/s) ===");
    Console.WriteLine($"{"lvl",3} {"outcome",-10} {"time",6} {"air@end",7} {"airMin",6} {"hearts-",7} {"score",6} {"depth",5} {"rows",4}");
    for (int level = 1; level <= 10; level++)
    {
        var sim = new Sim(StrateGenerator.CampaignBoard(level));
        sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
        sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level)); // R6.2
        sim.Gravity.WobbleDuration = CampaignManager.WobbleDurationForLevel(level);
        var bot = new TunnelBot();
        var r = sim.Run(bot, cadence, maxTime: 400f, Dt);
        Console.WriteLine(r.Row(level));
    }
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// 1b) R6.2 (F02) newcomer: reads the board for 4 s, then plays slowly. This is the player the
//     feedback came from — "je manque d'air tout de suite" — and the one the start buffer targets.
// ─────────────────────────────────────────────────────────────────────────────
foreach (var (name, make, cadence) in new (string, Func<IBot>, float)[]
{
    ("tunnel", () => new TunnelBot(),   0.60f),
    ("row-clr", () => new RowClearBot(), 0.50f),
})
{
    Console.WriteLine($"=== NEWCOMER — {name}, 4 s idle then 1 action / {cadence:0.00} s ===");
    Console.WriteLine($"{"lvl",3} {"outcome",-10} {"time",6} {"air@end",7} {"airMin",6} {"hearts-",7} {"score",6} {"depth",5} {"rows",4}");
    for (int level = 1; level <= 10; level++)
    {
        var sim = new Sim(StrateGenerator.CampaignBoard(level));
        sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
        sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level)); // R6.2
        sim.Gravity.WobbleDuration = CampaignManager.WobbleDurationForLevel(level);
        var r = sim.Run(new HesitantBot(make(), 4f), cadence, maxTime: 400f, Dt);
        Console.WriteLine(r.Row(level));
    }
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// 2) Row-clear bot: sweeps each row before descending. The v3 question is no longer
//    "does it clear lines" but "does the action economy pay out" — hence the
//    streak/burst/bomb columns and the points mix.
// ─────────────────────────────────────────────────────────────────────────────
foreach (float cadence in new[] { 0.15f, 0.25f })
{
    Console.WriteLine();
    Console.WriteLine($"=== ROW-CLEAR BOT (action every {cadence:0.00} s ≈ {1f / cadence:0.0} inputs/s) ===");
    Console.WriteLine(ActionHeader());
    for (int level = 1; level <= 10; level++)
    {
        var sim = new Sim(StrateGenerator.CampaignBoard(level));
        sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
        sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level)); // R6.2
        sim.Gravity.WobbleDuration = CampaignManager.WobbleDurationForLevel(level);
        var bot = new RowClearBot();
        var r = sim.Run(bot, cadence, maxTime: 400f, Dt);
        Console.WriteLine(r.RowWithActions(level));
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 2b) Same boards, tunnel bot — the v3 core loop is drilling, so the straight-down
//     player should still earn a healthy streak and trip bursts on the way down.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== TUNNEL-RUSH BOT — récompenses d'action ===");
Console.WriteLine(ActionHeader());
for (int level = 1; level <= 10; level++)
{
    var sim = new Sim(StrateGenerator.CampaignBoard(level));
    sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
    sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level)); // R6.2
    sim.Gravity.WobbleDuration = CampaignManager.WobbleDurationForLevel(level);
    var r = sim.Run(new TunnelBot(), 0.15f, maxTime: 400f, Dt);
    Console.WriteLine(r.RowWithActions(level));
}

static string ActionHeader() =>
    $"{"lvl",3} {"outcome",-10} {"time",6} {"airMin",6} {"hearts-",7} {"score",6} " +
    $"{"peakM",6} {"bursts",6} {"bigst",5} {"chain",5} {"bestC",5} {"PC",3} {"% Mo/B/Bo/D/PC/Bx",-23}";

// ─────────────────────────────────────────────────────────────────────────────
// 2c) Bomb-hunter bot (R2.8e): deliberately seeks buried bombs and digs at them to
//     light fuses — the other bots avoid bombs, so §15.4 could never tell whether the
//     bomb economy pays out or the generator just never clusters them. Bombs appear
//     from level 6 (6 single, 7 chains, 8+ mix), so this runs 6-10.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== BOMB-HUNTER BOT (R2.8e) — l'économie des bombes ===");
Console.WriteLine(BombHeader());
for (int level = 6; level <= 10; level++)
{
    string[] board = StrateGenerator.CampaignBoard(level);
    int bombsOnBoard = board.Sum(row => row.Count(c => c == 'X'));

    var sim = new Sim(board);
    sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
    sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level)); // R6.2
    sim.Gravity.WobbleDuration = CampaignManager.WobbleDurationForLevel(level);
    var r = sim.Run(new BombBot(), 0.15f, maxTime: 400f, Dt);
    Console.WriteLine(r.RowWithBombs(level, bombsOnBoard));
}

static string BombHeader() =>
    $"{"lvl",3} {"outcome",-10} {"time",6} {"hearts-",7} {"score",6} " +
    $"{"bombs",6} {"det",4} {"chains",6} {"bestC",5} {"ptsBomb",7} {"%Bo",4}";

// ─────────────────────────────────────────────────────────────────────────────
// 3) Board porosity: v3 needs stacked gaps, not just any gap — a chunk only bursts
//    if it can fall BurstFallThreshold rows. Counts cells with 2+ empties beneath
//    them, i.e. how many genuine burst opportunities the generator actually leaves.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== POROSITÉ / OPPORTUNITÉS DE BURST (rangées de contenu) ===");
Console.WriteLine($"{"lvl",3} {"rangées",8} {"% solide",9} {"cellules avec 2+ vides dessous",32}");
for (int level = 1; level <= 10; level++)
{
    string[] rows = StrateGenerator.CampaignBoard(level);
    int first = StrateGenerator.SpawnRows;
    int last = rows.Length - StrateGenerator.FloorRows - 1;

    int content = 0, solid = 0, total = 0, burstReady = 0, solidCells = 0;
    for (int y = first; y <= last; y++)
    {
        content++;
        int s = rows[y].Count(c => c != '.');
        solid += s;
        total += rows[y].Length;

        for (int x = 0; x < rows[y].Length; x++)
        {
            if (rows[y][x] == '.') continue;
            solidCells++;
            int gap = 0;
            for (int d = 1; d <= GravitySystem.BurstFallThreshold && y + d <= last; d++)
            {
                if (rows[y + d][x] != '.') break;
                gap++;
            }
            if (gap >= GravitySystem.BurstFallThreshold) burstReady++;
        }
    }
    double pct = solidCells == 0 ? 0 : 100.0 * burstReady / solidCells;
    Console.WriteLine($"{level,3} {content,8} {100.0 * solid / total,8:0.0}% {burstReady,10} / {solidCells,-6} ({pct,4:0.0}%)");
}

// ─────────────────────────────────────────────────────────────────────────────
// 4) Undermine pattern: drill UP the last support of a column and the freed chunk
//    drops on your head. In v3 this is a burst setup rather than a line clear —
//    the scenarios verify the crush/dodge contract still holds (design rule 4).
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== UNDERMINE / DODGE (scripté) ===");

string[] keystoneMap =
{
    "...",
    ".B.",
    ".A.",
    "AA.",
    "AAA",
};

// Scenario A: canonical keystone — undermine, drill up, dodge during the wobble.
{
    var sim = new Sim(keystoneMap, spawn: new GridPos(2, 3));
    sim.ScriptAt(0.20f, s => s.Avatar.TryDrill(-1, 0)); // tunnel under the column
    sim.ScriptAt(0.40f, s => s.Avatar.TryMove(-1));     // step into the hole, under the A support
    sim.ScriptAt(0.60f, s => s.Avatar.TryDrill(0, -1)); // KEYSTONE: drill up the last support of row 2
    sim.ScriptAt(0.80f, s => s.Avatar.TryMove(1));      // dodge back out before B lands
    var r = sim.Run(bot: null, actionInterval: 999f, maxTime: 5f, Dt, checkWin: false);
    Console.WriteLine($"A) undermine + dodge  : bursts={r.Bursts}  PC={r.PerfectClears}  heartsLost={r.HeartsLost}  score={r.Score}");
}

// Scenario B: same play, no dodge — the freed chunk falls on the avatar.
{
    var sim = new Sim(keystoneMap, spawn: new GridPos(2, 3));
    sim.ScriptAt(0.20f, s => s.Avatar.TryDrill(-1, 0));
    sim.ScriptAt(0.40f, s => s.Avatar.TryMove(-1));
    sim.ScriptAt(0.60f, s => s.Avatar.TryDrill(0, -1));
    var r = sim.Run(bot: null, actionInterval: 999f, maxTime: 5f, Dt, checkWin: false);
    Console.WriteLine($"B) undermine sans dodge: bursts={r.Bursts}  PC={r.PerfectClears}  heartsLost={r.HeartsLost}  score={r.Score}");
}

// ─────────────────────────────────────────────────────────────────────────────
// 5) Wobble comparison on levels 1-3: does a longer telegraph reduce self-crush?
//    (v3: it no longer buys line-clear time, it is purely the dodge window.)
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== WOBBLE 0.6 / 0.8 / 0.9 s — niveaux 1-3, drain campagne ===");
foreach (float cadence in new[] { 0.15f, 0.25f })
{
    Console.WriteLine();
    Console.WriteLine($"--- ROW-CLEAR ({cadence:0.00} s) ---");
    Console.WriteLine($"{"lvl",3} {"wobble",6} {"outcome",-10} {"time",6} {"airMin",6} {"hearts-",7} {"score",6} {"bursts",6} {"peakM",6}");
    for (int level = 1; level <= 3; level++)
    {
        foreach (float wobble in new[] { 0.6f, 0.8f, 0.9f })
        {
            var sim = new Sim(StrateGenerator.CampaignBoard(level));
            sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
            sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level)); // R6.2
            sim.Gravity.WobbleDuration = wobble;
            var r = sim.Run(new RowClearBot(), cadence, 400f, Dt);
            Console.WriteLine($"{level,3} {wobble,5:0.0}s {r.Outcome,-10} {r.Time,5:0.0}s {r.AirMin,5:0.0}% {r.HeartsLost,7} {r.Score,6} {r.Bursts,6} {r.PeakMomentum,6}");
        }
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 7) R7.14 — SCORE GATES vs the v3.2 momentum economy. The gate (CampaignManager.ScoreMinimumForLevel)
//    was sized for v3.1 (§9: "a tunnel-bot at 50 pts/row earns ~2,000 on a 40-row level"). Momentum,
//    fissure breaks, Power Drills, cascade and the Danger Zone all pay more now — does the gate still
//    ask for anything? Score is read when the bot reaches the exit row (the game would hold it there).
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== R7.14 — SEUILS DE SCORE vs ÉCONOMIE MOMENTUM ===");
foreach (var (name, make, cadence, idle) in new (string, Func<IBot>, float, float)[]
{
    ("tunnel rapide 0.15 s", () => new TunnelBot(),   0.15f, 0f),
    ("tunnel réaliste 0.30 s", () => new TunnelBot(), 0.30f, 0f),
    ("débutant 0.60 s",      () => new TunnelBot(),   0.60f, 4f),
    ("row-clear 0.25 s",     () => new RowClearBot(), 0.25f, 0f),
})
{
    Console.WriteLine($"--- {name} ---");
    Console.WriteLine($"{"lvl",3} {"outcome",-10} {"time",6} {"hearts-",7} {"score",6} {"gate",5} {"×gate",6} " +
                      $"{"peakM",5} {"PD",3} {"graze",5} {"fiss",5} {"casc",4} {"% Mo/B/Bo/D/PC/Bx",-23}");
    for (int level = 1; level <= 10; level++)
    {
        var sim = new Sim(StrateGenerator.CampaignBoard(level));
        sim.Air.DrainRate = CampaignManager.DrainRateForLevel(level);
        sim.Air.BeginStartBuffer(CampaignManager.AirStartGraceForLevel(level));
        sim.Gravity.WobbleDuration = CampaignManager.WobbleDurationForLevel(level);
        IBot bot = idle > 0f ? new HesitantBot(make(), idle) : make();
        var r = sim.Run(bot, cadence, maxTime: 400f, Dt);
        int gate = CampaignManager.ScoreMinimumForLevel(level);
        string ratio = gate > 0 ? $"{(float)r.Score / gate,5:0.0}×" : "    —";
        Console.WriteLine($"{level,3} {r.Outcome,-10} {r.Time,5:0.0}s {r.HeartsLost,7} {r.Score,6} {gate,5} {ratio,6} " +
                          $"{r.PeakMomentum,5} {r.PowerDrills,3} {r.Grazes,5} {r.FissureBreaks,5} {r.MaxCascade,4} {r.PointsMix(),-23}");
    }
    Console.WriteLine();
}

// ─────────────────────────────────────────────────────────────────────────────
// 6) ENDLESS (R3.1): the campaign is 10 authored boards, endless is an open-ended
//    chain of generated segments — so it needs its own measurement. Two questions:
//    a) does the generated terrain stay PLAYABLE as the difficulty ramps (hard/steel
//       climb while porosity climbs too — do bots get walled in?),
//    b) does the burst opportunity hold at depth (the §15.6 porosity value was set by
//       estimate, never measured — that is the open half of R3.1).
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== ENDLESS — DESCENTES (drain rampé par profondeur) ===");
Console.WriteLine($"{"bot",-10} {"seed",6} {"outcome",-10} {"time",6} {"seg",3} {"depth",5} {"airMin",6} {"hearts-",7} " +
                  $"{"score",7} {"peakM",6} {"bursts",6} {"bigst",5} {"drain",6} {"% Mo/B/Bo/D/PC/Bx",-23}");
foreach (int seed in new[] { 101, 202, 303 })
{
    foreach (var (name, make) in new (string, Func<IBot>)[] { ("tunnel", () => new TunnelBot()), ("row-clear", () => new RowClearBot()) })
    {
        var (r, segments, drain) = RunEndless(make, seed, 0.15f, maxTime: 400f, dt: Dt);
        Console.WriteLine($"{name,-10} {seed,6} {r.Outcome,-10} {r.Time,5:0.0}s {segments,3} {r.Depth,5} {r.AirMin,5:0.0}% {r.HeartsLost,7} " +
                          $"{r.Score,7} {r.PeakMomentum,6} {r.Bursts,6} {r.BiggestBurst,5} {drain,5:0.0}% {r.PointsMix(),-23}");
    }
}

// ─────────────────────────────────────────────────────────────────────────────
// 6b) R7.15 — does the depth drain ramp ever catch a momentum driller? The bots above die to crushes
//     long before the ramp bites (they don't dodge, §15.5), so this run makes them crush-immune: a
//     "perfect dodger" whose only way to die is air. Depth at air-death = where the ramp wins.
// ─────────────────────────────────────────────────────────────────────────────
Console.WriteLine();
Console.WriteLine("=== R7.15 — ENDLESS, ESQUIVE PARFAITE (seul l'air tue) ===");
Console.WriteLine($"{"bot",-22} {"seed",5} {"outcome",-10} {"time",6} {"depth",5} {"drain",6} {"airMin",6} " +
                  $"{"drain/s",7} {"gain/s",6} {"idle",5} {"score",7} {"PD",3} {"% Mo/B/Bo/D/PC/Bx",-23}");
foreach (var (name, make, cadence) in new (string, Func<IBot>, float)[]
{
    ("tunnel rapide 0.15 s",   () => new TunnelBot(),   0.15f),
    ("tunnel réaliste 0.30 s", () => new TunnelBot(),   0.30f),
    ("tunnel lent 0.60 s",     () => new TunnelBot(),   0.60f),
    ("row-clear 0.25 s",       () => new RowClearBot(), 0.25f),
})
{
    foreach (int seed in new[] { 101, 202, 303 })
    {
        var (r, segments, drain) = RunEndless(() => new UnstuckBot(make()), seed, cadence, maxTime: 600f, dt: Dt, crushImmune: true);
        float time   = Math.Max(0.1f, r.Time);
        float gained = r.Drills * AirSystem.DrillRestoreAmount + r.Capsules * AirSystem.CapsuleRestoreAmount
                     + r.Bursts * AirSystem.BurstRestoreAmount;
        Console.WriteLine($"{name,-22} {seed,5} {r.Outcome,-10} {r.Time,5:0.0}s {r.Depth,5} {drain,5:0.0}% {r.AirMin,5:0.0}% " +
                          $"{r.AirDrained / time,7:0.00} {gained / time,6:0.00} {r.Time - r.LastDrillAt,4:0.0}s {r.Score,7} {r.PowerDrills,3} {r.PointsMix(),-23}");
    }
}

Console.WriteLine();
Console.WriteLine("=== ENDLESS — TERRAIN PAR PROFONDEUR (moyenne sur 6 seeds, 40 rangées) ===");
Console.WriteLine($"{"depth",6} {"% solide",9} {"% dur+acier",12} {"% bombe",8} {"burst-ready",12}   (campagne: 11-15 % burst-ready)");
foreach (int startDepth in new[] { 0, 40, 80, 160, 320 })
{
    double solid = 0, hard = 0, bomb = 0, burstReady = 0;
    const int seeds = 6;
    for (int s = 0; s < seeds; s++)
    {
        var m = MeasureBoard(StrateGenerator.EndlessSegment(s * 7919 + 13, StrateGenerator.DefaultWidth, 40, startDepth));
        solid += m.solid; hard += m.hard; bomb += m.bomb; burstReady += m.burstReady;
    }
    Console.WriteLine($"{startDepth,6} {solid / seeds,8:0.0}% {hard / seeds,11:0.0}% {bomb / seeds,7:0.0}% {burstReady / seeds,11:0.0}%");
}

/// <summary>
/// Drives a full endless run: a chain of generated segments sharing one score, one air tank and one
/// set of hearts — the harness mirror of GameBootstrap's seam (§5.15). Returns the run result, the
/// number of seams crossed, and the drain rate the descent ended on.
/// </summary>
static (RunResult r, int segments, float drain) RunEndless(Func<IBot> makeBot, int seed, float cadence, float maxTime, float dt,
                                                           bool crushImmune = false)
{
    var p = new Persist { Endless = new EndlessManager(seed), CrushImmune = crushImmune };
    p.Endless.DepthChanged     += d    => p.Score.AwardDepth(d);
    p.Endless.DrainRateChanged += rate => p.Air.DrainRate = rate;
    p.Air.DrainRate = p.Endless.DrainRate;
    p.Air.BeginStartBuffer(); // R6.2: once per run — the seam is the same run continuing

    while (!p.Dead && p.Elapsed < maxTime)
    {
        var sim = new Sim(p.Endless.BuildCurrentSegment(StrateGenerator.DefaultWidth), spawn: null, persist: p);
        var res = sim.Run(makeBot(), cadence, maxTime, dt);
        if (p.Dead) break;
        if (res.Outcome != "WIN") break;   // ran out of clock inside the segment (walled in, or timeout)

        p.Endless.AdvanceSegment();        // the seam: next board, same run
        p.Segments++;
    }

    // "WIN" is a per-segment marker here — an endless run that is still alive simply ran out of clock.
    if (!p.Dead) p.Result.Outcome = "timeout";
    return (p.Result, p.Segments, p.Air.DrainRate);
}

/// <summary>Terrain stats for one board's content rows: densities + genuine burst opportunity.</summary>
static (double solid, double hard, double bomb, double burstReady) MeasureBoard(string[] rows)
{
    int first = StrateGenerator.SpawnRows;
    int last  = rows.Length - StrateGenerator.FloorRows - 1;
    int cells = 0, solid = 0, hard = 0, bomb = 0, solidCells = 0, burstReady = 0;

    for (int y = first; y <= last; y++)
    {
        for (int x = 0; x < rows[y].Length; x++)
        {
            char c = rows[y][x];
            cells++;
            if (c == '.') continue;
            solid++;
            solidCells++;
            if (c == 'H' || c == 'S') hard++;
            if (c == 'X') bomb++;

            // A chunk only bursts if it can fall BurstFallThreshold rows — stacked gaps, not any gap.
            int gap = 0;
            for (int d = 1; d <= GravitySystem.BurstFallThreshold && y + d <= last; d++)
            {
                if (rows[y + d][x] != '.') break;
                gap++;
            }
            if (gap >= GravitySystem.BurstFallThreshold) burstReady++;
        }
    }

    return (100.0 * solid / cells, 100.0 * hard / cells, 100.0 * bomb / cells,
            solidCells == 0 ? 0 : 100.0 * burstReady / solidCells);
}

// ─────────────────────────────────────────────────────────────────────────────
// Harness
// ─────────────────────────────────────────────────────────────────────────────

sealed class RunResult
{
    public string Outcome = "timeout";
    public float Time;
    public float AirEnd, AirMin = 100f;
    public int HeartsLost, Score, Depth, ContentRows, Drills, Capsules, Bombs;

    // v3 action-reward instrumentation (replaces the v2 line-source breakdown).
    public int PerfectClears, Bursts, BurstCells, BombChains;
    public int PeakMomentum, BiggestBurst, BestBombChain; // PeakMomentum = ×1/×2/×4/×6 (R7.12)

    // v3.2 (R7.14) momentum instrumentation.
    public int PowerDrills, Grazes, FissureBreaks, MaxCascade;
    public float LastDrillAt; // R7.15: sim time of the last drill — "idle" at death = stuck vs out-paced

    /// <summary>Where the points actually came from — the balance question v3 cares about.</summary>
    public int PtsDrill, PtsBurst, PtsBomb, PtsDepth, PtsPerfect;
    public int PtsFissure, PtsPower, PtsGraze, PtsFreefall;

    /// <summary>Momentum-driven points: drill pay + fissure breaks + Power Drill waves.</summary>
    public int PtsMomentum => PtsDrill + PtsFissure + PtsPower;

    /// <summary>% momentum / burst / bomb / depth / perfect clear / flat bonuses (graze + freefall).</summary>
    public string PointsMix()
    {
        int total = Math.Max(1, Score);
        return $"{100 * PtsMomentum / total,3}/{100 * PtsBurst / total,3}/{100 * PtsBomb / total,3}/" +
               $"{100 * PtsDepth / total,3}/{100 * PtsPerfect / total,3}/{100 * (PtsGraze + PtsFreefall) / total,3}";
    }

    /// <summary>Air economy: what the run spent vs what it earned back, by source.</summary>
    public float AirDrained;

    public string Row(int level)
    {
        float fromCapsules = Capsules * AirSystem.CapsuleRestoreAmount;
        float fromBursts   = Bursts   * AirSystem.BurstRestoreAmount;
        float fromDrills   = Drills   * AirSystem.DrillRestoreAmount;
        return $"{level,3} {Outcome,-10} {Time,5:0.0}s {AirEnd,6:0.0}% {AirMin,5:0.0}% {HeartsLost,7} {Score,6} {Depth,5} {ContentRows,4}" +
               $" | drain -{AirDrained,5:0.0} drill +{fromDrills,5:0.0} caps +{fromCapsules,5:0.0}({Capsules,2}) burst +{fromBursts,5:0.0}({Bursts,2})";
    }

    public string RowWithActions(int level) =>
        $"{level,3} {Outcome,-10} {Time,5:0.0}s {AirMin,5:0.0}% {HeartsLost,7} {Score,6} " +
        $"{PeakMomentum,6} {Bursts,6} {BiggestBurst,5} {BombChains,5} {BestBombChain,5} {PerfectClears,3} {PointsMix(),-23}";

    /// <summary>Bomb-economy view (§15.4 / R2.8e): what the bomb-hunter actually detonated and earned.
    /// <paramref name="bombsOnBoard"/> is the authored count, so 'det' vs 'bombs' shows arming reach.</summary>
    public string RowWithBombs(int level, int bombsOnBoard)
    {
        int total = Math.Max(1, Score);
        return $"{level,3} {Outcome,-10} {Time,5:0.0}s {HeartsLost,7} {Score,6} " +
               $"{bombsOnBoard,6} {Bombs,4} {BombChains,6} {BestBombChain,5} {PtsBomb,7} {100 * PtsBomb / total,3}%";
    }
}

interface IBot { void Act(Sim sim); }

/// <summary>
/// R7.15: the tunnel / row-clear bots loop forever in some pockets (steel underfoot, a bomb below the
/// sidestep — they step off and climb back on). A human escapes by drilling anything, often lighting the
/// bomb. After `patience` s without a drill this wrapper drills the first drillable neighbour (down,
/// left, right, up), then tries to walk. Used with crush immunity, so a lit bomb is harmless.
/// </summary>
sealed class UnstuckBot : IBot
{
    readonly IBot _inner;
    readonly float _patience;
    public UnstuckBot(IBot inner, float patience = 1.5f) { _inner = inner; _patience = patience; }

    public void Act(Sim s)
    {
        if (s.Avatar.IsFalling || s.SimTime - s.LastDrillAt < _patience) { _inner.Act(s); return; }

        var pos = s.Avatar.Position;
        foreach (var (dx, dy) in new[] { (0, 1), (-1, 0), (1, 0), (0, -1) })
        {
            var c = pos.Offset(dx, dy);
            if (s.Grid.InBounds(c) && s.Grid.Get(c).IsDrillable() && s.Avatar.TryDrill(dx, dy)) return;
        }
        if (!s.Avatar.TryMove(-1)) s.Avatar.TryMove(1);
    }
}

/// <summary>
/// R6.2 (F02): a first-time player. Stands at spawn reading the board for `idle` seconds, then plays
/// the inner bot at whatever (slow) cadence the caller runs it. The other bots act from frame 0 at
/// 4-7 inputs/s, which is exactly the player the air clock was never hurting.
/// </summary>
sealed class HesitantBot : IBot
{
    readonly IBot _inner;
    readonly float _idle;
    public HesitantBot(IBot inner, float idle) { _inner = inner; _idle = idle; }
    public void Act(Sim sim) { if (sim.SimTime >= _idle) _inner.Act(sim); }
}

/// <summary>
/// What survives a board swap. A campaign level is one Sim, but an endless RUN is a chain of Sims
/// (one per segment) that must share the score, the air tank, the hearts and the clock — exactly
/// what GameBootstrap carries across the seam (§5.15). Streak and Depth stay per-board, like the
/// real game's LoadLevel resets.
/// </summary>
sealed class Persist
{
    public readonly ScoreSystem  Score  = new();
    public readonly AirSystem    Air    = new();
    public readonly HealthSystem Health = new();
    public readonly RunResult    Result = new();
    public readonly MomentumTracker Momentum = new(); // v3.2: survives an endless seam (R7.6)
    public readonly GrazeSystem     Graze    = new();
    public bool PowerDrillPending;                     // ⚡D1: completed at the end of the drill handler

    public EndlessManager Endless;  // null for a single-board run
    public float          Elapsed;  // sim clock, carried across segments
    public bool           Wired;    // persistent-system handlers are subscribed once, not per segment
    public bool           Dead;
    public int            Segments;
    public bool           CrushImmune; // R7.15: "perfect dodger" — only air can end the run
    public Sim            Current;     // the segment being played (persistent handlers run on the first Sim)
}

sealed class Sim
{
    public readonly GridModel Grid;
    public readonly AvatarModel Avatar;
    public readonly GravitySystem Gravity;
    public readonly CollapseSystem Collapse;
    public readonly BombSystem Bombs;
    public readonly ChainTracker Chain;
    public readonly ScoreSystem Score;
    public readonly FissureTracker Fissures;            // v3.2 per board, like the game
    public MomentumTracker Momentum => _p.Momentum;
    readonly List<GridPos> _dangerCells = new();
    public readonly DepthTracker Depth = new();
    public readonly AirSystem Air;
    public readonly HealthSystem Health;

    public float SimTime;
    public float LastDrillAt => _r.LastDrillAt;

    // v3: the campaign win is depth-only — no line gate (CLAUDE.md §5.7).

    readonly Persist _p;
    readonly RunResult _r;
    readonly List<(float t, Action<Sim> a)> _script = new();

    public Sim(string[] map, GridPos? spawn = null, Persist persist = null)
    {
        _p      = persist ?? new Persist();
        Score   = _p.Score;
        Air     = _p.Air;
        Health  = _p.Health;
        _r      = _p.Result;
        SimTime = _p.Elapsed;

        Grid = GridModel.FromStringMap(map);
        Avatar = new AvatarModel(Grid, spawn ?? new GridPos(Grid.Width / 2, 0));
        Gravity = new GravitySystem(Grid);
        Collapse = new CollapseSystem(Grid);
        Bombs = new BombSystem(Grid, Collapse);
        Chain = new ChainTracker(Collapse, Gravity, Bombs); // subscribes to PerfectClear/ChunkBurst FIRST, like GameBootstrap
        Fissures = new FissureTracker(Grid);
        _p.Current = this;

        // v3.2 cascade (R7.5b/R7.6): the chain IS the cascade; a new board never inherits it.
        Score.CascadeMultiplier = 1;
        Chain.LinkAdded += link =>
        {
            Score.CascadeMultiplier = link; // ScoreSystem caps the pay at ×3 (R7.14)
            _r.MaxCascade = Math.Max(_r.MaxCascade, link);
        };
        Chain.ChainCompleted += _ => Score.CascadeMultiplier = 1;
        Score.DangerZone = Air.IsDangerZone;

        _r.ContentRows += Grid.Height - StrateGenerator.SpawnRows - StrateGenerator.FloorRows;

        // — exact GameBootstrap wiring (CLAUDE.md §7) —

        // Persistent systems are shared across an endless run's segments, so their handlers are
        // subscribed exactly once — re-wiring per segment would double-count every award.
        if (!_p.Wired)
        {
            _p.Wired = true;

            // Attribute every award to its source so the run reports where points came from.
            Score.OnScore += evt =>
            {
                switch (evt.Source)
                {
                    case ScoreSource.Drill:        _r.PtsDrill   += evt.Points; break;
                    case ScoreSource.FissureBreak: _r.PtsFissure += evt.Points; break;
                    case ScoreSource.PowerDrill:   _r.PtsPower   += evt.Points; break;
                    case ScoreSource.Graze:        _r.PtsGraze   += evt.Points; break;
                    case ScoreSource.Freefall:     _r.PtsFreefall+= evt.Points; break;
                    case ScoreSource.Burst:        _r.PtsBurst   += evt.Points; break;
                    case ScoreSource.Bomb:         _r.PtsBomb    += evt.Points; break;
                    case ScoreSource.Depth:        _r.PtsDepth   += evt.Points; break;
                    case ScoreSource.PerfectClear: _r.PtsPerfect += evt.Points; break;
                }
            };

            Air.AirDepleted       += () => { _p.Dead = true; _r.Outcome = "air-death"; _p.Current?.DumpSurroundings(); };

            // v3.2 persistent wiring — mirrors GameBootstrap.Awake (R7.6).
            Air.DangerZoneChanged += d => Score.DangerZone = d;
            _p.Momentum.PowerDrillActivated += _ => _p.PowerDrillPending = true;
            _p.Graze.GrazeTriggered += _ =>
            {
                Score.AwardGraze();
                _p.Momentum.ExtendTimer(GrazeSystem.MomentumExtension);
            };
            Health.HealthDepleted += () => { _p.Dead = true; _r.Outcome = "crushed"; };
        }

        // Exact GameBootstrap Drilled order (R7.6 / R7.7b / R7.8) — momentum, graze, then side effects,
        // fissures, and the Power Drill follow-through last.
        Avatar.Drilled += (cell, oldType, direction) =>
        {
            Momentum.NotifyDrill(oldType);
            Score.AwardDrill(Momentum.Multiplier);
            Air.RestoreDrill(); // R5.2 — was missing here, so every ledger before R6.2 under-counted air
            _p.Graze.NotifyDrill(cell, DangerCells());
            if (oldType == CellType.AirCapsule) { Air.RestoreCapsule(); _r.Capsules++; }
            Bombs.NotifyDrilled(cell);
            _r.Drills++;
            _r.LastDrillAt = SimTime;
            Fissures.NotifyDrill(cell, Momentum.CurrentTier);
            if (_p.PowerDrillPending)
            {
                _p.PowerDrillPending = false;
                PowerDrillFollowThrough(cell, direction);
                Momentum.CompletePowerDrill();
            }
        };

        Fissures.FissureBroke += _ => Score.AwardFissureBreak(Momentum.Multiplier);
        Avatar.FreefallCell   += _ => Score.AwardFreefall();

        // ── Chunk gravity + burst ────────────────────────────────────────
        Gravity.ChunkLanded += chunk => Bombs.NotifyChunkLanded(chunk);

        Gravity.ChunkBurst += (cells, fallDistance, _) =>
        {
            Score.AwardBurst(cells.Count, fallDistance);
            Air.RestoreBurst();
            _r.Bursts++;
            _r.BurstCells += cells.Count;
        };
        Gravity.AirCapsuleLiberated += _ => { Air.RestoreCapsule(); _r.Capsules++; };
        Gravity.BombArmedByBurst    += pos => Bombs.ArmBombAt(pos);
        Gravity.AvatarHitByBurst    += OnCrushed;
        Gravity.AvatarCrushed       += _ => OnCrushed();

        // ── Bombs ────────────────────────────────────────────────────────
        Bombs.BombScored += (destroyed, chainMult) =>
        {
            Score.AwardBomb(destroyed, chainMult);
            if (chainMult > 1) { Air.RestoreBombChain(chainMult); _r.BombChains++; }
        };
        Bombs.AirCapsuleLiberated += _ => { Air.RestoreCapsule(); _r.Capsules++; };
        Bombs.BombExploded        += _ => _r.Bombs++;
        Bombs.AvatarHitByBlast    += _ => OnCrushed();

        // ── Perfect Clear (rare jackpot) ─────────────────────────────────
        Collapse.PerfectClear += _ =>
        {
            Score.AwardPerfectClear(Chain.CurrentChain);
            Air.RestorePerfectClear();
        };
        Collapse.AvatarCrushed += _ => OnCrushed();

        // ── Depth ────────────────────────────────────────────────────────
        // Endless scores depth from EndlessManager (absolute across segments, §6.3); the per-board
        // DepthTracker would restart the count at every seam. The driver owns that subscription.
        if (_p.Endless == null)
            Depth.NewDepthReached += row => Score.AwardDepth(row);

        // Match GameBootstrap.LoadLevel: settle the generated board to a stable rest state BEFORE
        // play, silently (no burst, no shockwave). Without this the harness would measure a board
        // that demolishes itself on load — the exact artifact the R2.8d fix removed (§15.1).
        Gravity.Settle();
    }

    /// <summary>R7.15 diagnostic: the 7-wide band around the avatar when air runs out (stuck vs out-paced).</summary>
    public void DumpSurroundings()
    {
        if (Environment.GetEnvironmentVariable("DUMP_STUCK") != "1") return;
        var a = Avatar.Position;
        Console.WriteLine($"   [stuck @ {a}, falling={Avatar.IsFalling}]");
        for (int y = a.Y - 2; y <= a.Y + 3; y++)
        {
            if (y < 0 || y >= Grid.Height) continue;
            var row = new System.Text.StringBuilder("   ");
            for (int x = 0; x < Grid.Width; x++)
            {
                var p = new GridPos(x, y);
                row.Append(p == a ? '@' : Grid.Get(p) switch
                {
                    CellType.Empty => '.', CellType.ColorA => 'A', CellType.ColorB => 'B', CellType.ColorC => 'C',
                    CellType.Hard => 'H', CellType.HardCracked => 'h', CellType.Steel => 'S',
                    CellType.AirCapsule => 'P', CellType.Bomb => 'X', CellType.Diamond => 'D', _ => '?',
                });
            }
            Console.WriteLine(row);
        }
    }

    /// <summary>Harness has no EnemySystem — graze dangers are the armed bombs only.</summary>
    List<GridPos> DangerCells()
    {
        _dangerCells.Clear();
        Bombs.CopyArmedCells(_dangerCells);
        return _dangerCells;
    }

    /// <summary>Mirror of GameBootstrap.PowerDrillFollowThrough (R7.8).</summary>
    void PowerDrillFollowThrough(GridPos first, DrillDirection direction)
    {
        var step = direction switch
        {
            DrillDirection.Down => new GridPos(0, 1), DrillDirection.Up => new GridPos(0, -1),
            DrillDirection.Left => new GridPos(-1, 0), _ => new GridPos(1, 0),
        };
        var second = first.Offset(step.X, step.Y);
        if (!Grid.InBounds(second)) return;

        float mult = Momentum.Multiplier;
        CellType t = Grid.Get(second);
        if (t.IsDrillable() && Grid.Drill(second))
        {
            Score.AwardDrill(mult);
            Air.RestoreDrill();
            if (t == CellType.AirCapsule) { Air.RestoreCapsule(); _r.Capsules++; }
            Bombs.NotifyDrilled(second);
        }
        int destroyed = Gravity.ApplyShockwave(second, 1);
        Score.AwardPowerShockwave(destroyed, mult);
        if (t.IsDrillable())
            Fissures.NotifyDrill(second, Momentum.CurrentTier);
    }

    void OnCrushed()
    {
        if (_p.CrushImmune) return;
        if (Health.TryTakeDamage())
            _r.HeartsLost++;
    }

    public void ScriptAt(float t, Action<Sim> action) => _script.Add((t, action));

    public RunResult Run(IBot bot, float actionInterval, float maxTime, float dt, bool checkWin = true)
    {
        float actionTimer = 0f;
        int scriptIdx = 0;
        int winRow = Grid.Height - CampaignManager.WinDepthFromFloor;

        while (SimTime < maxTime && !_p.Dead)
        {
            SimTime += dt;

            while (scriptIdx < _script.Count && _script[scriptIdx].t <= SimTime)
                _script[scriptIdx++].a(this);

            actionTimer += dt;
            if (bot != null && actionTimer >= actionInterval)
            {
                actionTimer -= actionInterval;
                bot.Act(this);
            }

            // Canonical tick order (CLAUDE.md §7).
            Avatar.Tick(dt);
            Depth.NotifyPosition(Avatar.Position);
            _p.Endless?.NotifyAvatarPosition(Avatar.Position, Grid.Height);
            Bombs.Tick(dt, Avatar.Position);
            Collapse.Resolve(Avatar.Position);
            Gravity.Tick(dt, Avatar.Position);
            Chain.Tick(dt);
            bool freefall = Avatar.IsFalling;
            if (!freefall) Momentum.Tick(dt); // §M3.3: the window is frozen in freefall
            _p.Graze.Tick(dt);
            Health.Tick(dt);
            if (!freefall) // ⚡D4: no drain in freefall
            {
                _r.AirDrained += Air.EffectiveDrainRate * dt; // sampled before the tick (R6.2 buffer)
                Air.Tick(dt);
            }

            _r.AirMin = Math.Min(_r.AirMin, Air.Air);

            if (checkWin && Avatar.Position.Y >= winRow)
            {
                _r.Outcome = "WIN";
                break;
            }
        }

        _p.Elapsed       = SimTime;
        _r.Time          = SimTime;
        _r.AirEnd        = Air.Air;
        _r.Depth         = _p.Endless?.Depth ?? Depth.MaxDepth;
        _r.Score         = Score.Score;
        _r.PerfectClears = Score.PerfectClears;
        _r.PeakMomentum  = (int)Score.PeakMomentum;
        _r.PowerDrills   = Score.PowerDrills;
        _r.Grazes        = Score.Grazes;
        _r.FissureBreaks = Score.FissureBreaks;
        _r.BiggestBurst  = Score.BiggestBurst;
        _r.BestBombChain = Score.BestBombChain;
        return _r;
    }
}

sealed class TunnelBot : IBot
{
    public void Act(Sim s)
    {
        var avatar = s.Avatar;
        if (avatar.IsFalling) return;

        var pos = avatar.Position;
        var below = pos.Below;
        if (!s.Grid.InBounds(below)) return;

        var t = s.Grid.Get(below);
        if (!t.IsSolid()) return;                      // about to fall
        if (t.IsDrillable()) { avatar.TryDrill(0, 1); return; }

        // Steel or bomb below: sidestep toward the nearest passable column.
        int dir = FindSidestep(s, pos);
        if (dir == 0) return;                          // fully walled in — wait (timeout catches it)

        var side = pos.Offset(dir, 0);
        if (s.Grid.InBounds(side) && s.Grid.Get(side).IsDrillable())
            avatar.TryDrill(dir, 0);
        else if (!avatar.TryMove(dir))
            avatar.TryDrill(dir, 0);
    }

    static int FindSidestep(Sim s, GridPos pos)
    {
        for (int d = 1; d < s.Grid.Width; d++)
        {
            foreach (int sgn in new[] { -1, 1 })
            {
                int x = pos.X + sgn * d;
                if (x < 0 || x >= s.Grid.Width) continue;
                var below = new GridPos(x, pos.Y + 1);
                if (!s.Grid.InBounds(below)) continue;
                var c = s.Grid.Get(below);
                if (!c.IsSolid() || c.IsDrillable()) return sgn;
            }
        }
        return 0;
    }
}

sealed class RowClearBot : IBot
{
    public void Act(Sim s)
    {
        var avatar = s.Avatar;
        if (avatar.IsFalling) return;

        var pos = avatar.Position;

        // 1) Clear the row we stand in: head for the nearest drillable cell.
        int dir = NearestDrillableInRow(s, pos);
        if (dir != 0)
        {
            var side = pos.Offset(dir, 0);
            if (s.Grid.Get(side).IsDrillable()) { avatar.TryDrill(dir, 0); return; }
            if (avatar.TryMove(dir)) return;
            avatar.TryDrill(dir, 0);
            return;
        }

        // 2) Row done (or only Steel left): descend.
        var below = pos.Below;
        if (!s.Grid.InBounds(below)) return;
        var t = s.Grid.Get(below);
        if (!t.IsSolid()) return;
        if (t.IsDrillable()) { avatar.TryDrill(0, 1); return; }

        int side2 = TunnelBot_FindSidestep(s, pos);
        if (side2 == 0) return;
        var sideCell = pos.Offset(side2, 0);
        if (s.Grid.InBounds(sideCell) && s.Grid.Get(sideCell).IsDrillable())
            avatar.TryDrill(side2, 0);
        else if (!avatar.TryMove(side2))
            avatar.TryDrill(side2, 0);
    }

    static int NearestDrillableInRow(Sim s, GridPos pos)
    {
        for (int d = 1; d < s.Grid.Width; d++)
        {
            foreach (int sgn in new[] { -1, 1 })
            {
                int x = pos.X + sgn * d;
                if (x < 0 || x >= s.Grid.Width) continue;
                if (s.Grid.Get(new GridPos(x, pos.Y)).IsDrillable()) return sgn;
            }
        }
        return 0;
    }

    static int TunnelBot_FindSidestep(Sim s, GridPos pos)
    {
        for (int d = 1; d < s.Grid.Width; d++)
        {
            foreach (int sgn in new[] { -1, 1 })
            {
                int x = pos.X + sgn * d;
                if (x < 0 || x >= s.Grid.Width) continue;
                var below = new GridPos(x, pos.Y + 1);
                if (!s.Grid.InBounds(below)) continue;
                var c = s.Grid.Get(below);
                if (!c.IsSolid() || c.IsDrillable()) return sgn;
            }
        }
        return 0;
    }
}

// Bomb-hunter (R2.8e): unlike the other bots, this one WANTS bombs. A buried bomb arms when the
// player drills a cell adjacent to it (BombSystem.NotifyDrilled), so the strategy is simply to dig
// toward the nearest bomb — the drill that clears the last cell before it lights the fuse. It does
// not dodge the blast: the point is to measure whether bombs pay out (points, chains, air), and a
// clustered pair chains automatically via the blast BFS regardless of the bot's footwork (§15.4).
sealed class BombBot : IBot
{
    static readonly (int dx, int dy)[] Dirs = { (0, -1), (0, 1), (-1, 0), (1, 0) };

    public void Act(Sim s)
    {
        var avatar = s.Avatar;
        if (avatar.IsFalling) return;
        var pos = avatar.Position;

        // 1) If a drill from here would land on a cell touching a bomb, take it — that arms the fuse.
        foreach (var (dx, dy) in Dirs)
        {
            var d = pos.Offset(dx, dy);
            if (!s.Grid.InBounds(d) || !s.Grid.Get(d).IsDrillable()) continue;
            if (TouchesBomb(s, d)) { avatar.TryDrill(dx, dy); return; }
        }

        // 2) Otherwise head for the nearest bomb: close the horizontal gap first, then dig down its
        //    column. Two cells out, step 1 fires and arms it.
        var bomb = NearestBomb(s, pos);
        if (bomb is { } b)
        {
            int ddx = b.X - pos.X;
            if (ddx != 0)
            {
                int dir = Math.Sign(ddx);
                var side = pos.Offset(dir, 0);
                if (s.Grid.InBounds(side) && s.Grid.Get(side).IsDrillable()) { avatar.TryDrill(dir, 0); return; }
                if (avatar.TryMove(dir)) return;
                // Walled sideways (steel/bomb) — fall through and descend instead of stalling.
            }
            else
            {
                var below = pos.Below;
                if (s.Grid.InBounds(below) && s.Grid.Get(below).IsDrillable()) { avatar.TryDrill(0, 1); return; }
            }
        }

        // 3) No reachable bomb from here: descend like the tunnel bot to expose more of the board.
        Descend(s, avatar, pos);
    }

    static bool TouchesBomb(Sim s, GridPos cell)
    {
        foreach (var (dx, dy) in Dirs)
        {
            var n = cell.Offset(dx, dy);
            if (s.Grid.InBounds(n) && s.Grid.Get(n) == CellType.Bomb) return true;
        }
        return false;
    }

    static GridPos? NearestBomb(Sim s, GridPos from)
    {
        GridPos? best = null;
        int bestDist = int.MaxValue;
        for (int y = 0; y < s.Grid.Height; y++)
        {
            for (int x = 0; x < s.Grid.Width; x++)
            {
                if (s.Grid.Get(new GridPos(x, y)) != CellType.Bomb) continue;
                int dist = Math.Abs(x - from.X) + Math.Abs(y - from.Y);
                if (dist < bestDist) { bestDist = dist; best = new GridPos(x, y); }
            }
        }
        return best;
    }

    static void Descend(Sim s, AvatarModel avatar, GridPos pos)
    {
        var below = pos.Below;
        if (!s.Grid.InBounds(below)) return;
        var t = s.Grid.Get(below);
        if (!t.IsSolid()) return;                 // about to fall
        if (t.IsDrillable()) { avatar.TryDrill(0, 1); return; }

        // Steel/bomb directly below: sidestep toward the nearest passable column.
        for (int d = 1; d < s.Grid.Width; d++)
        {
            foreach (int sgn in new[] { -1, 1 })
            {
                int x = pos.X + sgn * d;
                if (x < 0 || x >= s.Grid.Width) continue;
                var bel = new GridPos(x, pos.Y + 1);
                if (!s.Grid.InBounds(bel)) continue;
                var c = s.Grid.Get(bel);
                if (!c.IsSolid() || c.IsDrillable())
                {
                    var sideCell = pos.Offset(sgn, 0);
                    if (s.Grid.InBounds(sideCell) && s.Grid.Get(sideCell).IsDrillable()) avatar.TryDrill(sgn, 0);
                    else avatar.TryMove(sgn);
                    return;
                }
            }
        }
    }
}

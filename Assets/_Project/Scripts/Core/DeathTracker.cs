using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>One heart lost: what hit the avatar, where, when, and how many hearts were left after.</summary>
    public readonly struct DamageHit
    {
        public readonly DeathCause Cause;
        public readonly GridPos    Position;
        public readonly float      Time;       // run seconds when the hit landed
        public readonly int        HeartsLeft;

        public DamageHit(DeathCause cause, GridPos position, float time, int heartsLeft)
        {
            Cause      = cause;
            Position   = position;
            Time       = time;
            HeartsLeft = heartsLeft;
        }

        public override string ToString() => $"{Cause} at {Position} t={Time:0.0}s ({HeartsLeft} left)";
    }

    /// <summary>
    /// Everything the death recap screen needs, frozen at the moment of death. Hits is a copy —
    /// the tracker can be Reset for the next run while the screen still shows this one.
    /// </summary>
    public readonly struct DeathReport
    {
        public readonly DeathCause      Cause;              // what ended the run
        public readonly GridPos         Position;           // avatar cell at death
        public readonly float           Time;               // run seconds at death
        public readonly float           TimeSinceLastDrill; // the cave-in diagnostic: how long since you stopped drilling
        public readonly float           Lead;               // R8.5/R8.9: Cave-in margin in rows at death (≥ 0)
        public readonly IReadOnlyList<DamageHit> Hits;      // every heart lost this run, oldest first

        public DeathReport(DeathCause cause, GridPos position, float time, float timeSinceLastDrill,
                           float lead, IReadOnlyList<DamageHit> hits)
        {
            Cause              = cause;
            Position           = position;
            Time               = time;
            TimeSinceLastDrill = timeSinceLastDrill;
            Lead               = Math.Max(0f, lead);
            Hits               = hits ?? Array.Empty<DamageHit>();
        }
    }

    /// <summary>
    /// R6.1 (F01 — "je comprends pas pourquoi je suis mort"): the run's damage log.
    ///
    /// Every system that can hurt the avatar already fires its own event, but they all funnelled into
    /// one anonymous OnAvatarCrushed(), so the game-over screen could only say "Plus de cœurs". This
    /// records each landed hit with its cause, plus how long it's been since the last drill (the
    /// meaningful "why" for a cave-in death: the front only catches a player who stopped drilling).
    ///
    /// Pure bookkeeping — it never deals damage or decides death. The caller reports hits that
    /// HealthSystem actually accepted, and declares the death; Died fires exactly once per run.
    /// </summary>
    public sealed class DeathTracker
    {
        private readonly List<DamageHit> _hits = new List<DamageHit>();

        // ── State ────────────────────────────────────────────────────
        /// <summary>Run seconds elapsed (sum of Tick dt — 0 while the game is paused).</summary>
        public float RunTime { get; private set; }

        /// <summary>Seconds since the last successful drill, or since the run started if none yet.</summary>
        public float TimeSinceLastDrill => RunTime - _lastDrillTime;

        public IReadOnlyList<DamageHit> Hits => _hits;
        public bool IsDead { get; private set; }

        /// <summary>The frozen report once dead; null during play.</summary>
        public DeathReport? Report { get; private set; }

        // ── Events ───────────────────────────────────────────────────
        /// <summary>Fired once, when NotifyDeath first succeeds this run.</summary>
        public event Action<DeathReport> Died;

        private float _lastDrillTime;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Advance the run clock. Call once per frame. Stops counting once dead.</summary>
        public void Tick(float dt)
        {
            if (IsDead || dt <= 0f)
                return;
            RunTime += dt;
        }

        /// <summary>Wire to AvatarModel.Drilled — resets the "last drill" clock.</summary>
        public void NotifyDrill()
        {
            if (IsDead) return;
            _lastDrillTime = RunTime;
        }

        /// <summary>
        /// Record a heart the avatar actually lost. Call only after HealthSystem.TryTakeDamage
        /// returned true (i-frame-absorbed hits are not damage). None is ignored — it is not a heart hit.
        /// Negative heart counts clamp to 0.
        /// </summary>
        public void NotifyHit(DeathCause cause, GridPos position, int heartsLeft)
        {
            if (IsDead || cause == DeathCause.None)
                return;
            _hits.Add(new DamageHit(cause, position, RunTime, Math.Max(0, heartsLeft)));
        }

        /// <summary>
        /// Declare the run over and freeze the report. Returns false (no event) if already dead —
        /// so two killing hits in the same frame only end the run once.
        /// None is coerced to the last recorded hit's cause (or CaveIn if no hit was recorded).
        /// `lead` is the Cave-in margin in rows (CeilingSystem.Lead) for the recap; negative clamps to 0.
        /// </summary>
        public bool NotifyDeath(DeathCause cause, GridPos position, float lead)
        {
            if (IsDead)
                return false;

            if (cause == DeathCause.None)
                cause = _hits.Count > 0 ? _hits[_hits.Count - 1].Cause : DeathCause.CaveIn;

            IsDead = true;
            var report = new DeathReport(cause, position, RunTime, TimeSinceLastDrill,
                                         lead, _hits.ToArray());
            Report = report;
            Died?.Invoke(report);
            return true;
        }

        /// <summary>Clear everything for a new run (or a new campaign level — hearts reset there too).</summary>
        public void Reset()
        {
            _hits.Clear();
            RunTime        = 0f;
            _lastDrillTime = 0f;
            IsDead         = false;
            Report         = null;
        }
    }
}

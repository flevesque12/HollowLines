using System;
using System.Collections.Generic;

namespace HollowLines.Core
{
    /// <summary>
    /// v3.2 Graze bonus (drill-momentum.md §M3.2): drilling right next to danger pays.
    ///
    /// Danger-agnostic on purpose — Core systems don't reference each other, so the caller
    /// (GameBootstrap) gathers the danger cells (active enemies, armed bombs, …) and passes them in.
    /// A drill grazes when the drilled cell is ON or cardinally adjacent to any of them
    /// (Manhattan distance ≤ 1), then the system cools down for <see cref="Cooldown"/> seconds.
    ///
    /// Caller ordering matters: evaluate the graze BEFORE the same drill arms a bomb or wakes an
    /// enemy, or the player would graze the danger they just created.
    ///
    /// Pure detection — ScoreSystem pays the points, MomentumTracker takes the time extension.
    /// </summary>
    public sealed class GrazeSystem
    {
        // ── Tuning (§M3.2) ───────────────────────────────────────────
        /// <summary>Seconds between two grazes — stops one danger from paying on every drill.</summary>
        public const float Cooldown = 0.5f;

        /// <summary>Seconds a graze adds to the momentum window (MomentumTracker.ExtendTimer).</summary>
        public const float MomentumExtension = 0.3f;

        // ── State ────────────────────────────────────────────────────
        /// <summary>Seconds before the next graze can trigger. 0 when ready.</summary>
        public float CooldownRemaining { get; private set; }

        public bool IsReady => CooldownRemaining <= 0f;

        // ── Events ───────────────────────────────────────────────────
        /// <summary>A graze landed — carries the drilled cell (popup / VFX anchor).</summary>
        public event Action<GridPos> GrazeTriggered;

        // ── API ──────────────────────────────────────────────────────

        /// <summary>
        /// Call on every successful drill with the current danger cells. Returns true (and fires
        /// GrazeTriggered) when the drill grazed. A null or empty danger set never grazes.
        /// </summary>
        public bool NotifyDrill(GridPos drilled, IEnumerable<GridPos> dangers)
        {
            if (!IsReady || dangers == null)
                return false;

            foreach (GridPos danger in dangers)
            {
                if (Math.Abs(danger.X - drilled.X) + Math.Abs(danger.Y - drilled.Y) > 1)
                    continue;

                CooldownRemaining = Cooldown;
                GrazeTriggered?.Invoke(drilled);
                return true;
            }
            return false;
        }

        /// <summary>Counts the cooldown down.</summary>
        public void Tick(float dt)
        {
            if (dt <= 0f || CooldownRemaining <= 0f)
                return;
            CooldownRemaining = Math.Max(0f, CooldownRemaining - dt);
        }

        /// <summary>Ready again — new run / level / segment.</summary>
        public void Reset()
        {
            CooldownRemaining = 0f;
        }
    }
}

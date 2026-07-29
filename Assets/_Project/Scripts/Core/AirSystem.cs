using System;

namespace HollowLines.Core
{
    /// <summary>
    /// The suffocation clock.
    ///
    /// Air drains constantly, and v3 pays it back for aggression (design rule 5): capsules,
    /// chunk bursts and bomb chains all restore air, so passive play suffocates. Perfect Clear
    /// still gives the biggest single top-up, but it is now a rare bonus rather than the loop.
    ///
    /// AirDepleted fires exactly once per run — the game loop handles the consequence (game over screen).
    /// Reset() clears the flag so the next run can deplete again.
    /// </summary>
    public sealed class AirSystem
    {
        // ── Tunables ────────────────────────────────────────────────
        public const float MaxAir           = 100f; // percentage
        public const float DefaultDrainRate = 5f;   // % per second (endless / debug boards)

        // Restore values (§15.1). The v3 draft numbers (+25 capsule / +5 burst / +3 per bomb /
        // +15 perfect clear) assumed these were RARE events. Measured on a real board they are not:
        // a single level-10 descent produced 78 bursts and 34 capsules, for +1240 % of air income
        // against 62 % of drain — a 20× oversupply that made the suffocation clock decorative.
        // Scaled so aggression offsets a meaningful share of the drain without erasing it.
        public const float PerfectClearRestoreAmount = 6f;   // % per Perfect Clear
        public const float CapsuleRestoreAmount      = 6f;   // % per air capsule (drilled or liberated)
        public const float BurstRestoreAmount        = 0.5f; // % per chunk burst
        public const float BombChainRestorePerBomb   = 1f;   // % per bomb in a sympathetic chain

        /// <summary>
        /// Drain in % per second. Campaign overrides this per level (CampaignManager.DrainRateForLevel);
        /// endless and debug boards keep the default.
        /// </summary>
        public float DrainRate { get; set; } = DefaultDrainRate;

        // ── State ────────────────────────────────────────────────────
        public float Air { get; private set; } = MaxAir;
        public bool IsEmpty => Air <= 0f;

        // ── Events ───────────────────────────────────────────────────
        /// <summary>Fired once when Air first reaches 0. Subscribe here for game-over logic.</summary>
        public event Action AirDepleted;

        /// <summary>Fired whenever Air changes — view updates the HUD bar here.</summary>
        public event Action<float> AirChanged;

        private bool _depleted;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>Call once per frame. Drains air; fires AirDepleted the first time Air hits 0.</summary>
        public void Tick(float dt)
        {
            if (Air <= 0f)
                return;

            Air = Math.Max(0f, Air - DrainRate * dt);
            AirChanged?.Invoke(Air);

            if (Air <= 0f && !_depleted)
            {
                _depleted = true;
                AirDepleted?.Invoke();
            }
        }

        /// <summary>+15% air for a full-void row. Wire to CollapseSystem.PerfectClear.</summary>
        public void RestorePerfectClear() => Restore(PerfectClearRestoreAmount);

        /// <summary>
        /// +25% air for a capsule, whether the avatar drilled it or a blast/shockwave freed it.
        /// Wire to AvatarModel.Drilled, BombSystem.AirCapsuleLiberated and GravitySystem.AirCapsuleLiberated.
        /// </summary>
        public void RestoreCapsule() => Restore(CapsuleRestoreAmount);

        /// <summary>+5% air for shattering a chunk on impact. Wire to GravitySystem.ChunkBurst.</summary>
        public void RestoreBurst() => Restore(BurstRestoreAmount);

        /// <summary>
        /// +3% air per bomb in a sympathetic chain. Wire to BombSystem.BombScored when chainMult > 1.
        /// Non-positive counts restore nothing (defensive — no negative air from a bad caller).
        /// </summary>
        public void RestoreBombChain(int bombCount)
        {
            if (bombCount <= 0)
                return;
            Restore(BombChainRestorePerBomb * bombCount);
        }

        /// <summary>Reset to full air for a new run.</summary>
        public void Reset()
        {
            Air = MaxAir;
            _depleted = false;
            AirChanged?.Invoke(Air);
        }

        // ── Private ──────────────────────────────────────────────────

        private void Restore(float amount)
        {
            if (amount <= 0f || Air >= MaxAir)
                return;

            float before = Air;
            Air = Math.Min(MaxAir, Air + amount);
            if (Air != before)
                AirChanged?.Invoke(Air);
        }
    }
}

using System;

namespace HollowLines.Core
{
    /// <summary>
    /// M3 step 2: the avatar's life counter.
    ///
    /// Three hearts, no natural regeneration. Any crush (chunk or collapse) or bomb blast costs one.
    /// I-frames after each hit prevent a single event from draining all hearts at once.
    /// HealthDepleted fires exactly once per run; Reset() arms it for the next.
    /// </summary>
    public sealed class HealthSystem
    {
        // ── Tunables ────────────────────────────────────────────────
        public const int   MaxHearts      = 3;
        public const float IFrameDuration = 1.5f; // seconds of post-hit invincibility

        // ── State ────────────────────────────────────────────────────
        public int  Hearts       { get; private set; } = MaxHearts;
        public bool IsAlive      => Hearts > 0;
        public bool IsInvincible => _iFrameTimer > 0f;

        // ── Events ───────────────────────────────────────────────────
        /// <summary>Fired on every successful hit. Payload = remaining heart count.</summary>
        public event Action<int> HeartsChanged;

        /// <summary>Fired once when Hearts reaches 0. Subscribe here for game-over logic.</summary>
        public event Action HealthDepleted;

        private float _iFrameTimer;
        private bool  _depleted;

        // ── Public API ───────────────────────────────────────────────

        /// <summary>
        /// Advance the i-frame cooldown. Call once per frame (after GravitySystem.Tick).
        /// </summary>
        public void Tick(float dt)
        {
            if (_iFrameTimer > 0f)
                _iFrameTimer = Math.Max(0f, _iFrameTimer - dt);
        }

        /// <summary>
        /// Attempt to deal one heart of damage.
        /// Returns false without effect if the avatar is invincible or already dead.
        /// Returns true when damage lands; HealthDepleted fires inside this call if Hearts hits 0.
        /// </summary>
        public bool TryTakeDamage()
        {
            if (!IsAlive || IsInvincible)
                return false;

            Hearts--;
            _iFrameTimer = IFrameDuration;
            HeartsChanged?.Invoke(Hearts);

            if (Hearts <= 0 && !_depleted)
            {
                _depleted = true;
                HealthDepleted?.Invoke();
            }

            return true;
        }

        /// <summary>Reset to full health for a new run.</summary>
        public void Reset()
        {
            Hearts      = MaxHearts;
            _iFrameTimer = 0f;
            _depleted   = false;
            HeartsChanged?.Invoke(Hearts);
        }
    }
}

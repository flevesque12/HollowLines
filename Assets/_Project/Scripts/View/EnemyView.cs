using System.Collections.Generic;
using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Renders enemies as sprites layered OVER the cell grid (R5.17, §6.5).
    ///
    /// Enemies are ACTORS, not CellTypes — they never appear in BoardView's tile array, so they get
    /// their own SpriteRenderers on a higher sorting order. They sit above the tiles (0) but below
    /// the avatar (10), so when a Crawler walks onto the driller's cell the driller stays readable
    /// at the exact moment contact damage fires.
    ///
    /// Dormant vs active is the whole visual language here: a buried enemy is dim and perfectly
    /// still (it cannot hurt you, §6.5), and waking up is what makes it opaque and start moving.
    /// Death VFX belong to VfxManager (§5.10) — this class just removes the sprite.
    /// </summary>
    public sealed class EnemyView : MonoBehaviour
    {
        [Header("— Look —")]
        [Tooltip("Crawler: a dark green bug.")]
        [SerializeField] private Color crawlerColor = new Color(0.30f, 0.55f, 0.22f);
        [Tooltip("Boomer: an orange orb.")]
        [SerializeField] private Color boomerColor  = new Color(0.95f, 0.55f, 0.15f);

        [Tooltip("Alpha of a buried, dormant enemy — visible enough to plan around, clearly inert.")]
        [Range(0.15f, 0.8f)]
        [SerializeField] private float dormantAlpha = 0.4f;

        [SerializeField] private float crawlerScale = 0.62f;
        [SerializeField] private float boomerScale  = 0.72f;

        [Header("— Motion —")]
        [Tooltip("Approach speed toward the enemy's current cell. Matches AvatarView's easing idea.")]
        [Range(4f, 40f)]
        [SerializeField] private float followSpeed = 12f;

        [Tooltip("How far an active Crawler shuffles side to side, in cells.")]
        [SerializeField] private float crawlerShuffleAmount = 0.10f;
        [SerializeField] private float crawlerShuffleSpeed  = 7f;

        [Tooltip("How much an active Boomer's pulse swells it.")]
        [SerializeField] private float boomerPulseAmount = 0.12f;
        [SerializeField] private float boomerPulseSpeed  = 3f;

        /// <summary>Above the tiles (0) and the exit glow (1), below the avatar (10).</summary>
        private const int EnemySortingOrder = 8;

        /// <summary>
        /// How often the enemy positions are re-read. EnemySystem has no "moved" event, so this has
        /// to poll — but a Crawler only steps every CrawlerMoveInterval (0.8 s), so re-reading at
        /// 0.1 s is far more responsive than anything that can actually change, while costing 10
        /// GetAllAlive() list allocations per second instead of 60. The per-frame lerp below is what
        /// makes the movement smooth; this only supplies the target.
        /// </summary>
        private const float PositionPollInterval = 0.1f;

        private EnemySystem _enemies;
        private float _pollTimer;

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();

        private sealed class Marker
        {
            public SpriteRenderer Sr;
            public EnemyType Type;
            public bool      Active;
            public Vector3   Base;    // eased cell position, WITHOUT the animation offset
            public Vector3   Target;  // where the model says it is
        }

        /// <summary>Called by GameBootstrap after the level's EnemySystem exists.</summary>
        public void Init(EnemySystem enemies)
        {
            _enemies = enemies;
            if (_enemies == null)
                return;

            _enemies.EnemySpawned   += OnEnemySpawned;
            _enemies.EnemyActivated += OnEnemyActivated;
            _enemies.EnemyKilled    += OnEnemyKilled;
        }

        private void OnDestroy()
        {
            if (_enemies == null)
                return;

            _enemies.EnemySpawned   -= OnEnemySpawned;
            _enemies.EnemyActivated -= OnEnemyActivated;
            _enemies.EnemyKilled    -= OnEnemyKilled;
        }

        // ── Events ───────────────────────────────────────────────────────

        private void OnEnemySpawned(int id, EnemyType type, GridPos pos)
        {
            if (_markers.ContainsKey(id))
                return;

            var go = new GameObject(type == EnemyType.Boomer ? "Boomer" : "Crawler");
            go.transform.SetParent(transform, false);

            Vector3 local = BoardView.ToLocal(pos);
            go.transform.localPosition = local;
            go.transform.localScale    = Vector3.one * ScaleFor(type);

            var sr = go.AddComponent<SpriteRenderer>();
            sr.sprite       = BoardView.GetUnitSprite();
            sr.sortingOrder = EnemySortingOrder;

            var marker = new Marker { Sr = sr, Type = type, Active = false, Base = local, Target = local };
            _markers[id] = marker;
            ApplyTint(marker);
        }

        private void OnEnemyActivated(int id)
        {
            if (!_markers.TryGetValue(id, out Marker marker))
                return;

            marker.Active = true;
            ApplyTint(marker);
        }

        /// <summary>The sprite goes away; the death burst is VfxManager's job (§5.10).</summary>
        private void OnEnemyKilled(int id, EnemyType type, KillMethod method, int bonus)
        {
            if (!_markers.TryGetValue(id, out Marker marker))
                return;

            if (marker.Sr != null)
                Destroy(marker.Sr.gameObject);
            _markers.Remove(id);
        }

        // ── Per-frame ────────────────────────────────────────────────────

        private void Update()
        {
            if (_markers.Count == 0)
                return;

            RefreshTargets();

            float dt  = Time.deltaTime;
            float now = Time.time;

            foreach (var kvp in _markers)
            {
                Marker m = kvp.Value;
                if (m.Sr == null)
                    continue;

                // Ease toward the cell the model reports, then lay the idle animation on top of that
                // base — the same split CameraShake uses, so movement and animation never fight.
                m.Base = Vector3.Lerp(m.Base, m.Target, followSpeed * dt);

                Vector3 offset = Vector3.zero;
                float   scale  = ScaleFor(m.Type);

                if (m.Active)
                {
                    if (m.Type == EnemyType.Crawler)
                    {
                        // Shuffle: a lateral wiggle with a half-rate bob, so it scuttles rather than slides.
                        offset.x = Mathf.Sin(now * crawlerShuffleSpeed) * crawlerShuffleAmount;
                        offset.y = Mathf.Abs(Mathf.Sin(now * crawlerShuffleSpeed * 0.5f)) * crawlerShuffleAmount * 0.4f;
                    }
                    else
                    {
                        // Boomer: breathing swell — it never moves, so the pulse is all it has.
                        float pulse = Mathf.Sin(now * boomerPulseSpeed) * 0.5f + 0.5f;
                        scale *= 1f + pulse * boomerPulseAmount;
                    }
                }

                m.Sr.transform.localPosition = m.Base + offset;
                m.Sr.transform.localScale    = Vector3.one * scale;
            }
        }

        /// <summary>Re-reads where the model says every living enemy is (see PositionPollInterval).</summary>
        private void RefreshTargets()
        {
            if (_enemies == null)
                return;

            _pollTimer -= Time.deltaTime;
            if (_pollTimer > 0f)
                return;
            _pollTimer = PositionPollInterval;

            foreach (EnemyEntity e in _enemies.GetAllAlive())
            {
                if (_markers.TryGetValue(e.Id, out Marker m))
                    m.Target = BoardView.ToLocal(e.Position);
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private float ScaleFor(EnemyType type) =>
            type == EnemyType.Boomer ? boomerScale : crawlerScale;

        private void ApplyTint(Marker marker)
        {
            Color c = marker.Type == EnemyType.Boomer ? boomerColor : crawlerColor;
            marker.Sr.color = new Color(c.r, c.g, c.b, marker.Active ? 1f : dormantAlpha);
        }
    }
}

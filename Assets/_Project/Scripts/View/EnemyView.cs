using System;
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
    /// R6.4 (F04 — "je voyais pas les ennemis"): enemies now have their own silhouettes and colours
    /// (EnemySprites — lime Crawler, violet Boomer, neither used by any block), a black outline, and
    /// eyes that are shut while dormant and open once awake. Dormant enemies breathe slowly instead of
    /// sitting at 40 % alpha on top of a block, where they were effectively invisible. A "!" pops over
    /// an enemy when it wakes, and blinks over an active Crawler that threatens the avatar (same row,
    /// close by) — DangerStarted lets the audio voice that same moment.
    /// Death VFX belong to VfxManager (§5.10) — this class just removes the sprite.
    /// </summary>
    public sealed class EnemyView : MonoBehaviour
    {
        [Header("— Look —")]
        [Tooltip("Crawler: acid lime — no block uses it, and it matches the Crawler kill popup.")]
        [SerializeField] private Color crawlerColor = new Color(0.62f, 0.95f, 0.20f);
        [Tooltip("Boomer: violet — no block uses it, and it matches the BOOM popup.")]
        [SerializeField] private Color boomerColor  = new Color(0.66f, 0.38f, 1.00f);

        [Tooltip("Grey tint of a dormant enemy: clearly asleep, still clearly THERE.")]
        [SerializeField] private Color dormantTint = new Color(0.72f, 0.72f, 0.72f, 0.95f);

        [SerializeField] private float crawlerScale = 0.9f;
        [SerializeField] private float boomerScale  = 0.95f;

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

        [Tooltip("Dormant breathing: a slow, small swell so a sleeping enemy reads as alive.")]
        [SerializeField] private float dormantBreathAmount = 0.05f;
        [SerializeField] private float dormantBreathSpeed  = 1.6f;

        [Header("— Alert \"!\" —")]
        [Tooltip("How long the wake-up \"!\" stays up.")]
        [SerializeField] private float wakeAlertSeconds = 1.1f;
        [Tooltip("An active Crawler on the avatar's row within this many columns counts as a threat.")]
        [SerializeField] private int dangerColumns = 3;

        /// <summary>Above the tiles (0) and the exit glow (1), below the avatar (10).</summary>
        private const int EnemySortingOrder = 8;
        private const int AlertSortingOrder = 9;

        /// <summary>
        /// How often the enemy positions are re-read. EnemySystem has no "moved" event, so this has
        /// to poll — but a Crawler only steps every CrawlerMoveInterval (0.8 s), so re-reading at
        /// 0.1 s is far more responsive than anything that can actually change, while costing 10
        /// GetAllAlive() list allocations per second instead of 60. The per-frame lerp below is what
        /// makes the movement smooth; this only supplies the target.
        /// </summary>
        private const float PositionPollInterval = 0.1f;

        /// <summary>
        /// R6.4: an active Crawler just became a threat to the avatar (rising edge, once per approach).
        /// GameBootstrap routes it to AudioManager.PlayEnemyAlert.
        /// </summary>
        public event Action<EnemyType> DangerStarted;

        private EnemySystem _enemies;
        private AvatarModel _avatar; // null-safe: without it there is simply no danger "!"
        private float _pollTimer;

        private Sprite _crawlerOpen, _crawlerShut, _boomerOpen, _boomerShut, _alert;

        private readonly Dictionary<int, Marker> _markers = new Dictionary<int, Marker>();

        private sealed class Marker
        {
            public SpriteRenderer Sr;
            public SpriteRenderer Alert;
            public EnemyType Type;
            public bool      Active;
            public GridPos   Cell;     // last polled model cell (for the danger test)
            public Vector3   Base;     // eased cell position, WITHOUT the animation offset
            public Vector3   Target;   // where the model says it is
            public float     WakeAlert;// seconds left on the wake-up "!"
            public bool      InDanger;
            public float     Phase;    // per-enemy animation offset, so a group doesn't move in lockstep
        }

        /// <summary>Called by GameBootstrap after the level's EnemySystem exists.</summary>
        public void Init(EnemySystem enemies, AvatarModel avatar = null)
        {
            _enemies = enemies;
            _avatar  = avatar;

            _crawlerOpen = EnemySprites.Crawler(crawlerColor, eyesOpen: true);
            _crawlerShut = EnemySprites.Crawler(crawlerColor, eyesOpen: false);
            _boomerOpen  = EnemySprites.Boomer(boomerColor, eyesOpen: true);
            _boomerShut  = EnemySprites.Boomer(boomerColor, eyesOpen: false);
            _alert       = EnemySprites.Alert();

            if (_enemies == null)
                return;

            _enemies.EnemySpawned   += OnEnemySpawned;
            _enemies.EnemyActivated += OnEnemyActivated;
            _enemies.EnemyKilled    += OnEnemyKilled;
        }

        private void OnDestroy()
        {
            if (_enemies != null)
            {
                _enemies.EnemySpawned   -= OnEnemySpawned;
                _enemies.EnemyActivated -= OnEnemyActivated;
                _enemies.EnemyKilled    -= OnEnemyKilled;
            }

            // The sprites are generated per board — free their textures with the board.
            foreach (Sprite s in new[] { _crawlerOpen, _crawlerShut, _boomerOpen, _boomerShut, _alert })
                if (s != null) { Destroy(s.texture); Destroy(s); }
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
            sr.sortingOrder = EnemySortingOrder;

            // The "!" is a sibling, not a child: the body scales/pulses and the alert must not.
            var alertGo = new GameObject("Alert");
            alertGo.transform.SetParent(transform, false);
            var alert = alertGo.AddComponent<SpriteRenderer>();
            alert.sprite       = _alert;
            alert.sortingOrder = AlertSortingOrder;
            alert.enabled      = false;

            var marker = new Marker
            {
                Sr = sr, Alert = alert, Type = type, Active = false, Cell = pos,
                Base = local, Target = local, Phase = id * 1.37f,
            };
            _markers[id] = marker;
            ApplyLook(marker);
        }

        private void OnEnemyActivated(int id)
        {
            if (!_markers.TryGetValue(id, out Marker marker))
                return;

            marker.Active    = true;
            marker.WakeAlert = wakeAlertSeconds;
            ApplyLook(marker);
        }

        /// <summary>The sprite goes away; the death burst is VfxManager's job (§5.10).</summary>
        private void OnEnemyKilled(int id, EnemyType type, KillMethod method, int bonus)
        {
            if (!_markers.TryGetValue(id, out Marker marker))
                return;

            if (marker.Sr != null)    Destroy(marker.Sr.gameObject);
            if (marker.Alert != null) Destroy(marker.Alert.gameObject);
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
                float   t      = now + m.Phase;

                if (!m.Active)
                {
                    // Asleep: a slow breath. Still, but not dead-still — it's a creature, not a tile.
                    scale *= 1f + (Mathf.Sin(t * dormantBreathSpeed) * 0.5f + 0.5f) * dormantBreathAmount;
                }
                else if (m.Type == EnemyType.Crawler)
                {
                    // Shuffle: a lateral wiggle with a half-rate bob, so it scuttles rather than slides.
                    offset.x = Mathf.Sin(t * crawlerShuffleSpeed) * crawlerShuffleAmount;
                    offset.y = Mathf.Abs(Mathf.Sin(t * crawlerShuffleSpeed * 0.5f)) * crawlerShuffleAmount * 0.4f;
                }
                else
                {
                    // Boomer: breathing swell — it never moves, so the pulse is all it has.
                    float pulse = Mathf.Sin(t * boomerPulseSpeed) * 0.5f + 0.5f;
                    scale *= 1f + pulse * boomerPulseAmount;
                }

                // Active enemies pulse in brightness too — the thing that can hurt you keeps moving
                // in the corner of your eye.
                if (m.Active)
                {
                    float glow = Mathf.Lerp(0.82f, 1f, Mathf.Sin(t * 5f) * 0.5f + 0.5f);
                    m.Sr.color = new Color(glow, glow, glow, 1f);
                }

                m.Sr.transform.localPosition = m.Base + offset;
                m.Sr.transform.localScale    = Vector3.one * scale;

                UpdateAlert(m, dt, now);
            }
        }

        /// <summary>
        /// The "!" over an enemy: a bouncing pop for wakeAlertSeconds after it wakes, then — for an
        /// active Crawler only — a fast blink while it threatens the avatar. Boomers never threaten
        /// (their blast never hurts the avatar, §6.5), so they only get the wake pop.
        /// </summary>
        private void UpdateAlert(Marker m, float dt, float now)
        {
            bool danger = m.Active && m.Type == EnemyType.Crawler && Threatens(m.Cell);
            if (danger && !m.InDanger)
                DangerStarted?.Invoke(m.Type);
            m.InDanger = danger;

            if (m.WakeAlert > 0f)
                m.WakeAlert = Mathf.Max(0f, m.WakeAlert - dt);

            bool show = m.WakeAlert > 0f || (danger && Mathf.Repeat(now * 6f, 1f) < 0.6f);
            m.Alert.enabled = show;
            if (!show) return;

            // Pop-in with overshoot for the wake alert; a steady size for the danger blink.
            float pop = 1f;
            if (m.WakeAlert > 0f)
            {
                float age = wakeAlertSeconds - m.WakeAlert;
                pop = age < 0.15f ? Mathf.Lerp(0.2f, 1.35f, age / 0.15f)
                    : age < 0.3f  ? Mathf.Lerp(1.35f, 1f, (age - 0.15f) / 0.15f)
                    : 1f;
            }
            float bob = Mathf.Abs(Mathf.Sin(now * 8f)) * 0.08f;
            m.Alert.transform.localPosition = m.Base + new Vector3(0f, 0.85f + bob, 0f);
            m.Alert.transform.localScale    = Vector3.one * 0.9f * pop;
        }

        private bool Threatens(GridPos cell)
        {
            if (_avatar == null) return false;
            GridPos a = _avatar.Position;
            return cell.Y == a.Y && Mathf.Abs(cell.X - a.X) <= dangerColumns;
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
                {
                    m.Cell   = e.Position;
                    m.Target = BoardView.ToLocal(e.Position);
                }
            }
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private float ScaleFor(EnemyType type) =>
            type == EnemyType.Boomer ? boomerScale : crawlerScale;

        /// <summary>Eyes shut + grey while dormant; eyes open + full colour once awake.</summary>
        private void ApplyLook(Marker marker)
        {
            bool boomer = marker.Type == EnemyType.Boomer;
            marker.Sr.sprite = marker.Active
                ? (boomer ? _boomerOpen : _crawlerOpen)
                : (boomer ? _boomerShut : _crawlerShut);
            marker.Sr.color = marker.Active ? Color.white : dormantTint;
        }
    }
}

using HollowLines.Core;
using UnityEngine;

namespace HollowLines.View
{
    /// <summary>
    /// Translates GameInput into AvatarModel intents.
    /// Holds no game rules — walking, step-up, and drill legality all live in the model.
    /// </summary>
    public sealed class AvatarController : MonoBehaviour
    {
        [Header("— Walk Feel —")]
        [Tooltip("Seconds between one-cell steps while the walk key is held.")]
        [Range(0.05f, 0.3f)]
        [SerializeField] private float walkRepeatInterval = 0.12f;

        private AvatarModel _model;
        private GameInput _input;
        private float _walkTimer;

        /// <summary>Called by GameBootstrap after the model exists. No FindObjectOfType — explicit wiring only.</summary>
        public void Init(AvatarModel model, GameInput input)
        {
            _model = model;
            _input = input;
            _input.DrillRequested += OnDrillRequested;
        }

        private void OnDestroy()
        {
            if (_input != null)
                _input.DrillRequested -= OnDrillRequested;
        }

        private void Update()
        {
            if (_model == null)
                return;

            // Avatar gravity (Tick) is driven by GameBootstrap.Update() to guarantee
            // correct order: avatar-fall → collapse → chunk-gravity → chain-tracker.

            int axis = _input != null ? _input.MoveAxis : 0;
            if (axis == 0)
            {
                _walkTimer = 0f; // first press after idle moves instantly
                return;
            }

            _walkTimer -= Time.deltaTime;
            if (_walkTimer <= 0f)
            {
                _model.TryMove(axis);
                _walkTimer = walkRepeatInterval;
            }
        }

        private void OnDrillRequested(Vector2Int direction)
        {
            _model?.TryDrill(direction.x, direction.y);
        }
    }
}

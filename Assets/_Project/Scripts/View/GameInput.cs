using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace HollowLines.View
{
    /// <summary>
    /// Single entry point for the Input System — no other script reads Keyboard/Gamepad directly.
    /// Desktop bindings: A/D (or Q/D) to walk, arrow keys to drill in the four directions.
    /// Xbox / gamepad bindings: left stick to walk; the four FACE BUTTONS drill in the direction of
    /// their physical position on the pad — Y = up, A = down, X = left, B = right. Matching the
    /// button diamond to the drill direction makes it read without a legend.
    ///
    /// While any full-screen overlay is up (main menu, pause, game over) Time.timeScale is 0, and we
    /// suppress all gameplay input so the same buttons the UI navigates with (D-pad, A) do not also
    /// move or drill the avatar behind the menu.
    /// </summary>
    public sealed class GameInput : MonoBehaviour
    {
        /// <summary>Left stick past this magnitude counts as a discrete walk in that direction.</summary>
        private const float StickDeadzone = 0.5f;

        /// <summary>-1, 0 or +1. Polled by AvatarController every frame while held.</summary>
        public int MoveAxis { get; private set; }

        /// <summary>Fired once per press with a cardinal direction in GRID space (+y = down).</summary>
        public event Action<Vector2Int> DrillRequested;

        private void Update()
        {
            // An overlay is showing (timeScale frozen): the UI owns the controller, not the avatar.
            if (Time.timeScale == 0f)
            {
                MoveAxis = 0;
                return;
            }

            Keyboard keyboard = Keyboard.current;
            Gamepad  gamepad  = Gamepad.current;

            // ── Move (held) : keyboard A/D/Q + gamepad left stick ────────────────
            bool left = false, right = false;
            if (keyboard != null)
            {
                left  |= keyboard.aKey.isPressed || keyboard.qKey.isPressed;
                right |= keyboard.dKey.isPressed;
            }
            if (gamepad != null)
            {
                float sx = gamepad.leftStick.x.ReadValue();
                left  |= sx < -StickDeadzone;
                right |= sx >  StickDeadzone;
            }
            MoveAxis = (right ? 1 : 0) - (left ? 1 : 0);

            // ── Drill (edge-triggered) : keyboard arrows + gamepad D-pad ─────────
            if (keyboard != null)
            {
                if (keyboard.leftArrowKey.wasPressedThisFrame)  DrillRequested?.Invoke(new Vector2Int(-1,  0));
                if (keyboard.rightArrowKey.wasPressedThisFrame) DrillRequested?.Invoke(new Vector2Int( 1,  0));
                if (keyboard.downArrowKey.wasPressedThisFrame)  DrillRequested?.Invoke(new Vector2Int( 0,  1));
                if (keyboard.upArrowKey.wasPressedThisFrame)    DrillRequested?.Invoke(new Vector2Int( 0, -1));
            }
            if (gamepad != null)
            {
                // Face buttons drill toward their position on the diamond (Y↑ A↓ X← B→).
                if (gamepad.buttonNorth.wasPressedThisFrame) DrillRequested?.Invoke(new Vector2Int( 0, -1)); // Y = up
                if (gamepad.buttonSouth.wasPressedThisFrame) DrillRequested?.Invoke(new Vector2Int( 0,  1)); // A = down
                if (gamepad.buttonWest.wasPressedThisFrame)  DrillRequested?.Invoke(new Vector2Int(-1,  0)); // X = left
                if (gamepad.buttonEast.wasPressedThisFrame)  DrillRequested?.Invoke(new Vector2Int( 1,  0)); // B = right
            }
        }
    }
}

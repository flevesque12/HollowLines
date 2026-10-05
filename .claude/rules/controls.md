---
paths:
  - "**/GameInput*"
---

## 16. Controls & input

Added after R2.8, before R3. All input flows through **`GameInput`** (gameplay) and the
**`InputSystemUIInputModule`** created by `UIScreenManager` (menus) — no other script reads devices.

### Bindings

| Action | Keyboard | Xbox / gamepad |
|---|---|---|
| Walk left / right | A/D (or Q/D) | Left stick X (deadzone 0.5) |
| Drill **up** | ↑ | **Y** |
| Drill **down** | ↓ | **A** |
| Drill **left** | ← | **X** |
| Drill **right** | → | **B** |
| Pause / resume | Esc | Start |
| Menu: navigate | arrows | D-pad / left stick |
| Menu: select / back | Enter / Esc | A / B |

**Face-button drill mapping (gamepad):** the four face buttons drill toward their **physical
position** on the diamond — Y↑ A↓ X← B→ — so it reads without a legend. This replaced an earlier
D-pad drill scheme (dev call). The D-pad is currently unused in gameplay (free for a future "also
move" binding if wanted).

### Two rules that keep it from crossing wires

1. **Gameplay input is suppressed while any overlay is open** (`GameInput.Update` early-returns when
   `Time.timeScale == 0f`). Menus freeze time, so the same buttons the UI navigates with — A (Submit),
   D-pad (Navigate) — do **not** also drill/move the avatar behind the menu. This is why A can mean
   both "Submit" (menu) and "drill down" (play) with no conflict.
2. **The EventSystem is created at runtime**, not in the scene (`UIScreenManager.EnsureEventSystem`,
   §5.12). Without it, UI Toolkit buttons are mouse-only — a gamepad cannot navigate them.

### Verified (2026-07-21, Unity play mode)

Xbox 360 pad shows up as `Gamepad.current` (an `XInputControllerWindows` device); EventSystem +
module present; the main-menu primary button auto-focuses; a `NavigationSubmitEvent` on the focused
"Jouer" started the game (overlay → None, `timeScale` → 1). The one hop that **cannot** be simulated
from the editor is the physical XInput-button → module step (a generic `GamepadState` event is
rejected by the XInput device's state format), so per-button feel is confirmed by hardware testing,
not code.

using System.Collections.Generic;
using System.Globalization;
using MelonLoader;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;

namespace KeySeat
{
    /// <summary>
    /// The player's own settings (UserData/MelonPreferences.cfg, category KeySeat): the key that opens the seat's
    /// window and the on-screen help. The seat keys themselves belong to each seat (see <see cref="SeatConfig"/>).
    /// </summary>
    internal static class Bindings
    {
        public static MelonPreferences_Entry<string> MenuKey;
        public static MelonPreferences_Entry<bool> ShowHud;

        private static MelonPreferences_Category _cat;

        public static void Init()
        {
            _cat = MelonPreferences.CreateCategory("KeySeat", "Command seat");
            MenuKey = _cat.CreateEntry("MenuKey", "<Keyboard>/f7", "Settings key", "Opens the seat's key settings while seated in a command seat.");
            ShowHud = _cat.CreateEntry("ShowHud", true, "Show HUD", "Shows the seat's keys while seated.");
        }

        public static void Save() => _cat.SaveToFile(false);

        // ---------------------------------------------------------------- reading controls

        private static readonly Dictionary<string, InputControl> Controls = new Dictionary<string, InputControl>();

        public static ButtonControl Find(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            if (!Controls.TryGetValue(path, out InputControl c) || c == null || c.device == null || !c.device.added)
            {
                c = InputSystem.FindControl(path);
                Controls[path] = c;
            }
            return c as ButtonControl;
        }

        public static bool IsPressed(string path) => Find(path)?.isPressed ?? false;

        public static bool WasPressed(string path) => Find(path)?.wasPressedThisFrame ?? false;

        public static string Display(string path)
        {
            if (string.IsNullOrEmpty(path)) return Loc.T("— aucune —", "— none —");
            try
            {
                // keyboard keys are stored by position (US layout names); show what is printed on the player's key (AZERTY...)
                string s = path.StartsWith("<Keyboard>") && Find(path) is ButtonControl key && !string.IsNullOrEmpty(key.displayName)
                    ? key.displayName
                    : InputControlPath.ToHumanReadableString(path, InputControlPath.HumanReadableStringOptions.OmitDevice);
                if (path.StartsWith("<Mouse>")) s = Loc.T("Souris ", "Mouse ") + s;
                else if (path.StartsWith("<Gamepad>")) s = Loc.T("Manette ", "Pad ") + s;
                return s.Length == 1 ? s.ToUpperInvariant() : s;
            }
            catch
            {
                return path;
            }
        }

        public static string FormatValue(float v) => v.ToString("0.###", CultureInfo.InvariantCulture);

        public static bool TryParseValue(string s, out float v) =>
            float.TryParse(s.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out v) && !float.IsNaN(v) && !float.IsInfinity(v);

        /// <summary>The first button pressed this frame on keyboard, mouse or gamepad, as a control path.</summary>
        public static string CapturePressed(bool allowMouse)
        {
            Keyboard kb = Keyboard.current;
            if (kb != null)
                foreach (KeyControl k in kb.allKeys)
                    if (k != null && k.wasPressedThisFrame && k.keyCode != Key.Escape)
                        return "<Keyboard>/" + k.name;
            Mouse m = Mouse.current;
            if (allowMouse && m != null)
            {
                foreach (ButtonControl b in new[] { m.leftButton, m.rightButton, m.middleButton, m.forwardButton, m.backButton })
                    if (b != null && b.wasPressedThisFrame)
                        return "<Mouse>/" + b.name;
            }
            Gamepad g = Gamepad.current;
            if (g != null)
                foreach (InputControl c in g.allControls)
                    if (c is ButtonControl b && !b.synthetic && b.wasPressedThisFrame)
                        return "<Gamepad>/" + b.path.Substring(g.path.Length + 1);
            return null;
        }

        public static bool AnyMouseButtonHeld()
        {
            Mouse m = Mouse.current;
            return m != null && (m.leftButton.isPressed || m.rightButton.isPressed || m.middleButton.isPressed);
        }

        /// <summary>Names of the game's piloting actions that already use this control, to warn about clashes.</summary>
        public static string GameActionsUsing(string path)
        {
            ButtonControl c = Find(path);
            if (c == null) return null;
            var names = new List<string>();
            foreach (var input in Object.FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
            {
                if (input == null || input.actions == null) continue;
                foreach (string mapName in new[] { "Piloting", "Common" })
                {
                    InputActionMap map = input.actions.FindActionMap(mapName);
                    if (map == null) continue;
                    foreach (InputAction a in map.actions)
                        foreach (InputBinding b in a.bindings)
                            if (!b.isComposite && !string.IsNullOrEmpty(b.effectivePath) && InputControlPath.Matches(b.effectivePath, c) && !names.Contains(a.name))
                                names.Add(a.name);
                }
            }
            return names.Count > 0 ? string.Join(", ", names) : null;
        }
    }
}

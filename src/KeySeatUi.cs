using System.Collections.Generic;
using KeySeat.Visual;
using Ships.Interface.Model.Parts;
using UI.Common;
using UI.Interface.Model;
using UnityEngine;
using UnityEngine.InputSystem;

namespace KeySeat
{
    /// <summary>
    /// While seated in a command seat: a small HUD listing the seat's keys, and the settings window (F7 by default)
    /// where the seat's keys are added, removed and set up. Changes are sent to the host when the window closes.
    /// </summary>
    internal sealed class KeySeatUi : MonoBehaviour, IClosableMenu
    {
        private const int CaptureMenuKey = -2, NotCapturing = -1;

        private static KeySeatUi _instance;

        private bool _open, _dirty;
        private SeatConfig _edit;
        private int _capturing = NotCapturing;
        private bool _captureArmed;
        private readonly List<string> _valueText = new List<string>();
        private readonly List<string> _clash = new List<string>();
        private readonly List<InputActionMap> _disabledMaps = new List<InputActionMap>();
        private Vector2 _scroll;

        private GUIStyle _window, _title, _label, _small, _button, _field, _hud, _hudOn;
        private Texture2D _white;

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("KeySeat.Ui");
            _instance = go.AddComponent<KeySeatUi>();
        }

        public static void Reset() => _instance = null;

        private void Update()
        {
            if (_open)
            {
                if (!SeatInput.Seated || !SeatInput.Seat.Equals(_editSeat))
                {
                    Close();
                    return;
                }
                if (_capturing != NotCapturing)
                {
                    UpdateCapture();
                    return;
                }
                Keyboard kb = Keyboard.current;
                if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (Bindings.WasPressed(Bindings.MenuKey.Value) && GUIUtility.keyboardControl == 0))
                    Close();
                return;
            }
            if (SeatInput.Seated && Bindings.WasPressed(Bindings.MenuKey.Value) && !SeatInput.AnyMenuOpen())
                Open();
        }

        private void UpdateCapture()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                _capturing = NotCapturing;
                return;
            }
            // the click on the button that started the capture must not be captured itself
            if (!_captureArmed)
            {
                _captureArmed = !Bindings.AnyMouseButtonHeld();
                return;
            }
            string path = Bindings.CapturePressed(allowMouse: _capturing != CaptureMenuKey);
            if (path == null) return;
            if (_capturing == CaptureMenuKey)
            {
                Bindings.MenuKey.Value = path;
                Bindings.Save();
            }
            else if (_capturing < _edit.Keys.Count)
            {
                _edit.Keys[_capturing].Path = path;
                _clash[_capturing] = Bindings.GameActionsUsing(path);
                _dirty = true;
            }
            _capturing = NotCapturing;
        }

        private PartContext _editSeat;

        private void Open()
        {
            _open = true;
            _dirty = false;
            _capturing = NotCapturing;
            _scroll = Vector2.zero;
            _editSeat = SeatInput.Seat;
            _edit = SeatConfigs.Get(SeatInput.Seat).Clone();
            RefreshRows();
            Svc.Get<MenuTracker>()?.TrackMenu(this);
            // no steering with the mouse, and no game shortcut (inventory, chat...) while typing in the window
            _disabledMaps.Clear();
            foreach (var input in FindObjectsByType<PlayerInput>(FindObjectsSortMode.None))
                foreach (string name in new[] { "Piloting", "Common" })
                {
                    InputActionMap map = input.actions?.FindActionMap(name);
                    if (map == null || !map.enabled) continue;
                    map.Disable();
                    _disabledMaps.Add(map);
                }
        }

        private void RefreshRows()
        {
            _valueText.Clear();
            _clash.Clear();
            foreach (KeyBinding k in _edit.Keys)
            {
                _valueText.Add(Bindings.FormatValue(k.Value));
                _clash.Add(Bindings.GameActionsUsing(k.Path));
            }
        }

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _capturing = NotCapturing;
            GUIUtility.keyboardControl = 0;
            if (_dirty && SeatInput.Seated && SeatInput.Seat.Equals(_editSeat))
                SeatConfigs.Submit(_editSeat, _edit);
            Bindings.Save();
            Svc.Get<MenuTracker>()?.UntrackMenu(this);
            foreach (InputActionMap map in _disabledMaps)
                if (map != null && (map.name != "Piloting" || SeatInput.Seated))
                    map.Enable();
            _disabledMaps.Clear();
        }

        private void OnDestroy()
        {
            if (_open) Close();
        }

        // ---------------------------------------------------------------- drawing

        private void OnGUI()
        {
            if (!SeatInput.Seated) return;
            EnsureStyles();
            float scale = Mathf.Max(0.75f, Screen.height / 1080f);
            Matrix4x4 old = GUI.matrix;
            GUI.matrix = Matrix4x4.Scale(new Vector3(scale, scale, 1f));
            float w = Screen.width / scale, h = Screen.height / scale;
            if (_open) DrawWindow(w, h);
            else if (Bindings.ShowHud.Value) DrawHud(h);
            GUI.matrix = old;
        }

        private void DrawHud(float h)
        {
            List<KeyBinding> keys = SeatConfigs.Get(SeatInput.Seat).Keys;
            var lines = new List<int>();
            for (int i = 0; i < keys.Count; i++)
                if (!string.IsNullOrEmpty(keys[i].Path)) lines.Add(i);
            float lh = 22f, bw = 300f, bh = 34f + lines.Count * lh;
            var box = new Rect(16f, h - bh - 140f, bw, bh);
            Fill(box, new Color(0f, 0f, 0f, 0.45f));
            GUI.Label(new Rect(box.x + 10f, box.y + 6f, bw - 20f, 20f),
                Loc.T($"{Bindings.Display(Bindings.MenuKey.Value)} : touches du siège", $"{Bindings.Display(Bindings.MenuKey.Value)}: seat keys"), _small);
            float y = box.y + 28f;
            foreach (int i in lines)
            {
                KeyBinding k = keys[i];
                bool on = i < SeatInput.KeyOn.Count && SeatInput.KeyOn[i];
                Fill(new Rect(box.x + 10f, y + 5f, 12f, 12f), OutputColor(k.Output) * (on ? 1f : 0.55f));
                string text = $"[{Bindings.Display(k.Path)}]  {k.Label}  → {k.Output + 1}" + (k.Mode == KeyMode.Toggle ? Loc.T("  (bascule)", "  (toggle)") : "");
                GUI.Label(new Rect(box.x + 30f, y, bw - 40f, lh), text, on ? _hudOn : _hud);
                y += lh;
            }
        }

        private void DrawWindow(float sw, float sh)
        {
            const float rowH = 40f, w = 940f;
            int visibleRows = Mathf.Clamp(_edit.Keys.Count, 1, 12);
            float h = 250f + visibleRows * rowH;
            var r = new Rect((sw - w) / 2f, (sh - h) / 2f, w, h);
            GUI.Box(r, GUIContent.none, _window);
            float x = r.x + 24f, y = r.y + 18f;
            GUI.Label(new Rect(x, y, w - 48f, 30f), Loc.T("Siège de commande — touches du siège", "Command seat — seat keys"), _title);
            y += 34f;
            GUI.Label(new Rect(x, y, w - 48f, 36f), Loc.T(
                "Ces touches appartiennent à ce siège : elles sont sauvegardées avec le monde et valent pour tous ceux qui s'y assoient. Chaque touche envoie sa valeur sur sa sortie (prises 1 à 8 sur le côté) ; si plusieurs touches d'une sortie sont actives, leurs valeurs s'additionnent.",
                "These keys belong to this seat: they are saved with the world and apply to whoever sits in it. Each key sends its value on its output (sockets 1 to 8 on the side); when several keys of one output are on, their values add up."), _small);
            y += 44f;

            float cOut = x, cName = x + 118f, cKey = x + 340f, cClear = x + 516f, cMode = x + 556f, cVal = x + 690f, cDel = x + 800f;
            GUI.Label(new Rect(cOut, y, 110f, 20f), Loc.T("Sortie", "Output"), _small);
            GUI.Label(new Rect(cName, y, 200f, 20f), Loc.T("Nom", "Name"), _small);
            GUI.Label(new Rect(cKey, y, 170f, 20f), Loc.T("Touche", "Key"), _small);
            GUI.Label(new Rect(cMode, y, 120f, 20f), Loc.T("Mode", "Mode"), _small);
            GUI.Label(new Rect(cVal, y, 100f, 20f), Loc.T("Valeur", "Value"), _small);
            y += 22f;

            var view = new Rect(x - 4f, y, w - 40f, visibleRows * rowH);
            var content = new Rect(0f, 0f, view.width - 20f, _edit.Keys.Count * rowH);
            _scroll = GUI.BeginScrollView(view, _scroll, content);
            float ox = -(x - 4f);
            int remove = -1;
            if (_edit.Keys.Count == 0)
                GUI.Label(new Rect(0f, 8f, 600f, 24f), Loc.T("Aucune touche. Ajoutez-en une ci-dessous.", "No key. Add one below."), _label);
            for (int i = 0; i < _edit.Keys.Count; i++)
            {
                KeyBinding k = _edit.Keys[i];
                float ry = i * rowH;

                // output: left click = next, right click = previous
                var outRect = new Rect(cOut + ox, ry + 3f, 106f, 28f);
                if (GUI.Button(outRect, "     " + Loc.T("Sortie ", "Output ") + (k.Output + 1), _button) && _capturing == NotCapturing)
                {
                    int step = Event.current.button == 1 ? SeatConfig.Outputs - 1 : 1;
                    k.Output = (byte)((k.Output + step) % SeatConfig.Outputs);
                    _dirty = true;
                }
                Fill(new Rect(outRect.x + 8f, ry + 10f, 14f, 14f), OutputColor(k.Output));

                string name = GUI.TextField(new Rect(cName + ox, ry + 3f, 212f, 28f), k.Name ?? "", SeatConfig.MaxName, _field);
                if (name != k.Name)
                {
                    k.Name = name;
                    _dirty = true;
                }

                string keyText = _capturing == i ? Loc.T("Appuyez sur une touche…", "Press a key…") : Bindings.Display(k.Path);
                if (GUI.Button(new Rect(cKey + ox, ry + 3f, 170f, 28f), keyText, _button) && _capturing == NotCapturing)
                    StartCapture(i);
                if (GUI.Button(new Rect(cClear + ox, ry + 3f, 32f, 28f), "✕", _button) && _capturing == NotCapturing)
                {
                    k.Path = "";
                    _clash[i] = null;
                    _dirty = true;
                }

                string modeText = k.Mode == KeyMode.Toggle ? Loc.T("Basculer", "Toggle") : Loc.T("Maintenir", "Hold");
                if (GUI.Button(new Rect(cMode + ox, ry + 3f, 124f, 28f), modeText, _button) && _capturing == NotCapturing)
                {
                    k.Mode = k.Mode == KeyMode.Toggle ? KeyMode.Hold : KeyMode.Toggle;
                    _dirty = true;
                }

                _valueText[i] = GUI.TextField(new Rect(cVal + ox, ry + 3f, 84f, 28f), _valueText[i] ?? "", 10, _field);
                if (Bindings.TryParseValue(_valueText[i], out float v) && v != k.Value)
                {
                    k.Value = v;
                    _dirty = true;
                }

                if (GUI.Button(new Rect(cDel + ox, ry + 3f, 92f, 28f), Loc.T("Supprimer", "Remove"), _button) && _capturing == NotCapturing)
                    remove = i;
                if (_clash[i] != null)
                    GUI.Label(new Rect(cKey + ox, ry + 30f, 420f, 14f), Loc.T("aussi utilisée par le jeu : ", "also used by the game: ") + _clash[i], _small);
            }
            GUI.EndScrollView();
            if (remove >= 0)
            {
                _edit.Keys.RemoveAt(remove);
                _valueText.RemoveAt(remove);
                _clash.RemoveAt(remove);
                _dirty = true;
            }
            y += visibleRows * rowH + 10f;

            GUI.enabled = _edit.Keys.Count < SeatConfig.MaxKeys && _capturing == NotCapturing;
            if (GUI.Button(new Rect(x, y, 200f, 30f), Loc.T("+ Ajouter une touche", "+ Add a key"), _button))
            {
                // next output after the last key's, unbound until a key is picked
                byte output = _edit.Keys.Count > 0 ? (byte)((_edit.Keys[_edit.Keys.Count - 1].Output + 1) % SeatConfig.Outputs) : (byte)0;
                _edit.Keys.Add(new KeyBinding { Output = output });
                _valueText.Add(Bindings.FormatValue(1f));
                _clash.Add(null);
                _dirty = true;
                _scroll.y = float.MaxValue;
                StartCapture(_edit.Keys.Count - 1);
            }
            GUI.enabled = _capturing == NotCapturing;
            if (GUI.Button(new Rect(x + 210f, y, 200f, 30f), Loc.T("Touches par défaut", "Default keys"), _button))
            {
                _edit = SeatConfig.Default();
                RefreshRows();
                _dirty = true;
            }
            GUI.enabled = true;
            y += 44f;

            GUI.Label(new Rect(x, y + 4f, 220f, 24f), Loc.T("Ouvrir ce menu :", "Open this window:"), _label);
            string menuText = _capturing == CaptureMenuKey ? Loc.T("Appuyez sur une touche…", "Press a key…") : Bindings.Display(Bindings.MenuKey.Value);
            if (GUI.Button(new Rect(x + 160f, y + 3f, 150f, 28f), menuText, _button) && _capturing == NotCapturing)
                StartCapture(CaptureMenuKey);
            bool hud = GUI.Toggle(new Rect(x + 330f, y + 6f, 230f, 24f), Bindings.ShowHud.Value, Loc.T(" Afficher l'aide à l'écran", " Show on-screen help"));
            if (hud != Bindings.ShowHud.Value) Bindings.ShowHud.Value = hud;
            if (GUI.Button(new Rect(r.xMax - 24f - 150f, y + 3f, 150f, 28f), Loc.T("Enregistrer", "Save"), _button) && _capturing == NotCapturing)
                Close();
            GUI.Label(new Rect(x + 330f, y + 30f, 400f, 18f), Loc.T("(touche et aide à l'écran : réglages personnels)", "(window key and on-screen help: your own settings)"), _small);
        }

        private void StartCapture(int slot)
        {
            _capturing = slot;
            _captureArmed = false;
            GUIUtility.keyboardControl = 0;
        }

        private static Color OutputColor(int output)
        {
            switch (SeatPart.SlotShades[output])
            {
                case Shade.Red: return new Color(0.86f, 0.26f, 0.22f);
                case Shade.Orange: return new Color(0.96f, 0.56f, 0.16f);
                case Shade.Yellow: return new Color(0.96f, 0.85f, 0.22f);
                case Shade.Green: return new Color(0.32f, 0.76f, 0.32f);
                case Shade.Blue: return new Color(0.26f, 0.52f, 0.92f);
                case Shade.White: return new Color(0.95f, 0.95f, 0.95f);
                case Shade.Grey: return new Color(0.6f, 0.6f, 0.6f);
                default: return new Color(0.16f, 0.16f, 0.16f);
            }
        }

        private void Fill(Rect r, Color c)
        {
            Color old = GUI.color;
            GUI.color = c;
            GUI.DrawTexture(r, _white);
            GUI.color = old;
        }

        private void EnsureStyles()
        {
            if (_window != null) return;
            _white = Texture2D.whiteTexture;
            var bg = new Texture2D(1, 1);
            bg.SetPixel(0, 0, new Color(0.07f, 0.08f, 0.1f, 0.94f));
            bg.Apply();
            _window = new GUIStyle(GUI.skin.box) { normal = { background = bg } };
            _title = new GUIStyle(GUI.skin.label) { fontSize = 22, fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
            _label = new GUIStyle(GUI.skin.label) { fontSize = 16, normal = { textColor = new Color(0.9f, 0.9f, 0.9f) } };
            _small = new GUIStyle(GUI.skin.label) { fontSize = 13, wordWrap = true, normal = { textColor = new Color(0.7f, 0.72f, 0.76f) } };
            _button = new GUIStyle(GUI.skin.button) { fontSize = 15 };
            _field = new GUIStyle(GUI.skin.textField) { fontSize = 15, alignment = TextAnchor.MiddleLeft };
            _hud = new GUIStyle(GUI.skin.label) { fontSize = 14, normal = { textColor = new Color(0.82f, 0.84f, 0.88f) } };
            _hudOn = new GUIStyle(_hud) { fontStyle = FontStyle.Bold, normal = { textColor = Color.white } };
        }
    }
}

using System.Collections.Generic;
using System.Text;
using KeySeat.Visual;
using Ships.Interface.Model.Parts;
using TMPro;
using UI.Common;
using UI.Interface.Model;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

namespace KeySeat
{
    /// <summary>
    /// While seated in a command seat: the seat's keys listed like the game's control prompts, and the settings
    /// window (F7 by default), styled like the game's modals, where the seat's keys are added, removed and set up.
    /// Changes are sent to the host when the window closes.
    /// </summary>
    internal sealed class KeySeatUi : MonoBehaviour, IClosableMenu
    {
        private const int CaptureMenuKey = -2, NotCapturing = -1;

        // window layout, in the game's 3840x2160 reference units
        private const float Width = 2560f, Pad = 80f, RowH = 116f, CtrlH = 76f, ListY = 330f;
        private const int VisibleRows = 7;
        private const float CText = 38f;
        private static readonly float[] ColX = { 0f, 320f, 900f, 1330f, 1434f, 1774f, 2014f };
        private static readonly float[] ColW = { 300f, 560f, 410f, 84f, 320f, 220f, 280f };

        private static KeySeatUi _instance;

        private bool _open, _dirty, _swallowClick;
        private SeatConfig _edit;
        private PartContext _editSeat;
        private int _capturing = NotCapturing;
        private bool _captureArmed;
        private readonly List<InputActionMap> _disabledMaps = new List<InputActionMap>();

        private RectTransform _canvas, _window, _list, _hud;
        private ScrollRect _scroll;
        private GameButton _add, _defaults, _menuKey;
        private Toggle _hudToggle;
        private readonly List<Row> _rows = new List<Row>();

        private string _hudSignature;
        private float _hudCheckAt;
        private readonly List<(int key, Image fill, TextMeshProUGUI label)> _hudRows = new List<(int, Image, TextMeshProUGUI)>();

        private sealed class Row
        {
            public GameButton Output, Key, Clear, Mode, Remove;
            public Image Swatch;
            public TMP_InputField Name, Value;
            public TextMeshProUGUI Clash;
        }

        public static void Ensure()
        {
            if (_instance != null) return;
            var go = new GameObject("KeySeat.Ui");
            _instance = go.AddComponent<KeySeatUi>();
        }

        public static void Reset() => _instance = null;

        private bool Idle => _capturing == NotCapturing && !_swallowClick;

        // ---------------------------------------------------------------- frame

        private void Update()
        {
            if (_swallowClick && !Bindings.AnyMouseButtonHeld()) _swallowClick = false;
            if (!SeatInput.Seated)
            {
                if (_open) Close();
                if (_hud != null) _hud.gameObject.SetActive(false);
                return;
            }
            EnsureCanvas();

            if (_open)
            {
                if (!SeatInput.Seat.Equals(_editSeat))
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
                if ((kb != null && kb.escapeKey.wasPressedThisFrame) || (Bindings.WasPressed(Bindings.MenuKey.Value) && !Typing()))
                    Close();
            }
            else if (Bindings.WasPressed(Bindings.MenuKey.Value) && !SeatInput.AnyMenuOpen())
            {
                Open();
            }
            UpdateHud();
        }

        private static bool Typing()
        {
            GameObject selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
            return selected != null && selected.GetComponent<TMP_InputField>() is TMP_InputField f && f.isFocused;
        }

        private void UpdateCapture()
        {
            Keyboard kb = Keyboard.current;
            if (kb != null && kb.escapeKey.wasPressedThisFrame)
            {
                EndCapture();
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
            // a mouse button picked as a key must not also click the button under the cursor
            if (path.StartsWith("<Mouse>")) _swallowClick = true;
            if (_capturing == CaptureMenuKey)
            {
                Bindings.MenuKey.Value = path;
                Bindings.Save();
            }
            else if (_capturing < _edit.Keys.Count)
            {
                _edit.Keys[_capturing].Path = path;
                _dirty = true;
            }
            EndCapture();
        }

        private void StartCapture(int slot)
        {
            _capturing = slot;
            _captureArmed = false;
            EventSystem.current?.SetSelectedGameObject(null);
            RefreshAll();
        }

        private void EndCapture()
        {
            int slot = _capturing;
            _capturing = NotCapturing;
            if (slot >= 0 && slot < _rows.Count) RefreshRow(slot, checkClash: true);
            RefreshAll();
        }

        // ---------------------------------------------------------------- open / close

        private void Open()
        {
            _open = true;
            _dirty = false;
            _capturing = NotCapturing;
            _editSeat = SeatInput.Seat;
            _edit = SeatConfigs.Get(SeatInput.Seat).Clone();
            BuildWindow();
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

        public void Close()
        {
            if (!_open) return;
            _open = false;
            _capturing = NotCapturing;
            EventSystem.current?.SetSelectedGameObject(null);
            if (_window != null) Destroy(_window.gameObject);
            _window = null;
            _rows.Clear();
            if (_dirty && SeatInput.Seated && SeatInput.Seat.Equals(_editSeat))
                SeatConfigs.Submit(_editSeat, _edit);
            Bindings.Save();
            Svc.Get<MenuTracker>()?.UntrackMenu(this);
            foreach (InputActionMap map in _disabledMaps)
                if (map != null && (map.name != "Piloting" || SeatInput.Seated))
                    map.Enable();
            _disabledMaps.Clear();
        }

        private void Cancel()
        {
            _dirty = false;
            Close();
        }

        private void OnDestroy()
        {
            if (_open) Close();
            if (_canvas != null) Destroy(_canvas.gameObject);
        }

        // ---------------------------------------------------------------- building

        private void EnsureCanvas()
        {
            if (_canvas != null) return;
            GameStyle.Load();
            var go = new GameObject("KeySeat.Canvas", typeof(RectTransform));
            go.layer = 5;
            go.transform.SetParent(transform, false);
            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            var scaler = go.AddComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(3840f, 2160f);
            scaler.matchWidthOrHeight = 1f;
            go.AddComponent<GraphicRaycaster>();
            _canvas = (RectTransform)go.transform;
        }

        private void BuildWindow()
        {
            if (_window != null) Destroy(_window.gameObject);
            _rows.Clear();

            // dimmed screen behind, catching clicks
            _window = GameStyle.Fill(GameStyle.Node("KeySeat.Window", _canvas));
            GameStyle.Image(_window, "Shade", null, new Color(0f, 0f, 0f, 0.6f));

            float listH = VisibleRows * RowH;
            float bottomY = ListY + listH + 36f;
            float settingsY = bottomY + 84f + 70f;
            float height = settingsY + 84f + 56f + 60f;
            RectTransform panel = GameStyle.Node("Panel", _window);
            panel.anchorMin = panel.anchorMax = panel.pivot = new Vector2(0.5f, 0.5f);
            panel.sizeDelta = new Vector2(Width, height);
            GameStyle.Frame(panel, GameStyle.Panel, GameStyle.Blue);

            float inner = Width - 2 * Pad;
            GameStyle.Label(panel, Pad, 44f, inner, 100f, Loc.T("Touches du siège", "Seat keys"), GameStyle.Title, 72f, GameStyle.Blue, TextAlignmentOptions.Center);
            GameStyle.Para(panel, Pad, 156f, inner, 100f, Loc.T(
                "Ces touches appartiennent à ce siège : elles sont sauvegardées avec le monde et valent pour tous ceux qui s'y assoient. Chaque touche envoie sa valeur sur sa sortie (prises 1 à 8 sur le côté) ; si plusieurs touches d'une sortie sont actives, leurs valeurs s'additionnent.",
                "These keys belong to this seat: they are saved with the world and apply to whoever sits in it. Each key sends its value on its output (sockets 1 to 8 on the side); when several keys of one output are on, their values add up."),
                34f, GameStyle.Muted).alignment = TextAlignmentOptions.Top;

            string[] headers =
            {
                Loc.T("Sortie", "Output"), Loc.T("Nom", "Name"), Loc.T("Touche", "Key"), "", Loc.T("Mode", "Mode"), Loc.T("Valeur", "Value"), "",
            };
            for (int c = 0; c < headers.Length; c++)
                if (headers[c].Length > 0)
                    GameStyle.Label(panel, Pad + ColX[c], ListY - 60f, ColW[c], 50f, headers[c], GameStyle.Title, 36f, GameStyle.Blue);

            BuildList(panel, Pad, ListY, inner, listH);

            _add = GameStyle.Button(panel, Pad, bottomY, 520f, 84f, Loc.T("+ Ajouter une touche", "+ Add a key"), 40f, AddKey);
            _defaults = GameStyle.Button(panel, Pad + 550f, bottomY, 520f, 84f, Loc.T("Touches par défaut", "Default keys"), 40f, () =>
            {
                if (!Idle) return;
                _edit = SeatConfig.Default();
                _dirty = true;
                RebuildRows();
            });

            // separator, then the player's own settings and the closing buttons
            RectTransform line = GameStyle.Place(GameStyle.Node("Separator", panel), Pad, settingsY - 36f, inner, 3f);
            GameStyle.Image(line, "Line", null, new Color(GameStyle.Blue.r, GameStyle.Blue.g, GameStyle.Blue.b, 0.35f));

            GameStyle.Label(panel, Pad, settingsY, 360f, 84f, Loc.T("Ouvrir ce menu", "Open this window"), GameStyle.Title, 40f, GameStyle.Text);
            _menuKey = GameStyle.Button(panel, Pad + 370f, settingsY, 300f, 84f, "", 40f, () =>
            {
                if (Idle) StartCapture(CaptureMenuKey);
            });
            _hudToggle = GameStyle.Checkbox(panel, Pad + 730f, settingsY + 14f, 720f, Loc.T("Afficher l'aide à l'écran", "Show on-screen help"), 40f,
                Bindings.ShowHud.Value, v => Bindings.ShowHud.Value = v);
            GameStyle.Para(panel, Pad, settingsY + 96f, 1500f, 50f,
                Loc.T("Touche du menu et aide à l'écran : réglages personnels, pas ceux du siège.", "Window key and on-screen help: your own settings, not the seat's."),
                30f, GameStyle.Muted);

            GameStyle.Button(panel, Width - Pad - 900f, settingsY - 6f, 430f, 96f, Loc.T("Annuler", "Cancel"), 48f, () =>
            {
                if (Idle) Cancel();
            });
            GameStyle.Button(panel, Width - Pad - 440f, settingsY - 6f, 440f, 96f, Loc.T("Enregistrer", "Save"), 48f, () =>
            {
                if (Idle) Close();
            });

            RebuildRows();
        }

        private void BuildList(RectTransform panel, float x, float y, float w, float h)
        {
            RectTransform view = GameStyle.Place(GameStyle.Node("List", panel), x, y, w, h);
            RectTransform viewport = GameStyle.Fill(GameStyle.Node("Viewport", view));
            viewport.offsetMax = new Vector2(-40f, 0f);
            viewport.gameObject.AddComponent<RectMask2D>();
            var hit = viewport.gameObject.AddComponent<Image>();
            hit.color = Color.clear;

            _list = GameStyle.Node("Content", viewport);
            _list.anchorMin = new Vector2(0f, 1f);
            _list.anchorMax = new Vector2(1f, 1f);
            _list.pivot = new Vector2(0f, 1f);
            _list.anchoredPosition = Vector2.zero;

            RectTransform bar = GameStyle.Node("Scrollbar", view);
            bar.anchorMin = new Vector2(1f, 0f);
            bar.anchorMax = Vector2.one;
            bar.pivot = new Vector2(1f, 1f);
            bar.sizeDelta = new Vector2(16f, 0f);
            bar.anchoredPosition = Vector2.zero;
            GameStyle.Image(bar, "Track", GameStyle.UnitBg, GameStyle.PromptBack, 2f);
            RectTransform area = GameStyle.Fill(GameStyle.Node("Sliding Area", bar));
            RectTransform handle = GameStyle.Fill(GameStyle.Node("Handle", area));
            Image handleImage = GameStyle.Image(handle, "Fill", GameStyle.UnitBg, GameStyle.Blue, 2f);
            var scrollbar = bar.gameObject.AddComponent<Scrollbar>();
            scrollbar.handleRect = handle;
            scrollbar.targetGraphic = handleImage;
            scrollbar.direction = Scrollbar.Direction.BottomToTop;
            scrollbar.navigation = new Navigation { mode = Navigation.Mode.None };

            _scroll = view.gameObject.AddComponent<ScrollRect>();
            _scroll.viewport = viewport;
            _scroll.content = _list;
            _scroll.horizontal = false;
            _scroll.vertical = true;
            _scroll.movementType = ScrollRect.MovementType.Clamped;
            _scroll.scrollSensitivity = 60f;
            _scroll.verticalScrollbar = scrollbar;
            _scroll.verticalScrollbarVisibility = ScrollRect.ScrollbarVisibility.AutoHide;
        }

        private void RebuildRows()
        {
            for (int i = _list.childCount - 1; i >= 0; i--)
                Destroy(_list.GetChild(i).gameObject);
            _rows.Clear();
            _list.sizeDelta = new Vector2(0f, Mathf.Max(1, _edit.Keys.Count) * RowH);
            if (_edit.Keys.Count == 0)
                GameStyle.Label(_list, 0f, 20f, 1400f, 60f, Loc.T("Aucune touche. Ajoutez-en une ci-dessous.", "No key. Add one below."), GameStyle.Paragraph, 40f, GameStyle.Muted, TextAlignmentOptions.Left, FontStyles.Bold);
            for (int i = 0; i < _edit.Keys.Count; i++)
                _rows.Add(BuildRow(i));
            for (int i = 0; i < _rows.Count; i++)
                RefreshRow(i, checkClash: true);
            RefreshAll();
        }

        private Row BuildRow(int i)
        {
            KeyBinding k = _edit.Keys[i];
            RectTransform line = GameStyle.Place(GameStyle.Node("Row" + i, _list), 0f, i * RowH, ColX[6] + ColW[6], RowH);
            var row = new Row();
            float y = 6f;

            // output: left click = next, right click = previous
            row.Output = GameStyle.Button(line, ColX[0], y, ColW[0], CtrlH, "", CText, () => StepOutput(i, 1));
            row.Output.Label.margin = new Vector4(70f, 0f, 12f, 0f);
            row.Output.Rect.gameObject.AddComponent<RightClick>().OnRightClick = () => StepOutput(i, SeatConfig.Outputs - 1);
            RectTransform swatch = GameStyle.Place(GameStyle.Node("Swatch", row.Output.Rect), 22f, (CtrlH - 36f) / 2f, 36f, 36f);
            row.Swatch = GameStyle.Image(swatch, "Colour", GameStyle.UnitBg, Color.white, 2f);
            row.Swatch.raycastTarget = false;
            GameStyle.Image(swatch, "Border", GameStyle.UnitBorders, new Color(1f, 1f, 1f, 0.45f), 2f).raycastTarget = false;

            row.Name = GameStyle.Field(line, ColX[1], y, ColW[1], CtrlH, k.Name, SeatConfig.MaxName, CText, v =>
            {
                if (v == k.Name) return;
                k.Name = v;
                _dirty = true;
            });

            row.Key = GameStyle.Button(line, ColX[2], y, ColW[2], CtrlH, "", CText, () =>
            {
                if (Idle) StartCapture(i);
            });
            row.Clear = GameStyle.Button(line, ColX[3], y, ColW[3], CtrlH, "X", CText, () =>
            {
                if (!Idle) return;
                k.Path = "";
                _dirty = true;
                RefreshRow(i, checkClash: true);
            });
            row.Mode = GameStyle.Button(line, ColX[4], y, ColW[4], CtrlH, "", CText, () =>
            {
                if (!Idle) return;
                k.Mode = k.Mode == KeyMode.Toggle ? KeyMode.Hold : KeyMode.Toggle;
                _dirty = true;
                RefreshRow(i, checkClash: false);
            });
            row.Value = GameStyle.Field(line, ColX[5], y, ColW[5], CtrlH, Bindings.FormatValue(k.Value), 10, CText, v =>
            {
                if (Bindings.TryParseValue(v, out float f) && f != k.Value)
                {
                    k.Value = f;
                    _dirty = true;
                }
            }, GameStyle.Title); // the paragraph font has no minus sign
            row.Remove = GameStyle.Button(line, ColX[6], y, ColW[6], CtrlH, Loc.T("Supprimer", "Remove"), CText, () =>
            {
                if (!Idle) return;
                _edit.Keys.RemoveAt(i);
                _dirty = true;
                float pos = _scroll.verticalNormalizedPosition;
                RebuildRows();
                _scroll.verticalNormalizedPosition = pos;
            }, danger: true);

            row.Clash = GameStyle.Label(line, ColX[2], y + CtrlH + 2f, 1200f, 32f, "", GameStyle.Paragraph, 26f, GameStyle.RedText, TextAlignmentOptions.Left, FontStyles.Bold);
            return row;
        }

        private void StepOutput(int i, int step)
        {
            if (!Idle || i >= _edit.Keys.Count) return;
            KeyBinding k = _edit.Keys[i];
            k.Output = (byte)((k.Output + step) % SeatConfig.Outputs);
            _dirty = true;
            RefreshRow(i, checkClash: false);
        }

        private void AddKey()
        {
            if (!Idle || _edit.Keys.Count >= SeatConfig.MaxKeys) return;
            // next output after the last key's, unbound until a key is picked
            byte output = _edit.Keys.Count > 0 ? (byte)((_edit.Keys[_edit.Keys.Count - 1].Output + 1) % SeatConfig.Outputs) : (byte)0;
            _edit.Keys.Add(new KeyBinding { Output = output });
            _dirty = true;
            RebuildRows();
            Canvas.ForceUpdateCanvases();
            _scroll.verticalNormalizedPosition = 0f;
            StartCapture(_edit.Keys.Count - 1);
        }

        // ---------------------------------------------------------------- refreshing

        private void RefreshRow(int i, bool checkClash)
        {
            if (i >= _rows.Count || i >= _edit.Keys.Count) return;
            Row row = _rows[i];
            KeyBinding k = _edit.Keys[i];
            row.Output.Text = Loc.T("Sortie ", "Output ") + (k.Output + 1);
            row.Swatch.color = OutputColor(k.Output);
            row.Key.Text = _capturing == i ? Loc.T("Appuyez sur une touche…", "Press a key…") : Bindings.Display(k.Path);
            row.Key.Border.color = _capturing == i ? GameStyle.Blue : GameStyle.BlueBorder;
            row.Mode.Text = k.Mode == KeyMode.Toggle ? Loc.T("Basculer", "Toggle") : Loc.T("Maintenir", "Hold");
            if (checkClash)
            {
                string clash = Bindings.GameActionsUsing(k.Path);
                row.Clash.text = clash != null ? Loc.T("Aussi utilisée par le jeu : ", "Also used by the game: ") + clash : "";
            }
        }

        /// <summary>Texts and enabled state that depend on the capture in progress.</summary>
        private void RefreshAll()
        {
            if (_window == null) return;
            bool idle = _capturing == NotCapturing;
            for (int i = 0; i < _rows.Count; i++)
            {
                RefreshRow(i, checkClash: false);
                Row r = _rows[i];
                bool on = idle || _capturing == i;
                r.Output.Interactable = idle;
                r.Key.Interactable = on;
                r.Clear.Interactable = idle;
                r.Mode.Interactable = idle;
                r.Remove.Interactable = idle;
                r.Name.interactable = idle;
                r.Value.interactable = idle;
            }
            _add.Interactable = idle && _edit.Keys.Count < SeatConfig.MaxKeys;
            _defaults.Interactable = idle;
            _menuKey.Text = _capturing == CaptureMenuKey ? Loc.T("Appuyez…", "Press…") : Bindings.Display(Bindings.MenuKey.Value);
            _menuKey.Border.color = _capturing == CaptureMenuKey ? GameStyle.Blue : GameStyle.BlueBorder;
            _menuKey.Interactable = idle || _capturing == CaptureMenuKey;
            _hudToggle.interactable = idle;
        }

        // ---------------------------------------------------------------- HUD

        private const float HudW = 700f, HudRowH = 60f, HudGap = 10f, HudKey = 48f;

        private void UpdateHud()
        {
            bool show = !_open && Bindings.ShowHud.Value && !SeatInput.AnyMenuOpen();
            if (_hud != null) _hud.gameObject.SetActive(show);
            if (!show) return;

            List<KeyBinding> keys = SeatConfigs.Get(SeatInput.Seat).Keys;
            if (_hud == null || Time.unscaledTime >= _hudCheckAt)
            {
                _hudCheckAt = Time.unscaledTime + 0.25f;
                string sig = HudSignature(keys);
                if (_hud == null || sig != _hudSignature)
                {
                    _hudSignature = sig;
                    BuildHud(keys);
                }
            }
            foreach (var (key, fill, label) in _hudRows)
            {
                bool on = key < SeatInput.KeyOn.Count && SeatInput.KeyOn[key];
                fill.enabled = on;
                label.color = on ? Color.white : new Color(0.86f, 0.9f, 0.93f);
            }
        }

        private static string HudSignature(List<KeyBinding> keys)
        {
            var sb = new StringBuilder(Loc.French ? "fr|" : "en|").Append(Bindings.Display(Bindings.MenuKey.Value));
            foreach (KeyBinding k in keys)
                sb.Append('|').Append(k.Path).Append(';').Append(k.Label).Append(';').Append(k.Output).Append(';').Append((int)k.Mode);
            return sb.ToString();
        }

        private void BuildHud(List<KeyBinding> keys)
        {
            if (_hud != null) Destroy(_hud.gameObject);
            _hudRows.Clear();
            var shown = new List<int>();
            for (int i = 0; i < keys.Count; i++)
                if (!string.IsNullOrEmpty(keys[i].Path)) shown.Add(i);

            float height = 70f + shown.Count * (HudRowH + HudGap);
            _hud = GameStyle.Node("KeySeat.Hud", _canvas);
            _hud.anchorMin = _hud.anchorMax = _hud.pivot = Vector2.zero;
            _hud.anchoredPosition = new Vector2(60f, 300f);
            _hud.sizeDelta = new Vector2(HudW, height);

            // title line: [F7] Seat keys
            string menuKey = Bindings.Display(Bindings.MenuKey.Value);
            GameStyle.Key(_hud, 0f, 0f, 56f, menuKey, out RectTransform icon);
            GameStyle.Label(_hud, icon.sizeDelta.x + 18f, 0f, HudW, 56f, Loc.T("Touches du siège", "Seat keys"), GameStyle.Title, 40f, new Color32(0xF5, 0xF5, 0xF5, 0xFF));

            float y = 70f;
            foreach (int i in shown)
            {
                KeyBinding k = keys[i];
                RectTransform row = GameStyle.Place(GameStyle.Node("Key" + i, _hud), 0f, y, HudW, HudRowH);
                Image back = GameStyle.Image(row, "Back", GameStyle.UnitBg, new Color(GameStyle.PromptBack.r, GameStyle.PromptBack.g, GameStyle.PromptBack.b, 0.85f));
                back.raycastTarget = false;
                Image fill = GameStyle.Image(row, "Fill", GameStyle.UnitBg, GameStyle.PromptFill);
                fill.raycastTarget = false;
                fill.enabled = false;

                GameStyle.Key(row, 6f, (HudRowH - HudKey) / 2f, HudKey, Bindings.Display(k.Path), out RectTransform cap);
                float textX = 6f + Mathf.Max(HudKey, cap.sizeDelta.x) + 16f;
                string text = k.Label + (k.Mode == KeyMode.Toggle ? Loc.T("  · bascule", "  · toggle") : "");
                TextMeshProUGUI label = GameStyle.Label(row, textX, 0f, HudW - textX - 110f, HudRowH, text, GameStyle.Paragraph, 34f, Color.white, TextAlignmentOptions.Left, FontStyles.Bold);

                // output: its colour and number, as on the seat's console
                RectTransform sw = GameStyle.Place(GameStyle.Node("Swatch", row), HudW - 96f, (HudRowH - 28f) / 2f, 28f, 28f);
                GameStyle.Image(sw, "Colour", GameStyle.UnitBg, OutputColor(k.Output), 2f).raycastTarget = false;
                GameStyle.Image(sw, "Border", GameStyle.UnitBorders, new Color(1f, 1f, 1f, 0.45f), 2f).raycastTarget = false;
                GameStyle.Label(row, HudW - 58f, 0f, 50f, HudRowH, (k.Output + 1).ToString(), GameStyle.Title, 36f, Color.white, TextAlignmentOptions.Center);

                _hudRows.Add((i, fill, label));
                y += HudRowH + HudGap;
            }
        }

        // ---------------------------------------------------------------- colours

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
    }
}

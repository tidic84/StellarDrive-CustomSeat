using System;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace KeySeat
{
    /// <summary>
    /// The game's own UI look (fonts, sprites, colours, taken from its menus and modals), and small factories that
    /// build uGUI controls with it. Positions are in the game's 3840x2160 reference units, from the top-left corner.
    /// </summary>
    internal static class GameStyle
    {
        public static readonly Color Panel = new Color32(0x0D, 0x24, 0x32, 0xFF);
        public static readonly Color Blue = new Color32(0x32, 0x87, 0xBE, 0xFF);
        public static readonly Color BlueBorder = new Color32(0x23, 0x5E, 0x84, 0xFF);
        public static readonly Color FieldBack = new Color32(0x12, 0x31, 0x45, 0xFF);
        public static readonly Color CheckBlue = new Color32(0x2A, 0x6F, 0x9D, 0xFF);
        public static readonly Color Red = new Color32(0x9A, 0x4D, 0x4C, 0xFF);
        public static readonly Color RedText = new Color32(0xD7, 0x6C, 0x6A, 0xFF);
        public static readonly Color Text = new Color32(0xF2, 0xF2, 0xF2, 0xFF);
        public static readonly Color Muted = new Color32(0xB9, 0xC9, 0xD3, 0xFF);
        public static readonly Color KeyText = new Color32(0x09, 0x1B, 0x26, 0xFF);
        public static readonly Color PromptBack = new Color32(0x0D, 0x2A, 0x3E, 0xFF);
        public static readonly Color PromptFill = new Color32(0x32, 0x87, 0xBE, 0x87);

        public static TMP_FontAsset Title { get; private set; }
        public static TMP_FontAsset Paragraph { get; private set; }
        public static Sprite UnitBg { get; private set; }
        public static Sprite UnitBorders { get; private set; }
        public static Sprite Check { get; private set; }
        public static Sprite Keycap { get; private set; }

        /// <summary>Finds the game's UI assets (loaded with the game's HUD and menus).</summary>
        public static void Load()
        {
            if (Title != null && UnitBg != null) return;
            TMP_FontAsset[] fonts = Resources.FindObjectsOfTypeAll<TMP_FontAsset>();
            Title = fonts.FirstOrDefault(f => f.name.StartsWith("TitlesTabsLabels_Anta")) ?? TMP_Settings.defaultFontAsset;
            Paragraph = fonts.FirstOrDefault(f => f.name.StartsWith("Paragraphs_Mulish")) ?? Title;
            Sprite[] sprites = Resources.FindObjectsOfTypeAll<Sprite>();
            UnitBg = sprites.FirstOrDefault(s => s.name == "unit_bg");
            UnitBorders = sprites.FirstOrDefault(s => s.name == "unit_borders");
            Check = sprites.FirstOrDefault(s => s.name == "icon_check");
            // the key icon of the game's control prompts, made stretchable for long key names
            Texture2D key = Resources.FindObjectsOfTypeAll<Texture2D>().FirstOrDefault(t => t.name == "Blank-Key-full@4x");
            if (key != null)
            {
                float b = key.width * 0.3f;
                Keycap = Sprite.Create(key, new Rect(0, 0, key.width, key.height), new Vector2(0.5f, 0.5f), 100f, 0, SpriteMeshType.FullRect, new Vector4(b, b, b, b));
                Keycap.name = "KeySeat.Keycap";
            }
        }

        // ---------------------------------------------------------------- layout

        public static RectTransform Node(string name, Transform parent)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.layer = 5; // UI
            var rt = (RectTransform)go.transform;
            rt.SetParent(parent, false);
            return rt;
        }

        /// <summary>A rectangle placed from its parent's top-left corner.</summary>
        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static RectTransform Fill(RectTransform rt, float inset = 0f)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.offsetMin = new Vector2(inset, inset);
            rt.offsetMax = new Vector2(-inset, -inset);
            return rt;
        }

        public static Image Image(Transform parent, string name, Sprite sprite, Color color, float ppu = 1f)
        {
            Image img = Fill(Node(name, parent)).gameObject.AddComponent<Image>();
            img.sprite = sprite;
            img.type = sprite != null ? UnityEngine.UI.Image.Type.Sliced : UnityEngine.UI.Image.Type.Simple;
            img.pixelsPerUnitMultiplier = ppu;
            img.color = color;
            return img;
        }

        /// <summary>Background and border of the game's panels and modals.</summary>
        public static void Frame(RectTransform rt, Color back, Color border, float ppu = 1f)
        {
            Image bg = Image(rt, "Background", UnitBg, back, ppu);
            bg.raycastTarget = true;
            Image(rt, "Border", UnitBorders, border, ppu).raycastTarget = false;
        }

        public static TextMeshProUGUI Label(Transform parent, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            var t = Fill(Node("Text", parent)).gameObject.AddComponent<TextMeshProUGUI>();
            t.font = font;
            t.fontSize = size;
            t.enableAutoSizing = true;
            t.fontSizeMin = size * 0.5f;
            t.fontSizeMax = size;
            t.color = color;
            t.alignment = align;
            t.fontStyle = style;
            t.textWrappingMode = TextWrappingModes.NoWrap;
            t.overflowMode = TextOverflowModes.Ellipsis;
            t.raycastTarget = false;
            t.text = text;
            return t;
        }

        public static TextMeshProUGUI Label(RectTransform parent, float x, float y, float w, float h, string text, TMP_FontAsset font, float size, Color color,
            TextAlignmentOptions align = TextAlignmentOptions.Left, FontStyles style = FontStyles.Normal)
        {
            RectTransform rt = Place(Node("Label", parent), x, y, w, h);
            return Label(rt, text, font, size, color, align, style);
        }

        /// <summary>Wrapping paragraph text.</summary>
        public static TextMeshProUGUI Para(RectTransform parent, float x, float y, float w, float h, string text, float size, Color color)
        {
            TextMeshProUGUI t = Label(parent, x, y, w, h, text, GameStyle.Paragraph, size, color, TextAlignmentOptions.TopLeft, FontStyles.Bold);
            t.textWrappingMode = TextWrappingModes.Normal;
            t.overflowMode = TextOverflowModes.Overflow;
            t.enableAutoSizing = false;
            return t;
        }

        // ---------------------------------------------------------------- controls

        private static ColorBlock Tint(ColorBlock c, float normal, float highlighted)
        {
            c.normalColor = new Color(1f, 1f, 1f, normal);
            c.highlightedColor = new Color(1f, 1f, 1f, highlighted);
            c.selectedColor = c.normalColor;
            c.pressedColor = new Color32(0xC8, 0xC8, 0xC8, 0xFF);
            c.disabledColor = new Color32(0xB2, 0xB2, 0xB2, 0x33);
            c.colorMultiplier = 1f;
            c.fadeDuration = 0.1f;
            return c;
        }

        /// <summary>The game's action button (blue, or red for destructive actions).</summary>
        public static GameButton Button(RectTransform parent, float x, float y, float w, float h, string text, float size, UnityAction onClick, bool danger = false)
        {
            RectTransform rt = Place(Node("Button", parent), x, y, w, h);
            Image bg = Image(rt, "Background", UnitBg, danger ? Red : Blue, 2f);
            Image border = Image(rt, "Border", UnitBorders, danger ? Red : BlueBorder, 2f);
            border.raycastTarget = false;
            var label = Label(rt, text, Title, size, Text, TextAlignmentOptions.Center);
            label.margin = new Vector4(12f, 0f, 12f, 0f);
            var b = rt.gameObject.AddComponent<Button>();
            b.targetGraphic = bg;
            b.colors = Tint(b.colors, 0.2f, 0.5f);
            b.navigation = new Navigation { mode = Navigation.Mode.None };
            if (onClick != null) b.onClick.AddListener(onClick);
            return new GameButton(b, label, bg, border);
        }

        /// <summary>The game's text field (dark blue, blue border, bold Mulish text).</summary>
        public static TMP_InputField Field(RectTransform parent, float x, float y, float w, float h, string text, int limit, float size, UnityAction<string> onChange, TMP_FontAsset font = null)
        {
            RectTransform rt = Place(Node("Field", parent), x, y, w, h);
            Image bg = Image(rt, "Background", UnitBg, FieldBack, 2f);
            Image(rt, "Border", UnitBorders, Blue, 2f).raycastTarget = false;
            RectTransform area = Node("Text Area", rt);
            Fill(area);
            area.offsetMin = new Vector2(18f, 6f);
            area.offsetMax = new Vector2(-18f, -6f);
            area.gameObject.AddComponent<RectMask2D>();
            font = font ?? Paragraph;
            TextMeshProUGUI t = Label(area, "", font, size, Text, TextAlignmentOptions.Left, font == Paragraph ? FontStyles.Bold : FontStyles.Normal);
            t.enableAutoSizing = false;
            t.overflowMode = TextOverflowModes.Overflow;
            var field = rt.gameObject.AddComponent<TMP_InputField>();
            field.targetGraphic = bg;
            field.colors = Tint(field.colors, 1f, 1f);
            field.navigation = new Navigation { mode = Navigation.Mode.None };
            field.textViewport = area;
            field.textComponent = t;
            field.fontAsset = font;
            field.pointSize = size;
            field.characterLimit = limit;
            field.caretColor = Text;
            field.customCaretColor = true;
            field.caretWidth = 3;
            field.selectionColor = new Color(Blue.r, Blue.g, Blue.b, 0.6f);
            field.restoreOriginalTextOnEscape = false;
            field.text = text ?? "";
            if (onChange != null) field.onValueChanged.AddListener(onChange);
            return field;
        }

        /// <summary>The game's check box followed by its label.</summary>
        public static Toggle Checkbox(RectTransform parent, float x, float y, float w, string text, float size, bool value, UnityAction<bool> onChange)
        {
            RectTransform rt = Place(Node("Checkbox", parent), x, y, w, 56f);
            RectTransform box = Place(Node("Box", rt), 0f, 8f, 40f, 40f);
            Image bg = Image(box, "Background", UnitBg, CheckBlue, 2f);
            Image(box, "Contour", UnitBorders, CheckBlue, 2f).raycastTarget = false;
            RectTransform mark = Node("Checkmark", box);
            mark.anchorMin = mark.anchorMax = new Vector2(0.5f, 0.5f);
            mark.anchoredPosition = new Vector2(6f, 4f);
            mark.sizeDelta = new Vector2(46f, 40f);
            var check = mark.gameObject.AddComponent<Image>();
            check.sprite = Check;
            check.raycastTarget = false;
            Label(rt, 64f, 0f, w - 64f, 56f, text, Paragraph, size, Text, TextAlignmentOptions.Left, FontStyles.Bold);
            // the label is clickable too
            var hit = rt.gameObject.AddComponent<Image>();
            hit.color = Color.clear;
            var toggle = rt.gameObject.AddComponent<Toggle>();
            toggle.targetGraphic = bg;
            toggle.graphic = check;
            toggle.colors = Tint(toggle.colors, 0.2f, 1f);
            toggle.navigation = new Navigation { mode = Navigation.Mode.None };
            toggle.isOn = value;
            if (onChange != null) toggle.onValueChanged.AddListener(onChange);
            return toggle;
        }

        /// <summary>The key icon of the game's control prompts; wider for long key names.</summary>
        public static TextMeshProUGUI Key(RectTransform parent, float x, float y, float size, string text, out RectTransform icon)
        {
            icon = Place(Node("KeyIcon", parent), x, y, KeyWidth(text, size), size);
            if (Keycap != null) Image(icon, "Cap", Keycap, Text, Keycap.rect.width / size).raycastTarget = false; // corners as on a square icon of this size
            else Frame(icon, Text, Muted, 2f);
            TextMeshProUGUI t = Label(icon, text, Title, size * 0.66f, KeyText, TextAlignmentOptions.Center, FontStyles.Bold);
            t.margin = new Vector4(size * 0.12f, 0f, size * 0.12f, size * 0.06f);
            t.fontSizeMin = size * 0.25f;
            return t;
        }

        public static float KeyWidth(string text, float size) =>
            string.IsNullOrEmpty(text) || text.Length <= 2 ? size : Mathf.Min(size * 4f, size * 0.5f + text.Length * size * 0.33f);
    }

    internal readonly struct GameButton
    {
        public readonly Button Button;
        public readonly TextMeshProUGUI Label;
        public readonly Image Background, Border;

        public GameButton(Button button, TextMeshProUGUI label, Image background, Image border)
        {
            Button = button;
            Label = label;
            Background = background;
            Border = border;
        }

        public RectTransform Rect => (RectTransform)Button.transform;

        public string Text
        {
            set => Label.text = value;
        }

        /// <summary>Enabled or not, dimmed like the game's ActionButtonStylization.</summary>
        public bool Interactable
        {
            set
            {
                Button.interactable = value;
                float a = value ? 1f : 0.3f;
                Color b = Border.color;
                Border.color = new Color(b.r, b.g, b.b, a);
                Color t = Label.color;
                Label.color = new Color(t.r, t.g, t.b, a);
            }
        }
    }

    /// <summary>Right click on a button (cycling backwards).</summary>
    internal sealed class RightClick : MonoBehaviour, IPointerClickHandler
    {
        public Action OnRightClick;

        public void OnPointerClick(PointerEventData e)
        {
            if (e.button == PointerEventData.InputButton.Right) OnRightClick?.Invoke();
        }
    }
}

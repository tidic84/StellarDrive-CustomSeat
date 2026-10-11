#if DEV
using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Text;
using MelonLoader;
using TMPro;
using UI.Common;
using UI.MainMenu;
using UI.MainMenu.Common;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace KeySeat
{
    // Dev-only: dumps the game's menu hierarchies and screenshots them, to rebuild our UI from the same pieces.
    internal static class UiDump
    {
        private static readonly string DumpDir = Path.Combine(Path.GetTempPath(), "ks_uidump");

        public static void DumpScene(string name)
        {
            Directory.CreateDirectory(DumpDir);
            var sb = new StringBuilder();
            foreach (var canvas in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None).Where(c => c.isRootCanvas))
                DumpNode(canvas.transform, 0, sb);
            File.WriteAllText(Path.Combine(DumpDir, name + ".txt"), sb.ToString());
        }

        private static void DumpNode(Transform t, int depth, StringBuilder sb)
        {
            if (!t.gameObject.activeInHierarchy && depth > 0)
            {
                sb.Append(' ', depth * 2).Append("- ").Append(t.name).AppendLine(" (inactive)");
                return;
            }
            sb.Append(' ', depth * 2).Append("- ").Append(t.name);
            if (t is RectTransform rt)
                sb.Append($" rect={rt.rect.width:0}x{rt.rect.height:0} amin={rt.anchorMin} amax={rt.anchorMax} piv={rt.pivot} pos={rt.anchoredPosition} sd={rt.sizeDelta}");
            sb.AppendLine();
            foreach (var c in t.GetComponents<Component>())
            {
                if (c == null || c is Transform) continue;
                sb.Append(' ', depth * 2 + 4).Append(c.GetType().Name);
                switch (c)
                {
                    case Canvas cv: sb.Append($" mode={cv.renderMode} sort={cv.sortingOrder} scale={cv.scaleFactor}"); break;
                    case CanvasScaler cs: sb.Append($" {cs.uiScaleMode} ref={cs.referenceResolution} match={cs.matchWidthOrHeight}"); break;
                    case TMP_Text tx: sb.Append($" '{Trunc(tx.text)}' font={tx.font?.name} size={tx.fontSize} auto={tx.enableAutoSizing} color={Col(tx.color)} style={tx.fontStyle} align={tx.alignment} spacing={tx.characterSpacing}"); break;
                    case Image im: sb.Append($" sprite={im.sprite?.name} color={Col(im.color)} type={im.type} ppu={im.pixelsPerUnitMultiplier} mat={im.material?.name}"); break;
                    case RawImage ri: sb.Append($" tex={ri.texture?.name} color={Col(ri.color)}"); break;
                    case LayoutGroup lg: sb.Append($" pad={lg.padding} align={lg.childAlignment}" + (lg is HorizontalOrVerticalLayoutGroup h ? $" spacing={h.spacing} ctrlW={h.childControlWidth} ctrlH={h.childControlHeight} expW={h.childForceExpandWidth} expH={h.childForceExpandHeight}" : "")); break;
                    case LayoutElement le: sb.Append($" min={le.minWidth}x{le.minHeight} pref={le.preferredWidth}x{le.preferredHeight} flex={le.flexibleWidth}x{le.flexibleHeight}"); break;
                    case ContentSizeFitter f: sb.Append($" h={f.horizontalFit} v={f.verticalFit}"); break;
                    case Selectable s: sb.Append($" transition={s.transition} normal={Col(s.colors.normalColor)} hl={Col(s.colors.highlightedColor)} pressed={Col(s.colors.pressedColor)} disabled={Col(s.colors.disabledColor)}"); break;
                    case ScrollRect sr: sb.Append($" h={sr.horizontal} v={sr.vertical} sens={sr.scrollSensitivity}"); break;
                }
                if (c is Button b)
                {
                    for (int i = 0; i < b.onClick.GetPersistentEventCount(); i++)
                        sb.Append($" onClick={b.onClick.GetPersistentTarget(i)?.GetType().Name}.{b.onClick.GetPersistentMethodName(i)}");
                }
                sb.AppendLine();
            }
            for (int i = 0; i < t.childCount; i++)
                DumpNode(t.GetChild(i), depth + 1, sb);
        }

        private static string Trunc(string s) => s == null ? "" : (s.Length > 40 ? s.Substring(0, 40) : s).Replace("\n", "\\n");
        private static string Col(Color c) => $"#{ColorUtility.ToHtmlStringRGBA(c)}";
    }
}
#endif

using System;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using Items.Model;
using KeySeat.Visual;
using Research.Interface.Values;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using Ships.Services;
using UI.BuildMenu;
using UnityEngine;
using UnityEngine.Localization;
using Object = UnityEngine.Object;

namespace KeySeat
{
    /// <summary>
    /// The command seat: a copy of the advanced pilot seat (so flying, cables, saving and network sync all come
    /// from the game) with a keypad console and 8 more signal outputs, sockets 6 to 13.
    /// </summary>
    internal static class SeatPart
    {
        public const ushort SeatId = 9301;
        public const ushort NativeAdvancedSeatId = 29;
        public const byte FirstSocket = 6; // socket ids must follow the seat's own 0-5: the game indexes an array of socket count by id

        // Same palette order on the keypad, the socket tags and the settings window.
        public static readonly Shade[] SlotShades = { Shade.Red, Shade.Orange, Shade.Yellow, Shade.Green, Shade.Blue, Shade.White, Shade.Grey, Shade.Black };

        private static PartSettings _part;
        private static GameObject _templateRoot;
        private static bool _building;

        public static PartSettings Part => _part;

        public static bool IsSeatSocket(byte socket) => socket >= FirstSocket && socket < FirstSocket + SeatConfig.Outputs;

        public static void Patch(HarmonyLib.Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(PartSettingsProvider), "EnsureDictIsCached"),
                postfix: new HarmonyMethod(typeof(SeatPart), nameof(EnsureDictPostfix)));
            h.Patch(AccessTools.Method(typeof(BuildMenu), "Start"),
                prefix: new HarmonyMethod(typeof(SeatPart), nameof(BuildMenuStartPrefix)));
        }

        /// <summary>Parts are looked up through cached dictionaries; make sure ours is in every one.</summary>
        private static void EnsureDictPostfix(PartSettingsProvider __instance)
        {
            if (_building) return;
            var dict = Traverse.Create(__instance).Field("_partsDict").GetValue<Dictionary<ushort, PartSettings>>();
            if (dict == null) return;
            EnsurePart();
            if (_part != null && !dict.ContainsKey(SeatId))
                dict[SeatId] = _part;
        }

        public static void EnsurePart()
        {
            if (_part != null) return;
            _building = true;
            try
            {
                PartSettings seat = Resources.FindObjectsOfTypeAll<PartSettings>().FirstOrDefault(p => p.id == NativeAdvancedSeatId);
                if (seat == null) return;
                _part = Build(seat);
                KeySeatMod.Log.Msg("Command seat registered.");
            }
            catch (Exception ex)
            {
                KeySeatMod.Log.Error($"Command seat failed: {ex}");
            }
            finally
            {
                _building = false;
            }
        }

        private static PartSettings Build(PartSettings seat)
        {
            PartSettings p = Object.Instantiate(seat);
            p.name = "KeySeat.Part" + SeatId;
            p.id = SeatId;
            p.fullLabel = Loc.T("Siège de commande", "Command seat");
            p.description = Loc.T(
                "Siège de pilotage normal (avancé), plus un clavier et 8 sorties de signal sur le côté (1 à 8, de l'avant vers l'arrière, rangée du haut puis du bas). Le siège a ses propres touches, chacune assignée à une sortie : asseyez-vous et appuyez sur F7 pour les ajouter et les régler.",
                "A regular (advanced) pilot seat, plus a keypad and 8 signal outputs on its side (1 to 8, front to back, top row then bottom row). The seat has its own keys, each assigned to an output: sit down and press F7 to add and set them up.");
            p.localizedDescription = new LocalizedString();
            p.mass = 28f;
            ItemSettings[] items = ItemList();
            var cost = new List<ItemInstance>();
            foreach (var (item, qty) in new (uint, int)[] { (101, 10), (112, 6) })
            {
                ItemSettings s = items.FirstOrDefault(i => i.id == item);
                if (s != null) cost.Add(new ItemInstance(s, qty));
            }
            if (cost.Count > 0) Traverse.Create(p).Field("buildingCost").SetValue(cost);
            Object.DontDestroyOnLoad(p);

            GameObject prefab = Object.Instantiate(seat.part, TemplateRoot);
            prefab.name = "KeySeat";
            Transform template = prefab.GetComponentsInChildren<SocketObject>(true).OrderBy(s => s.Id).Last().transform;
#if DEV
            foreach (SocketObject so in prefab.GetComponentsInChildren<SocketObject>(true))
                KeySeatMod.Log.Msg($"[DEV] native socket {so.Id} {so.Type} at {prefab.transform.InverseTransformPoint(so.transform.position).ToString("F3")}");
#endif
            for (int i = 0; i < SeatConfig.Outputs; i++)
            {
                Transform s = Object.Instantiate(template.gameObject, prefab.transform).transform;
                s.name = "KeySocket" + (i + 1);
                s.localPosition = SocketPosition(i);
                s.localEulerAngles = new Vector3(0, 90f, 0);
                Traverse.Create(s.GetComponent<SocketObject>()).Field("id").SetValue((byte)(FirstSocket + i));
            }
            Transform visuals = prefab.transform.Find("Visuals") ?? prefab.transform;
            Console().transform.SetParent(visuals, false);
            p.part = prefab;
            return p;
        }

        // Outer face of the console, two rows of four: 1-4 on top, 5-8 below, front (+z) to back.
        private static readonly float[] RowY = { 0.55f, 0.32f };
        private static readonly float[] ColZ = { 0.21f, 0.07f, -0.07f, -0.21f };

        private static Vector3 SocketPosition(int i) => new Vector3(-0.5f, RowY[i / 4], ColZ[i % 4]);

        /// <summary>
        /// Keypad console on the left of the seat, with a coloured tag above each output. It stands on the rail,
        /// above the seat's own six sockets (y 0.07), so they stay reachable.
        /// </summary>
        private static GameObject Console()
        {
            var k = new MeshKit();
            k.Bevel(new Vector3(-0.43f, 0.43f, 0.0f), new Vector3(0.14f, 0.54f, 0.56f), 0.03f, Shade.HatchDark, Shade.Dark);
            k.Box(new Vector3(-0.43f, 0.71f, 0.0f), new Vector3(0.16f, 0.03f, 0.58f), Shade.Light);
            for (int i = 0; i < SeatConfig.Outputs; i++)
            {
                // keypad: outer column = 1-4, inner column = 5-8
                float x = i < 4 ? -0.465f : -0.395f;
                k.Box(new Vector3(x, 0.735f, ColZ[i % 4]), new Vector3(0.05f, 0.02f, 0.05f), SlotShades[i]);
                Vector3 sp = SocketPosition(i);
                k.Box(new Vector3(-0.501f, sp.y + 0.085f, sp.z), new Vector3(0.004f, 0.025f, 0.09f), SlotShades[i]);
            }
            var root = new GameObject("KeySeatConsole");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            body.AddComponent<MeshFilter>().sharedMesh = k.Build("KeySeat.Console");
            body.AddComponent<MeshRenderer>().sharedMaterial = ToolAtlas;
            return root;
        }

        /// <summary>The hand-drawn atlas the MeshKit uvs point into (the mining laser's material).</summary>
        private static Material ToolAtlas =>
            Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m != null && m.name == "MiningLaser")
            ?? Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m != null && m.name == "PartMetal");

        /// <summary>
        /// Holder for the prefab. It must stay active: the game reads part prefabs with GetComponentsInChildren
        /// (sockets, interactions), which skips inactive objects. It sits far below the world.
        /// </summary>
        private static Transform TemplateRoot
        {
            get
            {
                if (_templateRoot == null)
                {
                    _templateRoot = new GameObject("KeySeat.Templates");
                    _templateRoot.transform.position = new Vector3(0f, -100000f, 0f);
                    Object.DontDestroyOnLoad(_templateRoot);
                }
                return _templateRoot.transform;
            }
        }

        private static ItemSettings[] ItemList()
        {
            ItemSettingsList list = Resources.FindObjectsOfTypeAll<ItemSettingsList>().FirstOrDefault();
            return list != null ? Traverse.Create(list).Field("items").GetValue<ItemSettings[]>() : new ItemSettings[0];
        }

        // ------------------------------------------------------------------ build menu + research

        /// <summary>Puts the seat right after the advanced pilot seat, in the same build menu row.</summary>
        private static void BuildMenuStartPrefix(BuildMenu __instance)
        {
            EnsurePart();
            var tabs = Traverse.Create(__instance).Field("tabsConfig").GetValue<BuildMenuTabConfig[]>();
            if (_part == null || tabs == null) return;
            if (tabs.Any(t => t.rows.Any(r => r.parts.Contains(_part)))) return;
            foreach (BuildMenuTabConfig tab in tabs)
                foreach (BuildMenuTabConfig.BuildMenuRow row in tab.rows)
                {
                    int at = Array.FindIndex(row.parts, x => x != null && x.id == NativeAdvancedSeatId);
                    if (at < 0) continue;
                    var parts = row.parts.ToList();
                    parts.Insert(at + 1, _part);
                    row.parts = parts.ToArray();
                    return;
                }
            // no advanced seat anywhere: add it to the first tab
            BuildMenuTabConfig first = tabs[0];
            first.rows = first.rows.Concat(new[] { new BuildMenuTabConfig.BuildMenuRow { parts = new[] { _part } } }).ToArray();
        }

        /// <summary>Unlocked by the same research as the advanced pilot seat.</summary>
        public static void RegisterResearch()
        {
            EnsurePart();
            if (_part == null) return;
            foreach (ResearchSettingsList list in Resources.FindObjectsOfTypeAll<ResearchSettingsList>())
                foreach (ResearchSettings r in list.list)
                {
                    if (r == null || r.unlockedParts == null || !r.unlockedParts.Any(x => x != null && x.id == NativeAdvancedSeatId)) continue;
                    if (r.unlockedParts.Contains(_part)) continue;
                    r.unlockedParts = r.unlockedParts.Concat(new[] { _part }).ToArray();
                    KeySeatMod.Log.Msg($"Command seat unlocked by research '{r.techName}'.");
                }
        }
    }
}

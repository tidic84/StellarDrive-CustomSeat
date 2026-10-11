using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Connection;
using HarmonyLib;
using Managers.Interface;
using Saving.Services;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.State;

namespace KeySeat
{
    /// <summary>
    /// The keys of every command seat. The host owns them: they are saved next to world.json (keyseat.txt),
    /// sent to each player who joins, and changed through the seat's settings window. A seat that was never
    /// set up uses <see cref="SeatConfig.Default"/>. Without the mod the world loads fine (the seats don't).
    /// </summary>
    internal static class SeatConfigs
    {
        private const string FileName = "keyseat.txt";

        private static readonly Dictionary<PartContext, SeatConfig> Server = new Dictionary<PartContext, SeatConfig>();
        private static readonly Dictionary<PartContext, SeatConfig> Client = new Dictionary<PartContext, SeatConfig>();
        private static readonly SeatConfig DefaultConfig = SeatConfig.Default();

        /// <summary>Client: a seat's keys changed (another player edited them, or the host's answer to ours).</summary>
        public static event Action<PartContext> Changed;

        public static void Init(HarmonyLib.Harmony h)
        {
            h.Patch(AccessTools.Method(typeof(GameSaver), "SaveGame", new[] { typeof(string) }),
                postfix: new HarmonyMethod(typeof(SeatConfigs), nameof(AfterSave)));
            h.Patch(AccessTools.Method(typeof(GameLoader), nameof(GameLoader.LoadState)),
                postfix: new HarmonyMethod(typeof(SeatConfigs), nameof(AfterLoad)));
            h.Patch(AccessTools.Method(typeof(ShipState), nameof(ShipState.RemoveStatefulPart)),
                postfix: new HarmonyMethod(typeof(SeatConfigs), nameof(OnPartRemoved)));
            KeySeatNet.OnClientHello += conn => KeySeatNet.SendTo(conn, Op.Snapshot, WriteSnapshot);
            KeySeatNet.OnServer(Op.SetConfig, ServerOnSet);
            KeySeatNet.OnClient(Op.Config, ClientOnConfig);
            KeySeatNet.OnClient(Op.Snapshot, ClientOnSnapshot);
        }

        public static void ResetClient() => Client.Clear();

        public static void ResetServer() => Server.Clear();

        /// <summary>Client: the keys of a seat, as last received from the host. Read only.</summary>
        public static SeatConfig Get(PartContext seat) => Client.TryGetValue(seat, out SeatConfig c) ? c : DefaultConfig;

        /// <summary>Client: asks the host to replace the keys of the seat we sit in.</summary>
        public static void Submit(PartContext seat, SeatConfig config)
        {
            Client[seat] = config.Clone(); // right away, the host's answer will confirm it
            KeySeatNet.SendToServer(Op.SetConfig, w =>
            {
                w.Write(seat.ShipId);
                w.Write(seat.PartId);
                config.Write(w);
            });
        }

        // ---------------------------------------------------------------- server

        private static void ServerOnSet(NetworkConnection conn, BinaryReader r)
        {
            var seat = new PartContext(r.ReadUInt32(), r.ReadUInt16());
            SeatConfig config = SeatConfig.Read(r);
            // only the command seat the sender sits in
            TrackedShipServer ship = Svc.ShipsServer?.GetShip(seat.ShipId);
            if (ship == null || !ship.TryGetStatefulPart(seat.PartId, out StatefulPart part)) return;
            if (part.Settings == null || part.Settings.id != SeatPart.SeatId || !(part.State is PilotSeatAdvState { IsInUse: true })) return;
            Server[seat] = config;
            KeySeatNet.SendToAll(Op.Config, w => WriteConfig(w, seat, config));
        }

        private static void OnPartRemoved(ShipState __instance, ushort id)
        {
            var seat = new PartContext(__instance.Id, id);
            if (KeySeatNet.IsServer && Server.Remove(seat))
                KeySeatNet.SendToAll(Op.Config, w => WriteConfig(w, seat, null));
        }

        private static void WriteConfig(BinaryWriter w, PartContext seat, SeatConfig config)
        {
            w.Write(seat.ShipId);
            w.Write(seat.PartId);
            w.Write(config != null);
            config?.Write(w);
        }

        private static void WriteSnapshot(BinaryWriter w)
        {
            w.Write(Server.Count);
            foreach (var kv in Server) WriteConfig(w, kv.Key, kv.Value);
        }

        private static void AfterSave(string worldDirectoryFullName)
        {
            try
            {
                string path = Path.Combine(worldDirectoryFullName, FileName);
                var lines = Server.SelectMany(kv => kv.Value.ToLines($"{kv.Key.ShipId} {kv.Key.PartId}")).ToList();
                if (lines.Count == 0)
                {
                    if (File.Exists(path)) File.Delete(path);
                    return;
                }
                File.WriteAllLines(path, new[] { "# KeySeat v1: ship part output mode value key name (key and name URL-escaped, ~ = empty)" }.Concat(lines));
            }
            catch (Exception ex)
            {
                KeySeatMod.Log.Error($"Saving seat keys failed: {ex.Message}");
            }
        }

        private static void AfterLoad()
        {
            Server.Clear();
            try
            {
                string world = MainGameSceneLoadData.SaveFileFullName;
                string path = string.IsNullOrEmpty(world) ? null : Path.Combine(Path.GetDirectoryName(world), FileName);
                if (path != null && File.Exists(path))
                {
                    foreach (string line in File.ReadAllLines(path))
                    {
                        if (line.StartsWith("#") || string.IsNullOrWhiteSpace(line)) continue;
                        string[] p = line.Split(' ');
                        if (p.Length < 3) continue;
                        var seat = new PartContext(uint.Parse(p[0]), ushort.Parse(p[1]));
                        if (!Server.TryGetValue(seat, out SeatConfig c)) Server[seat] = c = new SeatConfig();
                        if (SeatConfig.TryParseLine(p, 2, out KeyBinding k) && c.Keys.Count < SeatConfig.MaxKeys) c.Keys.Add(k);
                    }
                    KeySeatMod.Log.Msg($"Loaded the keys of {Server.Count} command seats.");
                }
            }
            catch (Exception ex)
            {
                KeySeatMod.Log.Error($"Loading seat keys failed: {ex.Message}");
            }
            // in case our own client said hello before the world was read
            KeySeatNet.SendToAll(Op.Snapshot, WriteSnapshot);
        }

        // ---------------------------------------------------------------- client

        private static void ReadConfigInto(BinaryReader r)
        {
            var seat = new PartContext(r.ReadUInt32(), r.ReadUInt16());
            if (r.ReadBoolean()) Client[seat] = SeatConfig.Read(r);
            else Client.Remove(seat);
            Changed?.Invoke(seat);
        }

        private static void ClientOnConfig(BinaryReader r) => ReadConfigInto(r);

        private static void ClientOnSnapshot(BinaryReader r)
        {
            Client.Clear();
            int n = r.ReadInt32();
            for (int i = 0; i < n; i++) ReadConfigInto(r);
        }
    }
}

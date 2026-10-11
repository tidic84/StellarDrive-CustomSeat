using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishNet.Connection;
using HarmonyLib;
using Ships;
using Ships.Cables;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;

namespace KeySeat
{
    /// <summary>
    /// Server side: the pilot's client sends its output values; we feed them into the game's own signal network
    /// (it re-reads every emitter each tick), so any cable or logic setup works.
    /// </summary>
    internal static class SeatSignals
    {
        // server: (ship, part) -> output values
        private static readonly Dictionary<PartContext, float[]> Values = new Dictionary<PartContext, float[]>();

        private static byte[] _lastIn, _lastOut;

        public static void Init(HarmonyLib.Harmony h)
        {
            // Last, and merged with whatever is there: Arsenal also widens this list for its gunner seat.
            h.Patch(AccessTools.PropertyGetter(typeof(PilotSeatAdvState), nameof(PilotSeatAdvState.EmissionSockets)),
                postfix: new HarmonyMethod(typeof(SeatSignals), nameof(EmissionSocketsPostfix)) { priority = Priority.Last });
            h.Patch(AccessTools.Method(typeof(SignalConnectionGroupValues), "GetSocketSignal"),
                postfix: new HarmonyMethod(typeof(SeatSignals), nameof(GetSocketSignalPostfix)));
            h.Patch(AccessTools.Method(typeof(TrackedShipServer), nameof(TrackedShipServer.ClearInteraction)),
                postfix: new HarmonyMethod(typeof(SeatSignals), nameof(ClearInteractionPostfix)));
            KeySeatNet.OnServer(Op.Outputs, ServerOnInput);
        }

        public static void Reset() => Values.Clear();

        /// <summary>Advanced seats also emit on 6-13; the plain ones simply have nothing plugged there.</summary>
        private static void EmissionSocketsPostfix(ref byte[] __result)
        {
            if (__result == null) return;
            if (ReferenceEquals(__result, _lastIn))
            {
                __result = _lastOut;
                return;
            }
            byte[] merged = __result.Union(Enumerable.Range(SeatPart.FirstSocket, SeatConfig.Outputs).Select(i => (byte)i)).ToArray();
            _lastIn = __result;
            _lastOut = merged;
            __result = merged;
        }

        private static void GetSocketSignalPostfix(EntityPartSocket entityPartSocket, ref double __result)
        {
            byte socket = entityPartSocket.Socket.SocketId;
            if (!SeatPart.IsSeatSocket(socket)) return;
            if (!Values.TryGetValue(entityPartSocket.PartContext, out float[] v)) return;
            __result = v[socket - SeatPart.FirstSocket];
        }

        /// <summary>Whoever leaves the seat (or drops from the game), its outputs fall back to 0.</summary>
        private static void ClearInteractionPostfix(TrackedShipServer __instance, ushort partId)
        {
            Values.Remove(new PartContext(__instance.Id, partId));
        }

        private static void ServerOnInput(NetworkConnection conn, BinaryReader r)
        {
            var seat = new PartContext(r.ReadUInt32(), r.ReadUInt16());
            var v = new float[SeatConfig.Outputs];
            bool any = false;
            for (int i = 0; i < v.Length; i++)
            {
                v[i] = r.ReadSingle();
                if (float.IsNaN(v[i]) || float.IsInfinity(v[i])) v[i] = 0f;
                any |= v[i] != 0f;
            }
            if (!any)
            {
                Values.Remove(seat);
                return;
            }
            // only a command seat someone is sitting in
            TrackedShipServer ship = Svc.ShipsServer?.GetShip(seat.ShipId);
            if (ship == null || !ship.TryGetStatefulPart(seat.PartId, out StatefulPart part)) return;
            if (part.Settings == null || part.Settings.id != SeatPart.SeatId || !(part.State is PilotSeatAdvState { IsInUse: true })) return;
            Values[seat] = v;
        }
    }
}

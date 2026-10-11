using System.Collections.Generic;
using HarmonyLib;
using Players.Interface.Model;
using Ships;
using Ships.Interface.Model.Parts;
using UI.Common;
using UnityEngine;

namespace KeySeat
{
    /// <summary>
    /// Client side: while the local player sits in a command seat, reads that seat's keys and sends the outputs
    /// (the sum of the values of the keys that are on, per output).
    /// </summary>
    internal static class SeatInput
    {
        /// <summary>Per key of the current seat (same order as its config): is it on.</summary>
        public static readonly List<bool> KeyOn = new List<bool>();
        private static readonly List<bool> Toggled = new List<bool>();
        private static readonly float[] Sent = new float[SeatConfig.Outputs];

        private static PartContext _seat;
        private static bool _seated, _sentAny;
        private static float _resendAt;

        public static bool Seated => _seated;
        public static PartContext Seat => _seat;

        public static void Init() => SeatConfigs.Changed += seat =>
        {
            // new keys: start again from all off
            if (_seated && seat.Equals(_seat)) Toggled.Clear();
        };

        public static void Reset()
        {
            _seated = false;
            _sentAny = false;
            KeyOn.Clear();
            Toggled.Clear();
            System.Array.Clear(Sent, 0, Sent.Length);
        }

        public static void ClientUpdate()
        {
            if (!TryGetPilotedSeat(out PartContext seat))
            {
                if (_seated)
                {
                    if (_sentAny) Send(_seat, new float[SeatConfig.Outputs]);
                    Reset();
                }
                return;
            }
            if (!_seated || !seat.Equals(_seat))
            {
                Reset();
                _seated = true;
                _seat = seat;
            }

            List<KeyBinding> keys = SeatConfigs.Get(seat).Keys;
            while (Toggled.Count < keys.Count) Toggled.Add(false);
            KeyOn.Clear();
            // keys typed in the chat or a menu (ours included) are not seat commands
            bool listening = !AnyMenuOpen();
            var values = new float[SeatConfig.Outputs];
            for (int i = 0; i < keys.Count; i++)
            {
                KeyBinding k = keys[i];
                bool on;
                if (k.Mode == KeyMode.Toggle)
                {
                    if (listening && Bindings.WasPressed(k.Path)) Toggled[i] = !Toggled[i];
                    on = Toggled[i];
                }
                else
                {
                    on = listening && Bindings.IsPressed(k.Path);
                }
                KeyOn.Add(on);
                if (on) values[k.Output] += k.Value;
            }

            bool changed = false, any = false;
            for (int i = 0; i < values.Length; i++)
            {
                changed |= values[i] != Sent[i];
                any |= values[i] != 0f;
            }
            // resend now and then while something is on, in case the host missed the seat change
            if (changed || (any && Time.unscaledTime >= _resendAt))
                Send(seat, values);
        }

        /// <summary>The value an output currently sends.</summary>
        public static float Output(int output) => Sent[output];

        private static void Send(PartContext seat, float[] values)
        {
            values.CopyTo(Sent, 0);
            _sentAny = false;
            foreach (float v in values) _sentAny |= v != 0f;
            _resendAt = Time.unscaledTime + 0.5f;
            KeySeatNet.SendToServer(Op.Outputs, w =>
            {
                w.Write(seat.ShipId);
                w.Write(seat.PartId);
                foreach (float v in values) w.Write(v);
            });
        }

        public static bool TryGetPilotedSeat(out PartContext seat)
        {
            seat = default;
            if (Svc.PlayersClient == null || !Svc.PlayersClient.TryGetLocalPlayerState(out PlayerState state)) return false;
            if (state.Type != PlayerStateType.Piloting) return false;
            if (Svc.ShipsClient == null || !Svc.ShipsClient.TryGetTrackedShip(state.EntityId, out TrackedShipClient ship)) return false;
            if (!ship.TryGetStatefulPart(state.PartId, out StatefulPart part) || part.Settings == null || part.Settings.id != SeatPart.SeatId) return false;
            seat = new PartContext(state.EntityId, state.PartId);
            return true;
        }

        public static bool AnyMenuOpen()
        {
            MenuTracker menus = Svc.Get<MenuTracker>();
            if (menus == null) return false;
            List<MonoBehaviour> open = ActiveMenus(menus);
            return open != null && open.Count > 0;
        }

        private static readonly AccessTools.FieldRef<MenuTracker, List<MonoBehaviour>> ActiveMenus =
            AccessTools.FieldRefAccess<MenuTracker, List<MonoBehaviour>>("_activeMenus");
    }
}

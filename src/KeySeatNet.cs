using System;
using System.Collections.Generic;
using System.IO;
using FishNet;
using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Managing;
using FishNet.Serializing;
using FishNet.Transporting;

namespace KeySeat
{
    /// <summary>The mod's single FishNet broadcast; the payload is our own binary format (see <see cref="Op"/>).</summary>
    public struct KeySeatPacket : IBroadcast
    {
        public byte[] Data;
    }

    internal enum Op : byte
    {
        // client -> server
        Hello = 1,      // protocol version; the server answers with every seat's keys
        Outputs = 2,    // seat + the 8 output values of the pilot
        SetConfig = 3,  // seat + its new keys (from the settings window)

        // server -> client
        Config = 64,    // seat + its keys (or none: back to the default)
        Snapshot = 65,  // all seats' keys
    }

    /// <summary>Registers the broadcast with whichever NetworkManager is running and routes packets by opcode.</summary>
    internal static class KeySeatNet
    {
        public delegate void ServerHandler(NetworkConnection sender, BinaryReader r);
        public delegate void ClientHandler(BinaryReader r);

        private static readonly Dictionary<Op, ServerHandler> ServerHandlers = new Dictionary<Op, ServerHandler>();
        private static readonly Dictionary<Op, ClientHandler> ClientHandlers = new Dictionary<Op, ClientHandler>();

        private static NetworkManager _registeredOn;
        private static bool _serializersSet, _helloSent;

        public static event Action<NetworkConnection> OnClientHello;

        public static bool IsServer => InstanceFinder.IsServerStarted;
        public static bool IsClient => InstanceFinder.IsClientStarted;

        public static void OnServer(Op op, ServerHandler h) => ServerHandlers[op] = h;
        public static void OnClient(Op op, ClientHandler h) => ClientHandlers[op] = h;

        /// <summary>Called every frame: (re)binds to the current NetworkManager and says hello once the client is up.</summary>
        public static void Tick()
        {
            if (!_serializersSet)
            {
                _serializersSet = true;
                GenericWriter<KeySeatPacket>.SetWrite((w, p) => w.WriteUInt8ArrayAndSize(p.Data ?? Array.Empty<byte>()));
                GenericReader<KeySeatPacket>.SetRead(r => new KeySeatPacket { Data = r.ReadUInt8ArrayAndSizeAllocated() });
            }
            NetworkManager nm = InstanceFinder.NetworkManager;
            if (nm == null)
            {
                _registeredOn = null;
                _helloSent = false;
                return;
            }
            if (_registeredOn != nm)
            {
                _registeredOn = nm;
                _helloSent = false;
                nm.ServerManager.RegisterBroadcast<KeySeatPacket>(OnServerPacket);
                nm.ClientManager.RegisterBroadcast<KeySeatPacket>(OnClientPacket);
            }
            if (!_helloSent && nm.ClientManager.Started && nm.ClientManager.Connection != null && nm.ClientManager.Connection.IsAuthenticated)
            {
                _helloSent = true;
                SendToServer(Op.Hello, w => w.Write(KeySeatMod.ProtocolVersion));
            }
        }

        private static byte[] Build(Op op, Action<BinaryWriter> body)
        {
            using (var ms = new MemoryStream(64))
            using (var w = new BinaryWriter(ms))
            {
                w.Write((byte)op);
                body?.Invoke(w);
                w.Flush();
                return ms.ToArray();
            }
        }

        public static void SendToServer(Op op, Action<BinaryWriter> body)
        {
            if (!IsClient) return;
            InstanceFinder.ClientManager.Broadcast(new KeySeatPacket { Data = Build(op, body) }, Channel.Reliable);
        }

        public static void SendToAll(Op op, Action<BinaryWriter> body)
        {
            if (!IsServer) return;
            InstanceFinder.ServerManager.Broadcast(new KeySeatPacket { Data = Build(op, body) }, true, Channel.Reliable);
        }

        public static void SendTo(NetworkConnection conn, Op op, Action<BinaryWriter> body)
        {
            if (!IsServer || conn == null) return;
            InstanceFinder.ServerManager.Broadcast(conn, new KeySeatPacket { Data = Build(op, body) }, true, Channel.Reliable);
        }

        private static void OnServerPacket(NetworkConnection conn, KeySeatPacket p, Channel channel)
        {
            if (p.Data == null || p.Data.Length == 0) return;
            var op = (Op)p.Data[0];
            try
            {
                using (var r = new BinaryReader(new MemoryStream(p.Data, 1, p.Data.Length - 1)))
                {
                    if (op == Op.Hello)
                    {
                        int version = r.ReadInt32();
                        if (version != KeySeatMod.ProtocolVersion)
                            KeySeatMod.Log.Warning($"Client {conn.ClientId} runs KeySeat protocol {version}, host runs {KeySeatMod.ProtocolVersion}.");
                        OnClientHello?.Invoke(conn);
                        return;
                    }
                    if (ServerHandlers.TryGetValue(op, out ServerHandler h)) h(conn, r);
                }
            }
            catch (Exception ex)
            {
                KeySeatMod.Log.Error($"Server packet {op} failed: {ex}");
            }
        }

        private static void OnClientPacket(KeySeatPacket p, Channel channel)
        {
            if (p.Data == null || p.Data.Length == 0) return;
            var op = (Op)p.Data[0];
            try
            {
                using (var r = new BinaryReader(new MemoryStream(p.Data, 1, p.Data.Length - 1)))
                    if (ClientHandlers.TryGetValue(op, out ClientHandler h)) h(r);
            }
            catch (Exception ex)
            {
                KeySeatMod.Log.Error($"Client packet {op} failed: {ex}");
            }
        }
    }
}

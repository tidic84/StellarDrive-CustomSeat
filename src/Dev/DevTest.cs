#if DEV
using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using Managers.Interface;
using MelonLoader;
using MelonLoader.Utils;
using Players;
using Players.LocalClient.Controls;
using Ships;
using Ships.Interface.Model;
using Ships.Interface.Model.Gases;
using Ships.Interface.Model.Parts;
using Ships.Interface.Model.Parts.Common;
using Ships.Interface.Model.Parts.State;
using Ships.Interface.Model.Placement;
using Ships.Interface.Services;
using Ships.Interface.Settings;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.SceneManagement;
using WorldTracking.Interface.Services;
using Object = UnityEngine.Object;

namespace KeySeat
{
    // Dev-only: loads a test world, builds a ship with a command seat wired to 8 signal displays,
    // presses keys through the Input System and logs what the displays receive.
    public partial class KeySeatMod
    {
        private const string TestWorld = "KS_Test";
        private bool _launched;
        private uint _shipId;

        private void DevInit() { }

        private void DevUpdate() { }

        private void DevSceneLoaded(int buildIndex, string sceneName)
        {
            if (buildIndex == 0 && !_launched)
            {
                _launched = true;
                MelonCoroutines.Start(LaunchTestWorld());
            }
            if (sceneName == MainGameScene)
                MelonCoroutines.Start(RunTest());
        }

        private IEnumerator LaunchTestWorld()
        {
            for (int i = 0; i < 30; i++) yield return null;
            string file = Path.Combine(Application.persistentDataPath, "WorldSaves", TestWorld, "world.json");
            MainGameSceneLoadData.Mode = MainGameSceneLoadData.GameMode.SinglePlayer;
            MainGameSceneLoadData.SaveFileFullName = new FileInfo(file).FullName;
            SceneManager.LoadScene(MainGameScene, LoadSceneMode.Single);
        }

        private static IEnumerator Wait(float s)
        {
            float end = Time.realtimeSinceStartup + s;
            while (Time.realtimeSinceStartup < end) yield return null;
        }

        private static void Keys(params Key[] keys) => InputSystem.QueueStateEvent(Keyboard.current, new KeyboardState(keys));

        private void Shot(string name)
        {
            string path = Path.Combine(MelonEnvironment.UserDataDirectory, "ks_" + name + ".png");
            ScreenCapture.CaptureScreenshot(path);
            Log.Msg($"[DEV] screenshot {path}");
        }

        private string Displays()
        {
            TrackedShipServer ship = Svc.ShipsServer.GetShip(_shipId);
            return string.Join(" ", Enumerable.Range(2, 8).Select(p =>
                ship.TryGetStatefulPart((ushort)p, out StatefulPart part) && part.State is SignalDisplayState s ? s.SignalValue.ToString("0.##", CultureInfo.InvariantCulture) : "?"));
        }

        private static void Sit(uint ship) =>
            Svc.Get<Managers.Interface.Services.IShipInteractionNetworkService>().StartInteractWithPart(
                new Managers.Interface.Model.PartInteractNetworkRequest { ShipId = ship, PartId = 1 });

        private static void Stand() => Svc.Get<Managers.Interface.Services.IShipInteractionNetworkService>().StopInteract();

        private IEnumerator Tap(params Key[] keys)
        {
            Keys(keys);
            yield return Wait(0.5f);
            Keys();
            yield return Wait(1f);
        }

        private IEnumerator Hold(string what, string expect, params Key[] keys)
        {
            Keys(keys);
            yield return Wait(1f);
            string got = Displays();
            Log.Msg($"[DEV] {(got == expect ? "OK  " : "FAIL")} {what} -> {got}  (expect {expect})");
            Keys();
            yield return Wait(1f);
        }

        private void Expect(string what, string expect)
        {
            string got = Displays();
            Log.Msg($"[DEV] {(got == expect ? "OK  " : "FAIL")} {what} -> {got}  (expect {expect})");
        }

        private IEnumerator RunTest()
        {
            yield return Wait(10f);
            if (Environment.GetEnvironmentVariable("KS_PHASE") == "2")
            {
                yield return RunReloadTest();
                yield break;
            }
            SpawnShip();
            yield return Wait(3f);
            Sit(_shipId);
            yield return Wait(2f);
            Log.Msg($"[DEV] seated={SeatInput.Seated}, keys of a new seat: {SeatConfigs.Get(SeatInput.Seat).Keys.Count}");
            yield return Hold("default keys 1+8", "1 0 0 0 0 0 0 1", Key.Digit1, Key.Digit8);

            // the seat's own keys, through the same path as the settings window
            SeatConfig c = SeatConfig.Default();
            c.Keys[0].Name = "Laser";
            c.Keys[1].Mode = KeyMode.Toggle;
            c.Keys[2].Value = -0.5f;
            c.Keys.Add(new KeyBinding { Name = "Boost", Path = "<Keyboard>/z", Output = 0, Value = 2f });
            SeatConfigs.Submit(SeatInput.Seat, c);
            yield return Wait(1f);

            yield return Hold("1 + Z on output 1", "3 0 0 0 0 0 0 0", Key.Digit1, Key.Z);
            yield return Hold("Z alone", "2 0 0 0 0 0 0 0", Key.Z);
            yield return Hold("3 (value -0.5)", "0 0 -0.5 0 0 0 0 0", Key.Digit3);
            yield return Hold("4-8", "0 0 0 1 1 1 1 1", Key.Digit4, Key.Digit5, Key.Digit6, Key.Digit7, Key.Digit8);
            yield return Tap(Key.Digit2);
            Expect("toggle 2 on", "0 1 0 0 0 0 0 0");
            yield return Tap(Key.Digit2);
            Expect("toggle 2 off", "0 0 0 0 0 0 0 0");

            yield return Tap(Key.F7);
            Log.Msg($"[DEV] window open={SeatInput.AnyMenuOpen()}");
            yield return Hold("key 1 with window open", "0 0 0 0 0 0 0 0", Key.Digit1);
            Shot("window");
            yield return Tap(Key.Escape);
            Log.Msg($"[DEV] window closed, menus={SeatInput.AnyMenuOpen()}");

            Keys(Key.Z);
            yield return Wait(1f);
            Shot("hud");
            Keys();
            yield return Wait(1f);

            Stand();
            yield return Wait(2f);
            Expect("left the seat", "0 0 0 0 0 0 0 0");

            Svc.Get<Saving.Services.GameSaver>().SaveGame(new Saving.Interface.Model.SaveGameParams { IsAutomatic = false });
            yield return Wait(3f);
            string file = Path.Combine(Path.GetDirectoryName(MainGameSceneLoadData.SaveFileFullName), "keyseat.txt");
            Log.Msg($"[DEV] saved {file}:\n{(File.Exists(file) ? File.ReadAllText(file) : "MISSING")}");
            Log.Msg("[DEV] test done");
        }

        private IEnumerator RunReloadTest()
        {
            _shipId = Svc.ShipsClient.Ships.Where(s => s.TryGetStatefulPart(1, out StatefulPart p) && p.Settings != null && p.Settings.id == SeatPart.SeatId)
                .Select(s => s.Id).FirstOrDefault();
            Log.Msg($"[DEV] reload: seat ship {_shipId}");
            Sit(_shipId);
            yield return Wait(2f);
            SeatConfig c = SeatConfigs.Get(SeatInput.Seat);
            Log.Msg($"[DEV] {(c.Keys.Count == 9 && c.Keys[8].Name == "Boost" ? "OK  " : "FAIL")} reloaded keys: {string.Join(", ", c.Keys.Select(k => $"{k.Label}={k.Path}->{k.Output + 1}"))}");
            yield return Hold("Z after reload", "2 0 0 0 0 0 0 0", Key.Z);
            yield return Tap(Key.Digit2);
            Expect("toggle 2 after reload", "0 1 0 0 0 0 0 0");
            Stand();
            yield return Wait(2f);
            Log.Msg("[DEV] test done");
        }

        private void SpawnShip()
        {
            TrackedPlayerServer p = Svc.Get<Players.Services.PlayersServerTracker>().ActivePlayers.First();
            var settings = new PartSettingsProvider();
            HullPartSettings floor = (HullPartSettings)settings.GetPartSettingById(1);
            var hull = new System.Collections.Generic.List<HullPart>();
            for (int x = -2; x <= 2; x++)
                for (int z = -2; z <= 4; z++)
                    hull.Add(new HullPart(floor, new HullPartPlacement(new Vector3Int(x, 0, z), VoxelPartType.Floor, PartOrientation.Up, PartOrientation.Forward)));
            var parts = new System.Collections.Generic.List<StatefulPart>
            {
                new StatefulPart(1, settings.GetPartSettingById(SeatPart.SeatId), new FloatingPartPlacement(new Vector3(0, -0.46f, 0), PartOrientation.Forward)),
            };
            for (int i = 0; i < SeatConfig.Outputs; i++)
                parts.Add(new StatefulPart((ushort)(2 + i), settings.GetPartSettingById(82), new FloatingPartPlacement(new Vector3(-1.75f + i * 0.5f, -0.46f, 2.5f), PartOrientation.Forward)));
            var phys = p.PhysicsState;
            Quaternion rot = phys.Rotation;
            Vector3 up = rot * Vector3.up;
            Vector3 fwd = Vector3.ProjectOnPlane(rot * Vector3.forward, up).normalized;
            Quaternion shipRot = Quaternion.LookRotation(fwd, up);
            uint id = Svc.Get<IWorldObjectsIdsServerProvider>().CreateUniqueIdForObject();
            Svc.ShipsServer.LoadShip(new ShipStateSnapshot
            {
                Id = id,
                RelativeSpaceId = Svc.Get<WorldTracking.Interface.Services.ISpacesServerProvider>().GetOrCreateShipRelativeSpace(id),
                SpaceId = phys.SpaceId,
                Position = phys.Position + shipRot * new Vector3(3f, 0.7f, -1f),
                Rotation = shipRot,
                Velocity = Vector3.zero,
                RoomGasState = new RoomGasState[0],
                HullParts = hull.ToArray(),
                StatefulParts = parts.ToArray(),
            });
            _shipId = id;
            var cables = Svc.Get<Ships.Cables.CablesServerTracker>();
            for (int i = 0; i < SeatConfig.Outputs; i++)
                cables.AddCable(new CableState
                {
                    Type = CableType.Signal,
                    PlugA = new CablePlugSocket { EntityPartSocket = new EntityPartSocket(id, 1, (byte)(SeatPart.FirstSocket + i)) },
                    PlugB = new CablePlugSocket { EntityPartSocket = new EntityPartSocket(id, (ushort)(2 + i), 0) },
                });
            Log.Msg($"[DEV] ship {id}");
        }

        /// <summary>Screenshot of the test ship from a temporary camera placed in ship coordinates.</summary>
        private IEnumerator ShipShot(string name, Vector3 fromLocal, Vector3 targetLocal)
        {
            // the spawned seat itself (the prefab template sits far below the world)
            var found = Resources.FindObjectsOfTypeAll<Transform>()
                .Where(t => t.name == "KeySeatConsole" && t.gameObject.scene.IsValid() && t.position.y > -50000f).ToList();
            foreach (Transform t in found)
                Log.Msg($"[DEV] console {t.position} active={t.gameObject.activeInHierarchy} path={t.root.name}/{t.parent.parent?.name}");
            Vector3 eye = Camera.main != null ? Camera.main.transform.position : Vector3.zero;
            Transform rb = found.Where(t => t.gameObject.activeInHierarchy).OrderBy(t => (t.position - eye).sqrMagnitude).Select(t => t.parent).FirstOrDefault();
            if (rb == null)
            {
                Log.Msg("[DEV] seat not found");
                yield break;
            }
            Log.Msg($"[DEV] seat at {rb.position}, camera at {Camera.main?.transform.position}");
            Camera main = Camera.main;
            var go = new GameObject("KeySeat.DevCam");
            Camera cam = go.AddComponent<Camera>();
            if (main != null) cam.CopyFrom(main);
            cam.depth = 100f;
            cam.fieldOfView = 50f;
            go.transform.position = rb.TransformPoint(fromLocal);
            go.transform.rotation = Quaternion.LookRotation(rb.TransformPoint(targetLocal) - go.transform.position, rb.up);
            yield return Wait(0.5f);
            Shot(name);
            yield return Wait(0.5f);
            Object.Destroy(go);
        }
    }
}
#endif

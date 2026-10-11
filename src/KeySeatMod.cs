using MelonLoader;
using UnityEngine.SceneManagement;

[assembly: MelonInfo(typeof(KeySeat.KeySeatMod), "KeySeat", "1.1.0", "tidic84")]
[assembly: MelonGame("CuriousOwlGames", "StellarDrive")]

namespace KeySeat
{
    public partial class KeySeatMod : MelonMod
    {
        internal const string MainGameScene = "MainGame";
        internal const int ProtocolVersion = 2;

        internal static KeySeatMod Instance { get; private set; }
        internal static MelonLogger.Instance Log => Instance.LoggerInstance;

        internal static bool InGame => SceneManager.GetActiveScene().name == MainGameScene;

        public override void OnInitializeMelon()
        {
            Instance = this;
            Bindings.Init();
            SeatPart.Patch(HarmonyInstance);
            SeatSignals.Init(HarmonyInstance);
            SeatConfigs.Init(HarmonyInstance);
            SeatInput.Init();
#if DEV
            DevInit();
#endif
        }

        public override void OnSceneWasLoaded(int buildIndex, string sceneName)
        {
            Svc.Clear();
            SeatSignals.Reset();
            SeatInput.Reset();
            SeatConfigs.ResetClient();
            KeySeatUi.Reset();
            if (sceneName == MainGameScene)
            {
                SeatPart.EnsurePart();
                SeatPart.RegisterResearch();
                KeySeatUi.Ensure();
            }
#if DEV
            DevSceneLoaded(buildIndex, sceneName);
#endif
        }

        public override void OnSceneWasUnloaded(int buildIndex, string sceneName)
        {
            // the host's seat keys live as long as the world (they are read again with the next world)
            if (sceneName == MainGameScene) SeatConfigs.ResetServer();
        }

        public override void OnUpdate()
        {
            KeySeatNet.Tick();
            if (InGame) SeatInput.ClientUpdate();
#if DEV
            DevUpdate();
#endif
        }
    }
}

using Generated;
using HighwayEngineer.Client;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Server;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.Components;
using Constants = HighwayEngineer.Common.Helpers.Constants;

namespace HighwayEngineer
{
    /// <summary>
    /// Entry point. Owns the network channel and creates the server side (when this
    /// machine is the server) and the client side (when it is not a dedicated server);
    /// a listen host or single player runs both.
    /// </summary>
    [MySessionComponentDescriptor(MyUpdateOrder.BeforeSimulation)]
    public partial class HighwayEngineerSessionComponent : MySessionComponentBase,
        ISingleton<HighwayEngineerSessionComponent>
    {
        public static HighwayEngineerClientComponent Client;
        public static HighwayEngineerServerComponent Server;

        public static NetworkManager NetworkManager = new NetworkManager(new NetworkParameters(
            Constants.PORT,
            Constants.MAX_WIRE_PACKET_BYTES,
            Constants.MAX_FRAGMENTED_PACKET_BYTES,
            Constants.MAX_FRAGMENT_CHUNKS));

        /// <summary>This mod, for loading its content (styles) from LCD scripts.</summary>
        internal static MyObjectBuilder_Checkpoint.ModItem ModItem { get; private set; }

        public override void LoadData()
        {
            ModItem = ModContext.ModItem;
            ModLocalization.Load(ModContext.ModPathData);
            ConcreteMod.Detect();
            RegisterSingleton();

            if (MyAPIGateway.Session.IsServer)
            {
                Server = new HighwayEngineerServerComponent(this);
                Server.LoadData();
            }

            if (!MyAPIGateway.Utilities.IsDedicated)
            {
                Client = new HighwayEngineerClientComponent(this);
                Client.LoadData();
                MyAPIGateway.Gui.GuiControlRemoved += OnGuiControlRemoved;
                // The planner's UI is drawn by the Acrux UI mod. Mods load in any order, so it may connect now or
                // later; the client's non-UI work (road markings, repairs, contracts) runs either way.
                Acrux.Toolkit.Ui.ClientName = "HighwayEngineer";
                Acrux.Toolkit.AcruxUiToolkitClient.Connect(ACRUX_UI_REPLY_CHANNEL, () =>
                {
                    if (Client != null)
                        Client.OnUiConnected();
                });
            }
        }

        /// <summary>The language can change in the options screen; reload the texts when it closes.</summary>
        static void OnGuiControlRemoved(object control)
        {
            if (control != null && control.ToString().EndsWith("ScreenOptionsSpace"))
                ModLocalization.Reload();
        }

        /// <summary>This mod's private channel for the Acrux UI mod's reply ("HWENGUI" + 1).</summary>
        const long ACRUX_UI_REPLY_CHANNEL = 0x4857454E47554901L;

        public override void BeforeStart()
        {
            NetworkManager.Init();
            Server?.BeforeStart();
            Client?.BeforeStart();
        }

        public override void UpdateBeforeSimulation()
        {
            Server?.UpdateBeforeSimulation();
            Client?.UpdateBeforeSimulation();
        }

        public override void Draw()
        {
            Client?.Draw();
        }

        public override void SaveData()
        {
            Server?.SaveData();
        }

        protected override void UnloadData()
        {
            if (MyAPIGateway.Gui != null)
                MyAPIGateway.Gui.GuiControlRemoved -= OnGuiControlRemoved;
            ModLocalization.Unload();
            Client?.UnloadData();
            Server?.UnloadData();
            if (!MyAPIGateway.Utilities.IsDedicated)
                Acrux.Toolkit.AcruxUiToolkitClient.Disconnect();
            Client = null;
            Server = null;
            NetworkManager.Unregister();
            ConcreteMod.Unload();
            UnregisterSingleton();
        }
    }
}

using Acrux.Cartography.Maps;
using Acrux.Cartography;
using Generated;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Client.Apps.RoadPlanner;
using HighwayEngineer.Client.Gui;
using HighwayEngineer.Client.Helpers;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Input;
using VRage.Utils;

namespace HighwayEngineer.Client
{
    /// <summary>Client side: the planner screen, LCD planners and the U key.</summary>
    public partial class HighwayEngineerClientComponent : ISingleton<HighwayEngineerClientComponent>
    {
        readonly HighwayEngineerSessionComponent _session;
        RoadPlannerScreen _screen;
        readonly Roads.RoadMarkingDecals _roadMarkings = new Roads.RoadMarkingDecals();
        int _apiCheckFrames;
        bool _apiCheckReported;
        bool _started;
        int _uiWaitFrames;
        bool _uiMissingReported;

        /// <summary>
        /// True once the Acrux UI mod, which draws the planner, has connected. No Acrux UI type may be touched before:
        /// the planner screen and the LCD planners wait for it.
        /// </summary>
        internal static bool UiAvailable { get; private set; }

        public HighwayEngineerClientComponent(HighwayEngineerSessionComponent session)
        {
            RegisterSingleton();
            _session = session;
        }

        /// <summary>The cartography module, shared by the map screen and LCD planners.</summary>
        internal CartographyModule Cartography { get; private set; }

        public void LoadData()
        {
            MyAPIGateway.Entities.OnEntityAdd += LogBridgeArrived;
        }

        /// <summary>The Acrux UI mod connected (during LoadData or later, whichever mod loaded first).</summary>
        internal void OnUiConnected()
        {
            UiAvailable = true;
            if (_started)
                StartUi();
        }

        void StartUi()
        {
            if (_screen != null) return;
            Cartography = CartographyModule.Shared;
            _screen = new RoadPlannerScreen(Cartography, _session.ModContext.ModItem);
        }

        /// <summary>Multiplayer diagnostics: a bridge's voxel map reaching this machine (the server names them HighwayEngineerBridge_*).</summary>
        static void LogBridgeArrived(VRage.ModAPI.IMyEntity entity)
        {
            var map = entity as IMyVoxelMap;
            if (map == null || map.StorageName == null || !map.StorageName.StartsWith("HighwayEngineerBridge_")) return;
            MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Bridge: voxel map " + map.StorageName + " arrived (entity " +
                map.EntityId + ") at " + map.PositionComp.GetPosition() + ", storage " + (map.Storage == null ? "none" : map.Storage.Size.ToString()));
        }

        public void BeforeStart()
        {
            _started = true;
            if (UiAvailable)
                StartUi();
            Lcd.PlannerTerminalControls.Register();
            RoadLibraryChanged += _roadMarkings.OnRoadLibraryChanged;
            // Delivery boxes of contracts opened in an earlier session, and the roads built so far.
            RequestRoadContracts();
            RequestRoadLibrary();
        }

        public void Draw()
        {
            Contracts.DeliveryZoneRenderer.Draw(OpenContracts);
        }

        public void UpdateBeforeSimulation()
        {
            MinimapLocalConfig.UpdatePendingSave();
            UpdateRoadLibrary();
            _roadMarkings.Update();
            _voxelRepairs.Update();
            ReportMissingUi();
            if (_screen == null || MyAPIGateway.Input == null || MyAPIGateway.Gui == null) return;
            ValidateStartupApi();
            // The Open Map key from settings (ADK Key codes are the same virtual-key codes as MyKeys).
            if (!MyAPIGateway.Input.IsNewKeyPressed((MyKeys)MinimapLocalConfig.Current.OpenMapKey)) return;
            if (MyAPIGateway.Gui.ChatEntryVisible) return;
            if (_screen.IsOpen)
            {
                // Only the topmost screen owns input: while another screen sits above the planner (its settings
                // screen, a Keen dialog, another mod's screen) the key belongs to it. A focused text control of
                // the planner owns printable keys too.
                if (_screen.HasFocus && !_screen.HasKeyboardFocus) _screen.Close();
                return;
            }
            if (IsOtherScreenOpen(MyAPIGateway.Gui)) return;
            _screen.Open();
        }

        /// <summary>
        /// Whether some screen other than gameplay owns input while the planner is closed: the terminal, a Keen
        /// gameplay screen (toolbar config, help, players...), any screen that shows the cursor, or an Acrux mod's
        /// keyboard claim (the LCD mod's text entry, Wristpad and Doom screens hide the cursor). ModAPI exposes no
        /// screen stack, so a cursorless mod screen that does not claim the keyboard cannot be detected.
        /// </summary>
        static bool IsOtherScreenOpen(IMyGui gui)
        {
            return gui.IsCursorVisible ||
                   gui.GetCurrentScreen != MyTerminalPageEnum.None ||
                   gui.ActiveGamePlayScreen != null ||
                   Acrux.ScreenSpace.KeyboardCapture.IsCaptured;
        }

        /// <summary>Once, a few seconds in: the world has HighwayEngineer but not the Acrux UI mod its planner needs.</summary>
        void ReportMissingUi()
        {
            if (UiAvailable || _uiMissingReported || !_started) return;
            if (++_uiWaitFrames < 300) return;
            _uiMissingReported = true;
            MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Acrux UI is not loaded; the road planner is unavailable.");
            RoadChat.Show(LocHelper.GetLoc("HighwayEngineer_Ui_Missing"));
        }

        void ValidateStartupApi()
        {
            if (_apiCheckReported) return;
            _apiCheckFrames++;
            if (_apiCheckFrames != 1 && _apiCheckFrames % 30 != 0) return;

            ApiProvider api;
            string error;
            if (RoadApi.TryValidate(out api, out error))
            {
                _apiCheckReported = true;
                MyAPIGateway.Utilities.ShowNotification(
                    LocHelper.Format("HighwayEngineer_Api_Ready", api.GetApiVersion()), 4000, "Green");
                return;
            }
            // Other mods may register their API after this session component starts.
            if (_apiCheckFrames < 300) return;
            _apiCheckReported = true;
            MyLog.Default.WriteLineAndConsole("[HighwayEngineer] " + LocHelper.Resolve(error));
            MyAPIGateway.Utilities.ShowNotification(LocHelper.GetLoc("HighwayEngineer_Api_UnavailableSeeChat"), 10000, "Red");
            RoadChat.Show(error);
        }

        public void UnloadData()
        {
            if (MyAPIGateway.Entities != null)
                MyAPIGateway.Entities.OnEntityAdd -= LogBridgeArrived;
            MinimapLocalConfig.FlushPendingSave();
            ResetRoadDepots();
            RoadLibraryChanged -= _roadMarkings.OnRoadLibraryChanged;
            _roadMarkings.Clear();
            _voxelRepairs.Clear();
            Library.Clear();
            if (_screen != null && _screen.IsOpen) _screen.CloseNow();
            _screen = null;
            // The cartography module is shared by every mod using Acrux UI; the toolkit releases it.
            Cartography = null;
            Gui.VoxelMaterialThumbnails.Clear();
            Gui.GameIconSprites.Clear();
            Gui.Styling.MinimapStyles.Unload();
            Maps.PlanetMetadataWarmup.Unload();
            UiAvailable = false;
            Lcd.LcdEyeTracking.Reset();
            Lcd.PlannerTerminalControls.Unregister();
            UnregisterSingleton();
        }
    }
}

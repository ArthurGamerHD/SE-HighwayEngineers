using Acrux.Core;
using HighwayEngineer.Client.Apps.RoadPlanner;
using HighwayEngineer.Client.Infrastructure;
using Sandbox.Game.GameSystems.TextSurfaceScripts;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.Input;
using VRageMath;

namespace HighwayEngineer.Client.Lcd
{
    /// <summary>
    /// The road planner on an LCD, selectable from the panel's script list. It hosts
    /// the same <see cref="RoadPlannerView"/> as the full-screen map.
    /// </summary>
    [MyTextSurfaceScript(ID, TITLE)]
    public sealed class RoadPlannerSurfaceScript : LcdControlSurfaceScript
    {
        public const string ID = "HighwayEngineer_RoadPlanner";
        /// <summary>Localization key of the script list entry (the game looks it up in MyTexts).</summary>
        public const string TITLE = "HighwayEngineer_Lcd_ScriptTitle";

        RoadPlannerView _view;

        public RoadPlannerSurfaceScript(Sandbox.ModAPI.Ingame.IMyTextSurface surface, IMyCubeBlock block,
            Vector2 size)
            : base(surface, block, size)
        {
        }

        int _surfaceIndex = -1;

        /// <summary>The view scales its own icons and text to the panel size.</summary>
        protected override float BaseRenderScaling => 1f;

        protected override float UserScale
        {
            get
            {
                if (_surfaceIndex < 0)
                    _surfaceIndex = PlannerSettings.GetSurfaceIndex(OwnerBlock, OwnerSurface);
                return PlannerSettings.GetScale(OwnerBlock, _surfaceIndex);
            }
        }

        /// <summary>Drags, transitions and map renders in flight update every frame.</summary>
        protected override bool NeedsFrameUpdates => _view != null && _view.ViewModel.NeedsFrameUpdates;

        protected override VisualElement CreateContent()
        {
            if (HighwayEngineerSessionComponent.Client?.Cartography == null)
                return null;
            _view = new RoadPlannerView(HighwayEngineerSessionComponent.Client.Cartography, new PanelHost());
            return _view;
        }

        protected override void OnLayout(RectangleF bounds)
        {
            if (_view != null)
                _view.ApplyLayout(bounds);
        }

        protected override void OnArranged()
        {
            if (_view != null)
                _view.OnArranged();
        }

        protected override void UpdateContent()
        {
            if (_view == null)
                return;
            _view.UpdateFrame();
            HandleKeys();
        }

        /// <summary>
        /// The planner's hotkeys while the player looks at this panel: + / - lift or lower the
        /// selected node (making its segments a bridge, for gaps the automatic mode misses),
        /// numpad + / - zoom, Ctrl+Z undo, Ctrl+Y or Ctrl+Shift+Z redo. Keys the character also
        /// uses (roll, centre) are left out: the game still gets them while looking at an LCD.
        /// </summary>
        void HandleKeys()
        {
            VRage.ModAPI.IMyInput input = MyAPIGateway.Input;
            if (input == null || MyAPIGateway.Gui == null || !IsLookedAt || Host == null || Host.HasKeyboardFocus ||
                _view.IsMessageBoxOpen || MyAPIGateway.Gui.ChatEntryVisible || MyAPIGateway.Gui.IsCursorVisible)
                return;
            RoadPlannerViewModel viewModel = _view.ViewModel;
            if (input.IsAnyCtrlKeyPressed())
            {
                if (input.IsNewKeyPressed(MyKeys.Y) || input.IsNewKeyPressed(MyKeys.Z) && input.IsAnyShiftKeyPressed())
                    viewModel.RedoRoadEdit();
                else if (input.IsNewKeyPressed(MyKeys.Z))
                    viewModel.UndoRoadEdit();
                return;
            }
            MinimapLocalConfig config = MinimapLocalConfig.Current;
            if (input.IsNewKeyPressed((MyKeys)config.ZoomInKey))
                viewModel.ZoomMap(120);
            if (input.IsNewKeyPressed((MyKeys)config.ZoomOutKey))
                viewModel.ZoomMap(-120);
            if (input.IsNewKeyPressed((MyKeys)config.BridgeRaiseKey))
                viewModel.LiftRoadNode(true);
            if (input.IsNewKeyPressed((MyKeys)config.BridgeLowerKey))
                viewModel.LiftRoadNode(false);
        }

        protected override void BeforeDraw()
        {
            if (_view != null)
                _view.BeginDraw();
        }

        protected override void AfterDraw()
        {
            if (_view != null)
                _view.EndDraw();
        }

        protected override void OnDisposing()
        {
            if (_view != null)
                _view.Shutdown();
            _view = null;
        }

        /// <summary>A panel cannot close, and has no separate settings screen.</summary>
        sealed class PanelHost : IRoadPlannerHost
        {
            public bool IsFullScreen => false;

            public void Close()
            {
            }

            public void CloseNow()
            {
            }

            public void OpenSettings()
            {
            }
        }
    }
}

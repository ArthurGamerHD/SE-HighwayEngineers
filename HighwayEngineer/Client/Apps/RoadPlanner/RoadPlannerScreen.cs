using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.ScreenSpace;
using HighwayEngineer.Client.Apps.Settings;
using HighwayEngineer.Client.Gui.Styling;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Input;
using VRageMath;
using GuiHost = Acrux.Gui;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    /// <summary>
    /// Full-screen host of the road planner (<see cref="RoadPlannerView"/>): fits it to
    /// the game viewport, feeds it screen input and keyboard shortcuts, and opens the
    /// settings screen. The same view also runs on LCDs (RoadPlannerSurfaceScript).
    /// </summary>
    public sealed class RoadPlannerScreen : MyModScreen, IRoadPlannerHost
    {
        const float KEYBOARD_ROLL_RADIANS_PER_FRAME = 0.02f;

        readonly CartographyModule _cartography;
        readonly MyObjectBuilder_Checkpoint.ModItem _modItem;
        readonly GuiHost _gui = GuiHost.PrepareRender(new ScreenSpaceRenderer());
        RoadPlannerView _view;
        VisualTree _visualTree;
        ScreenSpaceInputSource _screenInputSource;
        MinimapSettingsScreen _settingsScreen;

        public bool HasKeyboardFocus =>
            (_visualTree != null && _visualTree.HasKeyboardFocus) ||
            (_view != null && _view.IsMessageBoxOpen) ||
            (_settingsScreen != null && _settingsScreen.IsOpen && _settingsScreen.HasKeyboardFocus);

        public RoadPlannerScreen(
            CartographyModule cartography,
            MyObjectBuilder_Checkpoint.ModItem modItem)
            : base(
                "Minimap",
                new Vector2(0.5f, 0.5f),
                new Vector2(1f, 1f),
                Vector4.Zero)
        {
            _cartography = cartography;
            _modItem = modItem;
            CloseButtonEnabled = false;
            CloseOnEscape = true;
            EnabledBackgroundFade = false;
            PausesGameInSinglePlayer = false;
        }

        bool IRoadPlannerHost.IsFullScreen => true;

        void IRoadPlannerHost.Close()
        {
            Close();
        }

        void IRoadPlannerHost.CloseNow()
        {
            CloseNow();
        }

        void IRoadPlannerHost.OpenSettings()
        {
            if (_settingsScreen != null && _settingsScreen.IsOpen)
                return;
            _settingsScreen = new MinimapSettingsScreen(_view.ViewModel);
            _settingsScreen.Open();
        }

        protected override void OnOpened()
        {
            MinimapStyles.RegisterScreen(_modItem, MinimapLocalConfig.Current.Theme);
            _view = new RoadPlannerView(_cartography, this);
            MinimapStyles.Attach(_view);
            _visualTree = _gui.CreateVisualTree(_view);
            LayoutVisualTree();
            ProceduralCubemapApi.Api.ApiProvider api;
            string error;
            if (!HighwayEngineer.Common.Helpers.RoadApi.TryValidate(out api, out error))
                _view.ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_PushRoad_UnavailableTitle"), error);
        }

        protected override void OnLayoutChanged()
        {
            LayoutVisualTree();
        }

        protected override void OnUpdate(bool hasFocus)
        {
            if (_view == null)
                return;
            _view.UpdateFrame();
            if ((_view.PendingInvalidation & (UiInvalidation.Measure | UiInvalidation.Arrange)) != 0)
                LayoutVisualTree();
        }

        protected override void OnHandleInput()
        {
            var input = MyAPIGateway.Input;
            if (_visualTree != null)
            {
                if (_screenInputSource == null && input != null)
                    _screenInputSource = new ScreenSpaceInputSource(input);
                if (_screenInputSource != null)
                    _screenInputSource.AdvanceFrame();
                _visualTree.HandleInput(_screenInputSource);
            }

            if (input == null || _view == null || HasKeyboardFocus)
                return;
            RoadPlannerViewModel viewModel = _view.ViewModel;

            if (input.IsAnyCtrlKeyPressed())
            {
                // Ctrl+Z undo; Ctrl+Y or Ctrl+Shift+Z redo (road draft only).
                if (input.IsNewKeyPressed(MyKeys.Y) ||
                    input.IsNewKeyPressed(MyKeys.Z) && input.IsAnyShiftKeyPressed())
                    viewModel.RedoRoadEdit();
                else if (input.IsNewKeyPressed(MyKeys.Z))
                    viewModel.UndoRoadEdit();
            }

            MinimapLocalConfig config = MinimapLocalConfig.Current;
            float roll = 0f;
            if (input.IsKeyPress((MyKeys)config.RollLeftKey))
                roll += KEYBOARD_ROLL_RADIANS_PER_FRAME;
            if (input.IsKeyPress((MyKeys)config.RollRightKey))
                roll -= KEYBOARD_ROLL_RADIANS_PER_FRAME;
            if (Math.Abs(roll) > 0.000001f)
                viewModel.RollMap(roll);

            if (input.IsNewKeyPressed((MyKeys)config.ZoomInKey))
                viewModel.ZoomMap(120);
            if (input.IsNewKeyPressed((MyKeys)config.ZoomOutKey))
                viewModel.ZoomMap(-120);
            if (input.IsNewKeyPressed((MyKeys)config.CenterMapKey))
                viewModel.CenterMap();
            if (input.IsNewKeyPressed((MyKeys)config.BridgeRaiseKey))
                viewModel.LiftRoadNode(true);
            if (input.IsNewKeyPressed((MyKeys)config.BridgeLowerKey))
                viewModel.LiftRoadNode(false);
            if (input.IsNewKeyPressed((MyKeys)config.SurfaceViewKey))
                viewModel.ToggleSurfaceView();
        }

        protected override void OnDraw(float transitionAlpha)
        {
            if (_visualTree == null)
                return;
            _view.BeginDraw();
            _visualTree.Draw(transitionAlpha);
            _view.EndDraw();
        }

        protected override void OnClosed(bool isUnloading)
        {
            if (_settingsScreen != null && _settingsScreen.IsOpen)
                _settingsScreen.CloseNow();
            _settingsScreen = null;

            if (_visualTree != null)
                _visualTree.ResetInput();
            if (_view != null)
            {
                _view.Shutdown();
                _view.Dispose();
            }

            _visualTree = null;
            _screenInputSource = null;
            MinimapStyles.Detach(_view);
            _view = null;
        }

        void LayoutVisualTree()
        {
            if (_view == null)
                return;

            Vector2 viewportOffset = Vector2.Zero;
            Vector2 viewportSize = Vector2.Zero;
            if (MyAPIGateway.Session != null && MyAPIGateway.Session.Camera != null)
            {
                viewportOffset = MyAPIGateway.Session.Camera.ViewportOffset;
                viewportSize = MyAPIGateway.Session.Camera.ViewportSize;
            }
            if (viewportSize.X <= 0f || viewportSize.Y <= 0f)
            {
                RectangleF safeArea = MyScreenSpace.GetSafeGuiArea();
                viewportOffset = new Vector2(safeArea.X, safeArea.Y);
                viewportSize = new Vector2(safeArea.Width, safeArea.Height);
            }
            var contentBounds = new RectangleF(viewportOffset, viewportSize);
            _view.ApplyLayout(contentBounds);
            _view.Arrange(contentBounds);
            _view.OnArranged();
        }
    }
}

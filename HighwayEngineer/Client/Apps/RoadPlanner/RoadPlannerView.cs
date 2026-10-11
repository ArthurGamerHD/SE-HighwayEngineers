using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Controls.Chrome;
using Acrux.Controls.Primitives;
using Acrux.Core;
using Acrux.Layout;
using Acrux.Styling;
using HighwayEngineer.Client.Apps.AutoRoute;
using HighwayEngineer.Client.Apps.BridgeSettings;
using HighwayEngineer.Client.Apps.PushRoad;
using HighwayEngineer.Client.Apps.Repair;
using HighwayEngineer.Client.Apps.RoadSettings;
using HighwayEngineer.Client.Gui.Styling;
using HighwayEngineer.Client.Gui.UserControls;
using HighwayEngineer.Client.Networking;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    /// <summary>
    /// The road planner (map, road tools, layers), shared by the full-screen map
    /// and the LCD script. The host arranges it, calls <see cref="UpdateFrame"/>
    /// every update and brackets drawing with <see cref="BeginDraw"/>/<see cref="EndDraw"/>.
    /// </summary>
    internal sealed partial class RoadPlannerView : UserControl
    {
        const float ICON_MARGIN_PIXELS = 12f;
        const float ICON_GAP_PIXELS = 8f;
        const float ICON_SIZE_PIXELS = 44f;
        const float TOP_ROWS = 6f;
        const float BOTTOM_ROWS = 3f;

        readonly IRoadPlannerHost _host;
        readonly CaptionButton _captionCloseButton;
        readonly RoadSettingsPanel _roadSettings;
        readonly BridgeSettingsPanel _bridgeSettings;
        readonly Popup _settingsFlyout;
        readonly MapContextMenu _signalContextMenu;
        readonly MessageBox _messageBox;
        readonly PushRoadDialogViewModel _pushDialog = new PushRoadDialogViewModel();
        readonly Popup _pushDialogPopup;
        readonly RepairDialogViewModel _repairDialog = new RepairDialogViewModel();
        readonly RepairDialog _repairDialogView;
        readonly Popup _repairDialogPopup;
        PlanetWaypointMarker _selectedWaypoint;
        PlanetTradeStationMarker _selectedTradeStation;
        Color _roadActionAccent;
        // The tool the road action buttons' tooltips were last set for.
        string _roadActionsTool;
        RectangleF _contentBounds;
        RectangleF _mapBounds;
        float _mainHeight;
        float _edge;
        float _gap;
        bool _shutDown;
        const int NOTICE_FRAMES = 240;
        string _noticeShown;
        int _noticeVersion = -1;
        int _noticeHideFrame;
        MapOverlayFrame _drawnOverlays;
        bool _drawnMarkerVisible;
        Vector2 _drawnMarkerPosition;
        float _drawnMarkerRotation;

        public RoadPlannerView(CartographyModule cartography, IRoadPlannerHost host)
        {
            _host = host;
            ViewModel = new RoadPlannerViewModel(
                cartography,
                host.Close,
                host.CloseNow,
                host.OpenSettings);
            ViewModel.MessageBoxRequested = ShowMessageBox;
            DataContext = ViewModel;
            InitializeComponent();
            // The backdrop under the invoice and repair dialogs takes every pointer event (press,
            // drag, hover, wheel) itself, so the map and its controls cannot be used, nor the draft
            // changed, until the dialog closes.
            _dialogBackdrop.Interactive = true;
            if (host.IsFullScreen)
                _root.StyleClass = "planner-backdrop";
            else
            {
                // The footer's fixed part (planet, layer, node count) is screen-only;
                // its changing part becomes the notice.
                _status.Visible = false;
                _noticeLayer.Visible = true;
            }

            RoadPlannerViewModel viewModel = ViewModel;
            _mapView.DraggedAt = viewModel.DragRoadOrOrbit;
            _mapView.DragStarted = viewModel.BeginRoadDrag;
            _mapView.DragEnded = viewModel.EndRoadDrag;
            _mapView.Clicked = viewModel.CreatePlanetSurfaceWaypoint;
            _mapView.RightClicked = viewModel.EditRoadNode;
            _mapView.Scrolled = viewModel.ZoomMap;
            _mapView.MiddleDragged = viewModel.MiddleDragMap;

            _planetWaypoints.MarkersProvider = () => viewModel.PresentedOverlays.Nodes;
            _planetWaypoints.GpsMarkersProvider = () => viewModel.PresentedOverlays.Gps;
            _planetWaypoints.GpsClicked = OpenGpsFlyout;
            _planetWaypoints.RoadSegmentLabelsProvider = () => viewModel.PresentedOverlays.Labels;
            _planetWaypoints.BridgeTogglesProvider = () => viewModel.PresentedOverlays.BridgeToggles;
            _planetWaypoints.BridgeToggleClicked = viewModel.ToggleBridgeSegment;
            _planetWaypoints.TradeStationsProvider = () => viewModel.PresentedOverlays.Stations;
            _planetWaypoints.DepotsProvider = () => viewModel.PresentedOverlays.Depots;
            _planetWaypoints.DepotHovered = viewModel.SetHoveredDepot;
            _planetWaypoints.PlayerMarkerProvider = viewModel.GetPlanetPlayerMarker;
            _planetWaypoints.WaypointClicked = (marker, point) => viewModel.SelectRoadNode(marker.Hash - 1);
            _planetWaypoints.WaypointRightClicked = (marker, point) => viewModel.RemoveRoadNode(marker.Hash - 1);
            _planetWaypoints.RoadDragStarted = viewModel.BeginRoadNodeDrag;
            _planetWaypoints.RoadScrolled = viewModel.ScrollDraggedNode;
            _planetWaypoints.RoadDraggedAt = viewModel.DragRoadOrOrbit;
            _planetWaypoints.RoadDragEnded = viewModel.EndRoadDrag;
            _planetWaypoints.TradeStationClicked = OpenTradeStationFlyout;
            _tangentOverlay.TangentProvider = () => viewModel.PresentedOverlays.Tangents;
            _cursorBadge.IconProvider = () => viewModel.BulldozeMode ? "HighwayEngineer_Icon_Bulldoze"
                : viewModel.RepairMode ? "HighwayEngineer_Icon_Roller" : null;
            _cursorBadge.PointerProvider = () => _mapView.IsPointerOver ? (Vector2?)_mapView.HoverPosition : null;

            _captionCloseButton = _captionCloseSlot.Add(new CaptionButton(CaptionGlyph.Close, "close"));
            _captionCloseButton.Bind<RoadPlannerViewModel, Action>(source => source.CloseClicked,
                value => _captionCloseButton.Clicked = value, nameof(RoadPlannerViewModel.CloseClicked));

            _roadsButton.Visible = viewModel.UpdateRoadsToggle();

            _streetViewControl.DragCompleted = viewModel.SpawnStreetView;
            // The road settings, with the settings of the draft's bridges under them.
            _roadSettings = new RoadSettingsPanel(viewModel.RoadSettings);
            _bridgeSettings = new BridgeSettingsPanel(viewModel.BridgeSettings);
            var settingsPanels = new StackPanel { Spacing = 6f };
            settingsPanels.Add(_roadSettings);
            settingsPanels.Add(_bridgeSettings);
            _settingsFlyout = new Popup
            {
                Child = settingsPanels,
                Placement = PlacementMode.LeftEdgeAlignedTop,
                IsLightDismissEnabled = true,
                HorizontalOffset = -8f
            };
            _settingsButton.Flyout = _settingsFlyout;
            _settingsButton.SelectedProvider = () => _settingsButton.Flyout != null && _settingsButton.Flyout.IsOpen;

            _confirmRoadGlyph.ColorProvider = () => MinimapTheme.UsesLightTextShade(_roadActionAccent)
                ? Color.White
                : new Color(8, 10, 12);
            _cancelRoadGlyph.Cancel = true;
            _autoRoadGlyph.Auto = true;
            _pushRoadButton.Clicked = ConfirmRoadAction;
            _cancelRoadButton.Clicked = DiscardRoadAction;

            _autoRoadButton.Flyout = new Popup
            {
                Child = new AutoRoutePanel(viewModel.AutoRoutePanel),
                Placement = PlacementMode.LeftEdgeAlignedTop,
                IsLightDismissEnabled = true,
                HorizontalOffset = -8f
            };
            _autoRoadButton.Flyout.Opened = () => ViewModel.BeginAutoRoute();


            // Added last so the popups draw and hit-test above every map control.
            _signalContextMenu = _root.Add(new MapContextMenu(LocHelper.GetLoc("HighwayEngineer_Menu_Hide"),
                LocHelper.GetLoc("HighwayEngineer_Menu_Details"), LocHelper.GetLoc("HighwayEngineer_Menu_Delete"))
            {
                ItemClicked = ActivateSignalFlyoutItem
            });
            _signalContextMenu.SetItemForeground(2, new Color(255, 95, 95));
            _messageBox = _root.Add(new MessageBox());
            // Not light-dismissable: clicks outside are swallowed, so the draft cannot
            // change under the dialog. Escape closes it like Cancel.
            _pushDialogPopup = _root.Add(new Popup
            {
                Child = new PushRoadDialog(_pushDialog),
                Placement = PlacementMode.Center,
                IsLightDismissEnabled = false
            });
            _pushDialogPopup.Closed = () =>
            {
                _pushDialog.Dismiss();
                SetVisible(_dialogBackdrop, false);
            };
            _pushDialog.Closed = push => _pushDialogPopup.Close();
            _repairDialogView = new RepairDialog(_repairDialog);
            _repairDialogPopup = _root.Add(new Popup
            {
                Child = _repairDialogView,
                Placement = PlacementMode.Center,
                IsLightDismissEnabled = false
            });
            _repairDialogPopup.Closed = () => SetVisible(_dialogBackdrop, false);
            _repairDialog.Closed = repair =>
            {
                _repairDialogPopup.Close();
                if (repair && !_shutDown)
                    QuoteRepair();
            };
            _pushDialog.Accepted = contract =>
            {
                if (!_shutDown)
                    ViewModel.RoadContractAccepted(contract);
            };

            ApplyThemeChrome();
            viewModel.Open();
        }

        public RoadPlannerViewModel ViewModel { get; private set; }

        public bool IsMessageBoxOpen => _messageBox != null && _messageBox.IsOpen ||
                                        _pushDialogPopup != null && _pushDialogPopup.IsOpen ||
                                        _repairDialogPopup != null && _repairDialogPopup.IsOpen;

        /// <summary>Sizes rows, columns and icons for <paramref name="bounds"/>; call before arranging there.</summary>
        public void ApplyLayout(RectangleF bounds)
        {
            _contentBounds = bounds;
            float shortSide = Math.Min(bounds.Width, bounds.Height);
            float scale = Math.Max(0.75f, shortSide / 700f);
            _edge = ICON_MARGIN_PIXELS * scale;
            _gap = ICON_GAP_PIXELS * scale;
            float iconSize = ICON_SIZE_PIXELS * scale;
            float selectorHeight = 24f * scale;
            float selectorWidth = 232f * scale;
            float footerHeight = _host.IsFullScreen ? 16f * scale : 0f;
            _mainHeight = Math.Max(1f, bounds.Height - footerHeight);

            // Top right: close, settings, then the pill of toggles (roads, GPS, auto-bridge, follow): six rows, two gaps.
            float topControlsHeight = iconSize * TOP_ROWS + _gap * 2f + _edge;
            // Nine rows of 32 (scaled); the bridge and auto-road panels have four of the same.
            float roadSettingsHeight = 288f * scale;
            // Bottom right: the pill of road tools (bulldoze, repair), then north lock: three rows, one gap.
            float bottomControlsHeight = iconSize * BOTTOM_ROWS + _gap + _edge;
            topControlsHeight = Math.Min(topControlsHeight, _mainHeight * 0.4f);
            bottomControlsHeight = Math.Min(bottomControlsHeight, _mainHeight * 0.6f);
            iconSize = Math.Min(iconSize, Math.Max(1f, Math.Min(
                (topControlsHeight - _edge - _gap * 2f) / TOP_ROWS,
                (bottomControlsHeight - _edge - _gap) / BOTTOM_ROWS)));
            float middleHeight = Math.Max(1f, _mainHeight - topControlsHeight - bottomControlsHeight);
            float centerWidth = Math.Min(bounds.Width * 0.48f, selectorWidth);
            float sideWidth = Math.Max(1f, (bounds.Width - centerWidth) * 0.5f);

            _screenGrid.SetRows(_mainHeight, footerHeight);
            _mapGrid.SetColumns(sideWidth, centerWidth, sideWidth);
            _mapGrid.SetRows(topControlsHeight, middleHeight, bottomControlsHeight);

            _title.Height = selectorHeight;
            _titlePanel.Margin = new Vector4(_edge, _edge, 0f, 0f);
            ConfigureButtonStack(_topRightControls, iconSize, _gap, _edge, true);
            ConfigureButtonStack(_bottomLeftControls, iconSize, _gap, _edge, false);
            ConfigureButtonStack(_bottomRightControls, iconSize, _gap, _edge, true);
            _layerControls.Width = null;
            // Each layer button carries the gap after it (MapLayerItem.Margin), water included.
            _layerControls.Spacing = 0f;
            _waterButton.Width = _waterButton.Height = iconSize;
            SizeIcon(_waterButton, iconSize);
            _roadSettings.ApplyScale(Math.Max(1f, Math.Min(330f * scale, sideWidth - _edge * 2f)),
                roadSettingsHeight, scale);
            _bridgeSettings.ApplyScale(Math.Max(1f, Math.Min(330f * scale, sideWidth - _edge * 2f)),
                roadSettingsHeight * 4f / 9f, scale);
            _pushDialog.ApplyScale(Math.Max(1f, Math.Min(560f * scale, bounds.Width - _edge * 4f)), scale);
            _repairDialogView.ApplyScale(Math.Max(1f, Math.Min(420f * scale, bounds.Width - _edge * 4f)), scale);
            ViewModel.AutoRoutePanel.ApplyScale(Math.Max(1f, Math.Min(330f * scale, sideWidth - _edge * 2f)),
                roadSettingsHeight * 4f / 9f, scale);
            ViewModel.SetLayerButtonSize(iconSize, _gap);
        }

        /// <summary>Call after the host arranged the view at the bounds given to <see cref="ApplyLayout"/>.</summary>
        public void OnArranged()
        {
            SyncMapBounds();
            UpdateRoadActionButtons();
        }

        public void UpdateFrame()
        {
            if (_shutDown)
                return;
            if (MyAPIGateway.Input != null)
            {
                bool shift = MyAPIGateway.Input.IsAnyShiftKeyPressed();
                ViewModel.RoadSettings.UpdateModifiers(MyAPIGateway.Input.IsAnyCtrlKeyPressed(), shift);
                ViewModel.MarkRangeModifier = shift;
            }
            // Light dismiss may close the flyout without raising Closed, so poll it.
            if (ViewModel.AutoRouteActive && !_autoRoadButton.Flyout.IsOpen)
                ViewModel.EndAutoRoute();
            ViewModel.Update();
            _pushRoadButton.Enabled = ViewModel.CanPushRoad;
            UpdateLayerPreviews(_layerItems);
            ApplyThemeChrome();
            SyncMapBounds();
            UpdateRoadActionButtons();
            _cursorBadge.Refresh();
            if (!_host.IsFullScreen)
                UpdateNotice();
        }

        /// <summary>
        /// Shows the editor status for a few seconds whenever it changes; while a push or
        /// bulldoze is waiting for the voxel API it stays up until the API answers.
        /// </summary>
        void UpdateNotice()
        {
            int frame = Sandbox.ModAPI.MyAPIGateway.Session == null
                ? 0
                : Sandbox.ModAPI.MyAPIGateway.Session.GameplayFrameCounter;
            string text = ViewModel.StatusNotice;
            int version = ViewModel.StatusNoticeVersion;
            bool pending = ViewModel.IsPushPending;
            if (!string.Equals(text, _noticeShown, StringComparison.Ordinal) || version != _noticeVersion)
            {
                _noticeShown = text;
                _noticeVersion = version;
                _noticeText.Text = text;
                _noticeHideFrame = frame + NOTICE_FRAMES;
                SetVisible(_notice, !string.IsNullOrEmpty(text));
            }
            if (pending)
                _noticeHideFrame = frame + NOTICE_FRAMES;
            else if (_notice.Visible && frame >= _noticeHideFrame)
                _notice.Visible = false;
        }

        public void BeginDraw()
        {
            if (_shutDown)
                return;
            ViewModel.BeginMapDraw();
            InvalidateChangedOverlays();
            UpdateRoadActionButtons();
        }

        /// <summary>
        /// The overlay controls read the presented frame through providers, which change no
        /// property. The screen redraws every frame anyway, but an LCD reuses its last frame
        /// unless the tree is invalidated, so a new frame (zoom, pan) or a moved player marker
        /// must invalidate them.
        /// </summary>
        void InvalidateChangedOverlays()
        {
            MapOverlayFrame overlays = ViewModel.PresentedOverlays;
            PlanetPlayerMarker marker = ViewModel.GetPlanetPlayerMarker();
            if (ReferenceEquals(overlays, _drawnOverlays) &&
                marker.Visible == _drawnMarkerVisible &&
                (!marker.Visible || marker.ScreenPosition == _drawnMarkerPosition &&
                 marker.Rotation == _drawnMarkerRotation))
                return;
            _drawnOverlays = overlays;
            _drawnMarkerVisible = marker.Visible;
            _drawnMarkerPosition = marker.ScreenPosition;
            _drawnMarkerRotation = marker.Rotation;
            const Acrux.Core.Invalidation.UiInvalidation render = Acrux.Core.Invalidation.UiInvalidation.Render;
            _planetWaypoints.Invalidate(render);
            _tangentOverlay.Invalidate(render);
        }

        public void EndDraw()
        {
            if (!_shutDown)
                ViewModel.EndMapDraw();
        }

        /// <summary>Releases the view model and the flyout; the host disposes the visual tree.</summary>
        public void Shutdown()
        {
            if (_shutDown)
                return;
            _shutDown = true;
            _settingsButton.Flyout = null;
            _settingsFlyout.Close();
            _settingsFlyout.Child = null;
            _roadSettings.Dispose();
            _bridgeSettings.Dispose();
            _settingsFlyout.Dispose();
            ViewModel.MessageBoxRequested = null;
            ViewModel.Dispose();
        }

        void SyncMapBounds()
        {
            RectangleF bounds = _mapView.Bounds;
            if (bounds.Width <= 0f || bounds.Height <= 0f || bounds == _mapBounds)
                return;
            _mapBounds = bounds;
            ViewModel.SetMapBounds(bounds);
        }

        void ApplyThemeChrome()
        {
            bool fullScreen = _host.IsFullScreen;
            bool spaceEngineers = MinimapStyles.IsSpaceEngineersStyle;
            SetVisible(_closeButton, fullScreen && spaceEngineers);
            SetVisible(_captionCloseSlot, fullScreen && !spaceEngineers);
        }

        void UpdateRoadActionButtons()
        {
            SetVisible(_roadsButton, ViewModel.UpdateRoadsToggle());
            // Build mode anchors on the draft's last node; bulldoze and repair on the
            // last built road clicked. The same pair of buttons serves all three.
            bool marking = ViewModel.MarkMode;
            string tool = ViewModel.BulldozeMode ? "bulldoze" : ViewModel.RepairMode ? "repair" : "build";
            if (_roadActionsTool != tool)
            {
                _roadActionsTool = tool;
                ToolTip.SetTip(_pushRoadButton, LocHelper.GetLoc(tool == "bulldoze" ? "HighwayEngineer_Planner_Tip_ConfirmBulldoze"
                    : tool == "repair" ? "HighwayEngineer_Planner_Tip_ConfirmRepair" : "HighwayEngineer_Planner_Tip_Push"));
                ToolTip.SetTip(_cancelRoadButton, LocHelper.GetLoc(tool == "bulldoze" ? "HighwayEngineer_Planner_Tip_DiscardBulldoze"
                    : tool == "repair" ? "HighwayEngineer_Planner_Tip_DiscardRepair" : "HighwayEngineer_Planner_Tip_Cancel"));
            }
            MapOverlayFrame overlays = ViewModel.PresentedOverlays;
            Vector2 anchor = Vector2.Zero;
            bool hasAnchor = false;
            if (marking)
            {
                if (overlays.MarkAnchor.HasValue)
                {
                    anchor = overlays.MarkAnchor.Value;
                    hasAnchor = true;
                }
            }
            else
            {
                var markers = overlays.Nodes;
                for (int i = 0; i < markers.Count; i++)
                    if (markers[i].Hash == overlays.NodeCount)
                    {
                        anchor = markers[i].ScreenPosition;
                        hasAnchor = true;
                    }
            }
            RectangleF map = _mapView.Bounds;
            bool visible = hasAnchor && map.Contains(anchor);
            bool auto = visible && ViewModel.CanAutoRoute;
            SetVisible(_pushRoadButton, visible);
            SetVisible(_cancelRoadButton, visible);
            SetVisible(_autoRoadButton, auto);
            if (!visible)
                return;

            _pushRoadButton.Enabled = marking ? ViewModel.CanConfirmMarks : ViewModel.CanPushRoad;
            _cancelRoadButton.Enabled = marking || ViewModel.CanCancelRoad;
            Color accent = MinimapTheme.FactionAccent;
            if (_roadActionAccent != accent)
            {
                _roadActionAccent = accent;
                _pushRoadButton.Background = new SolidColorBrush(accent);
            }
            float scale = Math.Max(.75f, Math.Min(map.Width, map.Height) / 700f);
            float size = 28f * scale;
            float gap = 4f * scale;
            // Actions sit left of the final node; its label stays on the right.
            float x = Math.Max(map.X + gap, anchor.X - size - 20f * scale);
            int buttons = auto ? 3 : 2;
            float y = Math.Max(map.Y + gap, Math.Min(anchor.Y - size * .5f, map.Bottom - (size + gap) * buttons));
            _pushRoadButton.Width = _cancelRoadButton.Width = _autoRoadButton.Width = size;
            _pushRoadButton.Height = _cancelRoadButton.Height = _autoRoadButton.Height = size;
            _confirmRoadGlyph.Width = _cancelRoadGlyph.Width = _autoRoadGlyph.Width = size * .8f;
            _confirmRoadGlyph.Height = _cancelRoadGlyph.Height = _autoRoadGlyph.Height = size * .8f;
            _roadActions.Place(_pushRoadButton, new RectangleF(x, y, size, size));
            _roadActions.Place(_cancelRoadButton, new RectangleF(x, y + size + gap, size, size));
            if (auto)
                _roadActions.Place(_autoRoadButton, new RectangleF(x, y + (size + gap) * 2f, size, size));
        }

        /// <summary>Paints the shown layer buttons' previews (their cubemaps arrive through the bindings).</summary>
        static void UpdateLayerPreviews(VisualElement element)
        {
            if (element == null || !element.Visible)
                return;
            var preview = element as LayerPreview;
            if (preview != null)
            {
                preview.UpdatePreviewTexture();
                return;
            }
            IReadOnlyList<VisualElement> children = element.VisualChildren;
            for (int i = 0; i < children.Count; i++)
                UpdateLayerPreviews(children[i]);
        }

        static void SetVisible(VisualElement control, bool visible)
        {
            if (control.Visible != visible)
                control.Visible = visible;
        }

        static void ConfigureButtonStack(StackPanel panel, float itemSize, float gap, float edge, bool alignRight)
        {
            IReadOnlyList<VisualElement> children = panel.VisualChildren;
            for (int i = 0; i < children.Count; i++)
            {
                var group = children[i] as Border;
                if (group != null && group.Child is StackPanel)
                {
                    ConfigureButtonGroup(group, itemSize);
                    continue;
                }
                children[i].Width = itemSize;
                children[i].Height = itemSize;
                SizeIcon(children[i] as Button, itemSize);
            }
            panel.Spacing = gap;
            bool alignBottom = panel.VerticalAlignment == VerticalAlignment.Bottom;
            panel.Margin = new Vector4(
                alignRight ? 0f : edge,
                alignBottom ? 0f : edge,
                alignRight ? edge : 0f,
                alignBottom ? edge : 0f);
        }

        /// <summary>
        /// A pill of buttons (Border.map-multi-button around a StackPanel), as on the Minimap's planetary map: as wide
        /// as a lone button, its buttons stacked without gaps inside its 1 px border and padding, sized as the LCD mod's.
        /// </summary>
        static void ConfigureButtonGroup(Border group, float itemSize)
        {
            const float inset = 2f;
            float buttonSize = Math.Max(1f, itemSize - inset * 2f);
            group.Width = itemSize;
            group.Height = null;
            group.Padding = new Vector4(inset - 1f);
            group.CornerRadius = new Vector4(itemSize * 0.5f);
            var buttons = (StackPanel)group.Child;
            buttons.Spacing = 0f;
            IReadOnlyList<VisualElement> children = buttons.VisualChildren;
            // As the LCD mod's tiny buttons: a circle, its icon inset 9 px of a 42 px pill.
            float iconSize = Math.Max(1f, buttonSize - 2f * 9f * itemSize / 42f);
            for (int i = 0; i < children.Count; i++)
            {
                children[i].Width = children[i].Height = buttonSize;
                var button = children[i] as Button;
                if (button == null)
                    continue;
                button.CornerRadius = new Vector4(buttonSize * 0.5f);
                var icon = button.Content as Border;
                if (icon == null)
                    continue;
                icon.Width = icon.Height = iconSize;
                icon.HorizontalAlignment = HorizontalAlignment.Center;
                icon.VerticalAlignment = VerticalAlignment.Center;
            }
        }

        /// <summary>
        /// Icon masks are sized explicitly to 75% of their button (as Button icons were):
        /// a size-less mask would shrink to nothing in a theme's shrink-to-fit content box.
        /// </summary>
        static void SizeIcon(Button button, float buttonSize)
        {
            var icon = button == null ? null : button.Content as Border;
            if (icon == null)
                return;
            icon.Width = icon.Height = buttonSize * 0.75f;
            icon.HorizontalAlignment = HorizontalAlignment.Center;
            icon.VerticalAlignment = VerticalAlignment.Center;
        }

        void OpenTradeStationFlyout(PlanetTradeStationMarker marker, Vector2 point)
        {
            if (marker == null)
                return;
            _selectedWaypoint = null;
            _selectedTradeStation = marker;
            OpenSignalFlyout(point, marker.GpsHashes, marker.AnyGpsShown, true);
        }

        void OpenGpsFlyout(PlanetWaypointMarker marker, Vector2 point)
        {
            if (marker == null)
                return;
            _selectedTradeStation = null;
            _selectedWaypoint = marker;
            OpenSignalFlyout(point, marker.GpsHashes, marker.AnyGpsShown, false);
        }

        void OpenSignalFlyout(Vector2 point, IReadOnlyList<int> gpsHashes, bool anyGpsShown, bool hasStationDetails)
        {
            bool hasGps = gpsHashes != null && gpsHashes.Count > 0;
            _signalContextMenu.SetItemText(0, LocHelper.GetLoc(anyGpsShown ? "HighwayEngineer_Menu_Hide" : "HighwayEngineer_Menu_Show"));
            _signalContextMenu.SetItemVisible(0, hasGps);
            _signalContextMenu.SetItemVisible(1, hasStationDetails);
            _signalContextMenu.SetItemVisible(2, hasGps);
            _signalContextMenu.Open(point);
        }

        void ActivateSignalFlyoutItem(int itemIndex)
        {
            IReadOnlyList<int> hashes = _selectedTradeStation != null
                ? _selectedTradeStation.GpsHashes
                : (_selectedWaypoint == null ? null : _selectedWaypoint.GpsHashes);
            switch (itemIndex)
            {
                case 0:
                    bool anyShown = _selectedTradeStation != null
                        ? _selectedTradeStation.AnyGpsShown
                        : _selectedWaypoint != null && _selectedWaypoint.AnyGpsShown;
                    ViewModel.SetGpsVisibility(hashes, !anyShown);
                    break;
                case 1:
                    ViewModel.ShowStationDetails(_selectedTradeStation);
                    break;
                case 2:
                    ViewModel.DeleteGps(hashes);
                    break;
            }
            _selectedWaypoint = null;
            _selectedTradeStation = null;
        }

        public void ShowMessageBox(string title, string description)
        {
            _messageBox.Show(LocHelper.Resolve(title), LocHelper.Resolve(description),
                LocHelper.GetLoc("HighwayEngineer_Common_Close"), Acrux.Controls.MessageBoxResult.Close);
        }

        void ConfirmRoadAction()
        {
            if (ViewModel.BulldozeMode)
                ConfirmBulldoze();
            else if (ViewModel.RepairMode)
                OpenRepairDialog();
            else
                ConfirmPushRoad();
        }

        void DiscardRoadAction()
        {
            if (ViewModel.MarkMode)
                ViewModel.DiscardMarks();
            else
                ViewModel.CancelRoad();
        }

        /// <summary>
        /// Asks the server what bulldozing the marks refunds or costs, then asks the player; the
        /// server does the removal (roads through the voxel API, bridges' voxel maps).
        /// </summary>
        void ConfirmBulldoze()
        {
            if (!ViewModel.CanConfirmBulldoze)
                return;
            RoadPlannerViewModel viewModel = ViewModel;
            viewModel.QuoteBulldoze(quote =>
            {
                if (_shutDown) return;
                if (quote == null)
                {
                    ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_Bulldoze_UnavailableTitle"), LocHelper.GetLoc("HighwayEngineer_Bulldoze_UnavailableBody"));
                    return;
                }
                if (quote.Reason != HighwayEngineer.Common.Roads.RoadRejectReason.None)
                {
                    ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_Bulldoze_CannotTitle"), RoadRejectReasonText.Describe(
                        quote.Reason, quote.Message, quote.RetryAfterSeconds));
                    return;
                }
                string price = quote.Exempt || quote.CreditDelta == 0
                    ? LocHelper.GetLoc("HighwayEngineer_Bulldoze_PriceFree")
                    : quote.CreditDelta > 0
                        ? LocHelper.Format("HighwayEngineer_Bulldoze_PriceRefund", quote.CreditDelta)
                        : LocHelper.Format("HighwayEngineer_Bulldoze_PriceCost", -quote.CreditDelta);
                string description = LocHelper.Format("HighwayEngineer_Bulldoze_ConfirmBody", LocHelper.Resolve(quote.Message), price);
                _messageBox.Show(LocHelper.GetLoc("HighwayEngineer_Bulldoze_ConfirmTitle"), description,
                    LocHelper.GetLoc("HighwayEngineer_Bulldoze_ConfirmButton"), Acrux.Controls.MessageBoxResult.Ok,
                    LocHelper.GetLoc("HighwayEngineer_Common_Cancel"), Acrux.Controls.MessageBoxResult.Cancel,
                    closed: result =>
                    {
                        if (result == Acrux.Controls.MessageBoxResult.Ok && !_shutDown)
                            viewModel.PushBulldoze(quote.QuoteId);
                    });
            });
        }

        void OpenRepairDialog()
        {
            if (!ViewModel.CanConfirmRepair)
                return;
            _repairDialog.Show(ViewModel.RoadMarkCount, MinimapTheme.FactionAccent);
            _repairDialogPopup.PlacementTarget = _root;
            _repairDialogPopup.Open();
            SetVisible(_dialogBackdrop, true);
        }

        /// <summary>
        /// Asks the server to check the repair chosen in the dialog, then asks the player; the
        /// server repaves (voxel API), fills holes (every machine) and rebuilds bridges.
        /// </summary>
        void QuoteRepair()
        {
            if (!ViewModel.CanConfirmRepair)
                return;
            RoadPlannerViewModel viewModel = ViewModel;
            viewModel.QuoteRepair(_repairDialog.ChosenMaterial, _repairDialog.RepairHoles,
                _repairDialog.RepaintGuideLines, quote =>
            {
                if (_shutDown) return;
                if (quote == null)
                {
                    ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_Repair_UnavailableTitle"), LocHelper.GetLoc("HighwayEngineer_Repair_UnavailableBody"));
                    return;
                }
                if (quote.Reason != HighwayEngineer.Common.Roads.RoadRejectReason.None)
                {
                    ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_Repair_CannotTitle"), RoadRejectReasonText.Describe(
                        quote.Reason, quote.Message, quote.RetryAfterSeconds));
                    return;
                }
                string description = LocHelper.Format("HighwayEngineer_Repair_ConfirmBody", LocHelper.Resolve(quote.Message));
                _messageBox.Show(LocHelper.GetLoc("HighwayEngineer_Repair_ConfirmTitle"), description,
                    LocHelper.GetLoc("HighwayEngineer_Repair_Confirm"), Acrux.Controls.MessageBoxResult.Ok,
                    LocHelper.GetLoc("HighwayEngineer_Common_Cancel"), Acrux.Controls.MessageBoxResult.Cancel,
                    closed: result =>
                    {
                        if (result == Acrux.Controls.MessageBoxResult.Ok && !_shutDown)
                            viewModel.PushRepair(quote.QuoteId);
                    });
            });
        }

        void ConfirmPushRoad()
        {
            // Bridges are voxel maps of their own; only roads need the voxel API.
            ProceduralCubemapApi.Api.ApiProvider api;
            string error;
            if (ViewModel.DraftHasRoads && !HighwayEngineer.Common.Helpers.RoadApi.TryValidate(out api, out error))
            {
                ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_PushRoad_UnavailableTitle"), error);
                return;
            }
            if (!ViewModel.CanPushRoad)
            {
                ShowMessageBox(LocHelper.GetLoc("HighwayEngineer_PushRoad_CannotTitle"), ViewModel.RoadParameterError ??
                    ViewModel.BridgeError ?? LocHelper.GetLoc("HighwayEngineer_PushRoad_CannotBody"));
                return;
            }

            _pushDialog.Show(ViewModel, MinimapTheme.FactionAccent);
            _pushDialogPopup.PlacementTarget = _root;
            _pushDialogPopup.Open();
            SetVisible(_dialogBackdrop, true);
        }
    }
}

using Adk.Mvvm;
using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using System.Globalization;
using Acrux.Core.Binding;
using Acrux.Core.Input;
using Acrux.ScreenSpace;
using Jakaria.API;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Client.Apps.AutoRoute;
using HighwayEngineer.Client.Apps.BridgeSettings;
using HighwayEngineer.Client.Apps.RoadSettings;
using HighwayEngineer.Client.Gui;
using HighwayEngineer.Client.Gui.Styling;
using HighwayEngineer.Client.Gui.UserControls;
using HighwayEngineer.Client.Helpers;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Client.Maps;
using HighwayEngineer.Client.Networking;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using Sandbox.Engine.Utils;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    /// <summary>
    /// Stable, control-agnostic state for the map screen. Controls bind their own
    /// properties to these raw observable values.
    /// </summary>
    internal sealed partial class RoadPlannerViewModel : ObservableObject, IDisposable
    {
        string _titleText;
        Color _titleColor = Color.White;
        string _footerText;
        Color _footerColor = Color.White;

        string _closeIcon;
        Action _closeClicked;
        string _settingsIcon;
        Action _settingsClicked;

        string _orientationIcon;
        bool _orientationSelected;
        float _orientationRotation;
        bool _orientationVisible = true;
        Action _orientationClicked;

        string _followIcon;
        bool _followSelected;
        bool _followVisible = true;
        Action _followClicked;

        string _waypointsIcon;
        bool _waypointsSelected;
        bool _waypointsControlVisible = true;
        Action _waypointsClicked;

        string _waterIcon;
        bool _waterSelected;
        bool _waterVisible;
        bool _waterEnabled = true;
        Action _waterClicked;
        IMyGeneratedTexture _mapTextureValue;
        bool _planetWaypointsVisible = true;
        bool _streetViewVisible = true;
        bool _streetViewEnabled = true;

        [PreviewWith("ROAD PLANNER")]
        public string TitleText
        {
            get { return _titleText; }
            set { RaiseAndSetIfChanged(ref _titleText, value, nameof(TitleText)); }
        }

        [PreviewWith("#FF55CDEB")]
        public Color TitleColor
        {
            get { return _titleColor; }
            set { RaiseAndSetIfChanged(ref _titleColor, value, nameof(TitleColor)); }
        }

        [PreviewWith("EarthLike  Satellite  4 nodes")]
        public string FooterText
        {
            get { return _footerText; }
            set { RaiseAndSetIfChanged(ref _footerText, value, nameof(FooterText)); }
        }

        [PreviewWith("#FFB4C3CA")]
        public Color FooterColor
        {
            get { return _footerColor; }
            set { RaiseAndSetIfChanged(ref _footerColor, value, nameof(FooterColor)); }
        }

        [PreviewWith("HighwayEngineer_Icon_Cross")]
        public string CloseIcon
        {
            get { return _closeIcon; }
            set { RaiseAndSetIfChanged(ref _closeIcon, value, nameof(CloseIcon)); }
        }

        public Action CloseClicked
        {
            get { return _closeClicked; }
            set { RaiseAndSetIfChanged(ref _closeClicked, value, nameof(CloseClicked)); }
        }

        [PreviewWith("HighwayEngineer_Icon_Settings")]
        public string SettingsIcon
        {
            get { return _settingsIcon; }
            set { RaiseAndSetIfChanged(ref _settingsIcon, value, nameof(SettingsIcon)); }
        }

        public Action SettingsClicked
        {
            get { return _settingsClicked; }
            set { RaiseAndSetIfChanged(ref _settingsClicked, value, nameof(SettingsClicked)); }
        }

        [PreviewWith("HighwayEngineer_Icon_CompassIcon")]
        public string OrientationIcon
        {
            get { return _orientationIcon; }
            set { RaiseAndSetIfChanged(ref _orientationIcon, value, nameof(OrientationIcon)); }
        }

        public bool OrientationSelected
        {
            get { return _orientationSelected; }
            set { RaiseAndSetIfChanged(ref _orientationSelected, value, nameof(OrientationSelected)); }
        }

        public float OrientationRotation
        {
            get { return _orientationRotation; }
            set { RaiseAndSetIfChanged(ref _orientationRotation, value, nameof(OrientationRotation)); }
        }

        [PreviewWith(true)]
        public bool OrientationVisible
        {
            get { return _orientationVisible; }
            set { RaiseAndSetIfChanged(ref _orientationVisible, value, nameof(OrientationVisible)); }
        }

        public Action OrientationClicked
        {
            get { return _orientationClicked; }
            set { RaiseAndSetIfChanged(ref _orientationClicked, value, nameof(OrientationClicked)); }
        }

        [PreviewWith("HighwayEngineer_Icon_Lock")]
        public string FollowIcon
        {
            get { return _followIcon; }
            set { RaiseAndSetIfChanged(ref _followIcon, value, nameof(FollowIcon)); }
        }

        [PreviewWith(true)]
        public bool FollowSelected
        {
            get { return _followSelected; }
            set { RaiseAndSetIfChanged(ref _followSelected, value, nameof(FollowSelected)); }
        }

        [PreviewWith(true)]
        public bool FollowVisible
        {
            get { return _followVisible; }
            set { RaiseAndSetIfChanged(ref _followVisible, value, nameof(FollowVisible)); }
        }

        public Action FollowClicked
        {
            get { return _followClicked; }
            set { RaiseAndSetIfChanged(ref _followClicked, value, nameof(FollowClicked)); }
        }

        [PreviewWith("HighwayEngineer_Icon_WaypointIcon")]
        public string WaypointsIcon
        {
            get { return _waypointsIcon; }
            set { RaiseAndSetIfChanged(ref _waypointsIcon, value, nameof(WaypointsIcon)); }
        }

        [PreviewWith(true)]
        public bool WaypointsSelected
        {
            get { return _waypointsSelected; }
            set { RaiseAndSetIfChanged(ref _waypointsSelected, value, nameof(WaypointsSelected)); }
        }

        [PreviewWith(true)]
        public bool WaypointsVisible
        {
            get { return _waypointsControlVisible; }
            set { RaiseAndSetIfChanged(ref _waypointsControlVisible, value, nameof(WaypointsVisible)); }
        }

        public Action WaypointsClicked
        {
            get { return _waypointsClicked; }
            set { RaiseAndSetIfChanged(ref _waypointsClicked, value, nameof(WaypointsClicked)); }
        }

        [PreviewWith("HighwayEngineer_Icon_WaterIcon")]
        public string WaterIcon
        {
            get { return _waterIcon; }
            set { RaiseAndSetIfChanged(ref _waterIcon, value, nameof(WaterIcon)); }
        }

        public bool WaterSelected
        {
            get { return _waterSelected; }
            set { RaiseAndSetIfChanged(ref _waterSelected, value, nameof(WaterSelected)); }
        }

        [PreviewWith(true)]
        public bool WaterVisible
        {
            get { return _waterVisible; }
            set { RaiseAndSetIfChanged(ref _waterVisible, value, nameof(WaterVisible)); }
        }

        [PreviewWith(true)]
        public bool WaterEnabled
        {
            get { return _waterEnabled; }
            set { RaiseAndSetIfChanged(ref _waterEnabled, value, nameof(WaterEnabled)); }
        }

        public Action WaterClicked
        {
            get { return _waterClicked; }
            set { RaiseAndSetIfChanged(ref _waterClicked, value, nameof(WaterClicked)); }
        }

        public IMyGeneratedTexture MapTexture
        {
            get { return _mapTextureValue; }
            private set { RaiseAndSetIfChanged(ref _mapTextureValue, value, nameof(MapTexture)); }
        }

        public bool PlanetWaypointsVisible
        {
            get { return _planetWaypointsVisible; }
            private set { RaiseAndSetIfChanged(ref _planetWaypointsVisible, value, nameof(PlanetWaypointsVisible)); }
        }

        [PreviewWith(true)]
        public bool StreetViewVisible
        {
            get { return _streetViewVisible; }
            private set { RaiseAndSetIfChanged(ref _streetViewVisible, value, nameof(StreetViewVisible)); }
        }

        [PreviewWith(true)]
        public bool StreetViewEnabled
        {
            get { return _streetViewEnabled; }
            private set { RaiseAndSetIfChanged(ref _streetViewEnabled, value, nameof(StreetViewEnabled)); }
        }

        const float COMPASS_ICON_NORTH_CORRECTION = -MathHelper.PiOver4;
        const double STREET_CAMERA_HEIGHT_METERS = 2.5d;
        const int MODE_TRANSITION_FRAMES = 12;
        const int TRADE_STATION_REFRESH_FRAMES = 600;
        const float SIGNAL_CLUSTER_DISTANCE_PIXELS = 28f;
        const float PLANET_MINIMUM_ZOOM = 0.55f;

        static readonly Color CurrentPlanetColor = new Color(117, 201, 241);
        static readonly Color FocusedPlanetColor = new Color(0x83, 0xad, 0x10);
        static readonly MySoundPair HudClickSound = new MySoundPair("HudClick");
        static readonly MySoundPair HudGpsSound = new MySoundPair("ArcHudGPSNotification3");
        static readonly CartographyLayer[] MapLayers =
        {
            CartographyLayer.Satellite,
            CartographyLayer.Terrain,
            CartographyLayer.Materials,
            CartographyLayer.Biomes
        };

        readonly CartographyModule _cartography;
        readonly MinimapLocalConfig _config;
        readonly MapData _mapData = new MapData();
        // The planner zooms down to road detail (no upper limit) and draws the roads itself, as overlays.
        readonly MapViewState _viewState = new MapViewState
        {
            MaximumZoom = float.MaxValue,
            RoadsVisible = false,
            AtmosphereVisible = false,
            Layer = CartographyLayer.Terrain
        };
        readonly Random _streetViewRandom = new Random();
        readonly List<IMyGps> _gpsEntries = new List<IMyGps>();
        readonly KnownTradeStations _knownTradeStations = new KnownTradeStations();
        readonly List<PlanetTradeStationMarker> _planetTradeStationMarkers =
            new List<PlanetTradeStationMarker>();
        readonly Dictionary<long, PlanetTradeStationMarker> _planetTradeStationMarkersById =
            new Dictionary<long, PlanetTradeStationMarker>();
        readonly HashSet<int> _temporarilyVisibleGpsHashes = new HashSet<int>();
        readonly PlanetPlayerMarker _planetPlayerMarker = new PlanetPlayerMarker();
        readonly Action _closeRequested;
        readonly Action _closeNow;
        readonly MapLayerItem[] _layerItems;
        readonly Action _openSettings;

        CartographyMapRenderer _mapRenderer;
        DynamicMapTexture _mapTexture;
        MyEntity3DSoundEmitter _uiSoundEmitter;
        RectangleF _mapBounds;

        // Planner state, in world coordinates. HighwayEngineer does not create GPS records.
        readonly List<Vector3D> _roadNodes = new List<Vector3D>();
        // Both tangents point in the direction of travel. At an interior node the
        // incoming handle is drawn behind the node, opposite its tangent vector.
        readonly List<Vector3D> _roadIncomingDirections = new List<Vector3D>();
        readonly List<Vector3D> _roadOutgoingDirections = new List<Vector3D>();
        readonly List<bool> _roadIncomingEdited = new List<bool>();
        readonly List<bool> _roadOutgoingEdited = new List<bool>();
        // True for nodes created automatically where the draft crosses a built road.
        readonly List<bool> _roadIntersections = new List<bool>();
        // Per node: whether the segment ending there is a bridge; Auto follows the gap detection
        // while automatic bridges are on. Node 0 starts no segment, so its entry is unused.
        enum BridgeSegmentMode { Auto, Bridge, Road }
        readonly List<BridgeSegmentMode> _roadBridgeModes = new List<BridgeSegmentMode>();
        // Per node: metres the bridge deck is lifted above the terrain there (0: none); see LiftRoadNode.
        readonly List<double> _roadNodeLifts = new List<double>();
        readonly PlanetSurfaceDrag _planetSurfaceDrag = new PlanetSurfaceDrag();
        bool _planetGrabActive;
        int _editingNode = -1;
        enum RoadDragMode { None, Orbit, MoveNode, IncomingTangent, OutgoingTangent }
        RoadDragMode _roadDragMode;
        RoadDragMode _activeTangent = RoadDragMode.OutgoingTangent;
        public bool TryGetTangentOverlay(out RoadTangentHandles handles)
        {
            handles = new RoadTangentHandles();
            if (!RoadsVisible) return false;
            if (_editingNode < 0 || _editingNode >= _roadNodes.Count || _mapRenderer == null) return false;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null) return false;
            Vector3D n = Vector3D.Normalize(_roadNodes[_editingNode] - planet.Position);
            if (!PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                    _roadNodes[_editingNode], _mapData, _viewState, out handles.Center))
                return false;
            handles.Scale = Math.Max(0.75f, Math.Min(_mapBounds.Width, _mapBounds.Height) / 700f);
            if (_editingNode > 0)
                handles.HasEntrance = TryGetScreenTangentHandle(planet, n,
                    -_roadIncomingDirections[_editingNode], handles.Center,
                    handles.Scale, out handles.Entrance);
            if (_editingNode < _roadNodes.Count - 1 || _roadNodes.Count == 1)
                handles.HasExit = TryGetScreenTangentHandle(planet, n,
                    _roadOutgoingDirections[_editingNode], handles.Center,
                    handles.Scale, out handles.Exit);
            return handles.HasEntrance || handles.HasExit;
        }

        bool TryGetScreenTangentHandle(PlanetSnapshot planet, Vector3D normal,
            Vector3D tangent, Vector2 center, float scale, out Vector2 handle)
        {
            handle = Vector2.Zero;
            tangent -= normal * Vector3D.Dot(tangent, normal);
            if (tangent.Normalize() < 1e-8) return false;
            // The planet projection supplies only the direction. Handle spacing
            // is then measured in screen pixels, independent of map zoom.
            for (int sign = 1; sign >= -1; sign -= 2)
            {
                Vector3D probe = planet.Position + Vector3D.Normalize(
                    normal + tangent * (0.04 * sign)) * planet.Radius;
                Vector2 projected;
                if (!PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        probe, _mapData, _viewState, out projected))
                    continue;
                Vector2 direction = (projected - center) * sign;
                if (direction.LengthSquared() < 0.01f) continue;
                direction.Normalize();
                handle = center + direction * (44f * scale);
                return true;
            }
            return false;
        }
        RoadDragMode TangentHandleAt(Vector2 point)
        {
            if (!RoadsVisible || PresentedOverlays.EditingNode != _editingNode ||
                !PresentedOverlays.Tangents.HasValue) return RoadDragMode.None;
            RoadTangentHandles handles = PresentedOverlays.Tangents.Value;
            float hitRadiusSquared = 400f * handles.Scale * handles.Scale;
            if (handles.HasEntrance && Vector2.DistanceSquared(point, handles.Entrance) <= hitRadiusSquared)
                return RoadDragMode.IncomingTangent;
            if (handles.HasExit && Vector2.DistanceSquared(point, handles.Exit) <= hitRadiusSquared)
                return RoadDragMode.OutgoingTangent;
            return RoadDragMode.None;
        }
        public void BeginRoadDrag(Vector2 point)
        {
            _planetGrabActive = false;
            // Road editing is off during a push and while marking built roads
            // (bulldoze, repair); dragging then always orbits, still grabbing the surface.
            bool editable = !_pushPending && !MarkMode;
            int hit = editable && RoadsVisible ? FindRoadNode(point) : -1;
            RoadDragMode handle = editable ? TangentHandleAt(point) : RoadDragMode.None;
            _roadDragMode = handle != RoadDragMode.None ? handle :
                hit >= 0 ? RoadDragMode.MoveNode : RoadDragMode.Orbit;
            if (_roadDragMode != RoadDragMode.Orbit)
                _roadDragStart = CaptureRoadDraft();
            if (_roadDragMode == RoadDragMode.MoveNode)
                _editingNode = hit;
            else if (handle != RoadDragMode.None)
                _activeTangent = handle;
            else if (!_viewState.SurfaceViewActive && _mapBounds.Width > 0f && _mapBounds.Height > 0f)
            {
                // Keep the surface point under the cursor fixed while dragging
                // (outside the globe, dragging falls back to a velocity orbit).
                Vector2 center = _mapBounds.Center + new Vector2(
                    _viewState.PlanetCenterOffsetNormalized.X * _mapBounds.Width,
                    _viewState.PlanetCenterOffsetNormalized.Y * _mapBounds.Height);
                float radius = Math.Min(_mapBounds.Width, _mapBounds.Height) * 0.47f *
                    Math.Max(_viewState.Zoom, 0.0001f);
                _planetGrabActive = _planetSurfaceDrag.Begin(point, center, radius,
                    _viewState.PlanetOrbitView, _viewState.PlanetOrbitRight,
                    _viewState.PlanetOrbitUp);
            }
        }
        public void BeginRoadNodeDrag(int index, Vector2 point)
        {
            _planetGrabActive = false;
            if (_pushPending || MarkMode ||
                index < 0 || index >= _roadNodes.Count)
            {
                _roadDragMode = RoadDragMode.None;
                return;
            }
            _roadDragStart = CaptureRoadDraft();
            _editingNode = index;
            _roadDragMode = RoadDragMode.MoveNode;
        }
        public void EndRoadDrag(Vector2 point, bool wasDragged)
        {
            _planetGrabActive = false;
            _roadDragMode = RoadDragMode.None;
            // One history entry per drag: the draft at press, if release changed it.
            if (_roadDragStart != null && !_roadDragStart.SameRoad(CaptureRoadDraft()))
            {
                RecordRoadHistory(_roadDragStart);
                AppendIntersectionStatus(RefreshRoadIntersections());
            }
            _roadDragStart = null;
        }

        bool _pushPending;
        bool _roadsVisible = true;
        [PreviewWith(true)]
        public bool RoadsVisible
        {
            get { return _roadsVisible; }
            private set { RaiseAndSetIfChanged(ref _roadsVisible, value, nameof(RoadsVisible)); }
        }
        public bool CanCancelRoad => !_pushPending && _roadNodes.Count > 0;

        public void SetRoadsVisible(bool visible)
        {
            if (RoadsVisible == visible) return;
            RoadsVisible = visible;
            _roadDragMode = RoadDragMode.None;
            _textureDirty = true;
        }

        /// <summary>
        /// Whether the road visibility toggle is shown: planetary view with built
        /// roads or a draft to hide. While hidden, roads are switched back on so
        /// the editor can't get stuck with everything invisible.
        /// </summary>
        public bool UpdateRoadsToggle()
        {
            bool shown = _committedRoads.Count > 0 || _roadNodes.Count > 0;
            if (!shown) SetRoadsVisible(true);
            return shown;
        }

        public void CancelRoad()
        {
            if (!CanCancelRoad) return;
            RecordRoadHistory();
            ClearRoadDraft();
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_DraftCancelled");
        }

        /// <summary>Removes the draft's nodes and edits; the caller sets the status.</summary>
        void ClearRoadDraft()
        {
            _roadNodes.Clear();
            _roadIncomingDirections.Clear();
            _roadOutgoingDirections.Clear();
            _roadIncomingEdited.Clear();
            _roadOutgoingEdited.Clear();
            _roadIntersections.Clear();
            _roadBridgeModes.Clear();
            _roadNodeLifts.Clear();
            _editingNode = -1;
            _roadDragMode = RoadDragMode.None;
            _roadSlopeWarning = null;
            _textureDirty = true;
        }

        // Built segments marked by the bulldoze or repair tool, by their road library id, so they
        // survive the built-road list being reloaded. A bridge is marked whole.
        readonly List<RoadCurve> _roadMarks = new List<RoadCurve>();
        Vector3D _markAnchor;
        bool _hasMarkAnchor;
        // The last road clicked, where a Shift+click range starts.
        RoadCurve _lastMarkedRoad;
        bool _hasLastMarkedRoad;

        /// <summary>Shift held: a click marks every road between it and the last click.</summary>
        public bool MarkRangeModifier { get; set; }

        bool _bulldozeMode;
        [PreviewWith(true)]
        public bool BulldozeMode
        {
            get { return _bulldozeMode; }
            private set { RaiseAndSetIfChanged(ref _bulldozeMode, value, nameof(BulldozeMode)); }
        }

        bool _repairMode;
        /// <summary>The repair tool: marked roads get a new material and/or their holes filled.</summary>
        public bool RepairMode
        {
            get { return _repairMode; }
            private set { RaiseAndSetIfChanged(ref _repairMode, value, nameof(RepairMode)); }
        }

        /// <summary>A tool that marks built roads (bulldoze or repair) is on: the draft cannot be edited.</summary>
        public bool MarkMode => BulldozeMode || RepairMode;
        public int RoadMarkCount => _roadMarks.Count;
        public bool CanConfirmBulldoze => BulldozeMode && !_pushPending && _roadMarks.Count > 0;
        public bool CanConfirmRepair => RepairMode && !_pushPending && _roadMarks.Count > 0;
        /// <summary>The marked roads can be confirmed with the active tool.</summary>
        public bool CanConfirmMarks => CanConfirmBulldoze || CanConfirmRepair;

        /// <summary>"Bulldoze: …" or "Repair: …", for the editor status.</summary>
        string MarkToolStatus(string text)
        {
            return LocHelper.Format(RepairMode ? "HighwayEngineer_Status_RepairPrefix" : "HighwayEngineer_Status_BulldozePrefix", text);
        }

        public void SetBulldozeMode(bool enabled)
        {
            if (BulldozeMode == enabled) return;
            if (enabled) SetRepairMode(false);
            BulldozeMode = enabled;
            EnterMarkMode(enabled, LocHelper.GetLoc("HighwayEngineer_Status_BulldozeHint"));
        }

        public void SetRepairMode(bool enabled)
        {
            if (RepairMode == enabled) return;
            if (enabled) SetBulldozeMode(false);
            RepairMode = enabled;
            EnterMarkMode(enabled, LocHelper.GetLoc("HighwayEngineer_Status_RepairHint"));
        }

        void EnterMarkMode(bool enabled, string status)
        {
            // Marks belong to the tool that made them.
            _roadMarks.Clear();
            _hasMarkAnchor = false;
            _hasLastMarkedRoad = false;
            // Leave any node or tangent drag behind; the draft itself is kept.
            _roadDragMode = RoadDragMode.None;
            _roadDragStart = null;
            if (enabled) _editingNode = -1;
            RoadEditorStatus = enabled ? status : LocHelper.GetLoc("HighwayEngineer_Status_DefaultHint");
            _textureDirty = true;
        }

        // Automatic bridges: draft segments that cross a gap are planned as bridges (painted purple),
        // unless the player set them otherwise with the bridge badge beside them. Consecutive
        // bridged segments are one bridge, following their curves.
        bool _autoBridge = MinimapLocalConfig.Current.Bridge.AutoDetect;
        public bool AutoBridge
        {
            get { return _autoBridge; }
            private set { RaiseAndSetIfChanged(ref _autoBridge, value, nameof(AutoBridge)); }
        }

        BridgeSettingsViewModel _bridgeSettings;

        /// <summary>The bridge settings flyout's state, saved in this machine's config.</summary>
        public BridgeSettingsViewModel BridgeSettings =>
            _bridgeSettings ?? (_bridgeSettings = new BridgeSettingsViewModel(MinimapLocalConfig.Current.Bridge));

        public void SetAutoBridge(bool enabled)
        {
            if (AutoBridge == enabled || _pushPending) return;
            AutoBridge = enabled;
            MinimapLocalConfig.Current.Bridge.AutoDetect = enabled;
            MinimapLocalConfig.RequestSave();
            RoadEditorStatus = enabled
                ? LocHelper.GetLoc("HighwayEngineer_Status_AutoBridgeOn")
                : LocHelper.GetLoc("HighwayEngineer_Status_AutoBridgeOff");
            _textureDirty = true;
        }

        /// <summary>Switches a draft segment (by its end node) between bridge and road.</summary>
        public void ToggleBridgeSegment(int segment)
        {
            if (_pushPending || MarkMode || segment < 1 || segment >= _roadNodes.Count) return;
            bool bridge = !IsBridgeSegment(segment);
            RecordRoadHistory();
            _roadBridgeModes[segment] = bridge ? BridgeSegmentMode.Bridge : BridgeSegmentMode.Road;
            // A road lies on the terrain: its ends lose their lift.
            if (!bridge)
                _roadNodeLifts[segment - 1] = _roadNodeLifts[segment] = 0.0;
            RoadEditorStatus = LocHelper.Format(bridge ? "HighwayEngineer_Status_SegmentIsBridge" : "HighwayEngineer_Status_SegmentIsRoad", segment);
            _textureDirty = true;
        }

        /// <summary>
        /// "+" (<paramref name="raise"/>): lifts the selected node, or the last one when none is,
        /// another <see cref="BridgeLiftStepMeters"/> above the terrain and makes its segments
        /// bridges. "-": lowers it as much; under <see cref="BridgeLiftMinMeters"/> the lift is
        /// removed and its segments follow the gap detection again.
        /// </summary>
        public void LiftRoadNode(bool raise)
        {
            LiftRoadNode(raise, true);
        }

        /// <summary>
        /// The mouse wheel while a node is dragged lifts it (up) or lowers it (down) a step per
        /// notch instead of zooming: the way to force a bridge where gap detection finds none.
        /// The drag's own undo entry covers it.
        /// </summary>
        public void ScrollDraggedNode(int wheelDelta)
        {
            if (_roadDragMode != RoadDragMode.MoveNode || wheelDelta == 0) return;
            int steps = Math.Max(1, Math.Abs(wheelDelta) / 120);
            for (int i = 0; i < steps; i++)
                LiftRoadNode(wheelDelta > 0, false);
        }

        void LiftRoadNode(bool raise, bool recordHistory)
        {
            if (!RoadsVisible || _pushPending || MarkMode || _roadNodes.Count < 2) return;
            int node = _editingNode >= 0 && _editingNode < _roadNodes.Count ? _editingNode : _roadNodes.Count - 1;
            double lift = _roadNodeLifts[node];
            if (!raise && lift <= 0.0) return;
            lift = raise
                ? Math.Min(RoadLimits.MaxBridgeHeightMeters, lift + BridgeLiftStepMeters)
                : lift - BridgeLiftStepMeters;
            if (lift < BridgeLiftMinMeters) lift = 0.0;
            if (lift == _roadNodeLifts[node]) return;
            if (recordHistory) RecordRoadHistory();
            _roadNodeLifts[node] = lift;
            // The segments either side: bridges while lifted; back to automatic unless their
            // other end is lifted too.
            for (int segment = node; segment <= node + 1; segment++)
            {
                if (segment < 1 || segment >= _roadNodes.Count) continue;
                int other = segment == node ? node - 1 : node + 1;
                if (lift > 0.0)
                    _roadBridgeModes[segment] = BridgeSegmentMode.Bridge;
                else if (_roadNodeLifts[other] <= 0.0)
                    _roadBridgeModes[segment] = BridgeSegmentMode.Auto;
            }
            RoadEditorStatus = lift > 0.0
                ? LocHelper.Format("HighwayEngineer_Status_NodeLifted", node + 1, lift)
                : LocHelper.Format("HighwayEngineer_Status_NodeLowered", node + 1);
            AppendIntersectionStatus(RefreshRoadIntersections());
            _textureDirty = true;
        }

        /// <summary>
        /// Removes node <paramref name="index"/>'s bridge mode with the node: the two segments it
        /// joined become one, a bridge or a road if either was set to be.
        /// </summary>
        void RemoveBridgeMode(int index)
        {
            if (index > 0 && index + 1 < _roadBridgeModes.Count)
            {
                BridgeSegmentMode before = _roadBridgeModes[index], after = _roadBridgeModes[index + 1];
                _roadBridgeModes[index + 1] = before == BridgeSegmentMode.Bridge || after == BridgeSegmentMode.Bridge
                    ? BridgeSegmentMode.Bridge
                    : before == BridgeSegmentMode.Road || after == BridgeSegmentMode.Road
                        ? BridgeSegmentMode.Road
                        : BridgeSegmentMode.Auto;
            }
            _roadBridgeModes.RemoveAt(index);
            if (_roadBridgeModes.Count > 0) _roadBridgeModes[0] = BridgeSegmentMode.Auto;
        }

        public void DiscardMarks()
        {
            if (_roadMarks.Count == 0) return;
            _roadMarks.Clear();
            _hasMarkAnchor = false;
            _hasLastMarkedRoad = false;
            RoadEditorStatus = MarkToolStatus(LocHelper.GetLoc("HighwayEngineer_Status_MarksDiscarded"));
            _textureDirty = true;
        }

        void ToggleRoadMark(Vector2 screenPoint)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            int roadIndex;
            Vector3D position;
            Vector3D tangent;
            if (planet == null ||
                !TryHitCommittedRoad(screenPoint, planet, out roadIndex, out position, out tangent))
                return;
            RoadCurve road = _committedRoads[roadIndex];
            if (road.SegmentId == 0 && road.ModificationId == null)
            {
                RoadEditorStatus = MarkToolStatus(LocHelper.GetLoc("HighwayEngineer_Status_RoadStillLoading"));
                return;
            }
            // Shift+click marks the connected roads from the last click to this one.
            bool notConnected = false;
            int from = MarkRangeModifier ? FindLastMarkedRoad() : -1;
            List<int> path = from >= 0 && from != roadIndex ? FindCommittedRoadPath(from, roadIndex) : null;
            if (path != null)
            {
                foreach (int index in path)
                    SetRoadMark(_committedRoads[index], true);
            }
            else
            {
                if (from >= 0 && from != roadIndex)
                    notConnected = true;
                SetRoadMark(road, FindRoadMark(road) < 0);
            }
            _lastMarkedRoad = road;
            _hasLastMarkedRoad = true;
            // The confirm/discard buttons follow the last road clicked.
            _markAnchor = position;
            _hasMarkAnchor = _roadMarks.Count > 0;
            string marked = LocHelper.Format(_roadMarks.Count == 1
                ? "HighwayEngineer_Status_Marked_One"
                : "HighwayEngineer_Status_Marked_Many", _roadMarks.Count);
            if (notConnected)
                marked = LocHelper.Format("HighwayEngineer_Status_MarkNotConnected", marked);
            RoadEditorStatus = MarkToolStatus(marked);
            _textureDirty = true;
        }

        /// <summary>Marks or unmarks a road; a bridge is removed whole, so all its segments go together.</summary>
        void SetRoadMark(RoadCurve road, bool mark)
        {
            foreach (RoadCurve part in _committedRoads)
            {
                if (road.BridgeId != 0 ? part.BridgeId != road.BridgeId : !SameMarkTarget(part, road))
                    continue;
                int marked = FindRoadMark(part);
                if (mark && marked < 0) _roadMarks.Add(part);
                else if (!mark && marked >= 0) _roadMarks.RemoveAt(marked);
            }
        }

        int FindLastMarkedRoad()
        {
            if (!_hasLastMarkedRoad) return -1;
            for (int i = 0; i < _committedRoads.Count; i++)
                if (SameMarkTarget(_committedRoads[i], _lastMarkedRoad))
                    return i;
            return -1;
        }

        // Unit directions closer than this share a node (about 0.6 m on a 60 km planet).
        const double RoadNodeJoinDistanceSquared = 1e-10;

        static bool RoadsTouch(RoadCurve a, RoadCurve b)
        {
            return Vector3D.DistanceSquared(a.Start, b.Start) < RoadNodeJoinDistanceSquared ||
                   Vector3D.DistanceSquared(a.Start, b.End) < RoadNodeJoinDistanceSquared ||
                   Vector3D.DistanceSquared(a.End, b.Start) < RoadNodeJoinDistanceSquared ||
                   Vector3D.DistanceSquared(a.End, b.End) < RoadNodeJoinDistanceSquared;
        }

        /// <summary>
        /// The shortest chain of built roads joined end to end from <paramref name="from"/> to
        /// <paramref name="to"/>, both included, by arc length; null when they are not connected.
        /// </summary>
        List<int> FindCommittedRoadPath(int from, int to)
        {
            int count = _committedRoads.Count;
            var distance = new double[count];
            var previous = new int[count];
            var done = new bool[count];
            for (int i = 0; i < count; i++)
            {
                distance[i] = double.PositiveInfinity;
                previous[i] = -1;
            }
            distance[from] = 0;
            while (true)
            {
                int current = -1;
                for (int i = 0; i < count; i++)
                    if (!done[i] && !double.IsPositiveInfinity(distance[i]) &&
                        (current < 0 || distance[i] < distance[current]))
                        current = i;
                if (current < 0) return null;
                if (current == to) break;
                done[current] = true;
                RoadCurve road = _committedRoads[current];
                for (int i = 0; i < count; i++)
                {
                    if (done[i] || !RoadsTouch(road, _committedRoads[i])) continue;
                    double through = distance[current] + _committedRoads[i].Arc;
                    if (through >= distance[i]) continue;
                    distance[i] = through;
                    previous[i] = current;
                }
            }
            var path = new List<int>();
            for (int i = to; i >= 0; i = previous[i])
                path.Add(i);
            return path;
        }

        /// <summary>
        /// The same road to remove: a road library segment by its id, a road known only from the
        /// voxel API's listing by its modification line.
        /// </summary>
        static bool SameMarkTarget(RoadCurve a, RoadCurve b)
        {
            return a.SegmentId != 0 || b.SegmentId != 0
                ? a.SegmentId == b.SegmentId
                : a.ModificationId != null && a.ModificationId == b.ModificationId && a.LineIndex == b.LineIndex;
        }

        bool IsRoadMarked(RoadCurve road)
        {
            return FindRoadMark(road) >= 0;
        }

        int FindRoadMark(RoadCurve road)
        {
            for (int i = 0; i < _roadMarks.Count; i++)
                if (SameMarkTarget(_roadMarks[i], road))
                    return i;
            return -1;
        }

        static bool SameRoadCurve(RoadCurve a, RoadCurve b)
        {
            return Vector3D.DistanceSquared(a.Start, b.Start) < 1e-12 &&
                   Vector3D.DistanceSquared(a.End, b.End) < 1e-12 &&
                   Vector3D.DistanceSquared(a.StartTangent, b.StartTangent) < 1e-12 &&
                   Vector3D.DistanceSquared(a.EndTangent, b.EndTangent) < 1e-12;
        }

        /// <summary>
        /// Asks the server what bulldozing the marked segments refunds or costs;
        /// <paramref name="quoted"/> gets its answer (null when it could not be asked).
        /// </summary>
        public void QuoteBulldoze(Action<PacketBulldozeQuoteResponse> quoted)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (!CanConfirmBulldoze || planet == null || client == null)
            {
                quoted(null);
                return;
            }
            var ids = new List<long>();
            var lines = new List<RoadLineRef>();
            CollectMarks(ids, lines);
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_BulldozeQuoting");
            client.RequestBulldozeQuote(planet.EntityId, ids.ToArray(), lines, response =>
            {
                if (_disposed) return;
                if (response.Reason != RoadRejectReason.None)
                    RoadEditorStatus = LocHelper.Format("HighwayEngineer_Status_BulldozePrefix",
                        RoadRejectReasonText.Describe(response.Reason, response.Message, response.RetryAfterSeconds));
                quoted(response);
            });
        }

        /// <summary>
        /// Carries out a bulldoze quote on the server: it removes the roads (through the voxel API)
        /// and bridges (their voxel maps) and settles the credits.
        /// </summary>
        public void PushBulldoze(long quoteId)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (!CanConfirmBulldoze || planet == null || client == null) return;
            long planetId = planet.EntityId;
            _pushPending = true;
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Bulldoze_Running");
            client.RequestBulldoze(quoteId, status =>
            {
                if (_disposed) return;
                if (status.State == RoadJobState.Running)
                {
                    Action<string, string> show = MessageBoxRequested;
                    if (show != null)
                        show(LocHelper.GetLoc("HighwayEngineer_Bulldoze_RunningTitle"), LocHelper.GetLoc("HighwayEngineer_Bulldoze_RunningBody"));
                    return;
                }
                _pushPending = false;
                bool success = status.State == RoadJobState.Completed;
                string message = success
                    ? status.CreditDelta > 0
                        ? LocHelper.Format("HighwayEngineer_Bulldoze_Refunded", LocHelper.Resolve(status.Message), status.CreditDelta)
                        : status.CreditDelta < 0
                            ? LocHelper.Format("HighwayEngineer_Bulldoze_Charged", LocHelper.Resolve(status.Message), -status.CreditDelta)
                            : LocHelper.Resolve(status.Message)
                    : RoadRejectReasonText.Describe(status.Reason, status.Message, 0f);
                if (success)
                {
                    _roadMarks.Clear();
                    _hasMarkAnchor = false;
                    _hasLastMarkedRoad = false;
                    if (_cartography != null) _cartography.RefreshPlanet(planetId);
                    _mapData.RefreshPlanets();
                    _committedRoadPlanetId = 0L;
                    _textureDirty = true;
                }
                ReportPushResult(success, message, PushKind.Bulldoze);
            });
        }

        /// <summary>
        /// The marked roads as the server takes them: library segments by id, roads known only from
        /// the voxel API by their listed line, with its geometry exactly as listed, which the server
        /// checks against its own listing.
        /// </summary>
        void CollectMarks(List<long> ids, List<RoadLineRef> lines)
        {
            foreach (RoadCurve mark in _roadMarks)
            {
                double[] listed;
                if (mark.SegmentId != 0)
                    ids.Add(mark.SegmentId);
                else if (_listedGeometry.TryGetValue(ListedKey(mark.ModificationId, mark.LineIndex), out listed))
                    lines.Add(new RoadLineRef
                    {
                        ModificationId = mark.ModificationId,
                        LineIndex = mark.LineIndex,
                        Geometry = listed
                    });
            }
        }

        /// <summary>
        /// Asks the server to check repairing the marked roads: paving them with
        /// <paramref name="material"/> (null keeps theirs), filling their holes and/or painting their guide lines.
        /// <paramref name="quoted"/> gets its answer (null when it could not be asked).
        /// </summary>
        public void QuoteRepair(string material, bool repairHoles, bool repaintGuideLines,
            Action<PacketRepairQuoteResponse> quoted)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (!CanConfirmRepair || planet == null || client == null)
            {
                quoted(null);
                return;
            }
            var ids = new List<long>();
            var lines = new List<RoadLineRef>();
            CollectMarks(ids, lines);
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_RepairQuoting");
            client.RequestRepairQuote(planet.EntityId, ids.ToArray(), lines, material, repairHoles, repaintGuideLines, response =>
            {
                if (_disposed) return;
                if (response.Reason != RoadRejectReason.None)
                    RoadEditorStatus = LocHelper.Format("HighwayEngineer_Status_RepairPrefix",
                        RoadRejectReasonText.Describe(response.Reason, response.Message, response.RetryAfterSeconds));
                quoted(response);
            });
        }

        /// <summary>
        /// Carries out a repair quote on the server: new material through the voxel API, holes
        /// filled on every machine, bridges rebuilt.
        /// </summary>
        public void PushRepair(long quoteId)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (!CanConfirmRepair || planet == null || client == null) return;
            long planetId = planet.EntityId;
            _pushPending = true;
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Repair_Running");
            client.RequestRepair(quoteId, status =>
            {
                if (_disposed) return;
                if (status.State == RoadJobState.Running)
                {
                    Action<string, string> show = MessageBoxRequested;
                    if (show != null)
                        show(LocHelper.GetLoc("HighwayEngineer_Repair_RunningTitle"), LocHelper.GetLoc("HighwayEngineer_Repair_RunningBody"));
                    return;
                }
                _pushPending = false;
                bool success = status.State == RoadJobState.Completed;
                if (success)
                {
                    _roadMarks.Clear();
                    _hasMarkAnchor = false;
                    _hasLastMarkedRoad = false;
                    if (_cartography != null) _cartography.RefreshPlanet(planetId);
                    _mapData.RefreshPlanets();
                    _committedRoadPlanetId = 0L;
                    _textureDirty = true;
                }
                ReportPushResult(success, success ? LocHelper.Resolve(status.Message)
                    : RoadRejectReasonText.Describe(status.Reason, status.Message, 0f), PushKind.Repair);
            });
        }

        /// <summary>Screen position of the last clicked marked road, for the action buttons.</summary>
        internal bool TryGetMarkAnchor(out Vector2 screen)
        {
            screen = Vector2.Zero;
            return MarkMode && _hasMarkAnchor && _mapRenderer != null &&
                   PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds, _markAnchor,
                       _mapData, _viewState, out screen);
        }

        // Undo/redo of the unpushed draft. Each entry is a whole-draft snapshot
        // (drafts are a handful of nodes). Drags record once, from press to release.
        const int RoadHistoryLimit = 32;
        readonly List<RoadDraftSnapshot> _roadUndo = new List<RoadDraftSnapshot>();
        readonly List<RoadDraftSnapshot> _roadRedo = new List<RoadDraftSnapshot>();
        RoadDraftSnapshot _roadDragStart;

        sealed class RoadDraftSnapshot
        {
            public Vector3D[] Nodes;
            public Vector3D[] Incoming;
            public Vector3D[] Outgoing;
            public bool[] IncomingEdited;
            public bool[] OutgoingEdited;
            public bool[] Intersections;
            public BridgeSegmentMode[] BridgeModes;
            public double[] Lifts;
            public int EditingNode;
            public RoadDragMode ActiveTangent;

            public bool SameRoad(RoadDraftSnapshot other)
            {
                if (Nodes.Length != other.Nodes.Length) return false;
                for (int i = 0; i < Nodes.Length; i++)
                    if (Nodes[i] != other.Nodes[i] ||
                        Incoming[i] != other.Incoming[i] ||
                        Outgoing[i] != other.Outgoing[i] ||
                        IncomingEdited[i] != other.IncomingEdited[i] ||
                        OutgoingEdited[i] != other.OutgoingEdited[i] ||
                        Intersections[i] != other.Intersections[i] ||
                        BridgeModes[i] != other.BridgeModes[i] ||
                        Lifts[i] != other.Lifts[i])
                        return false;
                return true;
            }
        }

        RoadDraftSnapshot CaptureRoadDraft()
        {
            return new RoadDraftSnapshot
            {
                Nodes = _roadNodes.ToArray(),
                Incoming = _roadIncomingDirections.ToArray(),
                Outgoing = _roadOutgoingDirections.ToArray(),
                IncomingEdited = _roadIncomingEdited.ToArray(),
                OutgoingEdited = _roadOutgoingEdited.ToArray(),
                Intersections = _roadIntersections.ToArray(),
                BridgeModes = _roadBridgeModes.ToArray(),
                Lifts = _roadNodeLifts.ToArray(),
                EditingNode = _editingNode,
                ActiveTangent = _activeTangent
            };
        }

        void RestoreRoadDraft(RoadDraftSnapshot snapshot)
        {
            _roadNodes.Clear();
            _roadNodes.AddRange(snapshot.Nodes);
            _roadIncomingDirections.Clear();
            _roadIncomingDirections.AddRange(snapshot.Incoming);
            _roadOutgoingDirections.Clear();
            _roadOutgoingDirections.AddRange(snapshot.Outgoing);
            _roadIncomingEdited.Clear();
            _roadIncomingEdited.AddRange(snapshot.IncomingEdited);
            _roadOutgoingEdited.Clear();
            _roadOutgoingEdited.AddRange(snapshot.OutgoingEdited);
            _roadIntersections.Clear();
            _roadIntersections.AddRange(snapshot.Intersections);
            _roadBridgeModes.Clear();
            _roadBridgeModes.AddRange(snapshot.BridgeModes);
            _roadNodeLifts.Clear();
            _roadNodeLifts.AddRange(snapshot.Lifts);
            _editingNode = snapshot.EditingNode < _roadNodes.Count ? snapshot.EditingNode : -1;
            _activeTangent = snapshot.ActiveTangent;
            _roadDragMode = RoadDragMode.None;
            _roadSlopeWarning = null;
            _textureDirty = true;
        }

        /// <summary>Records the draft as it was before an edit; a new edit drops redo.</summary>
        void RecordRoadHistory(RoadDraftSnapshot before)
        {
            _roadUndo.Add(before);
            if (_roadUndo.Count > RoadHistoryLimit)
                _roadUndo.RemoveAt(0);
            _roadRedo.Clear();
        }

        void RecordRoadHistory()
        {
            RecordRoadHistory(CaptureRoadDraft());
        }

        void ClearRoadHistory()
        {
            _roadUndo.Clear();
            _roadRedo.Clear();
            _roadDragStart = null;
        }

        public void UndoRoadEdit()
        {
            StepRoadHistory(_roadUndo, _roadRedo, "HighwayEngineer_Status_Undo", "HighwayEngineer_Status_NothingToUndo");
        }

        public void RedoRoadEdit()
        {
            StepRoadHistory(_roadRedo, _roadUndo, "HighwayEngineer_Status_Redo", "HighwayEngineer_Status_NothingToRedo");
        }

        void StepRoadHistory(List<RoadDraftSnapshot> from, List<RoadDraftSnapshot> to,
            string doneKey, string emptyKey)
        {
            if (_pushPending || _roadDragStart != null) return;
            // Close a live auto-route session first so undo steps over it as a whole
            // and a pending reroute cannot overwrite the restored draft.
            if (AutoRouteActive)
            {
                _autoRouteDirty = false;
                EndAutoRoute();
            }
            if (from.Count == 0)
            {
                RoadEditorStatus = LocHelper.GetLoc(emptyKey);
                return;
            }
            RoadDraftSnapshot target = from[from.Count - 1];
            from.RemoveAt(from.Count - 1);
            to.Add(CaptureRoadDraft());
            if (to.Count > RoadHistoryLimit)
                to.RemoveAt(0);
            RestoreRoadDraft(target);
            RoadEditorStatus = LocHelper.Format(doneKey, _roadUndo.Count, _roadRedo.Count);
        }
        string _roadSlopeWarning;
        string _roadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_DefaultHint");
        int _roadEditorStatusVersion;

        public string RoadEditorStatus
        {
            get { return _roadEditorStatus; }
            private set
            {
                _roadEditorStatus = value;
                ++_roadEditorStatusVersion;
            }
        }

        /// <summary>The changing part of the footer: an error, a warning, or the last editor action.</summary>
        public string StatusNotice => RoadParameterError ?? _roadSlopeWarning ?? RoadEditorStatus;

        /// <summary>Changes whenever the editor status is set, even to the same text.</summary>
        public int StatusNoticeVersion => _roadEditorStatusVersion;

        /// <summary>A push or bulldoze is waiting for the voxel API.</summary>
        public bool IsPushPending => _pushPending;
        public string RoadParameterError { get; private set; }
        public string RoadMaterialSubtype { get; private set; } = "Soil";
        public float RoadWidthMeters { get; private set; } = 50f;
        public float RoadMaterialDepthMeters { get; private set; } = 10f;
        public float RoadFillAngleDegrees { get; private set; } = 35f;
        public float RoadMaximumSlopeDegrees { get; private set; } = 8f;
        public float RoadSegmentDistanceMeters { get; private set; } = 100f;
        public bool RoadAverageHeight { get; private set; } = true;
        public bool RoadFillShoulders { get; private set; } = true;
        public bool CanPushRoad =>
                                   !_pushPending && _roadNodes.Count >= 2 &&
                                   ResolveFocusedPlanet() != null &&
                                   RoadParameterError == null &&
                                   !RoadCrossesItself() &&
                                   BridgeError == null;

        /// <summary>The draft became a road contract: it is the server's to build now.</summary>
        public void RoadContractAccepted(RoadContractInfo contract)
        {
            ClearRoadDraft();
            ClearRoadHistory();
            RoadCostBreakdown costs = contract.Costs;
            if (contract.State != RoadContractState.Pending || costs == null)
            {
                RoadEditorStatus = LocHelper.GetLoc(contract.IsBridge
                    ? "HighwayEngineer_Status_ContractBuilding_Bridge"
                    : "HighwayEngineer_Status_ContractBuilding_Road");
                return;
            }
            var items = new List<string>();
            if (costs.ItemAmount > 0.0)
                items.Add(LocHelper.Format("HighwayEngineer_Contract_ItemKg", costs.ItemAmount,
                    ItemNames.Display(costs.ItemType, costs.ItemSubtype, costs.ItemSubtype)));
            if (costs.Item2Amount > 0.0)
                items.Add(LocHelper.Format("HighwayEngineer_Contract_ItemKg", costs.Item2Amount, LocHelper.Resolve(costs.Item2Name)));
            if (costs.Item3Amount > 0.0)
                items.Add(LocHelper.Format("HighwayEngineer_Contract_ItemCount", costs.Item3Amount,
                    ItemNames.Display(costs.Item3Type, costs.Item3Subtype, costs.Item3Name)));
            RoadEditorStatus = LocHelper.Format(contract.IsBridge
                ? "HighwayEngineer_Status_ContractDeliver_Bridge"
                : "HighwayEngineer_Status_ContractDeliver_Road", LocHelper.JoinAnd(items));
        }

        /// <summary>A built road changes the terrain: refresh the planet's map and its road list.</summary>
        void OnRoadContractChanged(RoadContractInfo contract)
        {
            if (contract.State != RoadContractState.Completed) return;
            if (_cartography != null)
                _cartography.RefreshPlanet(contract.PlanetId);
            _mapData.RefreshPlanets();
            _committedRoadPlanetId = 0L;
            _textureDirty = true;
        }

        /// <summary>Built roads or bridges changed (here or by another player): reload the planet's.</summary>
        void OnRoadLibraryChanged(long planetId)
        {
            if (planetId != 0 && planetId != _committedRoadPlanetId) return;
            _committedRoadPlanetId = 0L;
            _textureDirty = true;
        }

        /// <summary>The road settings flyout's state, saved on this machine between sessions.</summary>
        public RoadSettingsViewModel RoadSettings { get; private set; }

        /// <summary>Paint guide lines on the roads pushed (road settings).</summary>
        public bool RoadGuideLines { get; private set; }

        void ApplyRoadSettings(RoadSettingsConfig road)
        {
            RoadGuideLines = road.GuideLines;
            SetRoadParameters(road.Material, road.WidthMeters, road.DepthMeters, road.ShoulderAngleDegrees,
                road.MaxSlopeDegrees, road.SegmentDistanceMeters, road.AverageHeight, road.FillShoulders);
        }

        public void SetRoadParameters(string material, double width, double depth,
            float fillAngle, float maximumSlope, float segmentDistance,
            bool averageHeight, bool fillShoulders)
        {
            if (Math.Abs(RoadMaximumSlopeDegrees - maximumSlope) > 0.0001f ||
                Math.Abs(RoadSegmentDistanceMeters - segmentDistance) > 0.0001f ||
                Math.Abs(RoadFillAngleDegrees - fillAngle) > 0.0001f ||
                RoadAverageHeight != averageHeight || RoadFillShoulders != fillShoulders)
                _textureDirty = true;
            RoadAverageHeight = averageHeight;
            RoadFillShoulders = fillShoulders;
            RoadFillAngleDegrees = fillAngle;
            RoadMaximumSlopeDegrees = maximumSlope;
            RoadSegmentDistanceMeters = segmentDistance;
            string subtype = (material ?? string.Empty).Trim();

            if (subtype.Length == 0)
                RoadParameterError = LocHelper.GetLoc("HighwayEngineer_Status_EnterMaterial");
            else
            {
                RoadMaterialSubtype = subtype;
                if (Math.Abs(RoadWidthMeters - width) > 0.0001f)
                {
                    RoadWidthMeters = (float)width;
                    _textureDirty = true;
                }
                RoadMaterialDepthMeters = (float)depth;
                RoadParameterError = null;
            }
        }

        static bool TryRoadNumber(string text, float minimum, float maximum, out float value)
        {
            if (!float.TryParse(text, NumberStyles.Float, CultureInfo.CurrentCulture, out value) &&
                !float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value))
                return false;
            return !float.IsNaN(value) && !float.IsInfinity(value) &&
                   value >= minimum && value <= maximum;
        }
        readonly List<PlanetWaypointMarker> _roadNodeMarkers = new List<PlanetWaypointMarker>();
        readonly List<PlanetWaypointMarker> _gpsMarkers = new List<PlanetWaypointMarker>();
        readonly List<RoadSegmentLabel> _roadSegmentLabels = new List<RoadSegmentLabel>();
        readonly List<RoadCurve> _committedRoads = new List<RoadCurve>();
        readonly RoadLibrary _roadLibrary;
        long _committedRoadPlanetId;
        int _committedRoadGeneration;
        int _roadLibraryLoadGeneration;
        public IReadOnlyList<Vector3D> RoadNodes { get { return _roadNodes; } }

        int _frame;
        bool _layerSelectorExpanded;
        bool _waypointsVisible;
        bool _textureDirty = true;
        byte[] _lastMapPixels;
        readonly TextureFramePresentation<MapOverlayFrame> _overlayPresentation =
            new TextureFramePresentation<MapOverlayFrame>();
        internal MapOverlayFrame PresentedOverlays => _overlayPresentation.Current;
        internal void BeginMapDraw() { _overlayPresentation.BeginDraw(); }
        internal void EndMapDraw() { _overlayPresentation.EndDraw(); }
        bool _disposed;

        public RoadPlannerViewModel(
            CartographyModule cartography,
            Action closeRequested,
            Action closeNow,
            Action openSettings)
        {
            RoadSettings = new RoadSettingsViewModel(MinimapLocalConfig.Current.Road, ApplyRoadSettings);
            HighwayEngineerClientComponent.RoadContractChanged += OnRoadContractChanged;
            HighwayEngineerClientComponent.RoadLibraryChanged += OnRoadLibraryChanged;
            _cartography = cartography;
            if (_cartography != null)
                _cartography.PlanetInvalidated += OnPlanetInvalidated;
            _config = MinimapLocalConfig.Load();
            _roadLibrary = new RoadLibrary();
            _closeRequested = closeRequested;
            _closeNow = closeNow;
            _layerItems = new MapLayerItem[MapLayers.Length];
            for (int i = 0; i < MapLayers.Length; i++)
                _layerItems[i] = new MapLayerItem(MapLayers[i], GetLayerName(MapLayers[i]), SelectLayer);
            _openSettings = openSettings;
            _waypointsVisible = ResolveFullScreenGpsVisible();

            // Commands for the XAML view; the actions behind them are set in Open().
            CloseCommand = Command(() => CloseClicked);
            OrientationCommand = Command(() => OrientationClicked);
            FollowCommand = Command(() => FollowClicked);
            WaypointsCommand = Command(() => WaypointsClicked);
            WaterCommand = Command(() => WaterClicked);
            RoadsCommand = new DelegateCommand(_ => SetRoadsVisible(!RoadsVisible));
            BulldozeCommand = new DelegateCommand(_ => SetBulldozeMode(!BulldozeMode));
            RepairCommand = new DelegateCommand(_ => SetRepairMode(!RepairMode));
            BridgeCommand = new DelegateCommand(_ => SetAutoBridge(!AutoBridge));
        }

        /// <summary>True while the map has a pending repaint.</summary>
        public bool NeedsFrameUpdates => _textureDirty;

        public ICommand CloseCommand { get; private set; }
        public ICommand OrientationCommand { get; private set; }
        public ICommand FollowCommand { get; private set; }
        public ICommand WaypointsCommand { get; private set; }
        public ICommand WaterCommand { get; private set; }
        public ICommand RoadsCommand { get; private set; }
        public ICommand BulldozeCommand { get; private set; }
        public ICommand RepairCommand { get; private set; }
        public ICommand BridgeCommand { get; private set; }

        static ICommand Command(Func<Action> action)
        {
            return new DelegateCommand(_ =>
            {
                Action current = action();
                if (current != null)
                    current();
            });
        }

        public IReadOnlyList<CartographyLayer> Layers => MapLayers;

        /// <summary>The layer buttons (bottom left): the selected one, and the others while the selector is open.</summary>
        [PreviewWith(4)]
        public IReadOnlyList<MapLayerItem> LayerItems => _layerItems;

        /// <summary>The view's scaled button size and gap, for the layer buttons it builds from <see cref="LayerItems"/>.</summary>
        public void SetLayerButtonSize(float size, float gap)
        {
            for (int i = 0; i < _layerItems.Length; i++)
                _layerItems[i].SetSize(size, gap);
        }

        void UpdateLayerItems()
        {
            PlanetCubeFace face = GetPlayerPreviewFace();
            for (int i = 0; i < _layerItems.Length; i++)
            {
                MapLayerItem item = _layerItems[i];
                bool selected = item.Layer == _viewState.Layer;
                item.Update(selected, selected || _layerSelectorExpanded, GetLayerCubemap(item.Layer), face);
            }
        }
        public Action<string, string> MessageBoxRequested { get; set; }

        public void Open()
        {
            _disposed = false;
            _frame = 0;
            _textureDirty = true;
            _waypointsVisible = ResolveFullScreenGpsVisible();
            _temporarilyVisibleGpsHashes.Clear();
            ClearPlanetTradeStationMarkers();
            MinimapTheme.RefreshFactionAccent();
            _mapData.RefreshPlanets();
            _knownTradeStations.Refresh();
            _committedRoadPlanetId = 0L;
            ++_committedRoadGeneration;
            _committedRoads.Clear();
            LoadRoadLibraryAsync();
            PlanetSnapshot playerPlanet = _mapData.FindPlayerPlanet();
            _viewState.FocusedPlanetId = playerPlanet == null ? 0L : playerPlanet.EntityId;
            _mapRenderer = new CartographyMapRenderer(_cartography);
            _mapRenderer.Changed = MarkTextureDirty;
            ResetSurfaceView();
            _bridgePreview.Attach(_mapRenderer);

            CloseClicked = _closeRequested;
            SettingsClicked = _openSettings;
            OrientationClicked = TogglePlanetOrientationLock;
            FollowClicked = ToggleFollow;
            WaypointsClicked = ToggleWaypoints;
            WaterClicked = ToggleWater;
            RefreshPresentation();
            UpdateMapRequests();
        }

        public void ApplyLocalConfig()
        {
            _waypointsVisible = ResolveFullScreenGpsVisible();
            _textureDirty = true;
            RefreshPresentation();
        }

        public void CenterMap()
        {
            _viewState.PlanetCenterOffsetNormalized = Vector2.Zero;
            _textureDirty = true;
        }

        public void Update()
        {
            _frame++;
            if (_frame % 60 == 0)
                MinimapTheme.RefreshFactionAccent();
            if (_frame % 180 == 0)
            {
                _mapData.RefreshPlanets();
                if (_frame % 1800 == 0)
                    _committedRoadPlanetId = 0L;
                _textureDirty = true;
            }
            else if (_frame % 30 == 0)
            {
                _mapData.UpdatePlayerPosition();
                if (_viewState.FollowPlayer)
                    _textureDirty = true;
            }
            if (_frame % TRADE_STATION_REFRESH_FRAMES == 0)
                _knownTradeStations.Refresh();

            UpdateAutoRoute();
            RefreshDepots();
            UpdateMapRequests();
            RefreshPresentation();
            UpdateSurfaceView();
            if (_textureDirty)
                RenderMapTexture();
        }

        public void SetMapBounds(RectangleF bounds)
        {
            if (!_mapBounds.Equals(bounds))
            {
                // Absolute overlay coordinates cannot survive a viewport move,
                // even when the renderer keeps the same texture allocation.
                _overlayPresentation.Reset();
                _textureDirty = true;
            }
            _mapBounds = bounds;
            EnsureMapTextureForBounds();
            UpdateMapRequests();
            if (_textureDirty)
                RenderMapTexture();
            RefreshPresentation();
        }

        /// <summary>
        /// The API reported the planet changed (a push or bulldoze by any mod):
        /// request its built roads again on the next render.
        /// </summary>
        void OnPlanetInvalidated(CartographyPlanetInvalidatedEvent invalidated)
        {
            if (_disposed || invalidated == null ||
                invalidated.PlanetEntityId != _committedRoadPlanetId)
                return;
            _committedRoadPlanetId = 0L;
            _textureDirty = true;
        }

        public void Dispose()
        {
            HighwayEngineerClientComponent.RoadContractChanged -= OnRoadContractChanged;
            HighwayEngineerClientComponent.RoadLibraryChanged -= OnRoadLibraryChanged;
            _disposed = true;
            if (_cartography != null)
                _cartography.PlanetInvalidated -= OnPlanetInvalidated;
            _overlayPresentation.Reset();
            ++_roadLibraryLoadGeneration;
            _lastMapPixels = null;
            _bridgePreview.Detach();
            if (_mapRenderer != null)
                _mapRenderer.Dispose();
            if (_mapTexture != null)
                _mapTexture.Dispose();
            if (_uiSoundEmitter != null)
                _uiSoundEmitter.Cleanup();
            _mapRenderer = null;
            _mapTexture = null;
            _uiSoundEmitter = null;
            MapTexture = null;
            ClearPlanetTradeStationMarkers();
            _knownTradeStations.Clear();
            _planetPlayerMarker.Visible = false;
            _gpsEntries.Clear();
            _temporarilyVisibleGpsHashes.Clear();
        }

        void LoadRoadLibraryAsync()
        {
            int generation = ++_roadLibraryLoadGeneration;
            string xml;
            try { xml = RoadLibrary.ReadSessionXml(); }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(RoadPlannerViewModel));
                return;
            }
            if (string.IsNullOrWhiteSpace(xml)) return;
            if (MyAPIGateway.Parallel == null) return;
            RoadLibrary loaded = null;
            Exception failure = null;
            MyAPIGateway.Parallel.Start(() =>
            {
                try { loaded = RoadLibrary.ParseXml(xml); }
                catch (Exception error) { failure = error; }
            }, () =>
            {
                if (_disposed || _roadLibraryLoadGeneration != generation) return;
                if (failure != null)
                {
                    ErrorHandlerHelper.LogError(failure, typeof(RoadLibrary));
                    return;
                }
                _roadLibrary.Merge(loaded);
                _committedRoadPlanetId = 0L;
                _textureDirty = true;
            });
        }

        void RefreshPresentation()
        {
            CloseIcon = "HighwayEngineer_Icon_Cross";
            SettingsIcon = "HighwayEngineer_Icon_Settings";
            OrientationIcon = "HighwayEngineer_Icon_CompassIcon";
            WaypointsIcon = "HighwayEngineer_Icon_WaypointIcon";
            WaterIcon = "HighwayEngineer_Icon_WaterIcon";
            TitleText = LocHelper.GetLoc("HighwayEngineer_Planner_Header");
            TitleColor = MinimapTheme.Accent;
            FooterText = GetStatus();
            FooterColor = new Color(180, 195, 202);

            OrientationSelected =
                _viewState.PlanetOrientation == PlanetOrientationMode.NorthLocked;
            OrientationRotation = GetOrientationIconRotation();
            FollowSelected = _viewState.FollowPlayer;
            FollowIcon = _viewState.FollowPlayer ? "HighwayEngineer_Icon_Lock" : "HighwayEngineer_Icon_RotationPlane";
            WaypointsSelected = _waypointsVisible;
            WaterSelected = !_viewState.WaterVisible;

            PlanetWaypointsVisible = true;
            OrientationVisible = true;
            FollowVisible = true;
            WaypointsVisible = true;
            StreetViewVisible = true;
            StreetViewEnabled = CanUseStreetView();

            UpdateLayerItems();
            PlanetColorCubemap cubemap = GetLayerCubemap(_viewState.Layer);
            bool waterApi = WaterModAPI.Registered;
            WaterVisible = waterApi;
            WaterEnabled = waterApi && cubemap != null && cubemap.WaterOverlay != null;
        }

        public void SelectLayer(CartographyLayer layer)
        {
            if (!_layerSelectorExpanded && layer == _viewState.Layer)
            {
                _layerSelectorExpanded = true;
                UpdateLayerItems();
                return;
            }

            if (layer != _viewState.Layer)
            {
                _viewState.Layer = layer;
                _textureDirty = true;
                UpdateMapRequests();
            }
            _layerSelectorExpanded = false;
            UpdateLayerItems();
        }

        void TogglePlanetOrientationLock()
        {
            PlanetOrientationMode next = _viewState.PlanetOrientation == PlanetOrientationMode.NorthLocked
                ? PlanetOrientationMode.HeadingLocked
                : PlanetOrientationMode.NorthLocked;
            SetPlanetOrientationLock(next);
        }

        float GetOrientationIconRotation()
        {
            float northScreenRotation;
            if (_mapRenderer == null ||
                !_mapRenderer.TryGetPlanetNorthScreenRotation(
                    _mapData,
                    _viewState,
                    out northScreenRotation))
            {
                northScreenRotation = 0f;
            }

            return COMPASS_ICON_NORTH_CORRECTION + northScreenRotation;
        }

        void SetPlanetOrientationLock(PlanetOrientationMode mode)
        {
            if (mode == PlanetOrientationMode.Free)
                return;

            _viewState.PlanetOrientation = mode;
            _viewState.HasPlanetFreeReferenceUpWorld = false;
            ResetPlanetOrbit();
            _textureDirty = true;
        }

        void UnlockPlanetOrientation()
        {
            if (_viewState.PlanetOrientation == PlanetOrientationMode.Free)
                return;

            PlanetSnapshot planet = ResolveFocusedPlanet();
            Vector3D referenceUp = Vector3D.Up;
            if (_viewState.PlanetOrientation == PlanetOrientationMode.HeadingLocked &&
                _mapData.HasPlayerWorldMatrix)
            {
                referenceUp = _mapData.PlayerWorldMatrix.Forward;
            }
            else if (planet != null && planet.Planet != null)
            {
                // North is planet-local -Y (Acrux's CartographyMapRenderer.PlanetLocalNorth), not the planet's Up.
                referenceUp = planet.Planet.WorldMatrix.Down;
            }

            if (referenceUp.Normalize() <= 1e-9)
                referenceUp = Vector3D.Up;
            _viewState.PlanetFreeReferenceUpWorld = referenceUp;
            _viewState.HasPlanetFreeReferenceUpWorld = true;
            _viewState.PlanetOrientation = PlanetOrientationMode.Free;
        }

        void ToggleFollow()
        {
            SetFollowPlayer(!_viewState.FollowPlayer);
        }

        void SetFollowPlayer(bool follow)
        {
            if (_viewState.FollowPlayer == follow)
                return;

            if (follow)
            {
                _viewState.FollowPlayer = true;
                _viewState.HasStaticCameraPosition = false;
                _viewState.Yaw = 0f;
                _viewState.Pitch = 0f;
                ResetPlanetOrbit();
            }
            else
            {
                _mapData.UpdatePlayerPosition();
                _viewState.StaticCameraPosition = _mapData.PlayerPosition;
                _viewState.HasStaticCameraPosition = true;
                _viewState.FollowPlayer = false;
            }
            _textureDirty = true;
        }

        void ToggleWater()
        {
            if (!WaterEnabled)
                return;
            _viewState.WaterVisible = !_viewState.WaterVisible;
            _textureDirty = true;
        }

        void ToggleWaypoints()
        {
            // The on-map waypoint button edits the current full-screen map mode,
            // while the Settings-level FullScreen switch remains the master gate.
            MapDisplayConfig modeConfig = _config.PlanetaryMap;
            if (!ResolveFullScreenGpsVisible())
            {
                _config.FullScreen.DisplayMyGps = true;
                modeConfig.DisplayMyGps = true;
            }
            else
            {
                modeConfig.DisplayMyGps = false;
            }
            _waypointsVisible = ResolveFullScreenGpsVisible();
            _textureDirty = true;
            SaveLocalConfig();
            PlayUiSound(HudClickSound);
        }

        public void OrbitMap(Vector2 delta)
        {
            const float sensitivity = 0.009f;

            UnlockPlanetOrientation();
            RotatePlanetOrbit(-delta.X * sensitivity, delta.Y * sensitivity);

            if (_viewState.FollowPlayer)
                SetFollowPlayer(false);
            _textureDirty = true;
        }

        public void RollMap(float angle)
        {
            if (_viewState.SurfaceViewActive)
            {
                TurnSurfaceView(angle);
                return;
            }
            UnlockPlanetOrientation();

            Vector3 view = _viewState.PlanetOrbitView;
            Vector3 right = _viewState.PlanetOrbitRight;
            Vector3 up = _viewState.PlanetOrbitUp;
            RotateCameraRoll(ref view, ref right, ref up, angle);
            _viewState.PlanetOrbitView = view;
            _viewState.PlanetOrbitRight = right;
            _viewState.PlanetOrbitUp = up;

            if (_viewState.FollowPlayer)
                SetFollowPlayer(false);
            _textureDirty = true;
        }

        void FocusPlanet(long planetId)
        {
            if (_mapData.FindPlanet(planetId) == null)
                return;
            _viewState.FocusedPlanetId = planetId;
            _textureDirty = true;
        }

        int FindRoadNode(Vector2 screenPoint)
        {
            var markers = PresentedOverlays.Nodes;
            for (int i = markers.Count - 1; i >= 0; --i)
            {
                int index = markers[i].Hash - 1;
                if (index >= 0 && index < _roadNodes.Count &&
                    Vector2.DistanceSquared(markers[i].ScreenPosition, screenPoint) <= 144f)
                    return index;
            }
            return -1;
        }

        void RefreshAutomaticRoadTangents(PlanetSnapshot planet, int first, int last)
        {
            if (planet == null) return;
            for (int i = Math.Max(0, first); i <= Math.Min(last, _roadNodes.Count - 1); i++)
            {
                Vector3D normal = Vector3D.Normalize(_roadNodes[i] - planet.Position);
                Vector3D incoming = Vector3D.Zero;
                Vector3D outgoing = Vector3D.Zero;
                if (i > 0)
                    incoming = _roadNodes[i] - _roadNodes[i - 1];
                if (i + 1 < _roadNodes.Count)
                    outgoing = _roadNodes[i + 1] - _roadNodes[i];
                incoming -= normal * Vector3D.Dot(incoming, normal);
                outgoing -= normal * Vector3D.Dot(outgoing, normal);
                if (incoming.LengthSquared() > 1e-12) incoming.Normalize();
                if (outgoing.LengthSquared() > 1e-12) outgoing.Normalize();
                Vector3D smooth = incoming + outgoing;
                if (smooth.LengthSquared() < 1e-12)
                    smooth = outgoing.LengthSquared() > 1e-12 ? outgoing : incoming;
                if (smooth.LengthSquared() < 1e-12)
                    smooth = Vector3D.Cross(Vector3D.Up, normal);
                if (smooth.LengthSquared() < 1e-12)
                    smooth = Vector3D.Cross(Vector3D.Right, normal);
                smooth.Normalize();

                Vector3D entry = _roadIncomingEdited[i] ? _roadIncomingDirections[i] : smooth;
                Vector3D exit = _roadOutgoingEdited[i] ? _roadOutgoingDirections[i] : smooth;
                entry -= normal * Vector3D.Dot(entry, normal);
                exit -= normal * Vector3D.Dot(exit, normal);
                _roadIncomingDirections[i] = entry.LengthSquared() > 1e-12
                    ? Vector3D.Normalize(entry) : smooth;
                _roadOutgoingDirections[i] = exit.LengthSquared() > 1e-12
                    ? Vector3D.Normalize(exit) : smooth;
            }
        }

        /// <summary>
        /// Routes from <paramref name="pointA"/> to <paramref name="pointB"/> with A*,
        /// balancing length, grade and earthwork. When the draft is exactly those two
        /// nodes they are kept as they are (tangent edits included) and the route is
        /// inserted between them; otherwise the draft is replaced. Width, shoulder
        /// angle, max slope and segment distance always come from the editor settings,
        /// since those are what the push is validated against. A route with any
        /// segment over the slope limit, or that crosses itself, is rejected and the
        /// draft is left unchanged.
        /// </summary>
        public bool CreateRoad(Vector3D pointA, Vector3D pointB, RoadRouteWeights weights)
        {
            if (_pushPending || MarkMode) return false;
            RoadDraftSnapshot before = CaptureRoadDraft();
            if (!RouteDraft(pointA, pointB, weights)) return false;
            RecordRoadHistory(before);
            return true;
        }

        /// <summary>
        /// Inserts a validated route into the draft and sets the status. On failure the
        /// draft is left as it was. Records no history; callers decide the undo step.
        /// </summary>
        bool RouteDraft(Vector3D pointA, Vector3D pointB, RoadRouteWeights weights)
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            weights = weights != null ? weights.Clone() : new RoadRouteWeights();
            weights.RoadWidthMeters = RoadWidthMeters;
            weights.FillAngleDegrees = RoadFillAngleDegrees;
            weights.MaxGradeDegrees = RoadMaximumSlopeDegrees;
            weights.SegmentDistanceMeters = RoadSegmentDistanceMeters;
            RoadRouteResult route = RoadAutoRouter.CreateRoad(planet, pointA, pointB, weights);
            if (!route.Success)
            {
                RoadEditorStatus = LocHelper.Format("HighwayEngineer_Status_AutoRoadRejected", route.Error);
                return false;
            }

            RoadDraftSnapshot before = CaptureRoadDraft();
            bool keepEnds = _roadNodes.Count == 2 &&
                            Vector3D.DistanceSquared(_roadNodes[0], pointA) < 1e-6 &&
                            Vector3D.DistanceSquared(_roadNodes[1], pointB) < 1e-6;
            if (!keepEnds)
            {
                ClearRoadDraft();
                AddAutoRoadNode(_roadNodes.Count, pointA);
                AddAutoRoadNode(_roadNodes.Count, pointB);
            }
            for (int i = 1; i < route.Nodes.Count - 1; i++)
                AddAutoRoadNode(i, route.Nodes[i]);
            _editingNode = -1;
            RefreshAutomaticRoadTangents(planet, 0, _roadNodes.Count - 1);

            string invalid = FindInvalidRoadSegment(planet);
            if (invalid != null)
            {
                RestoreRoadDraft(before);
                RoadEditorStatus = LocHelper.Format("HighwayEngineer_Status_AutoRoadRejected", invalid);
                return false;
            }
            RoadEditorStatus = LocHelper.Format("HighwayEngineer_Status_AutoRoad", _roadNodes.Count - 1,
                route.LengthMeters / 1000.0, route.ClimbMeters, route.EarthworkCubicMeters);
            if (route.Attempts > 1)
                RoadEditorStatus = LocHelper.Format("HighwayEngineer_Status_AutoRoadRaised", RoadEditorStatus);
            AppendIntersectionStatus(RefreshRoadIntersections());
            _textureDirty = true;
            return true;
        }

        void AddAutoRoadNode(int index, Vector3D position)
        {
            _roadNodes.Insert(index, position);
            _roadIncomingDirections.Insert(index, Vector3D.Zero);
            _roadOutgoingDirections.Insert(index, Vector3D.Zero);
            _roadIncomingEdited.Insert(index, false);
            _roadOutgoingEdited.Insert(index, false);
            _roadIntersections.Insert(index, false);
            _roadBridgeModes.Insert(index, BridgeSegmentMode.Auto);
            _roadNodeLifts.Insert(index, 0.0);
        }

        /// <summary>Why the draft cannot be pushed as is, or null when every segment is valid.</summary>
        string FindInvalidRoadSegment(PlanetSnapshot planet)
        {
            if (planet == null) return LocHelper.GetLoc("HighwayEngineer_Invalid_NoPlanet");
            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve))
                    return LocHelper.Format("HighwayEngineer_Invalid_NoCurve", segment);
                if (!IsBridgeSegment(segment) && RoadCurveExceedsSlope(planet, curve, segment))
                    return LocHelper.Format("HighwayEngineer_Invalid_Slope", segment, RoadMaximumSlopeDegrees);
            }
            if (RoadCrossesItself()) return LocHelper.GetLoc("HighwayEngineer_Invalid_Crossing");
            return null;
        }

        /// <summary>Weights used by the planner's auto-route flyout.</summary>
        public RoadRouteWeights AutoRouteWeights { get; } = new RoadRouteWeights();

        AutoRoutePanelViewModel _autoRoutePanel;

        /// <summary>The auto-route flyout's state; changing a weight reroutes the live session.</summary>
        public AutoRoutePanelViewModel AutoRoutePanel =>
            _autoRoutePanel ?? (_autoRoutePanel = new AutoRoutePanelViewModel(
                AutoRouteWeights, MinimapLocalConfig.Current.Road, RequestAutoRoute));

        // Live auto-route session: while the flyout is open, every weight change
        // reroutes between the draft's first and last node; intermediate nodes are
        // dropped. The whole session is one undo step back to the draft as it was.
        const int AutoRouteSettleFrames = 6;
        RoadDraftSnapshot _autoRouteOriginal;
        RoadDraftSnapshot _autoRouteBase;
        bool _autoRouteDirty;
        int _autoRouteDirtyFrame;

        public bool AutoRouteActive => _autoRouteBase != null;

        /// <summary>Auto-routing needs a draft with a first and a last node (A and B).</summary>
        public bool CanAutoRoute => !_pushPending && !MarkMode &&
                                    (AutoRouteActive || _roadNodes.Count >= 2);

        /// <summary>
        /// Starts a live session: keeps only the first and last node (with any tangent
        /// edits) and routes between them with the current weights.
        /// </summary>
        public void BeginAutoRoute()
        {
            if (AutoRouteActive || !CanAutoRoute) return;
            _autoRouteOriginal = CaptureRoadDraft();
            KeepOnlyEndNodes();
            _autoRouteBase = CaptureRoadDraft();
            RerouteFromBase();
        }

        void KeepOnlyEndNodes()
        {
            int last = _roadNodes.Count - 1;
            if (last < 2) return;
            _roadNodes.RemoveRange(1, last - 1);
            _roadIncomingDirections.RemoveRange(1, last - 1);
            _roadOutgoingDirections.RemoveRange(1, last - 1);
            _roadIncomingEdited.RemoveRange(1, last - 1);
            _roadOutgoingEdited.RemoveRange(1, last - 1);
            _roadIntersections.RemoveRange(1, last - 1);
            _roadBridgeModes.RemoveRange(1, last - 1);
            _roadBridgeModes[1] = BridgeSegmentMode.Auto;
            _roadNodeLifts.RemoveRange(1, last - 1);
            _editingNode = -1;
            RefreshAutomaticRoadTangents(ResolveFocusedPlanet(), 0, 1);
        }

        /// <summary>Reroutes once the weights stop changing for a few frames.</summary>
        public void RequestAutoRoute()
        {
            if (!AutoRouteActive) return;
            _autoRouteDirty = true;
            _autoRouteDirtyFrame = _frame;
        }

        /// <summary>Keeps the current route and records the session as one undo step.</summary>
        public void EndAutoRoute()
        {
            if (!AutoRouteActive) return;
            if (_autoRouteDirty && CanAutoRoute) RerouteFromBase();
            _autoRouteDirty = false;
            if (!_autoRouteOriginal.SameRoad(CaptureRoadDraft()))
                RecordRoadHistory(_autoRouteOriginal);
            _autoRouteOriginal = null;
            _autoRouteBase = null;
        }

        void RerouteFromBase()
        {
            _autoRouteDirty = false;
            RestoreRoadDraft(_autoRouteBase);
            Vector3D[] ends = _autoRouteBase.Nodes;
            RouteDraft(ends[0], ends[ends.Length - 1], AutoRouteWeights);
            AutoRoutePanel.Status = RoadEditorStatus;
        }

        void UpdateAutoRoute()
        {
            if (!AutoRouteActive) return;
            if (_pushPending || MarkMode)
            {
                _autoRouteDirty = false;
                EndAutoRoute();
            }
            else if (_autoRouteDirty && _frame - _autoRouteDirtyFrame >= AutoRouteSettleFrames)
                RerouteFromBase();
        }

        public void RemoveRoadNode(int index)
        {
            if (_pushPending || MarkMode ||
                index < 0 || index >= _roadNodes.Count)
                return;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            RecordRoadHistory();
            _roadNodes.RemoveAt(index);
            _roadIncomingDirections.RemoveAt(index);
            _roadOutgoingDirections.RemoveAt(index);
            _roadIncomingEdited.RemoveAt(index);
            _roadOutgoingEdited.RemoveAt(index);
            _roadIntersections.RemoveAt(index);
            RemoveBridgeMode(index);
            _roadNodeLifts.RemoveAt(index);
            RefreshAutomaticRoadTangents(planet, index - 1, index);
            _editingNode = -1;
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_NodeRemoved");
            AppendIntersectionStatus(RefreshRoadIntersections());
            _textureDirty = true;
        }

        public void SelectRoadNode(int index)
        {
            if (_pushPending || MarkMode ||
                index < 0 || index >= _roadNodes.Count)
                return;
            _editingNode = index;
            _activeTangent = index == _roadNodes.Count - 1 && index > 0
                ? RoadDragMode.IncomingTangent : RoadDragMode.OutgoingTangent;
            RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_TangentHint");
            _textureDirty = true;
        }

        public void CreatePlanetSurfaceWaypoint(Vector2 screenPoint)
        {
            if (!RoadsVisible) return;
            if (_mapRenderer == null || _pushPending) return;
            if (MarkMode)
            {
                ToggleRoadMark(screenPoint);
                return;
            }
            int hit = FindRoadNode(screenPoint);
            if (hit >= 0)
            {
                // Left click selects a node for tangent editing, never adds a duplicate.
                SelectRoadNode(hit);
                return;
            }
            // A click on the middle of an existing segment inserts a node instead
            // of extending the chain. Node hit-testing has priority.
            int segmentIndex;
            Vector3D segmentPoint;
            Vector3D segmentTangent;
            if (TryFindRoadSegment(screenPoint, out segmentIndex, out segmentPoint,
                    out segmentTangent))
            {
                if (segmentTangent.LengthSquared() < 1e-16)
                    return;
                RecordRoadHistory();
                _roadNodes.Insert(segmentIndex, segmentPoint);
                _roadIncomingDirections.Insert(segmentIndex, segmentTangent);
                _roadOutgoingDirections.Insert(segmentIndex, segmentTangent);
                _roadIncomingEdited.Insert(segmentIndex, false);
                _roadOutgoingEdited.Insert(segmentIndex, false);
                _roadIntersections.Insert(segmentIndex, false);
                // Both halves stay what the segment was.
                _roadBridgeModes.Insert(segmentIndex, _roadBridgeModes[segmentIndex]);
                _roadNodeLifts.Insert(segmentIndex, 0.0);
                RefreshAutomaticRoadTangents(ResolveFocusedPlanet(), segmentIndex - 1, segmentIndex + 1);
                if (_editingNode >= segmentIndex) _editingNode++;
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_NodeInserted");
                AppendIntersectionStatus(RefreshRoadIntersections());
                _textureDirty = true;
                return;
            }
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null) return;
            Vector3D position;
            Vector3D snappedTangent;
            bool snapped = TrySnapToCommittedRoad(screenPoint, planet,
                out position, out snappedTangent);
            if (!snapped && !_mapRenderer.TryGetSurfacePosition(_mapBounds, screenPoint,
                    _mapData, _viewState, out position)) return;
            Vector3D normal = Vector3D.Normalize(position - planet.Position);
            Vector3D tangent = snapped ? snappedTangent : Vector3D.Cross(Vector3D.Up, normal);
            if (tangent.LengthSquared() < 1e-8) tangent = Vector3D.Cross(Vector3D.Right, normal);
            tangent.Normalize();
            if (_roadNodes.Count > 0)
            {
                tangent = position - _roadNodes[_roadNodes.Count - 1];
                tangent -= normal * Vector3D.Dot(tangent, normal);
                if (tangent.LengthSquared() < 1e-8) return;
                tangent.Normalize();
            }
            RecordRoadHistory();
            _roadNodes.Add(position);
            _roadIncomingDirections.Add(tangent);
            _roadOutgoingDirections.Add(tangent);
            _roadIncomingEdited.Add(false);
            _roadOutgoingEdited.Add(false);
            _roadIntersections.Add(false);
            _roadBridgeModes.Add(BridgeSegmentMode.Auto);
            _roadNodeLifts.Add(0.0);
            RefreshAutomaticRoadTangents(planet, _roadNodes.Count - 2, _roadNodes.Count - 1);
            RoadEditorStatus = LocHelper.GetLoc(snapped ? "HighwayEngineer_Status_NodeSnapped" : "HighwayEngineer_Status_NodeAdded");
            AppendIntersectionStatus(RefreshRoadIntersections());
            _textureDirty = true;
        }


        readonly List<RoadCrossing> _roadCrossings = new List<RoadCrossing>();

        void AppendIntersectionStatus(int added)
        {
            if (added > 0)
                RoadEditorStatus = LocHelper.Format(added == 1 ? "HighwayEngineer_Status_Intersections_One" : "HighwayEngineer_Status_Intersections_Many",
                    RoadEditorStatus, added);
        }

        /// <summary>
        /// Rebuilds the automatic intersection nodes where the draft crosses a
        /// built road. Each pushed segment's endpoints keep the terrain height
        /// there, so a node on the existing road preserves that road's surface.
        /// Bridges pass over roads (and roads under bridges) without a node; a
        /// draft bridge joins a built bridge only where their decks are within
        /// <see cref="BridgeJunctionMaxHeightMeters"/>.
        /// Runs once per committed edit, never during a drag.
        /// </summary>
        int RefreshRoadIntersections()
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Planet == null || planet.Radius <= 0) return 0;
            EnsureCommittedRoads(planet);

            bool stripped = false;
            for (int i = _roadNodes.Count - 1; i >= 0; i--)
            {
                if (!_roadIntersections[i]) continue;
                _roadNodes.RemoveAt(i);
                _roadIncomingDirections.RemoveAt(i);
                _roadOutgoingDirections.RemoveAt(i);
                _roadIncomingEdited.RemoveAt(i);
                _roadOutgoingEdited.RemoveAt(i);
                _roadIntersections.RemoveAt(i);
                RemoveBridgeMode(i);
                _roadNodeLifts.RemoveAt(i);
                if (_editingNode == i) _editingNode = -1;
                else if (_editingNode > i) _editingNode--;
                stripped = true;
            }
            if (stripped)
                RefreshAutomaticRoadTangents(planet, 0, _roadNodes.Count - 1);
            if (_committedRoads.Count == 0 || _roadNodes.Count < 2) return 0;

            // Inserting nodes from the end backwards leaves the segments before each
            // insertion as they were, so their decks are found once.
            double[] deckStart, deckEnd;
            DraftDeckHeights(planet, out deckStart, out deckEnd);

            // Skip crossings at (or right beside) an existing node, e.g. one the
            // user already snapped onto the road.
            double minimumDot = Math.Cos(Math.Max(RoadWidthMeters * 0.5, 1.0) / planet.Radius);
            int added = 0;
            for (int segment = _roadNodes.Count - 1; segment >= 1; segment--)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve)) continue;
                bool draftBridge = !double.IsNaN(deckStart[segment]);
                _junctions.Clear();
                foreach (RoadCurve road in _committedRoads)
                {
                    bool builtBridge = road.BridgeId != 0;
                    if (draftBridge != builtBridge) continue;
                    _roadCrossings.Clear();
                    RoadGeometry.FindRoadCrossings(curve, road, _roadCrossings);
                    foreach (RoadCrossing crossing in _roadCrossings)
                    {
                        if (builtBridge)
                        {
                            double along = road.Arc <= 0.0 ? 0.0 : MathHelper.Clamp(Math.Acos(MathHelper.Clamp(
                                Vector3D.Dot(road.Start, crossing.Direction), -1.0, 1.0)) / road.Arc, 0.0, 1.0);
                            double built = road.StartHeight + (road.EndHeight - road.StartHeight) * along;
                            double draft = deckStart[segment] +
                                           (deckEnd[segment] - deckStart[segment]) * crossing.DraftT;
                            if (Math.Abs(draft - built) >= BridgeJunctionMaxHeightMeters) continue;
                        }
                        _junctions.Add(crossing);
                    }
                }
                // Insert from the segment's end backwards so each lands at `segment`.
                _junctions.Sort((a, b) => b.DraftT.CompareTo(a.DraftT));
                Vector3D previous = curve.End;
                foreach (RoadCrossing crossing in _junctions)
                {
                    if (Vector3D.Dot(crossing.Direction, curve.Start) > minimumDot ||
                        Vector3D.Dot(crossing.Direction, previous) > minimumDot)
                        continue;
                    Vector3D surface = planet.Planet.GetClosestSurfacePointGlobal(
                        planet.Position + crossing.Direction * planet.MaximumRadius);
                    if (Vector3D.DistanceSquared(surface, planet.Position) <= 1.0) continue;
                    Vector3D tangent = curve.TangentAt(crossing.DraftT);
                    _roadNodes.Insert(segment, surface);
                    _roadIncomingDirections.Insert(segment, tangent);
                    _roadOutgoingDirections.Insert(segment, tangent);
                    _roadIncomingEdited.Insert(segment, false);
                    _roadOutgoingEdited.Insert(segment, false);
                    _roadIntersections.Insert(segment, true);
                    _roadBridgeModes.Insert(segment, _roadBridgeModes[segment]);
                    _roadNodeLifts.Insert(segment, 0.0);
                    if (_editingNode >= segment) _editingNode++;
                    previous = crossing.Direction;
                    added++;
                }
            }
            if (added > 0)
                RefreshAutomaticRoadTangents(planet, 0, _roadNodes.Count - 1);
            return added;
        }

        // The crossings of one draft segment that become intersection nodes.
        readonly List<RoadCrossing> _junctions = new List<RoadCrossing>();

        /// <summary>
        /// The deck height above the average radius at each draft segment's ends (by end-node
        /// index), as its bridge's deck runs (see <see cref="DeckHeights"/>); NaN for road.
        /// </summary>
        void DraftDeckHeights(PlanetSnapshot planet, out double[] start, out double[] end)
        {
            UpdateBridges(planet);
            int count = _roadNodes.Count;
            start = new double[count];
            end = new double[count];
            for (int i = 0; i < count; i++)
                start[i] = end[i] = double.NaN;
            for (int segment = 1; segment < count; segment++)
            {
                if (!IsBridgeSegment(segment)) continue;
                int first = segment;
                while (segment + 1 < count && IsBridgeSegment(segment + 1))
                    segment++;
                double[] deck = DeckHeights(planet, first - 1, segment);
                for (int i = first; i <= segment; i++)
                {
                    start[i] = deck[i - first];
                    end[i] = deck[i - first + 1];
                }
            }
        }

        // Draft segments (by end-node index) that cross another draft segment.
        // A self-crossing plan is invalid. Cached until the draft geometry changes.
        readonly HashSet<int> _selfCrossingSegments = new HashSet<int>();
        readonly List<RoadCurve> _selfCrossingCurves = new List<RoadCurve>();
        readonly List<int> _selfCrossingIndices = new List<int>();
        long _selfCrossingHash;
        bool _selfCrossingCached;

        bool RoadCrossesItself()
        {
            return UpdateSelfCrossings(ResolveFocusedPlanet());
        }

        bool UpdateSelfCrossings(PlanetSnapshot planet)
        {
            long hash = planet == null ? 0L : planet.EntityId;
            unchecked
            {
                for (int i = 0; i < _roadNodes.Count; i++)
                {
                    hash = HashVector(hash, _roadNodes[i]);
                    hash = HashVector(hash, _roadIncomingDirections[i]);
                    hash = HashVector(hash, _roadOutgoingDirections[i]);
                }
                hash = hash * 31 + _roadNodes.Count;
            }
            if (_selfCrossingCached && hash == _selfCrossingHash)
                return _selfCrossingSegments.Count > 0;
            _selfCrossingCached = true;
            _selfCrossingHash = hash;
            _selfCrossingSegments.Clear();
            if (planet == null || _roadNodes.Count < 3)
                return false;

            _selfCrossingCurves.Clear();
            _selfCrossingIndices.Clear();
            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve)) continue;
                _selfCrossingCurves.Add(curve);
                _selfCrossingIndices.Add(segment);
            }
            RoadGeometry.FindSelfCrossings(_selfCrossingCurves, _selfCrossingIndices,
                _selfCrossingSegments);
            return _selfCrossingSegments.Count > 0;
        }

        static long HashVector(long hash, Vector3D value)
        {
            unchecked
            {
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(value.X);
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(value.Y);
                return hash * 31 + BitConverter.DoubleToInt64Bits(value.Z);
            }
        }

        bool TryGetRoadCurve(PlanetSnapshot planet, int segment, out RoadCurve curve)
        {
            curve = new RoadCurve();
            if (planet == null || segment < 1 || segment >= _roadNodes.Count)
                return false;
            Vector3D start = _roadNodes[segment - 1] - planet.Position;
            Vector3D end = _roadNodes[segment] - planet.Position;
            if (start.Normalize() < 1e-8 || end.Normalize() < 1e-8)
                return false;
            double arc = Math.Acos(MathHelper.Clamp(Vector3D.Dot(start, end), -1.0, 1.0));
            if (arc < 1e-7 || arc > Math.PI - 1e-5)
                return false;
            Vector3D startTangent = _roadOutgoingDirections[segment - 1] -
                                    start * Vector3D.Dot(_roadOutgoingDirections[segment - 1], start);
            Vector3D endTangent = _roadIncomingDirections[segment] -
                                  end * Vector3D.Dot(_roadIncomingDirections[segment], end);
            if (startTangent.Normalize() < 1e-8 || endTangent.Normalize() < 1e-8)
                return false;
            curve.Start = start;
            curve.End = end;
            curve.StartTangent = startTangent;
            curve.EndTangent = endTangent;
            curve.Arc = arc;
            return true;
        }

        // ---- Bridges in the draft ----

        // A run of segments could be a bridge when the terrain under a deck laid straight (in
        // height) from its first node to its last falls this far below it somewhere, and nowhere
        // rises more than the tolerance above it.
        const double BridgeMinGapMeters = 12.0;
        const double BridgeTerrainToleranceMeters = RoadLimits.BridgeTerrainToleranceMeters;
        // A draft bridge crossing a built bridge joins it when their decks are this close there.
        const double BridgeJunctionMaxHeightMeters = 10.0;
        // Each "+" / "-" on a node moves its bridge deck this much; a lift under the minimum is removed.
        const double BridgeLiftStepMeters = 10.0;
        const double BridgeLiftMinMeters = 5.0;
        // The server's limits (BridgeBuilder): shortest bridge and steepest deck, which is also
        // no steeper than the road's maximum slope.
        const double BridgeMinLengthMeters = 10.0;
        const double BridgeMaxGradeLimitDegrees = 20.0;
        double BridgeMaxGradeDegrees => Math.Min(BridgeMaxGradeLimitDegrees, Math.Max(RoadMaximumSlopeDegrees, 0.01));

        sealed class SegmentProfile
        {
            public RoadCurve Curve;
            public double Length;
            /// <summary>Terrain height at evenly spaced points along the segment, both ends included.</summary>
            public double[] Heights;
            public double StartSurface, EndSurface;
            public bool SlopeKnown, SlopeBesideBridge, ExceedsSlope;
            public double SlopeMaxDegrees, SlopeSegmentDistance;
        }

        // Terrain profile of each segment (by end node), kept while its curve is unchanged.
        readonly List<SegmentProfile> _segmentProfiles = new List<SegmentProfile>();
        // Per node: the segment ending there could be a bridge.
        readonly List<bool> _bridgeCandidates = new List<bool>();
        readonly HashSet<int> _invalidBridgeSegments = new HashSet<int>();
        string _bridgeError;
        long _bridgeHash;
        bool _bridgeCached;

        /// <summary>Whether a draft segment (by end node) is planned as a bridge.</summary>
        bool IsBridgeSegment(int segment)
        {
            if (segment < 1 || segment >= _roadBridgeModes.Count) return false;
            BridgeSegmentMode mode = _roadBridgeModes[segment];
            return mode == BridgeSegmentMode.Bridge ||
                   mode == BridgeSegmentMode.Auto && AutoBridge && segment < _bridgeCandidates.Count &&
                   _bridgeCandidates[segment];
        }

        /// <summary>Why the draft's bridges cannot be built (too short or steep), or null.</summary>
        public string BridgeError
        {
            get
            {
                UpdateBridges(ResolveFocusedPlanet());
                return _bridgeError;
            }
        }

        public bool DraftHasBridges
        {
            get
            {
                UpdateBridges(ResolveFocusedPlanet());
                for (int segment = 1; segment < _roadNodes.Count; segment++)
                    if (IsBridgeSegment(segment)) return true;
                return false;
            }
        }

        public bool DraftHasRoads
        {
            get
            {
                UpdateBridges(ResolveFocusedPlanet());
                for (int segment = 1; segment < _roadNodes.Count; segment++)
                    if (!IsBridgeSegment(segment)) return true;
                return false;
            }
        }

        /// <summary>
        /// Finds the segments that could be bridges, then checks the bridges the draft plans.
        /// Runs again only when the draft, its bridge settings or automatic bridges change.
        /// </summary>
        void UpdateBridges(PlanetSnapshot planet)
        {
            long hash = planet == null ? 0L : planet.EntityId;
            unchecked
            {
                for (int i = 0; i < _roadNodes.Count; i++)
                {
                    hash = HashVector(hash, _roadNodes[i]);
                    hash = HashVector(hash, _roadIncomingDirections[i]);
                    hash = HashVector(hash, _roadOutgoingDirections[i]);
                    hash = hash * 31 + (int)_roadBridgeModes[i];
                    hash = hash * 31 + BitConverter.DoubleToInt64Bits(_roadNodeLifts[i]);
                }
                hash = hash * 31 + _roadNodes.Count;
                hash = hash * 31 + (AutoBridge ? 1 : 0);
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(BridgeMaxGradeDegrees);
                hash = hash * 31 + (int)BridgeSettings.Design;
            }
            if (_bridgeCached && hash == _bridgeHash) return;
            _bridgeCached = true;
            _bridgeHash = hash;
            _bridgeCandidates.Clear();
            _invalidBridgeSegments.Clear();
            _bridgeError = null;
            for (int i = 0; i < _roadNodes.Count; i++)
                _bridgeCandidates.Add(false);
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose || _roadNodes.Count < 2)
                return;

            while (_segmentProfiles.Count < _roadNodes.Count) _segmentProfiles.Add(null);
            if (_segmentProfiles.Count > _roadNodes.Count)
                _segmentProfiles.RemoveRange(_roadNodes.Count, _segmentProfiles.Count - _roadNodes.Count);
            for (int segment = 1; segment < _roadNodes.Count; segment++)
                _segmentProfiles[segment] = Profile(planet, segment, _segmentProfiles[segment]);

            // Longest runs first: from each node, the furthest node a bridge could reach.
            int last = _roadNodes.Count - 1;
            for (int from = 0; from < last;)
            {
                int reach = -1;
                double length = 0.0;
                for (int to = from + 1; to <= last; to++)
                {
                    if (_segmentProfiles[to] == null || _roadBridgeModes[to] == BridgeSegmentMode.Road) break;
                    length += _segmentProfiles[to].Length;
                    if (CouldBridge(planet, from, to, length)) reach = to;
                }
                if (reach < 0)
                {
                    from++;
                    continue;
                }
                for (int segment = from + 1; segment <= reach; segment++)
                    _bridgeCandidates[segment] = true;
                from = reach;
            }

            // The planned bridges must fit the server's limits.
            for (int segment = 1; segment <= last; segment++)
            {
                if (!IsBridgeSegment(segment)) continue;
                int first = segment;
                double length = 0.0;
                for (; segment <= last && IsBridgeSegment(segment); segment++)
                    length += _segmentProfiles[segment] == null ? 0.0 : _segmentProfiles[segment].Length;
                int end = segment - 1;
                string problem = length < BridgeMinLengthMeters
                    ? LocHelper.Format("HighwayEngineer_BridgeDraft_TooShort", BridgeMinLengthMeters)
                    : DeckTooSteep(planet, first - 1, end)
                        ? LocHelper.Format("HighwayEngineer_BridgeDraft_TooSteep", BridgeMaxGradeDegrees)
                        : DeckClearanceProblem(planet, first - 1, end);
                if (problem == null) continue;
                for (int i = first; i <= end; i++)
                    _invalidBridgeSegments.Add(i);
                if (_bridgeError == null)
                    _bridgeError = first == end
                        ? LocHelper.Format("HighwayEngineer_BridgeDraft_ErrorSegment", first, problem)
                        : LocHelper.Format("HighwayEngineer_BridgeDraft_ErrorSegments", first, end, problem);
            }
        }

        /// <summary>A segment's terrain profile: the cached one while its curve is unchanged.</summary>
        SegmentProfile Profile(PlanetSnapshot planet, int segment, SegmentProfile cached)
        {
            RoadCurve curve;
            if (!TryGetRoadCurve(planet, segment, out curve)) return null;
            if (cached != null && cached.Curve.Start == curve.Start && cached.Curve.End == curve.End &&
                cached.Curve.StartTangent == curve.StartTangent && cached.Curve.EndTangent == curve.EndTangent)
                return cached;
            int steps = (int)Math.Max(8, Math.Min(96, Math.Ceiling(curve.Arc * planet.Radius / 4.0)));
            var profile = new SegmentProfile
            {
                Curve = curve, Heights = new double[steps + 1],
                StartSurface = RoadGeometry.SurfaceHeight(planet.Planet, curve.Start),
                EndSurface = RoadGeometry.SurfaceHeight(planet.Planet, curve.End)
            };
            Vector3D previous = curve.Start;
            for (int i = 0; i <= steps; i++)
            {
                Vector3D direction = curve.Evaluate(i / (double)steps);
                profile.Heights[i] = SampleRoadTerrainHeight(planet, direction);
                profile.Length += planet.Radius * Math.Acos(MathHelper.Clamp(Vector3D.Dot(previous, direction), -1.0, 1.0));
                previous = direction;
            }
            return profile;
        }

        /// <summary>Whether a bridge from node <paramref name="from"/> to node <paramref name="to"/> crosses a gap.</summary>
        bool CouldBridge(PlanetSnapshot planet, int from, int to, double length)
        {
            if (length < BridgeMinLengthMeters || DeckTooSteep(planet, from, to)) return false;
            double[] deck = DeckHeights(planet, from, to);
            double deepest = 0.0;
            for (int segment = from + 1; segment <= to; segment++)
            {
                SegmentProfile profile = _segmentProfiles[segment];
                double start = deck[segment - 1 - from], end = deck[segment - from];
                int steps = profile.Heights.Length - 1;
                for (int i = 0; i <= steps; i++)
                {
                    double below = start + (end - start) * i / steps - profile.Heights[i];
                    if (below < -BridgeTerrainToleranceMeters) return false;
                    deepest = Math.Max(deepest, below);
                }
            }
            return deepest >= BridgeMinGapMeters;
        }

        /// <summary>
        /// The deck's height above the average radius at nodes <paramref name="from"/> to
        /// <paramref name="to"/> (a bridge's ends): pinned there and at lifted nodes, straight
        /// between pins by length along the segments, as the server plans it.
        /// </summary>
        double[] DeckHeights(PlanetSnapshot planet, int from, int to)
        {
            var heights = new double[to - from + 1];
            var along = new double[heights.Length];
            for (int node = from + 1; node <= to; node++)
                along[node - from] = along[node - 1 - from] + SegmentLength(planet, node);
            int pinned = 0;
            heights[0] = NodeHeight(planet, from);
            for (int k = 1; k < heights.Length; k++)
            {
                if (k < heights.Length - 1 && _roadNodeLifts[from + k] <= 0.0) continue;
                heights[k] = NodeHeight(planet, from + k);
                double span = along[k] - along[pinned];
                for (int free = pinned + 1; free < k; free++)
                    heights[free] = heights[pinned] + (heights[k] - heights[pinned]) *
                        (span > 1e-9 ? (along[free] - along[pinned]) / span : 0.0);
                pinned = k;
            }
            return heights;
        }

        /// <summary>Whether the deck from node <paramref name="from"/> to <paramref name="to"/> climbs too steeply anywhere.</summary>
        bool DeckTooSteep(PlanetSnapshot planet, int from, int to)
        {
            double[] deck = DeckHeights(planet, from, to);
            double maxGrade = Math.Tan(BridgeMaxGradeDegrees * Math.PI / 180.0);
            for (int node = from + 1; node <= to; node++)
                if (Math.Abs(deck[node - from] - deck[node - 1 - from]) >
                    Math.Max(SegmentLength(planet, node), 1e-3) * maxGrade)
                    return true;
            return false;
        }

        /// <summary>
        /// Why the deck from node <paramref name="from"/> to <paramref name="to"/> is not over the
        /// terrain, or (for beam bridges, whose piers stand on it; an arch spans any depth) more than
        /// <see cref="RoadLimits.MaxBridgeHeightMeters"/> above it; null when it fits.
        /// </summary>
        string DeckClearanceProblem(PlanetSnapshot planet, int from, int to)
        {
            double[] deck = DeckHeights(planet, from, to);
            for (int segment = from + 1; segment <= to; segment++)
            {
                SegmentProfile profile = segment < _segmentProfiles.Count ? _segmentProfiles[segment] : null;
                if (profile == null) continue;
                double start = deck[segment - 1 - from], end = deck[segment - from];
                int steps = profile.Heights.Length - 1;
                for (int i = 0; i <= steps; i++)
                {
                    double height = start + (end - start) * i / steps - profile.Heights[i];
                    if (height < -BridgeTerrainToleranceMeters)
                        return LocHelper.Format("HighwayEngineer_BridgeDraft_IntoTerrain", segment);
                    if (height > RoadLimits.MaxBridgeHeightMeters && BridgeSettings.Design == BridgeDesign.Beam)
                        return LocHelper.Format("HighwayEngineer_BridgeDraft_TooHigh", RoadLimits.MaxBridgeHeightMeters);
                }
            }
            return null;
        }

        /// <summary>A draft segment's length along the surface: its profile's, else its arc's.</summary>
        double SegmentLength(PlanetSnapshot planet, int segment)
        {
            if (segment < _segmentProfiles.Count && _segmentProfiles[segment] != null)
                return _segmentProfiles[segment].Length;
            RoadCurve curve;
            return TryGetRoadCurve(planet, segment, out curve) ? curve.Arc * planet.Radius : 0.0;
        }

        /// <summary>
        /// The bridge badges beside the draft's segments: on every segment that is, could be or was
        /// set to be a bridge, opposite its grade label.
        /// </summary>
        public IReadOnlyList<RoadBridgeToggle> GetBridgeToggles()
        {
            _bridgeToggles.Clear();
            if (!RoadsVisible || MarkMode || _mapRenderer == null || _roadNodes.Count < 2) return _bridgeToggles;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Radius <= 0) return _bridgeToggles;
            UpdateBridges(planet);
            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                bool bridge = IsBridgeSegment(segment);
                if (!bridge && !_bridgeCandidates[segment] && _roadBridgeModes[segment] == BridgeSegmentMode.Auto)
                    continue;
                RoadCurve curve;
                Vector2 midpoint, before, after;
                if (!TryGetRoadCurve(planet, segment, out curve) ||
                    !PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        planet.Position + curve.Evaluate(0.5) * planet.Radius, _mapData, _viewState, out midpoint))
                    continue;
                Vector2 offset = new Vector2(0f, 17f);
                if (PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        planet.Position + curve.Evaluate(0.49) * planet.Radius, _mapData, _viewState, out before) &&
                    PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        planet.Position + curve.Evaluate(0.51) * planet.Radius, _mapData, _viewState, out after))
                {
                    Vector2 direction = after - before;
                    if (direction.LengthSquared() > 0.0001f)
                    {
                        direction.Normalize();
                        offset = new Vector2(direction.Y, -direction.X) * 17f;
                    }
                }
                _bridgeToggles.Add(new RoadBridgeToggle
                {
                    Segment = segment,
                    ScreenPosition = midpoint + offset,
                    IsBridge = bridge,
                    IsInvalid = bridge && _invalidBridgeSegments.Contains(segment)
                });
            }
            return _bridgeToggles;
        }

        readonly List<RoadBridgeToggle> _bridgeToggles = new List<RoadBridgeToggle>();

        void EnsureCommittedRoads(PlanetSnapshot planet)
        {
            if (planet == null || _committedRoadPlanetId == planet.EntityId) return;
            _committedRoadPlanetId = planet.EntityId;
            int generation = ++_committedRoadGeneration;
            _committedRoads.Clear();
            // The server's road library (roads and bridges built by contracts) first: only its
            // segments can be bulldozed.
            AddLibraryRoads(planet);
            // Session-local roads are immediately available for snapping. Import the
            // potentially large persisted API recipe after the screen is responsive.
            foreach (SavedRoadSegment saved in _roadLibrary.GetSegments(planet))
            {
                RoadCurve curve;
                if (TryReadCommittedCurve(planet, saved.Geometry, out curve))
                {
                    curve.WidthMeters = saved.WidthMeters;
                    AddCommittedRoad(curve);
                }
            }
            try
            {
                ProceduralCubemapApi.Api.ApiProvider api;
                string apiError;
                if (HighwayEngineer.Common.Helpers.RoadApi.TryValidate(out api, out apiError))
                {
                    string xml = api.GetPlanetModificationsXml(planet.EntityId);
                    if (!string.IsNullOrWhiteSpace(xml) && MyAPIGateway.Parallel != null)
                    {
                        long planetId = planet.EntityId;
                        List<ListedRoadLine> geometry = null;
                        Exception failure = null;
                        MyAPIGateway.Parallel.Start(() =>
                        {
                            try
                            {
                                var listing = MyAPIGateway.Utilities
                                    .SerializeFromXML<HighwayEngineer.Common.Roads.PlanetModificationListing>(xml);
                                geometry = new List<ListedRoadLine>();
                                if (listing != null && listing.Modifications != null)
                                    foreach (HighwayEngineer.Common.Roads.PlanetModificationInfo modification in listing.Modifications)
                                    {
                                        if (modification == null || modification.Lines == null) continue;
                                        for (int i = 0; i < modification.Lines.Count; i++)
                                        {
                                            HighwayEngineer.Common.Roads.RuntimeProceduralLineOperation line = modification.Lines[i];
                                            if (line != null && string.Equals(line.Layer,
                                                    "material", StringComparison.OrdinalIgnoreCase) &&
                                                line.Geometry != null)
                                                geometry.Add(new ListedRoadLine
                                                {
                                                    Line = line,
                                                    ModificationId = modification.Id,
                                                    LineIndex = i
                                                });
                                        }
                                    }
                            }
                            catch (Exception error) { failure = error; }
                        }, () =>
                        {
                            if (_disposed || _committedRoadPlanetId != planetId ||
                                _committedRoadGeneration != generation) return;
                            if (failure != null)
                            {
                                ErrorHandlerHelper.LogError(failure, typeof(RoadPlannerViewModel));
                                return;
                            }
                            PlanetSnapshot current = ResolveFocusedPlanet();
                            if (current == null || current.EntityId != planetId) return;
                            // The API listing is authoritative: roads removed through
                            // the API must not linger from the session road library. The road
                            // library's segments stay, so they keep their ids; the listing adds
                            // roads built some other way.
                            _committedRoads.Clear();
                            _listedGeometry.Clear();
                            AddLibraryRoads(current);
                            foreach (ListedRoadLine listed in geometry)
                            {
                                _listedGeometry[ListedKey(listed.ModificationId, listed.LineIndex)] =
                                    listed.Line.Geometry;
                                RoadCurve curve;
                                if (TryReadCommittedCurve(current, listed.Line.Geometry, out curve))
                                {
                                    curve.WidthMeters = listed.Line.WidthMeters;
                                    curve.ModificationId = listed.ModificationId;
                                    curve.LineIndex = listed.LineIndex;
                                    AddCommittedRoad(curve);
                                }
                            }
                            _textureDirty = true;
                        });
                    }
                }
            }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(RoadPlannerViewModel));
            }
        }

        void AddLibraryRoads(PlanetSnapshot planet)
        {
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (client == null) return;
            foreach (RoadSegmentRecord segment in client.Library.SegmentsOn(planet.EntityId))
            {
                RoadCurve curve;
                if (!TryReadCommittedCurve(planet, segment.Geometry, out curve)) continue;
                curve.WidthMeters = segment.WidthMeters;
                curve.ModificationId = segment.ModificationId;
                curve.LineIndex = segment.LineIndex;
                curve.SegmentId = segment.SegmentId;
                curve.BridgeId = segment.BridgeId;
                curve.StartHeight = segment.StartHeight;
                curve.EndHeight = segment.EndHeight;
                _committedRoads.Add(curve);
            }
        }

        // Each listed road line's geometry exactly as the voxel API lists it, for bulldozing it.
        readonly Dictionary<string, double[]> _listedGeometry = new Dictionary<string, double[]>();

        static string ListedKey(string modificationId, int lineIndex)
        {
            return modificationId + "#" + lineIndex;
        }

        sealed class ListedRoadLine
        {
            public HighwayEngineer.Common.Roads.RuntimeProceduralLineOperation Line;
            public string ModificationId;
            public int LineIndex;
        }

        void AddCommittedRoad(RoadCurve curve)
        {
            for (int i = 0; i < _committedRoads.Count; i++)
            {
                RoadCurve existing = _committedRoads[i];
                if (Vector3D.DistanceSquared(existing.Start, curve.Start) < 1e-12 &&
                    Vector3D.DistanceSquared(existing.End, curve.End) < 1e-12 &&
                    Vector3D.DistanceSquared(existing.StartTangent, curve.StartTangent) < 1e-12 &&
                    Vector3D.DistanceSquared(existing.EndTangent, curve.EndTangent) < 1e-12)
                {
                    // The API recipe knows widths that older session segments lack.
                    if (existing.WidthMeters <= 0.0 && curve.WidthMeters > 0.0)
                    {
                        existing.WidthMeters = curve.WidthMeters;
                        _committedRoads[i] = existing;
                    }
                    return;
                }
            }
            _committedRoads.Add(curve);
        }

        static bool TryReadCommittedCurve(PlanetSnapshot planet, double[] geometry,
            out RoadCurve curve)
        {
            curve = new RoadCurve();
            return planet.Planet != null &&
                   RoadCurve.TryFromLocalGeometry(planet.Planet.WorldMatrix, geometry, out curve);
        }

        static int RoadCurveSamples(double arc, float projectedRadius)
        {
            return (int)Math.Max(16, Math.Min(4096,
                Math.Ceiling(arc * projectedRadius * 2)));
        }

        struct RoadCurvePoint
        {
            public double T;
            public Vector2 Screen;
            public bool Visible;
        }

        const int RoadCurveCoarseSamples = 32;
        const int RoadCurveMaxDepth = 24;

        readonly List<RoadCurvePoint> _roadCurvePoints = new List<RoadCurvePoint>();

        /// <summary>
        /// Projects a road curve to screen space as an ordered polyline. A coarse
        /// pass covers the whole curve; only spans whose screen bounds can reach
        /// <paramref name="clip"/> are subdivided, down to
        /// <paramref name="refinePixels"/> per segment. With
        /// unrestricted zoom almost all of a road is off-screen, so the cost now
        /// follows the visible length instead of the whole zoomed curve.
        /// </summary>
        static void FlattenRoadCurve(PlanetSnapshot planet, RoadCurve curve,
            ref PlanetSurfaceProjector projector, RectangleF clip,
            float refinePixels, List<RoadCurvePoint> points)
        {
            points.Clear();
            int uniformSamples = RoadCurveSamples(curve.Arc, projector.DisplayRadius);
            int coarse = Math.Min(RoadCurveCoarseSamples, uniformSamples);
            // Limb crossings keep the previous uniform sampling density.
            double limbStep = 1.0 / uniformSamples;
            RoadCurvePoint previous = ProjectRoadCurvePoint(planet, curve, ref projector, 0.0);
            points.Add(previous);
            for (int i = 1; i <= coarse; i++)
            {
                RoadCurvePoint next = ProjectRoadCurvePoint(planet, curve, ref projector,
                    i / (double)coarse);
                RefineRoadCurveSpan(planet, curve, ref projector, clip, refinePixels,
                    previous, next, limbStep, 0, points);
                points.Add(next);
                previous = next;
            }
        }

        static void RefineRoadCurveSpan(PlanetSnapshot planet, RoadCurve curve,
            ref PlanetSurfaceProjector projector, RectangleF clip,
            float refinePixels, RoadCurvePoint start, RoadCurvePoint end, double limbStep, int depth,
            List<RoadCurvePoint> points)
        {
            if (depth >= RoadCurveMaxDepth)
                return;
            if (start.Visible && end.Visible)
            {
                float length = Vector2.Distance(start.Screen, end.Screen);
                if (length <= refinePixels)
                    return;
                // A sub-span of a smooth curve bulges well under half its chord.
                float pad = length * 0.5f;
                if (Math.Max(start.Screen.X, end.Screen.X) + pad < clip.X ||
                    Math.Min(start.Screen.X, end.Screen.X) - pad > clip.Right ||
                    Math.Max(start.Screen.Y, end.Screen.Y) + pad < clip.Y ||
                    Math.Min(start.Screen.Y, end.Screen.Y) - pad > clip.Bottom)
                    return;
            }
            else if (!start.Visible && !end.Visible || end.T - start.T <= limbStep)
            {
                // Hidden hemisphere, or the limb is already located finely enough.
                return;
            }

            RoadCurvePoint middle = ProjectRoadCurvePoint(planet, curve, ref projector,
                (start.T + end.T) * 0.5);
            RefineRoadCurveSpan(planet, curve, ref projector, clip, refinePixels,
                start, middle, limbStep, depth + 1, points);
            points.Add(middle);
            RefineRoadCurveSpan(planet, curve, ref projector, clip, refinePixels,
                middle, end, limbStep, depth + 1, points);
        }

        static RoadCurvePoint ProjectRoadCurvePoint(PlanetSnapshot planet, RoadCurve curve,
            ref PlanetSurfaceProjector projector, double t)
        {
            var point = new RoadCurvePoint { T = t };
            point.Visible = projector.TryProject(
                planet.Position + curve.Evaluate(t) * planet.Radius, out point.Screen);
            return point;
        }

        bool RoadCurveExceedsSlope(PlanetSnapshot planet, RoadCurve curve, int segment)
        {
            UpdateBridges(planet);
            bool besideBridge = IsBridgeSegment(segment - 1) || IsBridgeSegment(segment + 1);
            RoadBuildParameters parameters = CurrentRoadParameters();
            SegmentProfile profile = segment < _segmentProfiles.Count ? _segmentProfiles[segment] : null;
            if (profile == null || planet.Planet == null || profile.Curve.Start != curve.Start ||
                profile.Curve.End != curve.End || profile.Curve.StartTangent != curve.StartTangent ||
                profile.Curve.EndTangent != curve.EndTangent)
                return RoadGeometry.ExceedsSlope(curve, planet.Radius,
                    Vector3D.Distance(_roadNodes[segment - 1], planet.Position) - planet.Radius,
                    Vector3D.Distance(_roadNodes[segment], planet.Position) - planet.Radius,
                    parameters, besideBridge);
            if (!profile.SlopeKnown || profile.SlopeBesideBridge != besideBridge ||
                profile.SlopeMaxDegrees != parameters.MaxSlopeDegrees ||
                profile.SlopeSegmentDistance != parameters.SegmentDistanceMeters)
            {
                profile.ExceedsSlope = RoadGeometry.ExceedsSlope(curve, planet.Planet.AverageRadius,
                    profile.StartSurface, profile.EndSurface, parameters, besideBridge);
                profile.SlopeKnown = true;
                profile.SlopeBesideBridge = besideBridge;
                profile.SlopeMaxDegrees = parameters.MaxSlopeDegrees;
                profile.SlopeSegmentDistance = parameters.SegmentDistanceMeters;
            }
            return profile.ExceedsSlope;
        }

        static Vector3D ToPlanetLocal(PlanetSnapshot planet, Vector3D worldDirection)
        {
            MatrixD world = planet.Planet.WorldMatrix;
            return new Vector3D(
                Vector3D.Dot(worldDirection, world.Right),
                Vector3D.Dot(worldDirection, world.Up),
                Vector3D.Dot(worldDirection, world.Backward));
        }

        bool TryFindRoadSegment(Vector2 cursor, out int index, out Vector3D position,
            out Vector3D tangent)
        {
            index = -1;
            position = Vector3D.Zero;
            tangent = Vector3D.Zero;
            if (_roadNodes.Count < 2 || _mapRenderer == null) return false;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null) return false;
            float best = 100f; // ten screen pixels from the displayed line
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData,
                    _viewState, out projector))
                return false;
            // Only spans near the cursor need full precision.
            var clip = new RectangleF(cursor.X - 11f, cursor.Y - 11f, 22f, 22f);
            for (int i = 1; i < _roadNodes.Count; i++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, i, out curve)) continue;
                FlattenRoadCurve(planet, curve, ref projector, clip, 2f, _roadCurvePoints);
                RoadCurvePoint last = default(RoadCurvePoint);
                foreach (RoadCurvePoint point in _roadCurvePoints)
                {
                    if (point.Visible && last.Visible)
                    {
                        Vector2 edge = point.Screen - last.Screen;
                        float edgeSq = edge.LengthSquared();
                        if (edgeSq > 0.01f)
                        {
                            float u = MathHelper.Clamp(Vector2.Dot(cursor - last.Screen, edge) / edgeSq, 0f, 1f);
                            float dist = Vector2.DistanceSquared(cursor, last.Screen + u * edge);
                            if (dist < best && !(i == 1 && last.T == 0.0) &&
                                !(i == _roadNodes.Count - 1 && point.T == 1.0))
                            {
                                double at = last.T + (point.T - last.T) * u;
                                Vector3D hit = curve.Evaluate(at);
                                best = dist;
                                index = i;
                                position = planet.Position + hit * planet.Radius;
                                tangent = curve.TangentAt(at);
                            }
                        }
                    }
                    last = point;
                }
            }
            return index >= 0;
        }

        /// <summary>
        /// Snaps to a built road: an existing node or junction near the cursor first, so roads
        /// meeting there share one junction, else the nearest point on a segment, which becomes
        /// a junction of that road.
        /// </summary>
        bool TrySnapToCommittedRoad(Vector2 cursor, PlanetSnapshot planet,
            out Vector3D position, out Vector3D tangent)
        {
            if (TrySnapToCommittedNode(cursor, planet, out position, out tangent))
                return true;
            int roadIndex;
            return TryHitCommittedRoad(cursor, planet, out roadIndex, out position, out tangent);
        }

        /// <summary>
        /// Nearest built road node within sixteen screen pixels: a segment end, which includes
        /// every junction where another road was snapped onto a segment.
        /// </summary>
        bool TrySnapToCommittedNode(Vector2 cursor, PlanetSnapshot planet,
            out Vector3D position, out Vector3D tangent)
        {
            position = Vector3D.Zero;
            tangent = Vector3D.Zero;
            if (_mapRenderer == null || planet == null || planet.Planet == null) return false;
            EnsureCommittedRoads(planet);
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData,
                    _viewState, out projector))
                return false;
            float best = 256f; // Within sixteen screen pixels of a node.
            bool found = false;
            foreach (RoadCurve curve in _committedRoads)
            {
                for (int end = 0; end <= 1; end++)
                {
                    Vector3D direction = end == 0 ? curve.Start : curve.End;
                    Vector2 screen;
                    if (!projector.TryProject(planet.Position + direction * planet.Radius, out screen))
                        continue;
                    float distance = Vector2.DistanceSquared(cursor, screen);
                    if (distance >= best) continue;
                    Vector3D surface = planet.Planet.GetClosestSurfacePointGlobal(
                        planet.Position + direction * planet.MaximumRadius);
                    if (Vector3D.DistanceSquared(surface, planet.Position) <= 1.0) continue;
                    best = distance;
                    position = surface;
                    tangent = curve.TangentAt(end);
                    found = true;
                }
            }
            return found;
        }

        /// <summary>Nearest built road segment within twelve screen pixels.</summary>
        bool TryHitCommittedRoad(Vector2 cursor, PlanetSnapshot planet, out int roadIndex,
            out Vector3D position, out Vector3D tangent)
        {
            roadIndex = -1;
            position = Vector3D.Zero;
            tangent = Vector3D.Zero;
            if (_mapRenderer == null || planet == null || planet.Planet == null) return false;
            EnsureCommittedRoads(planet);
            float best = 144f; // Within twelve screen pixels of a committed curve.
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData,
                    _viewState, out projector))
                return false;
            // Only spans near the cursor need full precision.
            var clip = new RectangleF(cursor.X - 13f, cursor.Y - 13f, 26f, 26f);
            bool found = false;
            for (int roadI = 0; roadI < _committedRoads.Count; roadI++)
            {
                RoadCurve curve = _committedRoads[roadI];
                FlattenRoadCurve(planet, curve, ref projector, clip, 2f, _roadCurvePoints);
                RoadCurvePoint last = default(RoadCurvePoint);
                foreach (RoadCurvePoint point in _roadCurvePoints)
                {
                    if (point.Visible && last.Visible)
                    {
                        Vector2 edge = point.Screen - last.Screen;
                        float edgeSq = edge.LengthSquared();
                        if (edgeSq > 0.01f)
                        {
                            float u = MathHelper.Clamp(Vector2.Dot(cursor - last.Screen, edge) /
                                edgeSq, 0f, 1f);
                            float distance = Vector2.DistanceSquared(cursor, last.Screen + u * edge);
                            if (distance < best)
                            {
                                double at = last.T + (point.T - last.T) * u;
                                Vector3D direction = curve.Evaluate(at);
                                Vector3D sample = planet.Position + direction * planet.MaximumRadius;
                                Vector3D surface = planet.Planet.GetClosestSurfacePointGlobal(sample);
                                if (Vector3D.DistanceSquared(surface, planet.Position) > 1.0)
                                {
                                    best = distance;
                                    position = surface;
                                    tangent = curve.TangentAt(at);
                                    roadIndex = roadI;
                                    found = true;
                                }
                            }
                        }
                    }
                    last = point;
                }
            }
            return found;
        }

        public void DragRoadOrOrbit(Vector2 pressPoint, Vector2 cursor, Vector2 delta)
        {
            if (_roadDragMode == RoadDragMode.None) BeginRoadDrag(pressPoint);
            if (_roadDragMode == RoadDragMode.Orbit)
            {
                if (_viewState.SurfaceViewActive)
                    PanSurfaceView(delta);
                else if (_planetGrabActive)
                {
                    UnlockPlanetOrientation();
                    if (_viewState.FollowPlayer)
                        SetFollowPlayer(false);
                    Vector2 center = _mapBounds.Center + new Vector2(
                        _viewState.PlanetCenterOffsetNormalized.X * _mapBounds.Width,
                        _viewState.PlanetCenterOffsetNormalized.Y * _mapBounds.Height);
                    float radius = Math.Min(_mapBounds.Width, _mapBounds.Height) * 0.47f *
                        Math.Max(_viewState.Zoom, 0.0001f);
                    Vector3 view;
                    Vector3 right;
                    Vector3 up;
                    _planetSurfaceDrag.Move(cursor, center, radius,
                        out view, out right, out up);
                    NormalizeSeOrbit(ref view, ref right, ref up);
                    _viewState.PlanetOrbitView = view;
                    _viewState.PlanetOrbitRight = right;
                    _viewState.PlanetOrbitUp = up;
                    _textureDirty = true;
                }
                else OrbitMap(delta);
                return;
            }
            if (_editingNode < 0 || _editingNode >= _roadNodes.Count || _mapRenderer == null) return;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null) return;
            Vector3D target = Vector3D.Zero;
            Vector3D snappedTangent;
            bool snapped = _roadDragMode == RoadDragMode.MoveNode &&
                TrySnapToCommittedRoad(cursor, planet, out target, out snappedTangent);
            if (!snapped && !_mapRenderer.TryGetSurfacePosition(_mapBounds, cursor,
                    _mapData, _viewState, out target)) return;
            if (_roadDragMode == RoadDragMode.MoveNode)
            {
                _roadNodes[_editingNode] = target;
                // A moved intersection is the user's node now.
                _roadIntersections[_editingNode] = false;
                RefreshAutomaticRoadTangents(planet, _editingNode - 1, _editingNode + 1);
                RoadEditorStatus = LocHelper.GetLoc(snapped ? "HighwayEngineer_Status_NodeSnapped" : "HighwayEngineer_Status_NodeMoved");
            }
            else if (!AimRoadTangent(_roadDragMode, target, planet)) return;
            _textureDirty = true;
        }

        bool AimRoadTangent(RoadDragMode side, Vector3D target, PlanetSnapshot planet)
        {
            if (side != RoadDragMode.IncomingTangent && side != RoadDragMode.OutgoingTangent)
                return false;
            Vector3D node = _roadNodes[_editingNode];
            Vector3D normal = Vector3D.Normalize(node - planet.Position);
            Vector3D tangent = side == RoadDragMode.IncomingTangent
                ? node - target : target - node;
            tangent -= normal * Vector3D.Dot(tangent, normal);
            if (tangent.LengthSquared() < 1e-12) return false;
            tangent.Normalize();
            if (side == RoadDragMode.IncomingTangent)
            {
                _roadIncomingDirections[_editingNode] = tangent;
                _roadIncomingEdited[_editingNode] = true;
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_EntranceUpdated");
            }
            else
            {
                _roadOutgoingDirections[_editingNode] = tangent;
                _roadOutgoingEdited[_editingNode] = true;
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_ExitUpdated");
            }
            // Keep an edited tangent: an auto intersection would be rebuilt without it.
            _roadIntersections[_editingNode] = false;
            _activeTangent = side;
            return true;
        }

        public void EditRoadNode(Vector2 screenPoint)
        {
            if (!RoadsVisible || MarkMode) return;
            if (_mapRenderer == null || _pushPending) return;
            int hit = FindRoadNode(screenPoint);
            if (hit >= 0)
            {
                RemoveRoadNode(hit);
                return;
            }
            if (_editingNode < 0) return;
            Vector3D target;
            if (!_mapRenderer.TryGetSurfacePosition(_mapBounds, screenPoint,
                    _mapData, _viewState, out target)) return;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null) return;
            RoadDraftSnapshot before = CaptureRoadDraft();
            if (AimRoadTangent(_activeTangent, target, planet))
            {
                RecordRoadHistory(before);
                AppendIntersectionStatus(RefreshRoadIntersections());
                _textureDirty = true;
            }
        }

        enum PushKind
        {
            Road,
            Bulldoze,
            Repair
        }

        void ReportPushResult(bool success, string message, PushKind kind = PushKind.Road)
        {
            message = LocHelper.Resolve(message);
            string done, failed;
            switch (kind)
            {
                case PushKind.Bulldoze:
                    done = "HighwayEngineer_Push_BulldozeDone";
                    failed = "HighwayEngineer_Push_BulldozeFailed";
                    break;
                case PushKind.Repair:
                    done = "HighwayEngineer_Push_RepairDone";
                    failed = "HighwayEngineer_Push_RepairFailed";
                    break;
                default:
                    done = "HighwayEngineer_Push_RoadDone";
                    failed = "HighwayEngineer_Push_RoadFailed";
                    break;
            }
            RoadEditorStatus = success
                ? LocHelper.GetLoc(kind == PushKind.Road ? "HighwayEngineer_Push_RoadCommitted" : done)
                : LocHelper.Format("HighwayEngineer_Push_FailedStatus", LocHelper.GetLoc(failed), message);
            Action<string, string> show = MessageBoxRequested;
            if (show != null)
            {
                try
                {
                    show(LocHelper.GetLoc(success ? done : failed), message);
                    return;
                }
                catch (Exception) { }
            }
            if (MyAPIGateway.Utilities != null)
            {
                MyAPIGateway.Utilities.ShowNotification(
                    LocHelper.Format(success ? "HighwayEngineer_Push_Notify" : "HighwayEngineer_Push_NotifySeeChat",
                        LocHelper.GetLoc(success ? done : failed)),
                    10000, success ? "Green" : "Red");
                RoadChat.Show(message);
            }
        }

        public IReadOnlyList<PlanetWaypointMarker> GetPlanetWaypointMarkers()
        {
            _roadNodeMarkers.Clear();
            if (!RoadsVisible) return _roadNodeMarkers;
            if (_mapRenderer == null) return _roadNodeMarkers;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null) return _roadNodeMarkers;
            for (int i = 0; i < _roadNodes.Count; i++)
            {
                Vector2 pos;
                if (!PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        _roadNodes[i], _mapData, _viewState, out pos)) continue;
                _roadNodeMarkers.Add(new PlanetWaypointMarker
                {
                    Hash = i + 1,
                    Name = _roadIntersections[i] ? LocHelper.GetLoc("HighwayEngineer_Marker_Intersection") : LocHelper.Format("HighwayEngineer_Marker_Node", i + 1),
                    IsIntersection = _roadIntersections[i],
                    ElevationText = Math.Round(NodeHeight(planet, i)).ToString("0", CultureInfo.InvariantCulture) + " m" +
                                    (_roadNodeLifts[i] > 0.0
                                        ? " (+" + _roadNodeLifts[i].ToString("0", CultureInfo.InvariantCulture) + ")"
                                        : ""),
                    ScreenPosition = pos,
                    Color = new Color(245, 198, 80),
                    AnyGpsShown = true
                });
            }
            return _roadNodeMarkers;
        }

        /// <summary>
        /// The player's GPS markers on the focused planet, as on the Minimap planetary map: only
        /// those shown on the HUD (or hidden from the map this session), clustered when close, and
        /// folded into a trade station's badge when on top of one. Call after the station markers.
        /// </summary>
        public IReadOnlyList<PlanetWaypointMarker> GetPlanetGpsMarkers()
        {
            _gpsMarkers.Clear();
            if (!_waypointsVisible || _mapRenderer == null ||
                _mapBounds.Width <= 0f || _mapBounds.Height <= 0f ||
                MyAPIGateway.Session == null || MyAPIGateway.Session.GPS == null ||
                MyAPIGateway.Session.Player == null)
                return _gpsMarkers;

            _gpsEntries.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(MyAPIGateway.Session.Player.IdentityId, _gpsEntries);
            float clusterDistance = SIGNAL_CLUSTER_DISTANCE_PIXELS * Math.Max(
                0.75f, Math.Min(_mapBounds.Width, _mapBounds.Height) / 700f);
            float clusterDistanceSquared = clusterDistance * clusterDistance;
            for (int i = 0; i < _gpsEntries.Count; i++)
            {
                IMyGps gps = _gpsEntries[i];
                if (gps == null ||
                    (!gps.ShowOnHud && !_temporarilyVisibleGpsHashes.Contains(gps.Hash)))
                    continue;

                Vector2 screenPosition;
                if (!_mapRenderer.TryProjectPlanetSurfacePosition(
                        _mapBounds, gps.Coords, _mapData, _viewState, out screenPosition))
                    continue;

                const float markerMargin = 32f;
                if (screenPosition.X < _mapBounds.X - markerMargin ||
                    screenPosition.X > _mapBounds.Right + markerMargin ||
                    screenPosition.Y < _mapBounds.Y - markerMargin ||
                    screenPosition.Y > _mapBounds.Bottom + markerMargin)
                    continue;

                PlanetTradeStationMarker stationMarker = FindStationCluster(screenPosition, clusterDistanceSquared);
                if (stationMarker != null)
                {
                    stationMarker.GpsHashes.Add(gps.Hash);
                    stationMarker.AnyGpsShown |= gps.ShowOnHud;
                    continue;
                }

                PlanetWaypointMarker marker = FindGpsCluster(screenPosition, clusterDistanceSquared);
                if (marker == null)
                {
                    marker = new PlanetWaypointMarker
                    {
                        Hash = gps.Hash,
                        Name = KnownTradeStations.TrimName(string.IsNullOrWhiteSpace(gps.Name)
                            ? LocHelper.GetLoc("HighwayEngineer_Marker_Gps") : gps.Name, 16),
                        ScreenPosition = screenPosition,
                        Color = gps.GPSColor
                    };
                    _gpsMarkers.Add(marker);
                }
                else
                {
                    int previousCount = marker.GpsHashes.Count;
                    marker.ScreenPosition = (marker.ScreenPosition * previousCount + screenPosition) /
                                            (previousCount + 1f);
                }
                marker.GpsHashes.Add(gps.Hash);
                marker.AnyGpsShown |= gps.ShowOnHud;
            }

            for (int i = 0; i < _gpsMarkers.Count; i++)
                if (_gpsMarkers[i].GpsHashes.Count > 1)
                    _gpsMarkers[i].Name = LocHelper.Format("HighwayEngineer_Marker_GpsSignals", _gpsMarkers[i].GpsHashes.Count);
            return _gpsMarkers;
        }

        PlanetWaypointMarker FindGpsCluster(Vector2 screenPosition, float maximumDistanceSquared)
        {
            PlanetWaypointMarker best = null;
            float bestDistanceSquared = maximumDistanceSquared;
            for (int i = 0; i < _gpsMarkers.Count; i++)
            {
                float distanceSquared = Vector2.DistanceSquared(screenPosition, _gpsMarkers[i].ScreenPosition);
                if (distanceSquared > bestDistanceSquared) continue;
                best = _gpsMarkers[i];
                bestDistanceSquared = distanceSquared;
            }
            return best;
        }

        public IReadOnlyList<RoadSegmentLabel> GetRoadSegmentLabels()
        {
            _roadSegmentLabels.Clear();
            if (!RoadsVisible) return _roadSegmentLabels;
            if (_mapRenderer == null ||
                _roadNodes.Count < 2) return _roadSegmentLabels;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Radius <= 0) return _roadSegmentLabels;

            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve)) continue;
                Vector2 midpoint;
                Vector3D middle = planet.Position + curve.Evaluate(0.5) * planet.Radius;
                if (!PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds, middle,
                        _mapData, _viewState, out midpoint)) continue;

                Vector2 before;
                Vector2 after;
                Vector2 offset = new Vector2(0f, -13f);
                if (PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        planet.Position + curve.Evaluate(0.49) * planet.Radius,
                        _mapData, _viewState, out before) &&
                    PlanetSurfaceProjector.TryProject(_mapRenderer, _mapBounds,
                        planet.Position + curve.Evaluate(0.51) * planet.Radius,
                        _mapData, _viewState, out after))
                {
                    Vector2 direction = after - before;
                    if (direction.LengthSquared() > 0.0001f)
                    {
                        direction.Normalize();
                        offset = new Vector2(-direction.Y, direction.X) * 13f;
                    }
                }

                // Use the curved centerline, rather than endpoint separation, for grade.
                int samples = Math.Max(16, Math.Min(256,
                    (int)Math.Ceiling(curve.Arc * planet.Radius / 20.0)));
                Vector3D previous = curve.Start;
                double length = 0.0;
                for (int i = 1; i <= samples; i++)
                {
                    Vector3D next = curve.Evaluate(i / (double)samples);
                    length += planet.Radius * Math.Acos(MathHelper.Clamp(
                        Vector3D.Dot(previous, next), -1.0, 1.0));
                    previous = next;
                }
                if (length <= 0.0) continue;
                double heightChange =
                    Vector3D.Distance(_roadNodes[segment], planet.Position) -
                    Vector3D.Distance(_roadNodes[segment - 1], planet.Position);
                double angle = Math.Atan2(heightChange, length) * 180.0 / Math.PI;
                _roadSegmentLabels.Add(new RoadSegmentLabel
                {
                    ScreenPosition = midpoint + offset,
                    Text = angle.ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture) + "°"
                });
            }
            return _roadSegmentLabels;
        }


        // ---- Road Logistic Depots ----

        static readonly Color DepotReuseRangeColor = new Color(120, 230, 140);
        static readonly Color DepotRoadRangeColor = new Color(90, 170, 255);
        const int DepotRangeRingPoints = 160;

        long _hoveredDepotId;
        PacketRoadDepotList _shownDepots;
        // The server quote's depot for the draft it priced (0: a new depot); valid while
        // the draft is unchanged, after which the planner falls back to its own prediction.
        bool _hasQuotedDepot;
        long _quotedDepotId;
        long _quotedDraftSignature;

        /// <summary>The pointer is over a depot marker (0: none); its ranges are drawn into the map.</summary>
        public void SetHoveredDepot(long gridId)
        {
            if (_hoveredDepotId == gridId) return;
            _hoveredDepotId = gridId;
            _textureDirty = true;
        }

        /// <summary>The server priced the current draft with this depot (0: a new one is spawned).</summary>
        public void SetQuotedDepot(long gridId)
        {
            _hasQuotedDepot = true;
            _quotedDepotId = gridId;
            _quotedDraftSignature = DraftSignature();
            _textureDirty = true;
        }

        /// <summary>Repaints when the server's depot list changes; call every update.</summary>
        void RefreshDepots()
        {
            PacketRoadDepotList depots = HighwayEngineerClientComponent.Depots;
            if (ReferenceEquals(depots, _shownDepots)) return;
            _shownDepots = depots;
            _textureDirty = true;
        }

        long DraftSignature()
        {
            long hash = _roadNodes.Count;
            foreach (Vector3D node in _roadNodes)
                hash = hash * 31 + node.GetHashCode();
            return hash;
        }

        /// <summary>
        /// The depot this draft would deliver to: the server's answer when it has priced this
        /// exact draft, otherwise a preview of its rule: the nearest faction station's faction
        /// owns a depot within the reuse radius of a node. (The server also reuses depots
        /// reachable along built roads, which only its quote shows.)
        /// </summary>
        long ResolveDraftDepot(PlanetSnapshot planet)
        {
            if (planet == null || _roadNodes.Count < 2) return 0;
            if (_hasQuotedDepot && _quotedDraftSignature == DraftSignature()) return _quotedDepotId;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.CreativeMode) return 0;
            PacketRoadDepotList depots = HighwayEngineerClientComponent.Depots;
            IMyFaction faction;
            IMyFactionStation station;
            if (depots.Depots.Count == 0 ||
                !ServiceProviders.TryFindNearest(_roadNodes[0], out faction, out station))
                return 0;
            long best = 0;
            double bestMeters = depots.ReuseRadiusMeters;
            foreach (RoadDepotInfo depot in depots.Depots)
            {
                if (depot.PlanetId != planet.EntityId || depot.Faction == null ||
                    depot.Faction.FactionId != faction.FactionId) continue;
                foreach (Vector3D node in _roadNodes)
                {
                    double meters = Vector3D.Distance(node, depot.Position);
                    if (meters > bestMeters) continue;
                    best = depot.GridId;
                    bestMeters = meters;
                }
            }
            return best;
        }

        /// <summary>Depot markers on the focused planet (fresh objects: the overlay frame keeps them).</summary>
        public List<RoadDepotMarker> GetRoadDepotMarkers()
        {
            var markers = new List<RoadDepotMarker>();
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (!RoadsVisible || planet == null || _mapRenderer == null ||
                _mapBounds.Width <= 0f || _mapBounds.Height <= 0f)
                return markers;
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData, _viewState, out projector))
                return markers;
            long used = ResolveDraftDepot(planet);
            foreach (RoadDepotInfo depot in HighwayEngineerClientComponent.Depots.Depots)
            {
                Vector2 screen;
                if (depot.PlanetId != planet.EntityId || depot.Faction == null ||
                    !projector.TryProject(depot.Position, out screen))
                    continue;
                const float markerMargin = 40f;
                if (screen.X < _mapBounds.X - markerMargin || screen.X > _mapBounds.Right + markerMargin ||
                    screen.Y < _mapBounds.Y - markerMargin || screen.Y > _mapBounds.Bottom + markerMargin)
                    continue;
                markers.Add(new RoadDepotMarker
                {
                    GridId = depot.GridId,
                    ScreenPosition = screen,
                    Icon = depot.Faction.Icon,
                    IconColor = depot.Faction.IconColor,
                    BackgroundColor = depot.Faction.BackgroundColor,
                    FactionName = string.IsNullOrWhiteSpace(depot.Faction.FactionTag)
                        ? depot.Faction.FactionName
                        : "[" + depot.Faction.FactionTag + "] " + depot.Faction.FactionName,
                    UsedByDraft = depot.GridId == used
                });
            }
            return markers;
        }

        /// <summary>
        /// The hovered depot's reuse radius and road range, as rings on the planet surface.
        /// The road range is measured along roads, so its ring is the farthest it can reach.
        /// </summary>
        void DrawHoveredDepotRanges(Acrux.Rendering.PixelCanvas canvas, PlanetSnapshot planet,
            float scaleX, float scaleY)
        {
            if (_hoveredDepotId == 0) return;
            PacketRoadDepotList depots = HighwayEngineerClientComponent.Depots;
            foreach (RoadDepotInfo depot in depots.Depots)
            {
                if (depot.GridId != _hoveredDepotId || depot.PlanetId != planet.EntityId) continue;
                DrawSurfaceRing(planet, depot.Position, depots.ReuseRoadDistanceMeters, DepotRoadRangeColor,
                    scaleX, scaleY);
                DrawSurfaceRing(planet, depot.Position, depots.ReuseRadiusMeters, DepotReuseRangeColor,
                    scaleX, scaleY);
                return;
            }
        }

        void DrawSurfaceRing(PlanetSnapshot planet, Vector3D center, double radiusMeters, Color color,
            float scaleX, float scaleY)
        {
            if (radiusMeters <= 0.0) return;
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData, _viewState, out projector))
                return;
            Vector3D up = center - planet.Position;
            if (up.Normalize() < 1e-9) return;
            Vector3D east = Vector3D.CalculatePerpendicularVector(up);
            Vector3D north = Vector3D.Cross(up, east);
            double arc = Math.Min(Math.PI, radiusMeters / planet.Radius);
            var pixels = new Vector2[DepotRangeRingPoints + 1];
            var visible = new bool[DepotRangeRingPoints + 1];
            for (int i = 0; i <= DepotRangeRingPoints; i++)
            {
                double angle = MathHelper.TwoPi * i / DepotRangeRingPoints;
                Vector3D direction = up * Math.Cos(arc) +
                                     (east * Math.Cos(angle) + north * Math.Sin(angle)) * Math.Sin(arc);
                Vector2 screen;
                if (!projector.TryProject(planet.Position + direction * planet.Radius, out screen)) continue;
                pixels[i] = new Vector2((screen.X - _mapBounds.X) * scaleX, (screen.Y - _mapBounds.Y) * scaleY);
                visible[i] = true;
            }
            _roadRaster.Add((target, first, end) =>
            {
                for (int i = 1; i < pixels.Length; i++)
                    if (visible[i - 1] && visible[i])
                        ScreenSpaceRoadStroke.Draw(target, pixels[i - 1], pixels[i], color, 2, first, end);
            });
        }

        public IReadOnlyList<PlanetTradeStationMarker> GetPlanetTradeStationMarkers()
        {
            for (int i = 0; i < _planetTradeStationMarkers.Count; i++)
            {
                _planetTradeStationMarkers[i].UsedThisFrame = false;
                _planetTradeStationMarkers[i].GpsHashes.Clear();
                _planetTradeStationMarkers[i].AnyGpsShown = false;
            }

            if (_mapRenderer == null ||
                _mapBounds.Width <= 0f || _mapBounds.Height <= 0f)
            {
                RemoveUnusedPlanetTradeStationMarkers();
                return _planetTradeStationMarkers;
            }

            IReadOnlyList<KnownTradeStation> stations = _knownTradeStations.Items;
            for (int i = 0; i < stations.Count; i++)
            {
                KnownTradeStation station = stations[i];
                Vector2 screenPosition;
                float maximumIconSize = float.PositiveInfinity;
                bool projected;
                projected = PlanetSurfaceProjector.TryProject(_mapRenderer, 
                    _mapBounds,
                    station.Position,
                    _mapData,
                    _viewState,
                    out screenPosition);
                if (!projected)
                {
                    continue;
                }

                const float markerMargin = 40f;
                if (screenPosition.X < _mapBounds.X - markerMargin ||
                    screenPosition.X > _mapBounds.Right + markerMargin ||
                    screenPosition.Y < _mapBounds.Y - markerMargin ||
                    screenPosition.Y > _mapBounds.Bottom + markerMargin)
                {
                    continue;
                }

                PlanetTradeStationMarker marker;
                if (!_planetTradeStationMarkersById.TryGetValue(
                        station.StationId,
                        out marker))
                {
                    marker = new PlanetTradeStationMarker
                    {
                        StationId = station.StationId
                    };
                    _planetTradeStationMarkersById.Add(station.StationId, marker);
                    _planetTradeStationMarkers.Add(marker);
                }

                marker.Name = station.Name;
                marker.FullName = station.FullName;
                marker.FactionId = station.FactionId;
                marker.FactionName = station.FactionName;
                marker.FullFactionName = station.FullFactionName;
                marker.FactionTag = station.FactionTag;
                marker.FactionDescription = station.FactionDescription;
                marker.RelationshipName = station.RelationshipName;
                marker.Reputation = station.Reputation;
                marker.Icon = station.Icon;
                marker.ScreenPosition = screenPosition;
                marker.BackgroundColor = station.BackgroundColor;
                marker.IconColor = station.IconColor;
                marker.RelationshipColor = station.RelationshipColor;
                marker.MaximumIconSize = maximumIconSize;
                marker.UsedThisFrame = true;
            }

            RemoveUnusedPlanetTradeStationMarkers();
            return _planetTradeStationMarkers;
        }

        PlanetSnapshot FindStationParentPlanet(Vector3D stationPosition)
        {
            PlanetSnapshot best = null;
            double bestSurfaceDistance = double.MaxValue;
            for (int i = 0; i < _mapData.Planets.Count; i++)
            {
                PlanetSnapshot planet = _mapData.Planets[i];
                double radius = planet.Radius > 0f
                    ? planet.Radius
                    : planet.MaximumRadius;
                if (radius <= 0d)
                    continue;

                double surfaceDistance = Math.Abs(
                    Vector3D.Distance(stationPosition, planet.Position) - radius);
                double terrainAllowance = Math.Abs(planet.MaximumRadius - radius);
                double maximumSurfaceDistance = Math.Max(
                    5000d,
                    Math.Min(radius * 0.25d, terrainAllowance + 10000d));
                if (surfaceDistance > maximumSurfaceDistance ||
                    surfaceDistance >= bestSurfaceDistance)
                {
                    continue;
                }

                best = planet;
                bestSurfaceDistance = surfaceDistance;
            }
            return best;
        }

        PlanetTradeStationMarker FindStationCluster(
            Vector2 screenPosition,
            float maximumDistanceSquared)
        {
            PlanetTradeStationMarker best = null;
            float bestDistanceSquared = maximumDistanceSquared;
            for (int i = 0; i < _planetTradeStationMarkers.Count; i++)
            {
                PlanetTradeStationMarker marker = _planetTradeStationMarkers[i];
                if (!marker.UsedThisFrame)
                    continue;
                float distanceSquared = Vector2.DistanceSquared(
                    screenPosition,
                    marker.ScreenPosition);
                if (distanceSquared > bestDistanceSquared)
                    continue;
                best = marker;
                bestDistanceSquared = distanceSquared;
            }
            return best;
        }

        public PlanetPlayerMarker GetPlanetPlayerMarker()
        {
            _planetPlayerMarker.Visible = false;
            MapOverlayFrame frame = PresentedOverlays;
            if (frame.PlanetView == null || _mapRenderer == null ||
                frame.Bounds.Width <= 0f || frame.Bounds.Height <= 0f)
            {
                return _planetPlayerMarker;
            }

            MatrixD playerWorld;
            if (!MapData.TryGetCurrentPlayerWorldMatrix(out playerWorld))
            {
                if (!_mapData.HasPlayerWorldMatrix)
                    return _planetPlayerMarker;
                playerWorld = _mapData.PlayerWorldMatrix;
            }

            Vector2 screenPosition;
            float headingRotation;
            if (!_mapRenderer.TryGetPlanetPlayerOverlay(
                    frame.Bounds,
                    frame.ProjectionData,
                    frame.PlanetView,
                    playerWorld,
                    out screenPosition,
                    out headingRotation))
            {
                return _planetPlayerMarker;
            }

            _planetPlayerMarker.ScreenPosition = screenPosition;
            _planetPlayerMarker.Rotation =
                COMPASS_ICON_NORTH_CORRECTION + headingRotation;
            _planetPlayerMarker.Visible = true;
            return _planetPlayerMarker;
        }

        public void SetGpsVisibility(IReadOnlyList<int> hashes, bool visible)
        {
            if (hashes == null || hashes.Count == 0 ||
                MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null ||
                MyAPIGateway.Session.GPS == null)
            {
                return;
            }

            long identityId = MyAPIGateway.Session.Player.IdentityId;
            _gpsEntries.Clear();
            MyAPIGateway.Session.GPS.GetGpsList(identityId, _gpsEntries);
            for (int i = 0; i < hashes.Count; i++)
            {
                int hash = hashes[i];
                if (visible)
                    _temporarilyVisibleGpsHashes.Remove(hash);
                else
                    _temporarilyVisibleGpsHashes.Add(hash);

                MyAPIGateway.Session.GPS.SetShowOnHud(identityId, hash, visible);
                for (int gpsIndex = 0; gpsIndex < _gpsEntries.Count; gpsIndex++)
                {
                    IMyGps gps = _gpsEntries[gpsIndex];
                    if (gps != null && gps.Hash == hash)
                        gps.ShowOnHud = visible;
                }
            }
            _textureDirty = true;
            PlayUiSound(HudClickSound);
        }

        public void DeleteGps(IReadOnlyList<int> hashes)
        {
            if (hashes == null || hashes.Count == 0 ||
                MyAPIGateway.Session == null || MyAPIGateway.Session.Player == null ||
                MyAPIGateway.Session.GPS == null)
            {
                return;
            }

            long identityId = MyAPIGateway.Session.Player.IdentityId;
            for (int i = 0; i < hashes.Count; i++)
            {
                int hash = hashes[i];
                _temporarilyVisibleGpsHashes.Remove(hash);
                MyAPIGateway.Session.GPS.RemoveGps(identityId, hash);
                MyAPIGateway.Session.GPS.RemoveLocalGps(hash);
            }
            _textureDirty = true;
            PlayUiSound(HudClickSound);
        }

        public void ShowStationDetails(PlanetTradeStationMarker station)
        {
            if (station == null)
                return;

            string faction = station.FullFactionName;
            if (!string.IsNullOrWhiteSpace(station.FactionTag))
                faction += " [" + station.FactionTag + "]";
            string description = LocHelper.Format("HighwayEngineer_Station_Details", station.FullName, faction,
                station.RelationshipName, station.Reputation);
            if (!string.IsNullOrWhiteSpace(station.FactionDescription))
                description += "\n\n" + station.FactionDescription;

            Action<string, string> show = MessageBoxRequested;
            if (show != null)
                show(LocHelper.GetLoc("HighwayEngineer_Station_DetailsTitle"), description);
            PlayUiSound(HudClickSound);
        }

        void RemoveUnusedPlanetTradeStationMarkers()
        {
            for (int i = _planetTradeStationMarkers.Count - 1; i >= 0; i--)
            {
                PlanetTradeStationMarker marker = _planetTradeStationMarkers[i];
                if (marker.UsedThisFrame)
                    continue;

                _planetTradeStationMarkers.RemoveAt(i);
                _planetTradeStationMarkersById.Remove(marker.StationId);
            }
        }

        void ClearPlanetTradeStationMarkers()
        {
            _planetTradeStationMarkers.Clear();
            _planetTradeStationMarkersById.Clear();
        }

        static string FormatSurfaceWaypointName(Vector3 localDirection)
        {
            double latitude = 0d;
            double longitude = 0d;
            if (localDirection.Normalize() > 0.000001f)
            {
                latitude = Math.Asin(ClampUnit(localDirection.Y)) * 180d / Math.PI;
                longitude = Math.Atan2(localDirection.X, localDirection.Z) * 180d / Math.PI;
            }

            return string.Format(
                "{0}°{1:0.0} {2}°{3:0.0}",
                latitude < 0d ? "S" : "N",
                Math.Abs(latitude),
                longitude < 0d ? "W" : "E",
                Math.Abs(longitude));
        }

        static double ClampUnit(double value)
        {
            if (value < -1d)
                return -1d;
            if (value > 1d)
                return 1d;
            return value;
        }

        static string SanitizeGpsName(string name)
        {
            return string.IsNullOrWhiteSpace(name)
                ? LocHelper.GetLoc("HighwayEngineer_Common_Unknown")
                : name.Replace(":", "_");
        }

        void PlayUiSound(MySoundPair sound)
        {
            if (sound == null)
                return;
            if (_uiSoundEmitter == null)
                _uiSoundEmitter = new MyEntity3DSoundEmitter(null, false, 0f);
            _uiSoundEmitter.PlaySound(sound, force2D: true);
        }

        void RotatePlanetOrbit(float horizontal, float vertical)
        {
            Vector3 view = _viewState.PlanetOrbitView;
            Vector3 right = _viewState.PlanetOrbitRight;
            Vector3 up = _viewState.PlanetOrbitUp;
            NormalizeSeOrbit(ref view, ref right, ref up);

            if (Math.Abs(horizontal) > 0.000001f)
            {
                view = RotateAroundAxis(view, up, horizontal);
                right = RotateAroundAxis(right, up, horizontal);
            }

            if (Math.Abs(vertical) > 0.000001f)
            {
                view = RotateAroundAxis(view, right, -vertical);
                up = RotateAroundAxis(up, right, -vertical);
            }

            NormalizeSeOrbit(ref view, ref right, ref up);
            _viewState.PlanetOrbitView = view;
            _viewState.PlanetOrbitRight = right;
            _viewState.PlanetOrbitUp = up;
        }

        static void RotateCameraRoll(
            ref Vector3 view,
            ref Vector3 right,
            ref Vector3 up,
            float angle)
        {
            NormalizeSeOrbit(ref view, ref right, ref up);
            right = RotateAroundAxis(right, view, angle);
            up = RotateAroundAxis(up, view, angle);
            NormalizeSeOrbit(ref view, ref right, ref up);
        }

        static Vector3 RotateAroundAxis(Vector3 value, Vector3 axis, float angle)
        {
            if (axis.Normalize() <= 0.000001f)
                return value;

            float cosine = (float)Math.Cos(angle);
            float sine = (float)Math.Sin(angle);
            return value * cosine +
                   Vector3.Cross(axis, value) * sine +
                   axis * Vector3.Dot(axis, value) * (1f - cosine);
        }

        static void NormalizeSeOrbit(
            ref Vector3 backward,
            ref Vector3 right,
            ref Vector3 up)
        {
            if (!IsFinite(backward) || backward.Normalize() <= 0.000001f)
                backward = Vector3.Backward;

            if (!IsFinite(right))
                right = Vector3.Right;
            right -= backward * Vector3.Dot(right, backward);
            if (right.Normalize() <= 0.000001f)
            {
                Vector3 referenceUp = IsFinite(up) ? up : Vector3.Up;
                right = Vector3.Cross(referenceUp, backward);
                if (!IsFinite(right) || right.Normalize() <= 0.000001f)
                {
                    referenceUp = Math.Abs(Vector3.Dot(backward, Vector3.Up)) > 0.98f
                        ? Vector3.Forward
                        : Vector3.Up;
                    right = Vector3.Cross(referenceUp, backward);
                    if (right.Normalize() <= 0.000001f)
                        right = Vector3.Right;
                }
            }

            up = Vector3.Cross(backward, right);
            if (!IsFinite(up) || up.Normalize() <= 0.000001f)
                up = Vector3.Up;

            right = Vector3.Cross(up, backward);
            if (!IsFinite(right) || right.Normalize() <= 0.000001f)
                right = Vector3.Right;
        }

        static bool IsFinite(Vector3 value)
        {
            return !float.IsNaN(value.X) && !float.IsInfinity(value.X) &&
                   !float.IsNaN(value.Y) && !float.IsInfinity(value.Y) &&
                   !float.IsNaN(value.Z) && !float.IsInfinity(value.Z);
        }

        void ResetPlanetOrbit()
        {
            _viewState.PlanetOrbitView = Vector3.Backward;
            _viewState.PlanetOrbitRight = Vector3.Right;
            _viewState.PlanetOrbitUp = Vector3.Up;
        }

        public void ZoomMap(int wheelDelta)
        {
            if (wheelDelta == 0)
                return;
            // Dragging a node, the wheel lifts it instead (the map may get the wheel while it moves).
            if (_roadDragMode == RoadDragMode.MoveNode)
            {
                ScrollDraggedNode(wheelDelta);
                return;
            }
            if (_viewState.SurfaceViewActive)
            {
                ZoomSurfaceView(wheelDelta);
                return;
            }

            float multiplier = wheelDelta > 0 ? 1.25f : 1f / 1.25f;
            float zoom = Math.Max(PLANET_MINIMUM_ZOOM, _viewState.Zoom * multiplier);
            if (!float.IsInfinity(zoom))
                _viewState.Zoom = zoom;
            _textureDirty = true;
        }

        bool EnsureFocusedPlanet()
        {
            PlanetSnapshot focused = ResolveFocusedPlanet();
            if (focused != null)
                return true;

            focused = _mapData.FindClosestPlanet();
            if (focused == null)
                return false;

            _viewState.FocusedPlanetId = focused.EntityId;
            return true;
        }

        PlanetSnapshot ResolveSelectedPlanet()
        {
            if (_viewState.FocusedPlanetId == 0L)
                return null;
            return _mapData.FindPlanet(_viewState.FocusedPlanetId);
        }

        PlanetSnapshot ResolveFocusedPlanet()
        {
            PlanetSnapshot focused = ResolveSelectedPlanet();
            return focused ?? _mapData.FindClosestPlanet();
        }


        static float LerpZoom(float start, float end, float amount)
        {
            start = Math.Max(0.0001f, start);
            end = Math.Max(0.0001f, end);
            return start * (float)Math.Pow(end / start, MathHelper.Clamp(amount, 0f, 1f));
        }

        bool MapPlanetsReady()
        {
            bool ready = true;
            for (int i = 0; i < _mapData.Planets.Count; i++)
                if (!PlanetMetadataWarmup.IsReady(_mapData.Planets[i].EntityId))
                    ready = false;
            return ready;
        }

        void UpdateMapRequests()
        {
            if (_mapRenderer != null && MapPlanetsReady())
                _mapRenderer.UpdateRequests(_mapData, MapMode.PlanetaryMap, _viewState);
        }

        void RenderMapTexture()
        {
            // Not ready: the texture stays dirty, so the map is drawn on the first frame it is.
            if (_mapTexture == null ||
                !_mapTexture.IsAvailable || _mapRenderer == null || !MapPlanetsReady())
                return;
            try
            {
                Vector2I size = _mapTexture.Size;
                int byteCount = checked(size.X * size.Y * 4);
                if (_lastMapPixels == null || _lastMapPixels.Length != byteCount)
                    _lastMapPixels = new byte[byteCount];
                var canvas = new Acrux.Rendering.PixelCanvas(_lastMapPixels, size.X, size.Y);
                // Paint synchronously, then pair this upload with its overlays.
                // Publish that captured state at draw time, not the newer input state.
                _mapRenderer.Render(canvas, _mapData, MapMode.PlanetaryMap, _viewState);
                DrawRoadOverlay(canvas);
                MapOverlayFrame overlays = MapOverlayFrame.Capture(this, _editingNode);
                overlays.Bounds = _mapBounds;
                {
                    // Player movement remains live, but projects through the
                    // camera/reference data of the visible texture, not new input.
                    overlays.ProjectionData = _mapData.CaptureProjectionData();
                    overlays.PlanetView = new MapViewState
                    {
                        FocusedPlanetId = _viewState.FocusedPlanetId,
                        Zoom = _viewState.Zoom,
                        FollowPlayer = _viewState.FollowPlayer,
                        HasStaticCameraPosition = _viewState.HasStaticCameraPosition,
                        StaticCameraPosition = _viewState.StaticCameraPosition,
                        PlanetOrientation = _viewState.PlanetOrientation,
                        HasPlanetFreeReferenceUpWorld = _viewState.HasPlanetFreeReferenceUpWorld,
                        PlanetFreeReferenceUpWorld = _viewState.PlanetFreeReferenceUpWorld,
                        PlanetOrbitView = _viewState.PlanetOrbitView,
                        PlanetOrbitRight = _viewState.PlanetOrbitRight,
                        PlanetOrbitUp = _viewState.PlanetOrbitUp,
                        PlanetCenterOffsetNormalized = _viewState.PlanetCenterOffsetNormalized,
                        MaximumZoom = _viewState.MaximumZoom,
                        SurfaceViewActive = _viewState.SurfaceViewActive,
                        SurfaceViewOnly = _viewState.SurfaceViewOnly,
                        SurfaceViewDistance = _viewState.SurfaceViewDistance,
                        SurfaceViewYaw = _viewState.SurfaceViewYaw,
                        SurfaceViewPitch = _viewState.SurfaceViewPitch,
                        AtmosphereVisible = false
                    };
                }
                _textureDirty = !_mapTexture.UploadPixels(_lastMapPixels);
                if (!_textureDirty)
                    GeneratedTextureNames.NotifyChanged(_mapTexture.Texture);
                if (!_textureDirty)
                    _overlayPresentation.Uploaded(overlays);
            }
            catch (Exception error)
            {
                _textureDirty = true;
                ErrorHandlerHelper.LogError(error, typeof(RoadPlannerViewModel));
            }
        }


        void DrawRoadOverlay(Acrux.Rendering.PixelCanvas canvas)
        {
            _roadSlopeWarning = null;
            if (!RoadsVisible) return;
            if (_mapRenderer == null ||
                _mapBounds.Width <= 0) return;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Radius <= 0) return;
            EnsureCommittedRoads(planet);
            float scaleX = canvas.Width / _mapBounds.Width;
            float scaleY = canvas.Height / _mapBounds.Height;
            float globeRadius = _viewState.SurfaceViewActive
                ? SurfaceViewGlobeRadius(planet, Math.Min(canvas.Width, canvas.Height))
                : Math.Min(canvas.Width, canvas.Height) * 0.47f * Math.Max(_viewState.Zoom, 0.0001f);
            // Geometry and game queries run here; the collected raster commands
            // then run over row bands on the worker pool.
            _roadRaster.Clear();
            DrawHoveredDepotRanges(canvas, planet, scaleX, scaleY);
            foreach (RoadCurve committed in _committedRoads)
                DrawRoadCurve(canvas, planet, committed,
                    BulldozeMode && IsRoadMarked(committed) ? new Color(235, 64, 64)
                        : RepairMode && IsRoadMarked(committed) ? new Color(245, 190, 60)
                        : committed.BridgeId != 0 ? new Color(150, 110, 220) : new Color(85, 205, 235),
                    RoadPixelWidth(committed.WidthMeters, globeRadius, planet),
                    scaleX, scaleY, true);
            int width = RoadPixelWidth(RoadWidthMeters, globeRadius, planet);
            if (UpdateSelfCrossings(planet))
                _roadSlopeWarning = LocHelper.GetLoc("HighwayEngineer_Status_CrossesItself");
            UpdateBridges(planet);
            if (_bridgeError != null && _roadSlopeWarning == null)
                _roadSlopeWarning = _bridgeError;
            var previews = new List<RoadPreviewSegment>();
            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve)) continue;
                // A bridge does not follow the terrain, so the road's slope limit is not its own.
                bool bridge = IsBridgeSegment(segment);
                bool exceedsSlope = bridge ? _invalidBridgeSegments.Contains(segment)
                    : RoadCurveExceedsSlope(planet, curve, segment);
                if (exceedsSlope && !bridge && _roadSlopeWarning == null)
                    _roadSlopeWarning = LocHelper.Format("HighwayEngineer_Status_MayExceedSlope", segment);
                bool crossesItself = _selfCrossingSegments.Contains(segment);
                Color roadColor = exceedsSlope || crossesItself ? new Color(235, 64, 64)
                    : bridge ? new Color(170, 96, 240) : new Color(72, 215, 112);
                previews.Add(new RoadPreviewSegment { Curve = curve, Color = roadColor,
                    Segment = segment, IsBridge = bridge });
            }
            // Bridges are not earthwork: they get no embankment preview.
            if (RoadFillShoulders && RoadFillAngleDegrees > 0f)
                foreach (RoadPreviewSegment preview in previews)
                    if (!preview.IsBridge)
                        DrawRoadEmbankment(canvas, planet, preview, width,
                            scaleX, scaleY, globeRadius, new Color(226, 180, 80));
            foreach (RoadPreviewSegment preview in previews)
                DrawRoadCurve(canvas, planet, preview.Curve, preview.Color, width,
                    scaleX, scaleY, false);
            RasterBands.Paint(canvas, _roadRaster);
            _roadRaster.Clear();
        }

        readonly List<Action<Acrux.Rendering.PixelCanvas, int, int>> _roadRaster =
            new List<Action<Acrux.Rendering.PixelCanvas, int, int>>();

        /// <summary>
        /// Roads are drawn at their physical width, with a one-pixel floor so
        /// narrow roads stay legible at whole-planet zoom. Unknown widths (older
        /// saved segments) fall back to a one-pixel line.
        /// </summary>
        static int RoadPixelWidth(double widthMeters, float globeRadius, PlanetSnapshot planet)
        {
            if (widthMeters <= 0.0) return 1;
            return (int)Math.Max(1, Math.Min(int.MaxValue,
                Math.Round(widthMeters * globeRadius / planet.Radius)));
        }

        struct RoadPreviewSegment
        {
            public RoadCurve Curve;
            public Color Color;
            public int Segment;
            public bool IsBridge;
        }

        struct RoadEmbankmentPoint
        {
            public Vector2 Pixel;
            public Vector2 LeftPixel;
            public float LeftWidth;
            public float RightWidth;
            public bool Visible;
            public bool LeftVisible;
        }

        void DrawRoadEmbankment(Acrux.Rendering.PixelCanvas canvas,
            PlanetSnapshot planet, RoadPreviewSegment preview, int roadWidth,
            float scaleX, float scaleY, float globeRadius, Color embankmentColor)
        {
            double slope = Math.Tan(RoadFillAngleDegrees * Math.PI / 180.0);
            if (slope <= 0.0 || planet.Planet == null || planet.Planet.MarkedForClose) return;
            int steps = (int)Math.Max(16, Math.Min(128,
                Math.Ceiling(preview.Curve.Arc * globeRadius * 0.5f)));
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData,
                    _viewState, out projector))
                return;
            double[] profile;
            RoadProfilePoint[] samples = SampleRoadProfile(planet, preview.Curve,
                preview.Segment, steps, out profile);
            if (samples == null) return;
            var points = new RoadEmbankmentPoint[steps + 1];
            float pixelsPerMeter = globeRadius / planet.Radius;
            for (int i = 0; i <= steps; i++)
            {
                Vector2 screen;
                if (projector.TryProject(planet.Position + samples[i].Direction * planet.Radius, out screen))
                {
                    points[i].Pixel = new Vector2((screen.X - _mapBounds.X) * scaleX,
                        (screen.Y - _mapBounds.Y) * scaleY);
                    points[i].Visible = true;
                }
                if (projector.TryProject(planet.Position + samples[i].Left * planet.Radius, out screen))
                {
                    points[i].LeftPixel = new Vector2((screen.X - _mapBounds.X) * scaleX,
                        (screen.Y - _mapBounds.Y) * scaleY);
                    points[i].LeftVisible = true;
                }
                double leftDifference = Math.Abs(profile[i] - samples[i].LeftTerrain);
                double rightDifference = Math.Abs(profile[i] - samples[i].RightTerrain);
                // Keep subpixel earthwork visible at whole-planet zoom. Bound
                // the preview footprint so shallow angles cannot stall a frame.
                points[i].LeftWidth = leftDifference > 0.001
                    ? Math.Max(2f, (float)Math.Min(128.0, leftDifference / slope * pixelsPerMeter)) : 0f;
                points[i].RightWidth = rightDifference > 0.001
                    ? Math.Max(2f, (float)Math.Min(128.0, rightDifference / slope * pixelsPerMeter)) : 0f;
            }
            _roadRaster.Add((target, first, end) =>
            {
                for (int i = 1; i <= steps; i++)
                    if (points[i - 1].Visible && points[i].Visible)
                        BlendRoadEmbankmentStrip(target, points[i - 1], points[i],
                            roadWidth * 0.5f, embankmentColor, first, end);
            });
        }

        /// <summary>
        /// Samples a draft segment's terrain and expected road surface; null when the
        /// segment has no length. See <see cref="RoadEarthwork.SampleProfile"/>.
        /// </summary>
        RoadProfilePoint[] SampleRoadProfile(PlanetSnapshot planet, RoadCurve curve,
            int segment, int steps, out double[] profile)
        {
            return RoadEarthwork.SampleProfile(curve, planet.Radius,
                direction => SampleRoadTerrainHeight(planet, direction),
                NodeHeight(planet, segment - 1), NodeHeight(planet, segment),
                CurrentRoadParameters(), steps, out profile);
        }

        /// <summary>Node <paramref name="node"/>'s height above the average radius, its lift included.</summary>
        double NodeHeight(PlanetSnapshot planet, int node)
        {
            return Vector3D.Distance(_roadNodes[node], planet.Position) - planet.Radius + _roadNodeLifts[node];
        }

        /// <summary>The editor's road settings, as a push and the server use them.</summary>
        public RoadBuildParameters CurrentRoadParameters()
        {
            return new RoadBuildParameters
            {
                Material = RoadMaterialSubtype,
                WidthMeters = RoadWidthMeters,
                DepthMeters = RoadMaterialDepthMeters,
                ShoulderAngleDegrees = RoadFillShoulders ? RoadFillAngleDegrees : 0.0,
                MaxSlopeDegrees = RoadMaximumSlopeDegrees,
                SegmentDistanceMeters = RoadSegmentDistanceMeters,
                AverageHeight = RoadAverageHeight,
                GuideLines = RoadGuideLines
            };
        }

        /// <summary>
        /// The draft as a server quote request: planet, road settings and each segment's
        /// planet-local geometry (the same encoding the push uses). False without a
        /// planet or with an invalid segment.
        /// </summary>
        public bool TryBuildRoadQuoteRequest(out PacketRoadQuoteRequest request)
        {
            request = null;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Planet == null || _roadNodes.Count < 2) return false;
            int perSegment = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var geometry = new double[(_roadNodes.Count - 1) * perSegment];
            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve)) return false;
                Array.Copy(curve.ToLocalGeometry(planet.Planet.WorldMatrix), 0,
                    geometry, (segment - 1) * perSegment, perSegment);
            }
            request = new PacketRoadQuoteRequest
            {
                PlanetId = planet.EntityId,
                Parameters = CurrentRoadParameters(),
                Geometry = geometry
            };
            UpdateBridges(planet);
            var bridged = new bool[_roadNodes.Count - 1];
            bool anyBridge = false;
            for (int segment = 1; segment < _roadNodes.Count; segment++)
                anyBridge |= bridged[segment - 1] = IsBridgeSegment(segment);
            if (anyBridge)
            {
                // A bridge's road fill is its own setting; width and depth stay the road's.
                BridgeSettingsConfig bridge = MinimapLocalConfig.Current.Bridge;
                request.Bridge = new BridgeBuildParameters
                {
                    StructureMaterial = bridge.StructureMaterial,
                    PavementMaterial = bridge.PavementMaterial,
                    Design = bridge.Design,
                    SegmentMeters = bridge.SegmentMeters,
                    Segments = bridged,
                    NodeLifts = _roadNodeLifts.Exists(lift => lift > 0.0) ? _roadNodeLifts.ToArray() : null
                };
            }
            return true;
        }

        /// <summary>Estimated cut and fill (m³) and length of the draft's road (bridged segments left out).</summary>
        public bool TryEstimateRoadEarthwork(out double cutCubicMeters, out double fillCubicMeters,
            out double lengthMeters)
        {
            cutCubicMeters = fillCubicMeters = lengthMeters = 0.0;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Planet == null || planet.Planet.MarkedForClose ||
                _roadNodes.Count < 2) return false;
            UpdateBridges(planet);
            var estimate = new RoadEarthworkEstimate();
            for (int segment = 1; segment < _roadNodes.Count; segment++)
            {
                RoadCurve curve;
                if (!TryGetRoadCurve(planet, segment, out curve)) return false;
                if (IsBridgeSegment(segment)) continue;
                RoadEarthwork.Add(ref estimate, RoadEarthwork.Estimate(new List<RoadCurve> { curve },
                    new List<double> { NodeHeight(planet, segment - 1), NodeHeight(planet, segment) }, planet.Radius,
                    direction => SampleRoadTerrainHeight(planet, direction), CurrentRoadParameters()));
            }
            cutCubicMeters = estimate.CutCubicMeters;
            fillCubicMeters = estimate.FillCubicMeters;
            lengthMeters = estimate.LengthMeters;
            return lengthMeters > 0.0;
        }

        static double SampleRoadTerrainHeight(PlanetSnapshot planet, Vector3D direction)
        {
            Vector3D surface = planet.Planet.GetClosestSurfacePointGlobal(
                planet.Position + direction * planet.MaximumRadius);
            return Vector3D.Distance(surface, planet.Position) - planet.Radius;
        }

        static void BlendRoadEmbankmentStrip(Acrux.Rendering.PixelCanvas canvas,
            RoadEmbankmentPoint start, RoadEmbankmentPoint end,
            float halfRoadWidth, Color color, int firstRow, int endRow)
        {
            Vector2 edge = end.Pixel - start.Pixel;
            float lengthSquared = edge.LengthSquared();
            if (lengthSquared < 0.01f) return;
            Vector2 leftPixel = start.LeftVisible ? start.LeftPixel : end.LeftPixel;
            bool leftIsPositive = !start.LeftVisible && !end.LeftVisible ||
                edge.X * (leftPixel.Y - start.Pixel.Y) -
                edge.Y * (leftPixel.X - start.Pixel.X) >= 0f;
            float extent = halfRoadWidth + Math.Max(Math.Max(start.LeftWidth, start.RightWidth),
                Math.Max(end.LeftWidth, end.RightWidth)) + 1f;
            int top = (int)Math.Max(Math.Max(0, firstRow), Math.Min(canvas.Height,
                Math.Floor(Math.Min(start.Pixel.Y, end.Pixel.Y) - extent)));
            int bottom = (int)Math.Max(-1, Math.Min(Math.Min(canvas.Height, endRow) - 1,
                Math.Ceiling(Math.Max(start.Pixel.Y, end.Pixel.Y) + extent)));
            for (int y = top; y <= bottom; y++)
            {
                // Pixel centres (x + 0.5) inside the outer capsule are candidates;
                // the road itself (inner capsule) never blends, so skip its span.
                double outerLeft, outerRight, innerLeft, innerRight;
                if (!ScreenSpaceRoadStroke.TryGetCapsuleRowSpan(start.Pixel, end.Pixel,
                        extent, y + 0.5, out outerLeft, out outerRight))
                    continue;
                bool hasInner = ScreenSpaceRoadStroke.TryGetCapsuleRowSpan(start.Pixel,
                    end.Pixel, halfRoadWidth - 0.01, y + 0.5, out innerLeft, out innerRight);
                int left = (int)Math.Max(0, Math.Min(canvas.Width, Math.Floor(outerLeft - 0.5)));
                int right = (int)Math.Max(-1, Math.Min(canvas.Width - 1, Math.Ceiling(outerRight - 0.5)));
                // Pixels strictly inside [innerLeft, innerRight] are on the road.
                int skipFrom = hasInner ? (int)Math.Ceiling(innerLeft - 0.5) : int.MaxValue;
                int skipTo = hasInner ? (int)Math.Floor(innerRight - 0.5) : int.MinValue;
                for (int x = left; x <= right; x++)
                {
                    if (x >= skipFrom && x <= skipTo)
                    {
                        x = skipTo;
                        continue;
                    }
                    float dx = x + 0.5f - start.Pixel.X;
                    float dy = y + 0.5f - start.Pixel.Y;
                    float t = MathHelper.Clamp((dx * edge.X + dy * edge.Y) / lengthSquared, 0f, 1f);
                    float cross = edge.X * dy - edge.Y * dx;
                    float closestX = dx - edge.X * t;
                    float closestY = dy - edge.Y * t;
                    float fromRoad = (float)Math.Sqrt(closestX * closestX + closestY * closestY) - halfRoadWidth;
                    if (fromRoad < 0f) continue;
                    float shoulder = (cross >= 0f) == leftIsPositive
                        ? MathHelper.Lerp(start.LeftWidth, end.LeftWidth, t)
                        : MathHelper.Lerp(start.RightWidth, end.RightWidth, t);
                    if (shoulder <= 0f || fromRoad > shoulder) continue;
                    canvas.BlendPixel(x, y, color, 0.55f * (1f - fromRoad / Math.Max(1f, shoulder)));
                }
            }
        }

        void DrawRoadCurve(Acrux.Rendering.PixelCanvas canvas,
            PlanetSnapshot planet, RoadCurve curve, Color color, int width,
            float scaleX, float scaleY, bool markEndpoints)
        {
            PlanetSurfaceProjector projector;
            if (!PlanetSurfaceProjector.TryCreate(_mapRenderer, _mapBounds, _mapData,
                    _viewState, out projector))
                return;
            // Only the part of the road that can touch the texture is refined.
            // Wide strokes hide chord error, so they need fewer, longer segments.
            float pixelScale = Math.Max(1e-6f, Math.Min(scaleX, scaleY));
            float margin = (width * 0.5f + 3f) / pixelScale;
            var clip = new RectangleF(_mapBounds.X - margin, _mapBounds.Y - margin,
                _mapBounds.Width + margin * 2f, _mapBounds.Height + margin * 2f);
            float refinePixels = Math.Max(2f, width * 0.25f) / pixelScale;
            FlattenRoadCurve(planet, curve, ref projector, clip, refinePixels, _roadCurvePoints);

            var pixels = new Vector2[_roadCurvePoints.Count];
            var connected = new bool[_roadCurvePoints.Count];
            var dots = new List<Vector2>(2);
            bool hadLast = false;
            for (int i = 0; i < _roadCurvePoints.Count; i++)
            {
                RoadCurvePoint point = _roadCurvePoints[i];
                if (!point.Visible)
                {
                    hadLast = false;
                    continue;
                }
                var pixel = new Vector2((point.Screen.X - _mapBounds.X) * scaleX,
                                        (point.Screen.Y - _mapBounds.Y) * scaleY);
                pixels[i] = pixel;
                connected[i] = hadLast;
                if (markEndpoints && (point.T == 0.0 || point.T == 1.0) &&
                    pixel.X >= -2 && pixel.X < canvas.Width + 2 &&
                    pixel.Y >= -2 && pixel.Y < canvas.Height + 2)
                    dots.Add(new Vector2((int)pixel.X, (int)pixel.Y));
                hadLast = true;
            }
            _roadRaster.Add((target, first, end) =>
            {
                for (int i = 1; i < pixels.Length; i++)
                    if (connected[i])
                        ScreenSpaceRoadStroke.Draw(target, pixels[i - 1], pixels[i],
                            color, width, first, end);
                // Endpoint dots: a radius-2 disc around the integer pixel.
                foreach (Vector2 dot in dots)
                    ScreenSpaceRoadStroke.Draw(target, dot, dot, color, 4, first, end);
            });
        }

        void EnsureMapTextureForBounds()
        {
            if (_mapBounds.Width <= 0f || _mapBounds.Height <= 0f)
                return;

            var requested = new Vector2I(
                Math.Max(1, (int)Math.Round(_mapBounds.Width)),
                Math.Max(1, (int)Math.Round(_mapBounds.Height)));
            Vector2I supported = DynamicMapTexture.FitToRenderer(requested);
            if (_mapTexture != null && _mapTexture.Size == supported)
            {
                MapTexture = _mapTexture.Texture;
                return;
            }

            if (_mapTexture != null)
                _mapTexture.Dispose();
            _mapTexture = supported.X > 0 && supported.Y > 0
                ? new DynamicMapTexture(GeneratedTextureNames.Next("MinimapMap"), requested)
                : null;
            _overlayPresentation.Reset();
            _lastMapPixels = null;
            MapTexture = _mapTexture == null ? null : _mapTexture.Texture;
            _textureDirty = true;
        }

        public PlanetColorCubemap GetLayerCubemap(CartographyLayer layer)
        {
            if (_mapRenderer == null)
                return null;
            PlanetSnapshot planet = ResolveFocusedPlanet();
            return planet == null ? null : _mapRenderer.GetCubemap(planet.EntityId, layer);
        }

        public PlanetCubeFace GetPlayerPreviewFace()
        {
            PlanetSnapshot snapshot = ResolveFocusedPlanet();
            if (snapshot == null || snapshot.Planet == null || snapshot.Planet.MarkedForClose)
                return PlanetCubeFace.Front;

            Vector3D worldDirection = _mapData.PlayerPosition - snapshot.Position;
            if (worldDirection.Normalize() <= 1e-9)
                worldDirection = snapshot.Planet.WorldMatrix.Backward;

            Vector3 localDirection = new Vector3(
                (float)Vector3D.Dot(worldDirection, snapshot.Planet.WorldMatrix.Right),
                (float)Vector3D.Dot(worldDirection, snapshot.Planet.WorldMatrix.Up),
                (float)Vector3D.Dot(worldDirection, snapshot.Planet.WorldMatrix.Backward));
            if (localDirection.Normalize() <= 0.000001f)
                return PlanetCubeFace.Front;

            float u;
            float v;
            return CartographyModule.DirectionToFaceUv(localDirection, out u, out v);
        }

        public void SpawnStreetView(Vector2 screenPoint)
        {
            if (!CanUseStreetView() || _mapRenderer == null)
                return;

            Vector3D surfacePosition;
            if (!_mapRenderer.TryGetSurfacePosition(
                    _mapBounds,
                    screenPoint,
                    _mapData,
                    _viewState,
                    out surfacePosition))
            {
                return;
            }

            PlanetSnapshot snapshot = ResolveFocusedPlanet();
            if (snapshot == null || snapshot.Planet == null)
                return;

            Vector3D up = surfacePosition - snapshot.Position;
            if (up.Normalize() <= 1e-9)
                return;
            Vector3D forward = GetRandomHorizonDirection(up);
            if (forward.Normalize() <= 1e-9)
                return;

            Vector3D cameraPosition = surfacePosition + up * STREET_CAMERA_HEIGHT_METERS;
            MatrixD cameraWorld = MatrixD.CreateWorld(cameraPosition, forward, up);
            MatrixD viewMatrix = MatrixD.Invert(cameraWorld);
            _closeNow();

            MyAPIGateway.Session.SetCameraController(
                MyCameraControllerEnum.Spectator,
                null,
                cameraPosition);
            MySpectatorCameraController spectator =
                MyAPIGateway.Session.CameraController as MySpectatorCameraController;
            if (spectator == null)
                return;
            spectator.SpectatorCameraMovement = MySpectatorCameraMovementEnum.UserControlled;
            spectator.SetViewMatrix((Matrix)viewMatrix);
            MyAPIGateway.Utilities.ShowNotification(LocHelper.GetLoc("HighwayEngineer_Planner_StreetViewActive"), 5000, "White");
        }

        bool CanUseStreetView()
        {
            if (ResolveFocusedPlanet() == null)
                return false;
            if (MyAPIGateway.Session == null)
                return false;
            if (MyAPIGateway.Session.SessionSettings != null &&
                MyAPIGateway.Session.SessionSettings.EnableSpectator)
            {
                return true;
            }

            var player = MyAPIGateway.Session.Player;
            ulong steamUserId = player == null ? 0UL : player.SteamUserId;
            return steamUserId != 0UL &&
                   MyAPIGateway.Session.IsUserAdmin(steamUserId) &&
                   (MyAPIGateway.Session.CreativeMode || MyAPIGateway.Session.HasCreativeRights);
        }

        Vector3D GetRandomHorizonDirection(Vector3D up)
        {
            Vector3D reference = Math.Abs(Vector3D.Dot(up, Vector3D.Up)) < 0.95d
                ? Vector3D.Up
                : Vector3D.Right;
            Vector3D tangent = Vector3D.Cross(reference, up);
            if (tangent.Normalize() <= 1e-9)
                return Vector3D.Zero;
            Vector3D bitangent = Vector3D.Cross(up, tangent);
            if (bitangent.Normalize() <= 1e-9)
                return Vector3D.Zero;
            double angle = _streetViewRandom.NextDouble() * MathHelper.TwoPi;
            return tangent * Math.Cos(angle) + bitangent * Math.Sin(angle);
        }

        void MarkTextureDirty()
        {
            _textureDirty = true;
        }

        bool ResolveFullScreenGpsVisible()
        {
            return _config.IsFullScreenGpsVisible();
        }

        public bool ResolveFullScreenRadioVisible()
        {
            return _config.IsFullScreenRadioVisible();
        }

        string _loggedMapState;

        /// <summary>The map's state in the game log when it changes: an LCD planner shows no status line.</summary>
        void LogMapState(string state)
        {
            if (state == _loggedMapState) return;
            _loggedMapState = state;
            VRage.Utils.MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Planner map: " + state);
        }

        void SaveLocalConfig()
        {
            _config.Save();
        }

        string GetStatus()
        {
            if (_mapTexture == null || !_mapTexture.IsAvailable)
            {
                LogMapState("no map texture (bounds " + _mapBounds + ")");
                return LocHelper.GetLoc("HighwayEngineer_Planner_MapNotReady");
            }
            if (_mapRenderer != null && !string.IsNullOrWhiteSpace(_mapRenderer.Error))
            {
                LogMapState("cartography error: " + _mapRenderer.Error);
                return LocHelper.Format("HighwayEngineer_Planner_CartographyError", _mapRenderer.Error);
            }

            PlanetSnapshot closest = ResolveFocusedPlanet();
            if (closest == null)
            {
                LogMapState("no planet found");
                return LocHelper.GetLoc("HighwayEngineer_Planner_NoPlanet");
            }
            bool loaded = GetLayerCubemap(_viewState.Layer) != null;
            LogMapState((loaded ? "ready: " : "decoding: ") + closest.Name + " (" + closest.EntityId + ") " + _viewState.Layer);
            return LocHelper.Format(
                "HighwayEngineer_Planner_Footer",
                closest.Name,
                GetLayerName(_viewState.Layer),
                LocHelper.GetLoc(loaded ? "HighwayEngineer_Planner_MapReady" : "HighwayEngineer_Planner_Decoding"),
                _roadNodes.Count,
                RoadParameterError ?? _roadSlopeWarning ?? RoadEditorStatus);
        }

        static string GetLayerName(CartographyLayer layer)
        {
            switch (layer)
            {
                case CartographyLayer.Terrain:
                    return LocHelper.GetLoc("HighwayEngineer_Layer_Terrain");
                case CartographyLayer.Materials:
                    return LocHelper.GetLoc("HighwayEngineer_Layer_Materials");
                case CartographyLayer.Biomes:
                    return LocHelper.GetLoc("HighwayEngineer_Layer_Biomes");
                default:
                    return LocHelper.GetLoc("HighwayEngineer_Layer_Satellite");
            }
        }
    }
}

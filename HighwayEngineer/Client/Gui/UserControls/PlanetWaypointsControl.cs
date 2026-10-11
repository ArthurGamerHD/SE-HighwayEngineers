using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System;
using System.Collections.Generic;
using Acrux.Controls;
using Acrux.Core;
using Acrux.Core.Invalidation;
using Acrux.Core.PropertySystem;
using Acrux.Layout;
using Acrux.Rendering;
using Acrux.ScreenSpace;
using HighwayEngineer.Client.Maps;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HighwayEngineer.Client.Gui.UserControls
{
    public sealed class PlanetWaypointsControl : VisualElement
    {
        const float DragThresholdSquared = 16f;

        public Func<IReadOnlyList<PlanetWaypointMarker>> MarkersProvider { get; set; }
        /// <summary>The player's GPS markers (drawn under the road nodes).</summary>
        public Func<IReadOnlyList<PlanetWaypointMarker>> GpsMarkersProvider { get; set; }
        public Action<PlanetWaypointMarker, Vector2> GpsClicked { get; set; }
        public Func<IReadOnlyList<RoadSegmentLabel>> RoadSegmentLabelsProvider { get; set; }
        public Func<IReadOnlyList<RoadBridgeToggle>> BridgeTogglesProvider { get; set; }
        /// <summary>A bridge badge was clicked (the segment's end node).</summary>
        public Action<int> BridgeToggleClicked { get; set; }
        public Func<IReadOnlyList<PlanetTradeStationMarker>> TradeStationsProvider { get; set; }
        public Func<IReadOnlyList<RoadDepotMarker>> DepotsProvider { get; set; }
        /// <summary>The depot under the pointer changed (its grid id; 0 when none).</summary>
        public Action<long> DepotHovered { get; set; }
        public Func<PlanetPlayerMarker> PlayerMarkerProvider { get; set; }
        public Action<PlanetWaypointMarker, Vector2> WaypointClicked { get; set; }
        public Action<PlanetWaypointMarker, Vector2> WaypointRightClicked { get; set; }
        public Action<PlanetTradeStationMarker, Vector2> TradeStationClicked { get; set; }
        public Action<int, Vector2> RoadDragStarted { get; set; }
        /// <summary>The wheel while a road node is held: its delta.</summary>
        public Action<int> RoadScrolled { get; set; }
        public Action<Vector2, Vector2, Vector2> RoadDraggedAt { get; set; }
        public Action<Vector2, bool> RoadDragEnded { get; set; }

        IReadOnlyList<PlanetWaypointMarker> _markers;
        IReadOnlyList<PlanetWaypointMarker> _gps;
        IReadOnlyList<PlanetTradeStationMarker> _stations;
        IReadOnlyList<RoadDepotMarker> _depots;
        IReadOnlyList<RoadBridgeToggle> _bridgeToggles;
        long _hoveredDepot;
        float _scale = 1f;
        Vector2 _roadPressPoint;
        Vector2 _roadLastPoint;
        bool _roadPressed;
        bool _roadDragging;

        protected override void DrawSelf(RenderContext context)
        {
            _stations =
                TradeStationsProvider == null ? null : TradeStationsProvider();
            _markers = MarkersProvider == null ? null : MarkersProvider();
            _gps = GpsMarkersProvider == null ? null : GpsMarkersProvider();
            _depots = DepotsProvider == null ? null : DepotsProvider();
            // Hover events only reach this control over a marker; leaving it clears the depot.
            if (!IsPointerOver)
                SetHoveredDepot(0);
            PlanetPlayerMarker player = PlayerMarkerProvider == null
                ? null
                : PlayerMarkerProvider();

            _scale = Math.Max(0.75f, Math.Min(Bounds.Width, Bounds.Height) / 700f);
            DrawDepots(context, _depots, _scale);
            DrawTradeStations(context, _stations, _scale);
            DrawGps(context, _gps, _scale);
            DrawRoadSegmentLabels(context, RoadSegmentLabelsProvider == null
                ? null : RoadSegmentLabelsProvider(), _scale);
            _bridgeToggles = BridgeTogglesProvider == null ? null : BridgeTogglesProvider();
            DrawBridgeToggles(context, _bridgeToggles, _scale);
            DrawWaypoints(context, _markers, _scale);
            DrawPlayer(context, player, _scale);
        }

        protected override bool HitTestSelf(Vector2 point)
        {
            PlanetTradeStationMarker station;
            PlanetWaypointMarker waypoint;
            RoadDepotMarker depot;
            int segment;
            return TryGetBridgeToggleAt(point, out segment) ||
                   TryGetStationAt(point, out station) ||
                   TryGetWaypointAt(point, out waypoint) ||
                   TryGetGpsAt(point, out waypoint) ||
                   TryGetDepotAt(point, out depot);
        }

        internal override void PointerHovered(Vector2 point)
        {
            RoadDepotMarker depot;
            SetHoveredDepot(TryGetDepotAt(point, out depot) ? depot.GridId : 0);
        }

        void SetHoveredDepot(long gridId)
        {
            if (_hoveredDepot == gridId) return;
            _hoveredDepot = gridId;
            if (DepotHovered != null)
                DepotHovered(gridId);
        }

        const float DepotIconSize = 30f;

        void DrawDepots(RenderContext context, IReadOnlyList<RoadDepotMarker> depots, float scale)
        {
            if (depots == null) return;
            var highlight = new Color(120, 230, 140);
            for (int i = 0; i < depots.Count; i++)
            {
                RoadDepotMarker depot = depots[i];
                float size = DepotIconSize * scale;
                float inset = 5f * scale;
                var badge = new RectangleF(depot.ScreenPosition - new Vector2(size * 0.5f), new Vector2(size));
                if (depot.UsedByDraft)
                {
                    float ring = 3f * scale;
                    context.Texture("SquareSimple", new RectangleF(badge.X - ring, badge.Y - ring,
                        badge.Width + ring * 2f, badge.Height + ring * 2f), highlight);
                }
                context.Texture("SquareSimple", new RectangleF(badge.Position + new Vector2(Math.Max(1f, scale)),
                    badge.Size), new Color(0, 0, 0, 210));
                Color background = depot.BackgroundColor;
                Color icon = depot.IconColor;
                if (!depot.UsedByDraft && depot.GridId != _hoveredDepot)
                {
                    background.A = 170;
                    icon.A = 170;
                }
                context.Texture("SquareSimple", badge, background);
                context.Texture(depot.Icon, new RectangleF(badge.X + inset, badge.Y + inset,
                    badge.Width - inset * 2f, badge.Height - inset * 2f), icon, true);
                string label = LocHelper.Format(depot.UsedByDraft
                    ? "HighwayEngineer_Depot_LabelForDraft"
                    : "HighwayEngineer_Depot_Label", depot.FactionName);
                context.Text(label, new Vector2(badge.Right + 5f * scale, depot.ScreenPosition.Y),
                    0.45f * scale, depot.UsedByDraft ? highlight : Color.White,
                    HorizontalAlignment.Left, VerticalAlignment.Center, TextOptions.GetDropShadow(this));
            }
        }

        bool TryGetDepotAt(Vector2 point, out RoadDepotMarker depot)
        {
            depot = null;
            if (_depots == null) return false;
            float half = DepotIconSize * _scale * 0.5f;
            for (int i = _depots.Count - 1; i >= 0; i--)
            {
                Vector2 offset = point - _depots[i].ScreenPosition;
                if (Math.Abs(offset.X) > half || Math.Abs(offset.Y) > half) continue;
                depot = _depots[i];
                return true;
            }
            return false;
        }

        internal override void PointerPressed(Vector2 point)
        {
            PlanetTradeStationMarker station;
            PlanetWaypointMarker waypoint = null;
            int segment;
            _roadPressed = !TryGetBridgeToggleAt(point, out segment) &&
                           !TryGetStationAt(point, out station) &&
                           TryGetWaypointAt(point, out waypoint);
            _roadDragging = false;
            _roadPressPoint = _roadLastPoint = point;
            if (_roadPressed && RoadDragStarted != null)
                RoadDragStarted(waypoint.Hash - 1, point);
        }

        internal override void PointerMoved(Vector2 point, Vector2 delta)
        {
            if (!_roadPressed || !IsPressed)
                return;
            if (!_roadDragging &&
                Vector2.DistanceSquared(_roadPressPoint, point) >= DragThresholdSquared)
                _roadDragging = true;
            if (_roadDragging && point != _roadLastPoint && RoadDraggedAt != null)
                RoadDraggedAt(_roadPressPoint, point, point - _roadLastPoint);
            _roadLastPoint = point;
        }

        internal override void PointerReleased(Vector2 point, bool clicked)
        {
            bool wasRoadPressed = _roadPressed;
            bool wasDragged = _roadDragging;
            _roadPressed = _roadDragging = false;
            if (wasRoadPressed && RoadDragEnded != null)
                RoadDragEnded(point, wasDragged);
            if (!clicked || wasDragged)
                return;

            int segment;
            if (TryGetBridgeToggleAt(point, out segment))
            {
                if (BridgeToggleClicked != null)
                    BridgeToggleClicked(segment);
                return;
            }

            PlanetTradeStationMarker station;
            if (TryGetStationAt(point, out station))
            {
                if (TradeStationClicked != null)
                    TradeStationClicked(station, point);
                return;
            }

            PlanetWaypointMarker waypoint;
            if (TryGetWaypointAt(point, out waypoint))
            {
                if (WaypointClicked != null)
                    WaypointClicked(waypoint, point);
                return;
            }

            if (TryGetGpsAt(point, out waypoint) && GpsClicked != null)
                GpsClicked(waypoint, point);
        }

        internal override bool PointerScrolled(Vector2 point, int delta)
        {
            if (!_roadPressed || RoadScrolled == null)
                return false;
            RoadScrolled(delta);
            return true;
        }

        internal override void PointerCaptureLost()
        {
            if (_roadPressed && RoadDragEnded != null)
                RoadDragEnded(_roadLastPoint, _roadDragging);
            _roadPressed = _roadDragging = false;
        }

        internal override void SecondaryPointerReleased(Vector2 point, bool clicked)
        {
            PlanetWaypointMarker waypoint;
            if (clicked && TryGetWaypointAt(point, out waypoint) && WaypointRightClicked != null)
                WaypointRightClicked(waypoint, point);
        }

        void DrawTradeStations(
            RenderContext context,
            IReadOnlyList<PlanetTradeStationMarker> stations,
            float scale)
        {
            if (stations == null)
                return;

            for (int i = 0; i < stations.Count; i++)
            {
                PlanetTradeStationMarker station = stations[i];
                float iconSize = Math.Min(32f * scale, station.MaximumIconSize);
                if (iconSize < 1f)
                    continue;
                float stationScale = iconSize / 32f;
                float iconInset = 6f * stationScale;
                float labelGap = 5f * stationScale;
                RectangleF badgeBounds = new RectangleF(
                    station.ScreenPosition - new Vector2(iconSize * 0.5f),
                    new Vector2(iconSize));
                context.Texture(
                    "Circle",
                    new RectangleF(
                        badgeBounds.Position + new Vector2(
                            Math.Max(1f, scale),
                            Math.Max(1f, scale * 2f)),
                        badgeBounds.Size),
                    new Color(0, 0, 0, 210));
                context.Texture("Circle", badgeBounds, station.BackgroundColor);
                context.Texture(
                    station.Icon,
                    new RectangleF(
                        badgeBounds.X + iconInset,
                        badgeBounds.Y + iconInset,
                        badgeBounds.Width - iconInset * 2f,
                        badgeBounds.Height - iconInset * 2f),
                    station.IconColor,
                    true);
                string label = station.Name + "\n" + station.FactionName;
                if (station.GpsHashes.Count > 0)
                    label = LocHelper.Format("HighwayEngineer_Station_MoreSignals", label, station.GpsHashes.Count);
                context.Text(
                    label,
                    new Vector2(
                        station.ScreenPosition.X + iconSize * 0.5f + labelGap,
                        station.ScreenPosition.Y),
                    0.5f * stationScale,
                    station.RelationshipColor,
                    HorizontalAlignment.Left, VerticalAlignment.Center,
                    TextOptions.GetDropShadow(this));
            }
        }

        void DrawWaypoints(
            RenderContext context,
            IReadOnlyList<PlanetWaypointMarker> markers,
            float scale)
        {
            if (markers == null)
                return;

            float iconSize = 22f * scale;
            float labelGap = 5f * scale;
            for (int i = 0; i < markers.Count; i++)
            {
                PlanetWaypointMarker marker = markers[i];
                RectangleF iconBounds = new RectangleF(
                    marker.ScreenPosition - new Vector2(iconSize * 0.5f),
                    new Vector2(iconSize));
                RectangleF shadowBounds = new RectangleF(
                    iconBounds.Position + new Vector2(Math.Max(1f, scale)),
                    iconBounds.Size);
                Color markerColor = marker.Color;
                markerColor.A = marker.AnyGpsShown ? byte.MaxValue : (byte)130;
                if (marker.IsIntersection)
                {
                    DrawCross(context, shadowBounds, new Color(0, 0, 0, 210), scale);
                    DrawCross(context, iconBounds, markerColor, scale);
                }
                else
                {
                    context.Texture(
                        "Circle",
                        shadowBounds,
                        new Color(0, 0, 0, 210),
                        true);
                    context.Texture(
                        "Circle",
                        iconBounds,
                        markerColor,
                        true);
                }
                Vector2 labelPosition = new Vector2(
                    marker.ScreenPosition.X + iconSize * 0.5f + labelGap,
                    marker.ScreenPosition.Y);
                context.Text(
                    marker.Name,
                    labelPosition + new Vector2(0f, string.IsNullOrEmpty(marker.ElevationText)
                        ? 0f : -5f * scale),
                    0.5f * scale,
                    markerColor,
                    HorizontalAlignment.Left, VerticalAlignment.Center,
                    TextOptions.GetDropShadow(this));
                if (!string.IsNullOrEmpty(marker.ElevationText))
                    context.Text(
                        marker.ElevationText,
                        labelPosition + new Vector2(0f, 7f * scale),
                        0.36f * scale,
                        Color.White,
                        HorizontalAlignment.Left, VerticalAlignment.Center,
                        TextOptions.GetDropShadow(this));
            }
        }

        /// <summary>An X marking a road intersection node.</summary>
        static void DrawCross(RenderContext context, RectangleF bounds, Color color, float scale)
        {
            float thickness = Math.Max(2f, 4f * scale);
            var bar = new RectangleF(
                bounds.Center.X - bounds.Width * 0.5f,
                bounds.Center.Y - thickness * 0.5f,
                bounds.Width, thickness);
            context.Texture("SquareSimple", bar, color, true, MathHelper.PiOver4);
            context.Texture("SquareSimple", bar, color, true, -MathHelper.PiOver4);
        }

        void DrawRoadSegmentLabels(RenderContext context,
            IReadOnlyList<RoadSegmentLabel> labels, float scale)
        {
            if (labels == null) return;
            for (int i = 0; i < labels.Count; i++)
                context.Text(labels[i].Text, labels[i].ScreenPosition,
                    0.42f * scale, Color.White,
                    HorizontalAlignment.Center, VerticalAlignment.Center,
                    TextOptions.GetDropShadow(this));
        }

        const float BridgeToggleSize = 22f;

        /// <summary>A round badge with the bridge icon: purple when the segment is a bridge, dim when it could be.</summary>
        static void DrawBridgeToggles(RenderContext context, IReadOnlyList<RoadBridgeToggle> toggles, float scale)
        {
            if (toggles == null) return;
            float size = BridgeToggleSize * scale;
            float inset = 4f * scale;
            for (int i = 0; i < toggles.Count; i++)
            {
                RoadBridgeToggle toggle = toggles[i];
                var badge = new RectangleF(toggle.ScreenPosition - new Vector2(size * 0.5f), new Vector2(size));
                context.Texture("Circle", new RectangleF(badge.Position + new Vector2(Math.Max(1f, scale)), badge.Size),
                    new Color(0, 0, 0, 210), true);
                Color background = toggle.IsInvalid ? new Color(235, 64, 64)
                    : toggle.IsBridge ? new Color(170, 96, 240) : new Color(60, 60, 70, 200);
                context.Texture("Circle", badge, background, true);
                context.Texture("HighwayEngineer_Icon_Bridge", new RectangleF(badge.X + inset, badge.Y + inset,
                    badge.Width - inset * 2f, badge.Height - inset * 2f),
                    toggle.IsBridge ? Color.White : new Color(200, 200, 210, 200), true);
            }
        }

        bool TryGetBridgeToggleAt(Vector2 point, out int segment)
        {
            segment = 0;
            if (_bridgeToggles == null) return false;
            float radius = BridgeToggleSize * _scale * 0.5f + 2f;
            for (int i = _bridgeToggles.Count - 1; i >= 0; i--)
            {
                if (Vector2.DistanceSquared(point, _bridgeToggles[i].ScreenPosition) > radius * radius) continue;
                segment = _bridgeToggles[i].Segment;
                return true;
            }
            return false;
        }

        /// <summary>GPS markers as on the Minimap: the waypoint icon in the GPS colour, dim when hidden from the HUD.</summary>
        void DrawGps(RenderContext context, IReadOnlyList<PlanetWaypointMarker> markers, float scale)
        {
            if (markers == null)
                return;

            float iconSize = 22f * scale;
            float labelGap = 5f * scale;
            for (int i = 0; i < markers.Count; i++)
            {
                PlanetWaypointMarker marker = markers[i];
                var iconBounds = new RectangleF(marker.ScreenPosition - new Vector2(iconSize * 0.5f), new Vector2(iconSize));
                var shadowBounds = new RectangleF(iconBounds.Position + new Vector2(Math.Max(1f, scale)), iconBounds.Size);
                Color markerColor = marker.Color;
                markerColor.A = marker.AnyGpsShown ? byte.MaxValue : (byte)130;
                context.Texture("HighwayEngineer_Icon_WaypointIcon", shadowBounds, new Color(0, 0, 0, 210), true);
                context.Texture("HighwayEngineer_Icon_WaypointIcon", iconBounds, markerColor, true);
                context.Text(
                    marker.Name,
                    new Vector2(marker.ScreenPosition.X + iconSize * 0.5f + labelGap, marker.ScreenPosition.Y),
                    0.5f * scale,
                    markerColor,
                    HorizontalAlignment.Left, VerticalAlignment.Center,
                    TextOptions.GetDropShadow(this));
            }
        }

        bool TryGetGpsAt(Vector2 point, out PlanetWaypointMarker marker)
        {
            marker = null;
            if (_gps == null)
                return false;

            float radius = 14f * _scale;
            float radiusSquared = radius * radius;
            for (int i = _gps.Count - 1; i >= 0; i--)
            {
                if (Vector2.DistanceSquared(point, _gps[i].ScreenPosition) > radiusSquared)
                    continue;
                marker = _gps[i];
                return true;
            }
            return false;
        }

        bool TryGetStationAt(
            Vector2 point,
            out PlanetTradeStationMarker station)
        {
            station = null;
            if (_stations == null)
                return false;

            for (int i = _stations.Count - 1; i >= 0; i--)
            {
                PlanetTradeStationMarker candidate = _stations[i];
                float iconSize = Math.Min(
                    32f * _scale,
                    candidate.MaximumIconSize);
                if (iconSize < 1f)
                    continue;
                float radius = Math.Max(2f, iconSize * 0.5625f);
                float radiusSquared = radius * radius;
                if (Vector2.DistanceSquared(point, candidate.ScreenPosition) > radiusSquared)
                    continue;
                station = candidate;
                return true;
            }
            return false;
        }

        bool TryGetWaypointAt(
            Vector2 point,
            out PlanetWaypointMarker waypoint)
        {
            waypoint = null;
            if (_markers == null)
                return false;

            float radius = 14f * _scale;
            float radiusSquared = radius * radius;
            for (int i = _markers.Count - 1; i >= 0; i--)
            {
                PlanetWaypointMarker candidate = _markers[i];
                if (Vector2.DistanceSquared(point, candidate.ScreenPosition) > radiusSquared)
                    continue;
                waypoint = candidate;
                return true;
            }
            return false;
        }

        static void DrawPlayer(
            RenderContext context,
            PlanetPlayerMarker player,
            float scale)
        {
            if (player == null || !player.Visible)
                return;

            float size = 34f * scale;
            var iconBounds = new RectangleF(
                player.ScreenPosition - new Vector2(size * 0.5f),
                new Vector2(size));
            context.Texture(
                "HighwayEngineer_Icon_GpsArrow",
                new RectangleF(
                    iconBounds.Position + new Vector2(
                        Math.Max(1f, scale),
                        Math.Max(2f, scale * 2f)),
                    iconBounds.Size),
                new Color(0, 0, 0, 190),
                true,
                player.Rotation);
            context.Texture(
                "HighwayEngineer_Icon_GpsArrow",
                iconBounds,
                Color.White,
                true,
                player.Rotation);
        }
    }
}

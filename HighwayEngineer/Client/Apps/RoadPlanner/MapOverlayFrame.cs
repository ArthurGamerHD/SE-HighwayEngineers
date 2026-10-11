using Acrux.Cartography.Maps;
using Acrux.Cartography;
using System.Collections.Generic;
using HighwayEngineer.Client.Gui.UserControls;
using HighwayEngineer.Client.Maps;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    // A snapshot of projected overlays belonging to one texture upload. Never
    // retain the view model's mutable marker lists or reused player/station objects.
    internal sealed class MapOverlayFrame
    {
        public IReadOnlyList<PlanetWaypointMarker> Nodes = new PlanetWaypointMarker[0];
        public IReadOnlyList<PlanetWaypointMarker> Gps = new PlanetWaypointMarker[0];
        public IReadOnlyList<RoadSegmentLabel> Labels = new RoadSegmentLabel[0];
        public IReadOnlyList<RoadBridgeToggle> BridgeToggles = new RoadBridgeToggle[0];
        public IReadOnlyList<PlanetTradeStationMarker> Stations = new PlanetTradeStationMarker[0];
        public IReadOnlyList<RoadDepotMarker> Depots = new RoadDepotMarker[0];
        public MapViewState PlanetView;
        public MapData ProjectionData;
        public RectangleF Bounds;
        public RoadTangentHandles? Tangents;
        // Where the bulldoze confirm/discard buttons sit, if anything is marked.
        public Vector2? MarkAnchor;
        public int NodeCount;
        public int EditingNode = -1;

        public static MapOverlayFrame Capture(RoadPlannerViewModel model, int editingNode)
        {
            var frame = new MapOverlayFrame
            {
                // Road-node objects are freshly allocated by the provider; only
                // its list is reused. Segment labels are value types.
                Nodes = new List<PlanetWaypointMarker>(model.GetPlanetWaypointMarkers()),
                Labels = new List<RoadSegmentLabel>(model.GetRoadSegmentLabels()),
                BridgeToggles = new List<RoadBridgeToggle>(model.GetBridgeToggles()),
                NodeCount = model.RoadNodes.Count,
                EditingNode = editingNode
            };
            RoadTangentHandles handles;
            if (model.TryGetTangentOverlay(out handles)) frame.Tangents = handles;
            Vector2 anchor;
            if (model.TryGetMarkAnchor(out anchor)) frame.MarkAnchor = anchor;
            // GPS markers fold into the station badges they sit on, so stations go first and
            // are copied after. GPS marker objects are freshly allocated by the provider.
            IReadOnlyList<PlanetTradeStationMarker> liveStations = model.GetPlanetTradeStationMarkers();
            frame.Gps = new List<PlanetWaypointMarker>(model.GetPlanetGpsMarkers());
            var stations = new List<PlanetTradeStationMarker>();
            foreach (PlanetTradeStationMarker marker in liveStations)
            {
                var copy = new PlanetTradeStationMarker
                {
                    StationId = marker.StationId, FactionId = marker.FactionId,
                    Name = marker.Name, FullName = marker.FullName,
                    FactionName = marker.FactionName, FullFactionName = marker.FullFactionName,
                    FactionTag = marker.FactionTag, FactionDescription = marker.FactionDescription,
                    RelationshipName = marker.RelationshipName, Reputation = marker.Reputation,
                    Icon = marker.Icon, ScreenPosition = marker.ScreenPosition,
                    BackgroundColor = marker.BackgroundColor, IconColor = marker.IconColor,
                    RelationshipColor = marker.RelationshipColor,
                    MaximumIconSize = marker.MaximumIconSize, AnyGpsShown = marker.AnyGpsShown
                };
                copy.GpsHashes.AddRange(marker.GpsHashes);
                stations.Add(copy);
            }
            frame.Stations = stations;
            frame.Depots = model.GetRoadDepotMarkers();
            return frame;
        }
    }
}

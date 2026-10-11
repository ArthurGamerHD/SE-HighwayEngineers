using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// Picks the faction that takes a road contract: the owner of the faction station nearest
    /// to the road. Colours and the default logo follow the Minimap mod's station markers.
    /// Faction data is synced to clients, so the planner uses the same lookup for its preview.
    /// </summary>
    public static class ServiceProviders
    {
        const string DefaultFactionIcon = "Textures\\FactionLogo\\Others\\OtherIcon_18.dds";

        public static bool TryFindNearest(Vector3D position, out IMyFaction faction, out IMyFactionStation station)
        {
            faction = null;
            station = null;
            if (MyAPIGateway.Session == null || MyAPIGateway.Session.Factions == null) return false;
            double best = double.MaxValue;
            foreach (IMyFaction candidate in MyAPIGateway.Session.Factions.Factions.Values)
            {
                if (candidate == null) continue;
                foreach (IMyFactionStation candidateStation in candidate.Stations)
                {
                    double distance = Vector3D.DistanceSquared(candidateStation.Position, position);
                    if (distance >= best) continue;
                    best = distance;
                    faction = candidate;
                    station = candidateStation;
                }
            }
            return station != null;
        }

        /// <summary>The faction (and, when given, its station) as shown on invoices and map markers.</summary>
        public static RoadServiceProvider Describe(IMyFaction faction, IMyFactionStation station)
        {
            string icon = faction.FactionIcon.HasValue && !string.IsNullOrWhiteSpace(faction.FactionIcon.Value.String)
                ? faction.FactionIcon.Value.String
                : DefaultFactionIcon;
            Vector3D position = station == null ? Vector3D.Zero : station.Position;
            return new RoadServiceProvider
            {
                FactionId = faction.FactionId,
                StationId = station == null ? 0 : station.Id,
                FactionName = faction.Name,
                FactionTag = faction.Tag,
                StationName = station == null ? null : station.Name,
                Icon = icon,
                IconColorPacked = FactionColor(faction.IconColor, new Color(58, 32, 63)).PackedValue,
                BackgroundColorPacked = FactionColor(faction.CustomColor, Color.Black).PackedValue,
                StationX = position.X,
                StationY = position.Y,
                StationZ = position.Z
            };
        }

        static Color FactionColor(Vector3 hsvOffset, Color fallback)
        {
            if (!hsvOffset.IsValid()) return fallback;
            Color color = MyColorPickerConstants.HSVOffsetToHSV(hsvOffset).HSVtoColor();
            color.A = byte.MaxValue;
            return color;
        }
    }
}

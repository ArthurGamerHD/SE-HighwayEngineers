using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.Gui
{
    internal sealed class KnownTradeStation
    {
        public long StationId;
        public long FactionId;
        public Vector3D Position;
        public string Name;
        public string FullName;
        public string FactionName;
        public string FullFactionName;
        public string FactionTag;
        public string FactionDescription;
        public string RelationshipName;
        public int Reputation;
        public string Icon;
        public Color BackgroundColor;
        public Color IconColor;
        public Color RelationshipColor;
        internal bool UsedThisRefresh;
    }

    /// <summary>
    /// Stable client-side view of the economy stations discovered by the local
    /// identity. Entries are reused by station id so overlay controls can keep
    /// their visual models alive between refreshes.
    /// </summary>
    internal sealed class KnownTradeStations
    {
        const string DefaultFactionIcon =
            "Textures\\FactionLogo\\Others\\OtherIcon_18.dds";
        const int MaximumStationNameLength = 24;
        const char Ellipsis = '…';

        readonly List<KnownTradeStation> _stations =
            new List<KnownTradeStation>();
        readonly Dictionary<long, KnownTradeStation> _stationsById =
            new Dictionary<long, KnownTradeStation>();
        readonly HashSet<long> _visitedStationIds = new HashSet<long>();

        public IReadOnlyList<KnownTradeStation> Items => _stations;

        public void Refresh()
        {
            for (int i = 0; i < _stations.Count; i++)
                _stations[i].UsedThisRefresh = false;

            _visitedStationIds.Clear();
            var session = MyAPIGateway.Session;
            var player = session == null ? null : session.Player;
            if (player == null || session.Factions == null)
            {
                RemoveUnused();
                return;
            }

            MyObjectBuilder_Checkpoint checkpoint = session.GetCheckpoint(session.Name);
            CollectVisitedStationIds(checkpoint, player.IdentityId);

            Dictionary<long, IMyFaction> factions = session.Factions.Factions;
            if (factions != null)
            {
                foreach (IMyFaction faction in factions.Values)
                {
                    if (faction == null)
                        continue;

                    foreach (IMyFactionStation station in faction.Stations)
                    {
                        if (station == null || !_visitedStationIds.Contains(station.Id))
                            continue;

                        KnownTradeStation model;
                        if (!_stationsById.TryGetValue(station.Id, out model))
                        {
                            model = new KnownTradeStation { StationId = station.Id };
                            _stationsById.Add(station.Id, model);
                            _stations.Add(model);
                        }

                        model.Position = station.Position;
                        model.FullName = string.IsNullOrWhiteSpace(station.Name)
                            ? LocHelper.GetLoc("HighwayEngineer_Station_DefaultName")
                            : station.Name;
                        model.Name = TrimName(model.FullName, MaximumStationNameLength);
                        model.FactionId = faction.FactionId;
                        model.FullFactionName = string.IsNullOrWhiteSpace(faction.Name)
                            ? faction.Tag
                            : faction.Name;
                        model.FactionName = TrimName(
                            model.FullFactionName,
                            MaximumStationNameLength);
                        model.FactionTag = faction.Tag ?? string.Empty;
                        model.FactionDescription = faction.Description ?? string.Empty;
                        model.Reputation = session.Factions
                            .GetReputationBetweenPlayerAndFaction(
                                player.IdentityId,
                                faction.FactionId);
                        ResolveRelationship(
                            faction,
                            player.IdentityId,
                            out model.RelationshipName,
                            out model.RelationshipColor);
                        model.Icon = faction.FactionIcon.HasValue &&
                                     !string.IsNullOrWhiteSpace(
                                         faction.FactionIcon.Value.String)
                            ? faction.FactionIcon.Value.String
                            : DefaultFactionIcon;
                        model.BackgroundColor = GetFactionColor(
                            faction.CustomColor,
                            Color.Black);
                        model.IconColor = GetFactionColor(
                            faction.IconColor,
                            new Color(58, 32, 63));
                        model.UsedThisRefresh = true;
                    }
                }
            }

            RemoveUnused();
        }

        void CollectVisitedStationIds(
            MyObjectBuilder_Checkpoint checkpoint,
            long identityId)
        {
            if (checkpoint == null || identityId == 0L)
                return;

            if (checkpoint.Identities != null)
            {
                for (int i = 0; i < checkpoint.Identities.Count; i++)
                {
                    MyObjectBuilder_Identity identity = checkpoint.Identities[i];
                    if (identity == null || identity.IdentityId != identityId ||
                        identity.VisitedStationIds == null)
                    {
                        continue;
                    }

                    for (int stationIndex = 0;
                         stationIndex < identity.VisitedStationIds.Count;
                         stationIndex++)
                    {
                        _visitedStationIds.Add(
                            identity.VisitedStationIds[stationIndex]);
                    }
                }
            }

            // Older worlds can still hold the same discoveries on player data.
            if (checkpoint.AllPlayersData == null ||
                checkpoint.AllPlayersData.Dictionary == null)
            {
                return;
            }

            foreach (var entry in checkpoint.AllPlayersData.Dictionary)
            {
                MyObjectBuilder_Player player = entry.Value;
                if (player == null || player.IdentityId != identityId ||
                    player.VisitedStationIds == null)
                {
                    continue;
                }

                for (int i = 0; i < player.VisitedStationIds.Count; i++)
                    _visitedStationIds.Add(player.VisitedStationIds[i]);
            }
        }

        void RemoveUnused()
        {
            for (int i = _stations.Count - 1; i >= 0; i--)
            {
                KnownTradeStation station = _stations[i];
                if (station.UsedThisRefresh)
                    continue;

                _stations.RemoveAt(i);
                _stationsById.Remove(station.StationId);
            }
        }

        static Color GetFactionColor(Vector3 hsvOffset, Color fallback)
        {
            if (!hsvOffset.IsValid())
                return fallback;

            Color color = MyColorPickerConstants
                .HSVOffsetToHSV(hsvOffset)
                .HSVtoColor();
            color.A = byte.MaxValue;
            return color;
        }

        static void ResolveRelationship(
            IMyFaction faction,
            long identityId,
            out string name,
            out Color color)
        {
            if (faction != null &&
                (faction.IsMember(identityId) || faction.IsFriendly(identityId)))
            {
                name = "Friendly";
                color = new Color(110, 230, 125);
                return;
            }
            if (faction != null && faction.IsEnemy(identityId))
            {
                name = "Enemy";
                color = new Color(255, 105, 105);
                return;
            }

            name = "Neutral";
            color = Color.White;
        }

        // Matches the LCD mod's start…end name treatment while enforcing the
        // requested hard maximum instead of its historical two-character grace.
        internal static string TrimName(string value, int maximumLength)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (maximumLength <= 0)
                return string.Empty;
            if (value.Length <= maximumLength)
                return value;
            if (maximumLength < 5)
                return maximumLength == 1
                    ? Ellipsis.ToString()
                    : value.Substring(0, maximumLength - 1) + Ellipsis;

            const int suffixLength = 3;
            int prefixLength = maximumLength - suffixLength - 1;
            return value.Substring(0, prefixLength) + Ellipsis +
                   value.Substring(value.Length - suffixLength, suffixLength);
        }

        public void Clear()
        {
            _stations.Clear();
            _stationsById.Clear();
            _visitedStationIds.Clear();
        }
    }
}

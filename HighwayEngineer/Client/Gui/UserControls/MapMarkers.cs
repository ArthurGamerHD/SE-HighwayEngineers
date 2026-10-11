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
using Sandbox.ModAPI;
using HighwayEngineer.Client.Maps;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage;
using VRageMath;

namespace HighwayEngineer.Client.Gui.UserControls
{
    public sealed class PlanetWaypointMarker
    {
        public int Hash;
        public string Name;
        public string ElevationText;
        public Vector2 ScreenPosition;
        public Color Color;
        public bool IsLastCreated;
        public bool IsIntersection;
        public bool AnyGpsShown;
        public readonly List<int> GpsHashes = new List<int>();
    }

    public struct RoadSegmentLabel
    {
        public Vector2 ScreenPosition;
        public string Text;
    }

    /// <summary>The bridge badge beside a draft segment: click to switch it between bridge and road.</summary>
    public struct RoadBridgeToggle
    {
        /// <summary>The segment's end node.</summary>
        public int Segment;
        public Vector2 ScreenPosition;
        public bool IsBridge;
        /// <summary>A bridge the server would refuse (too short or steep).</summary>
        public bool IsInvalid;
    }

    public sealed class PlanetTradeStationMarker
    {
        public long StationId;
        public long FactionId;
        public string Name;
        public string FullName;
        public string FactionName;
        public string FullFactionName;
        public string FactionTag;
        public string FactionDescription;
        public string RelationshipName;
        public int Reputation;
        public string Icon;
        public Vector2 ScreenPosition;
        public Color BackgroundColor;
        public Color IconColor;
        public Color RelationshipColor;
        public float MaximumIconSize = float.PositiveInfinity;
        public bool AnyGpsShown;
        public readonly List<int> GpsHashes = new List<int>();
        internal bool UsedThisFrame;
    }

    /// <summary>A Road Logistic Depot on the planner map, badged with its faction's logo.</summary>
    public sealed class RoadDepotMarker
    {
        public long GridId;
        public Vector2 ScreenPosition;
        public string Icon;
        public Color IconColor;
        public Color BackgroundColor;
        public string FactionName;
        /// <summary>The current draft's delivery goes here.</summary>
        public bool UsedByDraft;
    }

    public sealed class PlanetPlayerMarker
    {
        public bool Visible;
        public Vector2 ScreenPosition;
        public float Rotation;
    }
}

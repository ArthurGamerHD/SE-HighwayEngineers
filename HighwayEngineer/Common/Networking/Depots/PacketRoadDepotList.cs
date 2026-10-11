using System.Collections.Generic;
using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>
    /// Server to clients: every Road Logistic Depot, with the distances within which a new road
    /// reuses one. Sent with the contract list on join and to everyone whenever a depot is
    /// spawned or destroyed.
    /// </summary>
    [ProtoContract]
    [NetworkPayload(12)]
    public sealed partial class PacketRoadDepotList
    {
        [ProtoMember(1)] public List<RoadDepotInfo> Depots = new List<RoadDepotInfo>();
        /// <summary>A depot this close (straight line) to any node of a road is reused.</summary>
        [ProtoMember(2)] public double ReuseRadiusMeters;
        /// <summary>...or this far along built roads from the depot to a node of the road.</summary>
        [ProtoMember(3)] public double ReuseRoadDistanceMeters;
    }
}

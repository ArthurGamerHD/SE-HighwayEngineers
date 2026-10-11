using ProtoBuf;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>A road contract as its owner sees it.</summary>
    [ProtoContract]
    public sealed class RoadContractInfo
    {
        [ProtoMember(1)] public long ContractId;
        [ProtoMember(2)] public RoadContractState State;
        [ProtoMember(3)] public long PlanetId;
        [ProtoMember(4)] public RoadCostBreakdown Costs;
        [ProtoMember(5)] public DeliveryZone Zone;
        /// <summary>Item amount delivered so far, out of <see cref="RoadCostBreakdown.ItemAmount"/>.</summary>
        [ProtoMember(6)] public double Delivered;
        [ProtoMember(7)] public long CreditsPaid;
        [ProtoMember(8)] public long CreditsDue;
        [ProtoMember(9)] public int SegmentCount;
        [ProtoMember(10)] public RoadServiceProvider Provider;
        /// <summary>The game contract's id (Contracts screen); 0 when none (creative).</summary>
        [ProtoMember(11)] public long GameContractId;
        /// <summary>Second item amount delivered so far, out of <see cref="RoadCostBreakdown.Item2Amount"/>.</summary>
        [ProtoMember(12)] public double Delivered2;
        /// <summary>True for a bridge contract.</summary>
        [ProtoMember(13)] public bool IsBridge;
        /// <summary>Third item amount delivered so far, out of <see cref="RoadCostBreakdown.Item3Amount"/>.</summary>
        [ProtoMember(14)] public double Delivered3;
    }
}

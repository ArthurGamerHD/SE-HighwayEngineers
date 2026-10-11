using ProtoBuf;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>What a road, bridge or bulldoze costs, as priced by the server.</summary>
    [ProtoContract]
    public sealed class RoadCostBreakdown
    {
        [ProtoMember(1)] public double CutCubicMeters;
        [ProtoMember(2)] public double FillCubicMeters;
        [ProtoMember(3)] public double LengthMeters;
        [ProtoMember(4)] public double MaterialCubicMeters;
        /// <summary>Total Space Credits for the contract.</summary>
        [ProtoMember(5)] public long Credits;
        /// <summary>Part of <see cref="Credits"/> charged on accept.</summary>
        [ProtoMember(6)] public long CreditsUpfront;
        /// <summary>Part of <see cref="Credits"/> charged when the delivery completes.</summary>
        [ProtoMember(7)] public long CreditsOnDelivery;
        /// <summary>Item to deliver, e.g. MyObjectBuilder_Ingot / Stone (Gravel); null when none.</summary>
        [ProtoMember(8)] public string ItemType;
        [ProtoMember(9)] public string ItemSubtype;
        [ProtoMember(10)] public double ItemAmount;
        /// <summary>True when nothing is charged (creative).</summary>
        [ProtoMember(11)] public bool Exempt;
        /// <summary>Server price of earth movement, Space Credits per cubic metre of cut plus fill.</summary>
        [ProtoMember(12)] public double CreditsPerCubicMeter;
        /// <summary>Server price of the road material, item amount per cubic metre of pavement.</summary>
        [ProtoMember(13)] public double ItemPerCubicMeter;
        /// <summary>Part of <see cref="Credits"/> for earth movement.</summary>
        [ProtoMember(14)] public long EarthMovementCredits;
        /// <summary>
        /// Part of <see cref="Credits"/> for the Road Logistic Depot ("Road Construction Infrastructure");
        /// 0 when the contract needs no depot (creative, free roads, nothing to deliver).
        /// </summary>
        [ProtoMember(15)] public long InfrastructureCredits;
        /// <summary>The delivery goes to an existing Road Logistic Depot of the provider, so no fee.</summary>
        [ProtoMember(16)] public bool ReusesDepot;
        /// <summary>Bridges: concrete in the structure, deck slab and parapets.</summary>
        [ProtoMember(17)] public double StructureCubicMeters;
        /// <summary>Bridges: part of <see cref="Credits"/> for the concrete (0 when it is paid in <see cref="Item3Type"/>).</summary>
        [ProtoMember(18)] public long StructureCredits;
        /// <summary>Bridges: server price of the concrete, Space Credits per cubic metre.</summary>
        [ProtoMember(19)] public double StructureCreditsPerCubicMeter;
        /// <summary>A second item to deliver (bridges: Iron Ingot as "Steel Rebar"); null when none.</summary>
        [ProtoMember(20)] public string Item2Type;
        [ProtoMember(21)] public string Item2Subtype;
        [ProtoMember(22)] public double Item2Amount;
        /// <summary>The second item's name in this contract (e.g. "Steel Rebar").</summary>
        [ProtoMember(23)] public string Item2Name;
        /// <summary>Server price of the second item, item amount per cubic metre of concrete.</summary>
        [ProtoMember(24)] public double Item2PerCubicMeter;
        /// <summary>Bridges: the design the server chose; Auto when its bridges use different ones.</summary>
        [ProtoMember(25)] public BridgeDesign Design;
        /// <summary>How many bridges the contract builds.</summary>
        [ProtoMember(26)] public int BridgeCount;
        /// <summary>Length of the road that is bridged, included in <see cref="LengthMeters"/>.</summary>
        [ProtoMember(27)] public double BridgeLengthMeters;
        /// <summary>
        /// A third item to deliver (bridges with the Concrete Tool mod: its Concrete Mix, for the
        /// concrete instead of credits); null when none. Counted in items, not kilograms.
        /// </summary>
        [ProtoMember(28)] public string Item3Type;
        [ProtoMember(29)] public string Item3Subtype;
        [ProtoMember(30)] public double Item3Amount;
        /// <summary>The third item's name in this contract (e.g. "Concrete Mix").</summary>
        [ProtoMember(31)] public string Item3Name;
        /// <summary>Server price of the third item, item amount per cubic metre of concrete.</summary>
        [ProtoMember(32)] public double Item3PerCubicMeter;
    }
}

using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;
using VRage.Utils;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>A road or bridge contract as the server keeps it (saved with the world).</summary>
    public sealed class RoadContractRecord
    {
        public long ContractId;
        public ulong OwnerSteamId;
        public long OwnerIdentityId;
        public long PlanetId;
        public RoadBuildParameters Parameters;
        public double[] Geometry;
        /// <summary>Set when segments are bridged (with the designs priced); null for a plain road.</summary>
        public BridgeBuildParameters Bridge;
        public long Credits;
        public long CreditsUpfront;
        public long CreditsOnDelivery;
        /// <summary>Credits actually charged so far; refunded when the contract fails or is abandoned.</summary>
        public long CreditsPaid;
        public string ItemType;
        public string ItemSubtype;
        public double ItemAmount;
        public double Delivered;
        /// <summary>A second item to deliver (bridges: Iron Ingot as "Steel Rebar"); null when none.</summary>
        public string Item2Type;
        public string Item2Subtype;
        public string Item2Name;
        public double Item2Amount;
        public double Delivered2;
        /// <summary>A third item to deliver (bridges with the Concrete Tool mod: Concrete Mix); null when none.</summary>
        public string Item3Type;
        public string Item3Subtype;
        public string Item3Name;
        public double Item3Amount;
        public double Delivered3;
        public bool Exempt;
        public RoadContractState State;
        public long GameContractId;
        public int GpsHash;
        public RoadServiceProvider Provider;
        public DeliveryZone Zone;
        /// <summary>The Road Logistic Depot this contract delivers to (see <see cref="RoadDepotRecord"/>); 0 when none.</summary>
        public long DepotGridId;
        /// <summary>Part of <see cref="Credits"/> for a new depot; kept on refunds since the depot stays.</summary>
        public long InfrastructureCredits;

        public RoadContractInfo ToInfo()
        {
            return new RoadContractInfo
            {
                ContractId = ContractId,
                State = State,
                PlanetId = PlanetId,
                Costs = new RoadCostBreakdown
                {
                    Credits = Credits,
                    CreditsUpfront = CreditsUpfront,
                    CreditsOnDelivery = CreditsOnDelivery,
                    ItemType = ItemType,
                    ItemSubtype = ItemSubtype,
                    ItemAmount = ItemAmount,
                    Item2Type = Item2Type,
                    Item2Subtype = Item2Subtype,
                    Item2Name = Item2Name,
                    Item2Amount = Item2Amount,
                    Item3Type = Item3Type,
                    Item3Subtype = Item3Subtype,
                    Item3Name = Item3Name,
                    Item3Amount = Item3Amount,
                    Design = Bridge == null ? BridgeDesign.Auto : Bridge.DesignFor(0),
                    Exempt = Exempt
                },
                Zone = Zone,
                Delivered = Delivered,
                CreditsPaid = CreditsPaid,
                CreditsDue = Math.Max(0, Credits - CreditsPaid),
                SegmentCount = Geometry == null ? 0 : Geometry.Length / 12,
                Provider = Provider,
                GameContractId = GameContractId,
                Delivered2 = Delivered2,
                Delivered3 = Delivered3,
                IsBridge = OnlyBridges
            };
        }

        public int SegmentCount => Geometry == null ? 0 : Geometry.Length / Constants.GEOMETRY_VALUES_PER_SEGMENT;

        /// <summary>Whether a segment (0-based) is part of a bridge rather than built as road.</summary>
        public bool IsBridged(int segment) => Bridge != null && Bridge.IsBridged(segment);

        /// <summary>The runs of bridged segments, each built as one bridge (a method, so the save leaves it out).</summary>
        public List<BridgeRun> BridgeRuns() => Bridge == null ? new List<BridgeRun>() : Bridge.Runs(SegmentCount);

        /// <summary>True when every segment is bridged, so no road is pushed.</summary>
        public bool OnlyBridges => Bridge != null && Bridge.BridgesAll(SegmentCount);

        /// <summary>All items are in (each within a gram).</summary>
        public bool IsDelivered => Delivered >= ItemAmount - 0.001 && Delivered2 >= Item2Amount - 0.001 &&
                                   Delivered3 >= Item3Amount - 0.001;

        public bool IsOpen => State == RoadContractState.Pending || State == RoadContractState.AwaitingPayment ||
                              State == RoadContractState.Building;
    }

    /// <summary>
    /// All road contracts, depots and the road library. Stored in the world save itself (a
    /// session variable in Sandbox.sbc), so a backup or a reload without saving brings back the
    /// contracts and roads that match its world; a file in the world's mod storage outlives them.
    /// </summary>
    public sealed class RoadContractStore
    {
        const string SessionVariable = "HighwayEngineer.RoadContracts.Xml.v1";
        // Where earlier versions kept it, out of step with the save it sat beside: deleted, never read.
        const string LegacyFileName = "HighwayEngineerContracts.xml";

        public long NextContractId;
        public List<RoadContractRecord> Contracts = new List<RoadContractRecord>();
        public List<RoadDepotRecord> Depots = new List<RoadDepotRecord>();
        /// <summary>The road library: every built road and bridge segment (see RoadLibraryService).</summary>
        public List<RoadSegmentRecord> Segments = new List<RoadSegmentRecord>();
        public List<BuiltBridgeRecord> Bridges = new List<BuiltBridgeRecord>();
        public long NextSegmentId;
        public long NextBridgeId;
        public int LibraryRevision;

        public static RoadContractStore Load()
        {
            try
            {
                DeleteLegacyFile();
                string xml;
                if (!MyAPIGateway.Utilities.GetVariable(SessionVariable, out xml))
                    xml = null;
                RoadContractStore store = string.IsNullOrWhiteSpace(xml)
                    ? null
                    : MyAPIGateway.Utilities.SerializeFromXML<RoadContractStore>(xml);
                if (store != null)
                {
                    if (store.Contracts == null) store.Contracts = new List<RoadContractRecord>();
                    if (store.Depots == null) store.Depots = new List<RoadDepotRecord>();
                    if (store.Segments == null) store.Segments = new List<RoadSegmentRecord>();
                    if (store.Bridges == null) store.Bridges = new List<BuiltBridgeRecord>();
                    return store;
                }
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Could not read the road contracts: " + error.Message);
            }
            return new RoadContractStore();
        }

        static void DeleteLegacyFile()
        {
            if (MyAPIGateway.Utilities.FileExistsInWorldStorage(LegacyFileName, typeof(RoadContractStore)))
                MyAPIGateway.Utilities.DeleteFileInWorldStorage(LegacyFileName, typeof(RoadContractStore));
        }

        /// <summary>Puts the store in the session; the game writes it with the next world save.</summary>
        public void Save()
        {
            try
            {
                MyAPIGateway.Utilities.SetVariable(SessionVariable, MyAPIGateway.Utilities.SerializeToXML(this));
            }
            catch (Exception error)
            {
                // XmlSerializer wraps the cause ("There was an error generating the XML document").
                Exception cause = error.InnerException ?? error;
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Could not store the road contracts: " + cause.Message);
            }
        }
    }
}

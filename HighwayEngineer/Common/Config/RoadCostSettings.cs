using System;
using ProtoBuf;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Utils;

namespace HighwayEngineer.Common.Config
{
    /// <summary>
    /// Per-world construction prices. Stored as HighwayEngineerCosts.xml in the save's mod
    /// storage (Storage/&lt;mod&gt;/HighwayEngineerCosts.xml); the file is created with the
    /// defaults on first use and re-read on each load. The server will own it and
    /// send it to clients; until then each machine reads its own copy.
    /// </summary>
    [ProtoContract]
    public sealed class RoadCostSettings
    {
        const string FileName = "HighwayEngineerCosts.xml";

        /// <summary>Space Credits per cubic metre of cut plus fill.</summary>
        [ProtoMember(1)] public double EarthMovementCreditsPerCubicMeter = 0.05;
        /// <summary>Kilograms of Gravel per cubic metre of road material (width × depth × length).</summary>
        [ProtoMember(2)] public double RoadMaterialGravelKgPerCubicMeter = 0.05;
        /// <summary>Share of a contract's credits charged on accept; the rest when its delivery completes.</summary>
        [ProtoMember(3)] public double UpfrontCreditShare = 0.5;
        /// <summary>Play time after building during which bulldozing refunds instead of costing.</summary>
        [ProtoMember(4)] public double BulldozeRefundWindowHours = 1.0;
        /// <summary>Share of the credits paid that bulldozing refunds inside the window.</summary>
        [ProtoMember(5)] public double BulldozeEarlyRefundShare = 0.5;
        /// <summary>Share of the credits paid that bulldozing costs after the window.</summary>
        [ProtoMember(6)] public double BulldozeLateCostShare = 0.5;
        /// <summary>Fixed Space Credits per contract for its Road Logistic Depot (the depot's material).</summary>
        [ProtoMember(7)] public long RoadInfrastructureFeeCredits = 4700000;
        /// <summary>
        /// Space Credits per cubic metre of a bridge's concrete (structure, deck slab and parapets);
        /// paid instead in Concrete Mix when the Concrete Tool mod is loaded (see <see cref="ConcreteMixPerCubicMeter"/>).
        /// </summary>
        [ProtoMember(8)] public double BridgeStructureCreditsPerCubicMeter = 10.0;
        /// <summary>Kilograms of Gravel per cubic metre of a bridge's road fill.</summary>
        [ProtoMember(9)] public double BridgePavementGravelKgPerCubicMeter = 0.05;
        /// <summary>Kilograms of Iron Ingot ("Steel Rebar") per cubic metre of a bridge's concrete.</summary>
        [ProtoMember(10)] public double BridgeRebarKgPerCubicMeter = 1.0;
        // ProtoMember 11 was BridgeConcreteMixPerCubicMeter, now derived from the credits (ConcreteMixPerCubicMeter).

        public static RoadCostSettings Load()
        {
            var defaults = new RoadCostSettings();
            if (MyAPIGateway.Utilities == null) return defaults;
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, typeof(RoadCostSettings)))
                {
                    Save(defaults);
                    return defaults;
                }
                string xml;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(RoadCostSettings)))
                    xml = reader.ReadToEnd();
                RoadCostSettings loaded = MyAPIGateway.Utilities.SerializeFromXML<RoadCostSettings>(xml);
                if (loaded == null) return defaults;
                loaded.Sanitize();
                return loaded;
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Could not read " + FileName + ": " + error.Message);
                return defaults;
            }
        }

        static void Save(RoadCostSettings settings)
        {
            using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(RoadCostSettings)))
                writer.Write(MyAPIGateway.Utilities.SerializeToXML(settings));
        }

        /// <summary>Clamps values read from a hand-edited file to usable ranges.</summary>
        public void Sanitize()
        {
            EarthMovementCreditsPerCubicMeter = NonNegative(EarthMovementCreditsPerCubicMeter);
            RoadMaterialGravelKgPerCubicMeter = NonNegative(RoadMaterialGravelKgPerCubicMeter);
            UpfrontCreditShare = Math.Min(1.0, NonNegative(UpfrontCreditShare));
            BulldozeRefundWindowHours = NonNegative(BulldozeRefundWindowHours);
            BulldozeEarlyRefundShare = Math.Min(1.0, NonNegative(BulldozeEarlyRefundShare));
            BulldozeLateCostShare = NonNegative(BulldozeLateCostShare);
            if (RoadInfrastructureFeeCredits < 0) RoadInfrastructureFeeCredits = 0;
            BridgeStructureCreditsPerCubicMeter = NonNegative(BridgeStructureCreditsPerCubicMeter);
            BridgePavementGravelKgPerCubicMeter = NonNegative(BridgePavementGravelKgPerCubicMeter);
            BridgeRebarKgPerCubicMeter = NonNegative(BridgeRebarKgPerCubicMeter);
        }

        /// <summary>Concrete Mix per Space Credit of bridge concrete it replaces.</summary>
        const double ConcreteMixPerCredit = 0.001;

        /// <summary>
        /// With the Concrete Tool mod loaded: its Concrete Mix per cubic metre of a bridge's concrete,
        /// delivered instead of credits: a thousandth of <see cref="BridgeStructureCreditsPerCubicMeter"/>.
        /// </summary>
        public double ConcreteMixPerCubicMeter()
        {
            return BridgeStructureCreditsPerCubicMeter * ConcreteMixPerCredit;
        }

        /// <summary>Space Credits per cubic metre of bridge concrete in this world: none when it is paid in Concrete Mix.</summary>
        public double ConcreteCreditsPerCubicMeter()
        {
            return ConcreteMod.Present ? 0.0 : BridgeStructureCreditsPerCubicMeter;
        }

        static double NonNegative(double value)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value < 0.0 ? 0.0 : value;
        }
    }
}

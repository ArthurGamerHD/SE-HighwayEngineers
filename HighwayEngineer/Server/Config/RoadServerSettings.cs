using System;
using Sandbox.ModAPI;
using VRage.Utils;

namespace HighwayEngineer.Server.Config
{
    /// <summary>
    /// Server-only limits that keep heavy requests from stalling the server. Stored as
    /// HighwayEngineerServer.xml in the save's mod storage; created with these defaults.
    /// Admins and the local (host / single-player) player bypass the per-player limits
    /// and the queue cap; everyone's quotes are priced on background threads.
    /// </summary>
    public sealed class RoadServerSettings
    {
        const string FileName = "HighwayEngineerServer.xml";

        /// <summary>Quotes a player may request back to back before being limited.</summary>
        public int QuoteBurst = 3;
        /// <summary>Seconds for one more quote to become available.</summary>
        public double QuoteRefillSeconds = 2.0;
        /// <summary>Quotes waiting or running server-wide; more get ServerBusy.</summary>
        public int QuoteQueueLimit = 32;
        /// <summary>Quotes priced at the same time on background threads (one per player at most).</summary>
        public int MaxConcurrentQuotes = 4;
        /// <summary>Admin switch (/road free): every quote costs 0 credits and 0 material, any material allowed.</summary>
        public bool FreeRoads;
        /// <summary>Seconds a quote can be accepted after it is issued.</summary>
        public double QuoteLifetimeSeconds = 60.0;
        public int MaxSegmentsPerRoad = 512;
        /// <summary>Road contracts a player may have open (waiting for delivery, payment or building).</summary>
        public int MaxOpenContractsPerPlayer = 3;
        /// <summary>Contracts a player may accept back to back, then one more every <see cref="AcceptRefillSeconds"/>.</summary>
        public int AcceptBurst = 2;
        public double AcceptRefillSeconds = 10.0;
        /// <summary>Cross-sections sampled for one quote's earthwork; long roads sample coarser.</summary>
        public int MaxQuoteSamples = 20000;
        /// <summary>A faction's existing depot this close to any node of a new road is used instead of a new one.</summary>
        public double DepotReuseRadiusMeters = 2000.0;
        /// <summary>...or this far along built roads from the depot to a node of the new road.</summary>
        public double DepotReuseRoadDistanceMeters = 8000.0;

        public static RoadServerSettings Load()
        {
            var defaults = new RoadServerSettings();
            try
            {
                if (!MyAPIGateway.Utilities.FileExistsInWorldStorage(FileName, typeof(RoadServerSettings)))
                {
                    using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(RoadServerSettings)))
                        writer.Write(MyAPIGateway.Utilities.SerializeToXML(defaults));
                    return defaults;
                }
                string xml;
                using (var reader = MyAPIGateway.Utilities.ReadFileInWorldStorage(FileName, typeof(RoadServerSettings)))
                    xml = reader.ReadToEnd();
                RoadServerSettings loaded = MyAPIGateway.Utilities.SerializeFromXML<RoadServerSettings>(xml);
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

        public void Save()
        {
            try
            {
                using (var writer = MyAPIGateway.Utilities.WriteFileInWorldStorage(FileName, typeof(RoadServerSettings)))
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(this));
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Could not write " + FileName + ": " + error.Message);
            }
        }

        void Sanitize()
        {
            QuoteBurst = Math.Max(1, QuoteBurst);
            QuoteRefillSeconds = Positive(QuoteRefillSeconds, 2.0);
            QuoteQueueLimit = Math.Max(1, QuoteQueueLimit);
            MaxConcurrentQuotes = Math.Max(1, MaxConcurrentQuotes);
            QuoteLifetimeSeconds = Positive(QuoteLifetimeSeconds, 60.0);
            MaxSegmentsPerRoad = Math.Max(1, MaxSegmentsPerRoad);
            MaxOpenContractsPerPlayer = Math.Max(1, MaxOpenContractsPerPlayer);
            AcceptBurst = Math.Max(1, AcceptBurst);
            AcceptRefillSeconds = Positive(AcceptRefillSeconds, 10.0);
            MaxQuoteSamples = Math.Max(100, MaxQuoteSamples);
            DepotReuseRadiusMeters = Math.Max(0.0, DepotReuseRadiusMeters);
            DepotReuseRoadDistanceMeters = Math.Max(0.0, DepotReuseRoadDistanceMeters);
        }

        static double Positive(double value, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value) || value <= 0.0 ? fallback : value;
        }
    }
}

using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Config;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Common.Structures;
using HighwayEngineer.Server.Config;
using HighwayEngineer.Server.Contracts;
using HighwayEngineer.Server.Helpers;
using HighwayEngineer.Server.Structures;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Server.Quotes
{
    /// <summary>A priced road or bridge the player may accept until it expires; used once.</summary>
    public sealed class IssuedRoadQuote
    {
        public long QuoteId;
        public ulong SteamId;
        public long PlanetId;
        public RoadBuildParameters Parameters;
        public double[] Geometry;
        /// <summary>Set when segments are bridged, with the design the server chose for each bridge; null otherwise.</summary>
        public BridgeBuildParameters Bridge;
        public RoadCostBreakdown Costs;
        public DeliveryZone Zone;
        /// <summary>Faction station that posts the game contract; null when none is needed (creative).</summary>
        public RoadServiceProvider Provider;
        /// <summary>The provider's existing Road Logistic Depot the delivery goes to; 0 to spawn a new one.</summary>
        public long DepotGridId;
        public long ExpiresUtcTicks;
    }

    /// <summary>
    /// Prices and validates draft roads. Requests are rate-limited per player. Pricing
    /// runs on background threads, at most one per player and <see cref="RoadServerSettings.MaxConcurrentQuotes"/>
    /// at once: a newer request from the same player cancels the running one (answered as
    /// superseded) and starts when it has stopped. Replies are sent from the game thread.
    /// </summary>
    public sealed class RoadQuoteService
    {
        const float BusyRetrySeconds = 2f;
        const int IssuedQuotesPerPlayer = 3;
        const int PruneIntervalFrames = 300;

        readonly RoadServerSettings _settings;

        /// <summary>
        /// Finds the provider faction's existing depot for a road (faction id, planet, geometry);
        /// set by the server once the contract service exists.
        /// </summary>
        public Func<long, MyPlanet, double[], RoadDepotRecord> DepotFinder;

        /// <summary>
        /// The roads built or contracted on a planet, which a new depot must keep clear of;
        /// set by the server once the contract service exists.
        /// </summary>
        public Func<MyPlanet, List<RoadCurve>> ExistingRoads;
        readonly Func<RoadCostSettings> _costs;

        /// <summary>The world's current prices.</summary>
        public RoadCostSettings Prices => _costs();
        readonly Action<ulong, PacketRoadQuoteResponse> _reply;
        readonly RateLimiter _limiter;
        /// <summary>A player's job on a background thread, and the newest one waiting for it.</summary>
        sealed class PlayerQuotes
        {
            public RoadQuoteJob Running;
            public RoadQuoteJob Waiting;
        }

        readonly Dictionary<ulong, PlayerQuotes> _players = new Dictionary<ulong, PlayerQuotes>();
        // Players with a waiting job, oldest request first.
        readonly List<ulong> _waitingOrder = new List<ulong>();
        readonly Dictionary<long, IssuedRoadQuote> _issued = new Dictionary<long, IssuedRoadQuote>();
        int _runningCount;
        long _nextQuoteId;
        int _frame;

        public RoadQuoteService(RoadServerSettings settings, Func<RoadCostSettings> costs,
            Action<ulong, PacketRoadQuoteResponse> reply)
        {
            _settings = settings;
            _costs = costs;
            _reply = reply;
            _limiter = new RateLimiter(settings.QuoteBurst, settings.QuoteRefillSeconds);
        }

        public void Request(ulong steamId, PacketRoadQuoteRequest request)
        {
            if (request == null) return;
            bool bypass = ServerPlayers.BypassesLimits(steamId);
            float retryAfter;
            if (!bypass && !_limiter.TryTake(steamId, out retryAfter))
            {
                Reject(steamId, request.RequestId, RoadRejectReason.RateLimited,
                    LocHelper.Message("HighwayEngineer_Quote_RateLimited"), retryAfter);
                return;
            }

            // Validate before touching the player's current quote: a rejected request
            // must not cancel a good one.
            PlayerQuotes slot;
            _players.TryGetValue(steamId, out slot);
            int replaced = slot != null && slot.Waiting != null ? 1 : 0;
            if (!bypass && QueuedCount() - replaced >= _settings.QuoteQueueLimit)
            {
                Reject(steamId, request.RequestId, RoadRejectReason.ServerBusy,
                    LocHelper.Message("HighwayEngineer_Quote_Busy"), BusyRetrySeconds);
                return;
            }

            RoadRejectReason reason;
            string message;
            // Free roads (admin) are priced like creative ones: nothing to pay, any material.
            RoadQuoteJob job = RoadQuoteJob.TryCreate(steamId, request, _settings,
                ServerPlayers.IsCostExempt(steamId) || _settings.FreeRoads, out reason, out message);
            if (job == null)
            {
                Reject(steamId, request.RequestId, reason, message, 0f);
                return;
            }
            if (ExistingRoads != null)
                job.AvoidRoads(ExistingRoads(job.Planet));

            if (slot == null)
                _players[steamId] = slot = new PlayerQuotes();
            if (slot.Waiting != null)
            {
                Reject(steamId, slot.Waiting.Request.RequestId, RoadRejectReason.Superseded,
                    LocHelper.Message("HighwayEngineer_Quote_Superseded"), 0f);
                _waitingOrder.Remove(steamId);
            }
            // The running job stops at its next step and is answered as superseded when it ends;
            // this one starts after it.
            if (slot.Running != null)
                slot.Running.Cancel();
            slot.Waiting = job;
            _waitingOrder.Add(steamId);
            StartWaiting();
        }

        public void Update()
        {
            if (++_frame % PruneIntervalFrames == 0)
                PruneExpired();
            StartWaiting();
        }

        int QueuedCount()
        {
            return _runningCount + _waitingOrder.Count;
        }

        /// <summary>Starts waiting jobs whose player has none running, up to the concurrency cap.</summary>
        void StartWaiting()
        {
            for (int i = 0; i < _waitingOrder.Count && _runningCount < _settings.MaxConcurrentQuotes; i++)
            {
                PlayerQuotes slot = _players[_waitingOrder[i]];
                if (slot.Running != null) continue;
                RoadQuoteJob job = slot.Waiting;
                slot.Waiting = null;
                slot.Running = job;
                _waitingOrder.RemoveAt(i--);
                _runningCount++;
                MyAPIGateway.Parallel.StartBackground(job.Run, () => Completed(job));
            }
        }

        /// <summary>Game thread, after a background job ended (finished or cancelled).</summary>
        void Completed(RoadQuoteJob job)
        {
            _runningCount--;
            PlayerQuotes slot;
            if (_players.TryGetValue(job.SteamId, out slot) && ReferenceEquals(slot.Running, job))
            {
                slot.Running = null;
                if (slot.Waiting == null)
                    _players.Remove(job.SteamId);
            }
            if (job.IsCancelled)
                Reject(job.SteamId, job.Request.RequestId, RoadRejectReason.Superseded,
                    LocHelper.Message("HighwayEngineer_Quote_Superseded"), 0f);
            else
                Finish(job);
            StartWaiting();
        }

        /// <summary>Removes and returns the player's unexpired quote; false when it is unknown or expired.</summary>
        public bool TryTake(ulong steamId, long quoteId, out IssuedRoadQuote quote)
        {
            if (!_issued.TryGetValue(quoteId, out quote) || quote.SteamId != steamId)
            {
                quote = null;
                return false;
            }
            _issued.Remove(quoteId);
            return quote.ExpiresUtcTicks >= DateTime.UtcNow.Ticks;
        }

        public void ForgetPlayer(ulong steamId)
        {
            _limiter.Forget(steamId);
            PlayerQuotes slot;
            if (_players.TryGetValue(steamId, out slot))
            {
                // A running job still completes on its own; its reply goes nowhere.
                if (slot.Running != null) slot.Running.Cancel();
                slot.Waiting = null;
                _waitingOrder.Remove(steamId);
            }
            RemoveIssued(quote => quote.SteamId == steamId);
        }

        void Finish(RoadQuoteJob job)
        {
            if (job.Reason != RoadRejectReason.None)
            {
                _reply(job.SteamId, new PacketRoadQuoteResponse
                {
                    RequestId = job.Request.RequestId,
                    Reason = job.Reason,
                    Message = job.Message,
                    InvalidSegment = job.InvalidSegment
                });
                return;
            }

            // A new depot goes where the job found flat ground beside the road.
            DeliveryZone zone = RoadLogisticDepot.ZoneFromPivot(job.DepotPivot);
            // The nearest faction station takes the contract. Creative roads need no contract.
            RoadServiceProvider provider = null;
            IMyFaction faction;
            IMyFactionStation station;
            if (ServiceProviders.TryFindNearest(zone.ToOrientedBox().Center, out faction, out station))
                provider = ServiceProviders.Describe(faction, station);
            else if (!job.Exempt)
            {
                Reject(job.SteamId, job.Request.RequestId, RoadRejectReason.NoServiceProvider,
                    LocHelper.Message("HighwayEngineer_Contract_NoProvider"), 0f);
                return;
            }
            // A delivery needs a depot: the provider's own nearby one if it has one, else a new one.
            RoadDepotRecord depot = null;
            if (!job.Exempt && provider != null && (job.StoneMaterial || job.HasBridges) && DepotFinder != null)
                depot = DepotFinder(provider.FactionId, job.Planet, job.Request.Geometry);
            if (depot != null)
                zone = depot.Zone;
            RoadCostBreakdown costs = Price(job, _costs(), _settings.FreeRoads, depot != null);
            BridgeBuildParameters bridge = null;
            if (job.HasBridges)
            {
                // The contract builds the designs priced here, not whatever Auto picks later.
                bridge = job.Request.Bridge.Clone();
                bridge.RunDesigns = job.BridgePlans.ConvertAll(plan => plan.Design).ToArray();
            }
            var quote = new IssuedRoadQuote
            {
                QuoteId = ++_nextQuoteId,
                SteamId = job.SteamId,
                PlanetId = job.Request.PlanetId,
                Parameters = job.Request.Parameters,
                Geometry = job.Request.Geometry,
                Bridge = bridge,
                Costs = costs,
                Zone = zone,
                Provider = provider,
                DepotGridId = depot == null ? 0 : depot.GridId,
                ExpiresUtcTicks = DateTime.UtcNow.Ticks +
                                  (long)(_settings.QuoteLifetimeSeconds * TimeSpan.TicksPerSecond)
            };
            KeepNewestIssued(job.SteamId);
            _issued[quote.QuoteId] = quote;
            _reply(job.SteamId, new PacketRoadQuoteResponse
            {
                RequestId = job.Request.RequestId,
                Reason = RoadRejectReason.None,
                Costs = costs,
                QuoteId = quote.QuoteId,
                ExpiresSeconds = (float)_settings.QuoteLifetimeSeconds,
                Zone = quote.Zone,
                Provider = provider,
                DepotGridId = quote.DepotGridId
            });
        }

        /// <summary>
        /// The road's earthwork and pavement, and each bridge's concrete (Space Credits, or the
        /// Concrete Tool mod's Concrete Mix when it is loaded), road fill (Gravel) and Iron Ingot as
        /// "Steel Rebar" in proportion to the concrete's volume; the bridges' approach ramps count as
        /// fill. Stone-yielding materials are paid in Gravel; other materials are creative-only.
        /// </summary>
        static RoadCostBreakdown Price(RoadQuoteJob job, RoadCostSettings prices, bool free, bool reusesDepot)
        {
            double creditsPerCubicMeter = free ? 0.0 : prices.EarthMovementCreditsPerCubicMeter;
            double itemPerCubicMeter = free ? 0.0 : prices.RoadMaterialGravelKgPerCubicMeter;
            double structurePerCubicMeter = free ? 0.0 : prices.ConcreteCreditsPerCubicMeter();
            double mixPerCubicMeter = free || !ConcreteMod.Present ? 0.0 : prices.ConcreteMixPerCubicMeter();
            double deckItemPerCubicMeter = free ? 0.0 : prices.BridgePavementGravelKgPerCubicMeter;
            double rebarPerCubicMeter = free ? 0.0 : prices.BridgeRebarKgPerCubicMeter;
            RoadBuildParameters parameters = job.Request.Parameters;
            RoadEarthworkEstimate estimate = job.Estimate;

            double structureVolume = 0.0, deckFill = 0.0, rampFill = 0.0, bridgeLength = 0.0;
            BridgeDesign design = BridgeDesign.Auto;
            for (int i = 0; i < job.BridgePlans.Count; i++)
            {
                BridgePlan plan = job.BridgePlans[i];
                structureVolume += plan.StructureCubicMeters;
                deckFill += plan.PavementCubicMeters;
                rampFill += plan.RampCubicMeters;
                bridgeLength += plan.Length;
                design = i == 0 || design == plan.Design ? plan.Design : BridgeDesign.Auto;
            }

            double fill = estimate.FillCubicMeters + rampFill;
            double moved = estimate.CutCubicMeters + fill;
            long earthMovement = (long)Math.Ceiling(moved * creditsPerCubicMeter);
            double material = estimate.LengthMeters * parameters.WidthMeters * parameters.DepthMeters;
            long structure = (long)Math.Ceiling(structureVolume * structurePerCubicMeter);
            var costs = new RoadCostBreakdown
            {
                CutCubicMeters = estimate.CutCubicMeters,
                FillCubicMeters = fill,
                LengthMeters = estimate.LengthMeters + bridgeLength,
                MaterialCubicMeters = material + deckFill,
                EarthMovementCredits = earthMovement,
                Exempt = job.Exempt,
                CreditsPerCubicMeter = creditsPerCubicMeter,
                StructureCubicMeters = structureVolume,
                StructureCredits = structure,
                StructureCreditsPerCubicMeter = structurePerCubicMeter,
                Design = design,
                BridgeCount = job.BridgePlans.Count,
                BridgeLengthMeters = bridgeLength
            };
            double paved = (job.StoneMaterial ? material : 0.0) + (job.BridgeStoneMaterial ? deckFill : 0.0);
            if (paved > 0.0)
            {
                double gravel = (job.StoneMaterial ? material * itemPerCubicMeter : 0.0) +
                                (job.BridgeStoneMaterial ? deckFill * deckItemPerCubicMeter : 0.0);
                costs.ItemType = "MyObjectBuilder_Ingot";
                costs.ItemSubtype = "Stone";
                costs.ItemAmount = Math.Ceiling(gravel);
                costs.ItemPerCubicMeter = gravel / paved;
            }
            if (job.HasBridges)
            {
                costs.Item2Type = "MyObjectBuilder_Ingot";
                costs.Item2Subtype = "Iron";
                costs.Item2Name = LocHelper.Message("HighwayEngineer_Item_SteelRebar");
                costs.Item2Amount = Math.Ceiling(structureVolume * rebarPerCubicMeter);
                costs.Item2PerCubicMeter = rebarPerCubicMeter;
                if (mixPerCubicMeter > 0.0)
                {
                    costs.Item3Type = ConcreteMod.MixType;
                    costs.Item3Subtype = ConcreteMod.MixSubtype;
                    costs.Item3Name = ConcreteMod.MixName;
                    costs.Item3Amount = Math.Ceiling(structureVolume * mixPerCubicMeter);
                    costs.Item3PerCubicMeter = mixPerCubicMeter;
                }
            }
            // Only contracts with a delivery need a depot (exempt roads, free roads included, are built
            // at once), and only a new one is paid for.
            if (!job.Exempt && (costs.ItemAmount > 0.0 || costs.Item2Amount > 0.0 || costs.Item3Amount > 0.0))
            {
                costs.ReusesDepot = reusesDepot;
                if (!reusesDepot)
                    costs.InfrastructureCredits = prices.RoadInfrastructureFeeCredits;
            }
            long credits = earthMovement + structure + costs.InfrastructureCredits;
            long upfront = (long)Math.Round(credits * prices.UpfrontCreditShare);
            costs.Credits = credits;
            costs.CreditsUpfront = upfront;
            costs.CreditsOnDelivery = credits - upfront;
            return costs;
        }

        void KeepNewestIssued(ulong steamId)
        {
            var mine = new List<long>();
            foreach (IssuedRoadQuote quote in _issued.Values)
                if (quote.SteamId == steamId) mine.Add(quote.QuoteId);
            mine.Sort();
            for (int i = 0; i <= mine.Count - IssuedQuotesPerPlayer; i++)
                _issued.Remove(mine[i]);
        }

        void PruneExpired()
        {
            long now = DateTime.UtcNow.Ticks;
            RemoveIssued(quote => quote.ExpiresUtcTicks < now);
        }

        void RemoveIssued(Func<IssuedRoadQuote, bool> match)
        {
            var remove = new List<long>();
            foreach (IssuedRoadQuote quote in _issued.Values)
                if (match(quote)) remove.Add(quote.QuoteId);
            foreach (long id in remove)
                _issued.Remove(id);
        }

        void Reject(ulong steamId, int requestId, RoadRejectReason reason, string message, float retryAfter)
        {
            _reply(steamId, new PacketRoadQuoteResponse
            {
                RequestId = requestId,
                Reason = reason,
                Message = message,
                RetryAfterSeconds = retryAfter
            });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Common.Structures;
using HighwayEngineer.Server.Config;
using HighwayEngineer.Server.Helpers;
using HighwayEngineer.Server.Quotes;
using HighwayEngineer.Server.Structures;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using Sandbox.ModAPI.Contracts;
using VRage;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// Road and bridge contracts, from accepting a quote to the built road or bridge:
    /// <list type="number">
    /// <item>Accept: the upfront credits are charged and a custom game contract is posted from the
    /// provider faction's station and accepted for the player. A Road Logistic Depot is spawned
    /// beside the road start (or a nearby one of the faction reused), with a "Deliver Gravel" GPS at the delivery box by its contract terminal.</item>
    /// <item>Pending: the item is collected from accessible inventories inside the box. Destroying the
    /// depot's contract terminal fails the contract (no refund). Depots stay after their contracts
    /// end; later contracts of the same faction nearby deliver to them instead of paying for a new one.</item>
    /// <item>Delivered: the remaining credits are charged (AwaitingPayment until they can be) and the
    /// game contract is finished.</item>
    /// <item>Building: the server pushes the road (or builds the bridge's voxel maps), one contract
    /// at a time; a failed build refunds.</item>
    /// </list>
    /// A bridge contract delivers a second item too (Iron Ingot as "Steel Rebar"), and with the
    /// Concrete Tool mod a third (its Concrete Mix); it counts as delivered once all items are in. Abandoning the game contract cancels the road or bridge
    /// and refunds the credits paid.
    /// </summary>
    public sealed class RoadContractService
    {
        const int DeliveryCheckFrames = 100;
        const int PaymentRetryFrames = 1800;

        readonly RoadServerSettings _settings;
        readonly RoadQuoteService _quotes;
        readonly Action<ulong, PacketRoadContractStatus> _send;
        readonly Action<ulong, PacketRoadContractList> _sendList;
        readonly Action<ulong, PacketRoadDepotList> _sendDepots;
        readonly Action<PacketRoadDepotList> _broadcastDepots;
        readonly RateLimiter _acceptLimiter;
        readonly RateLimiter _listLimiter = new RateLimiter(2, 5.0);
        readonly RoadContractStore _store;
        RoadContractRecord _building;
        readonly BridgeBuilder _bridges = new BridgeBuilder();
        bool _subscribed;
        int _frame;

        public RoadContractService(RoadServerSettings settings, RoadQuoteService quotes,
            Action<ulong, PacketRoadContractStatus> send, Action<ulong, PacketRoadContractList> sendList,
            Action<ulong, PacketRoadDepotList> sendDepots, Action<PacketRoadDepotList> broadcastDepots,
            Action<PacketRoadLibraryChanged> broadcastLibrary, Action<ulong, PacketRoadLibrarySnapshot> sendLibrary)
        {
            _sendDepots = sendDepots;
            _broadcastDepots = broadcastDepots;
            _settings = settings;
            _quotes = quotes;
            _send = send;
            _sendList = sendList;
            _acceptLimiter = new RateLimiter(settings.AcceptBurst, settings.AcceptRefillSeconds);
            _store = RoadContractStore.Load();
            Library = new RoadLibraryService(_store, broadcastLibrary, sendLibrary);
        }

        /// <summary>Every road and bridge segment the contracts built.</summary>
        public RoadLibraryService Library { get; private set; }

        /// <summary>Hooks the game's contract events and reconciles contracts saved with the world.</summary>
        public void Start()
        {
            if (MyAPIGateway.ContractSystem != null && !_subscribed)
            {
                MyAPIGateway.ContractSystem.CustomFailFor += OnGameContractFailed;
                _subscribed = true;
            }
            foreach (RoadContractRecord record in _store.Contracts.ToArray())
            {
                // Game contracts only change while the server runs; one that is gone was
                // abandoned or failed without us hearing about it.
                if ((record.State == RoadContractState.Pending || record.State == RoadContractState.AwaitingPayment) &&
                    record.GameContractId != 0 && MyAPIGateway.ContractSystem != null &&
                    !MyAPIGateway.ContractSystem.IsContractActive(record.GameContractId))
                    Cancel(record, LocHelper.Message("HighwayEngineer_Contract_GameContractGone"));
            }
            RemeasureDepots();
        }

        /// <summary>
        /// Re-measures every depot's delivery box from its contract terminal (depots saved by an
        /// older placement rule move to the current one), moving the GPS of contracts delivering there.
        /// </summary>
        void RemeasureDepots()
        {
            foreach (RoadDepotRecord depot in _store.Depots)
            {
                var terminal = MyAPIGateway.Entities.GetEntityById(depot.TerminalId) as IMyCubeBlock;
                if (terminal == null || terminal.MarkedForClose) continue;
                depot.Zone = RoadLogisticDepot.ZoneFromPivot(RoadLogisticDepot.PivotFromTerminal(terminal.WorldMatrix));
                foreach (RoadContractRecord record in _store.Contracts)
                    if (record.State == RoadContractState.Pending && record.DepotGridId == depot.GridId)
                    {
                        record.Zone = depot.Zone;
                        RemoveDeliveryGps(record);
                        AddDeliveryGps(record);
                    }
            }
        }

        public void Unload()
        {
            if (_subscribed && MyAPIGateway.ContractSystem != null)
                MyAPIGateway.ContractSystem.CustomFailFor -= OnGameContractFailed;
            _subscribed = false;
            _store.Save();
        }

        public void Save()
        {
            _store.Save();
        }

        public void Update()
        {
            _frame++;
            if (_frame % DeliveryCheckFrames == 0)
            {
                CheckDepots();
                foreach (RoadContractRecord record in _store.Contracts.ToArray())
                    if (record.State == RoadContractState.Pending)
                        CollectDelivery(record);
            }
            if (_frame % PaymentRetryFrames == 0)
                foreach (RoadContractRecord record in _store.Contracts.ToArray())
                    if (record.State == RoadContractState.AwaitingPayment)
                        CompleteDelivery(record);
            if (_building == null)
                foreach (RoadContractRecord record in _store.Contracts)
                    if (record.State == RoadContractState.Building)
                    {
                        StartBuild(record);
                        break;
                    }
        }

        public void Accept(ulong steamId, PacketRoadContractAccept packet)
        {
            if (packet == null) return;
            bool bypass = ServerPlayers.BypassesLimits(steamId);
            float retryAfter;
            if (!bypass && !_acceptLimiter.TryTake(steamId, out retryAfter))
            {
                Reject(steamId, packet.RequestId, RoadRejectReason.RateLimited, LocHelper.Message("HighwayEngineer_Error_TooManyRequests"), retryAfter);
                return;
            }
            IssuedRoadQuote quote;
            if (!_quotes.TryTake(steamId, packet.QuoteId, out quote))
            {
                Reject(steamId, packet.RequestId, RoadRejectReason.QuoteExpired, LocHelper.Message("HighwayEngineer_Contract_QuoteExpired"), 0f);
                return;
            }
            RoadDepotRecord reusedDepot = FindDepot(quote.DepotGridId);
            if (quote.DepotGridId != 0 && (reusedDepot == null || IsDestroyed(reusedDepot)))
            {
                Reject(steamId, packet.RequestId, RoadRejectReason.QuoteExpired,
                    LocHelper.Message("HighwayEngineer_Contract_DepotGone"), 0f);
                return;
            }
            long identityId = MyAPIGateway.Players.TryGetIdentityId(steamId);
            if (identityId == 0)
            {
                Reject(steamId, packet.RequestId, RoadRejectReason.NoPermission, LocHelper.Message("HighwayEngineer_Contract_NoIdentity"), 0f);
                return;
            }
            if (!bypass && OpenContracts(steamId) >= _settings.MaxOpenContractsPerPlayer)
            {
                Reject(steamId, packet.RequestId, RoadRejectReason.TooManyContracts,
                    LocHelper.Message("HighwayEngineer_Contract_TooManyOpen", _settings.MaxOpenContractsPerPlayer), 0f);
                return;
            }

            RoadCostBreakdown costs = quote.Costs;
            var record = new RoadContractRecord
            {
                ContractId = ++_store.NextContractId,
                OwnerSteamId = steamId,
                OwnerIdentityId = identityId,
                PlanetId = quote.PlanetId,
                Parameters = quote.Parameters,
                Geometry = quote.Geometry,
                Bridge = quote.Bridge,
                Credits = costs.Credits,
                CreditsUpfront = costs.CreditsUpfront,
                CreditsOnDelivery = costs.CreditsOnDelivery,
                ItemType = costs.ItemType,
                ItemSubtype = costs.ItemSubtype,
                ItemAmount = costs.ItemAmount,
                Item2Type = costs.Item2Type,
                Item2Subtype = costs.Item2Subtype,
                Item2Name = costs.Item2Name,
                Item2Amount = costs.Item2Amount,
                Item3Type = costs.Item3Type,
                Item3Subtype = costs.Item3Subtype,
                Item3Name = costs.Item3Name,
                Item3Amount = costs.Item3Amount,
                Exempt = costs.Exempt,
                Provider = quote.Provider,
                Zone = quote.Zone,
                InfrastructureCredits = costs.InfrastructureCredits
            };

            if (!record.Exempt && record.CreditsUpfront > 0)
            {
                string error;
                if (!TryCharge(steamId, record.CreditsUpfront, out error))
                {
                    Reject(steamId, packet.RequestId, RoadRejectReason.InsufficientCredits, error, 0f);
                    return;
                }
                record.CreditsPaid = record.CreditsUpfront;
            }

            if (!record.Exempt && (record.ItemAmount > 0.0 || record.Item2Amount > 0.0 || record.Item3Amount > 0.0))
            {
                string error;
                if (!TryCreateGameContract(record, out error))
                {
                    Refund(record);
                    Reject(steamId, packet.RequestId, RoadRejectReason.ContractRejected, error, 0f);
                    return;
                }
                record.State = RoadContractState.Pending;
                if (reusedDepot != null)
                {
                    record.DepotGridId = reusedDepot.GridId;
                    record.Zone = reusedDepot.Zone;
                }
                AddDeliveryGps(record);
                if (reusedDepot == null)
                    SpawnDepot(record);
            }
            else
            {
                // Creative or nothing to deliver: build straight away.
                record.State = RoadContractState.Building;
            }
            _store.Contracts.Add(record);
            _store.Save();
            Send(record, packet.RequestId, null);
        }

        public void List(ulong steamId, PacketRoadContractListRequest request)
        {
            if (request == null) return;
            float retryAfter;
            var reply = new PacketRoadContractList { RequestId = request.RequestId };
            if (!ServerPlayers.BypassesLimits(steamId) && !_listLimiter.TryTake(steamId, out retryAfter))
                reply.RetryAfterSeconds = retryAfter;
            else
                foreach (RoadContractRecord record in _store.Contracts)
                    if (record.OwnerSteamId == steamId && record.IsOpen)
                        reply.Contracts.Add(record.ToInfo());
            _sendList(steamId, reply);
            if (reply.RetryAfterSeconds <= 0f)
                _sendDepots(steamId, DepotList());
        }

        /// <summary>
        /// Admin override: every contract still waiting for delivery or payment skips the rest of
        /// it (no further item or credits) and is built. Returns how many were completed.
        /// </summary>
        public int ForceCompleteAll()
        {
            int count = 0;
            foreach (RoadContractRecord record in _store.Contracts)
            {
                if (record.State != RoadContractState.Pending && record.State != RoadContractState.AwaitingPayment)
                    continue;
                RemoveDeliveryGps(record);
                if (record.GameContractId != 0 && MyAPIGateway.ContractSystem != null)
                    MyAPIGateway.ContractSystem.TryFinishCustomContract(record.GameContractId);
                record.State = RoadContractState.Building;
                Send(record, 0, LocHelper.Message(NounKey("HighwayEngineer_Contract_AdminCompleted", record)));
                count++;
            }
            if (count > 0)
                _store.Save();
            return count;
        }

        public void ForgetPlayer(ulong steamId)
        {
            _acceptLimiter.Forget(steamId);
            _listLimiter.Forget(steamId);
        }

        int OpenContracts(ulong steamId)
        {
            int count = 0;
            foreach (RoadContractRecord record in _store.Contracts)
                if (record.OwnerSteamId == steamId && record.IsOpen) count++;
            return count;
        }

        bool TryCreateGameContract(RoadContractRecord record, out string error)
        {
            error = null;
            if (record.Provider == null || MyAPIGateway.ContractSystem == null)
            {
                error = LocHelper.Message("HighwayEngineer_Contract_NoProvider");
                return false;
            }
            MyDefinitionId type;
            if (!MyDefinitionId.TryParse("MyObjectBuilder_ContractTypeDefinition", Constants.ROAD_CONTRACT_TYPE, out type))
            {
                error = LocHelper.Message("HighwayEngineer_Contract_TypeMissing");
                return false;
            }
            var contract = new MyContractCustom(type, 0L, 0, 0, 0,
                LocHelper.GetLoc(NounKey("HighwayEngineer_Contract_Title", record)), Describe(record), 0, 0, null);
            MyAddContractResultWrapper added = MyAPIGateway.ContractSystem.AddContract(contract, record.Provider.StationId);
            if (!added.Success)
            {
                error = record.Provider.FactionName == null
                    ? LocHelper.Message("HighwayEngineer_Contract_NotPosted")
                    : LocHelper.Message("HighwayEngineer_Contract_NotPostedBy", record.Provider.FactionName);
                return false;
            }
            MyActivationResults accepted = MyAPIGateway.ContractSystem.AcceptContract(record.OwnerIdentityId, added.ContractId);
            if (accepted != MyActivationResults.Success)
            {
                MyAPIGateway.ContractSystem.RemoveContract(added.ContractId);
                error = accepted == MyActivationResults.Fail_ContractLimitReachedHard
                    ? LocHelper.Message("HighwayEngineer_Contract_LimitReached")
                    : LocHelper.Message("HighwayEngineer_Contract_NotAccepted", accepted.ToString());
                return false;
            }
            record.GameContractId = added.ContractId;
            return true;
        }

        /// <summary>The game contract's description, in the server's language (the game stores it as text).</summary>
        static string Describe(RoadContractRecord record)
        {
            int bridges = record.BridgeRuns().Count;
            string what;
            if (record.OnlyBridges && bridges == 1)
            {
                BridgeDesign design = record.Bridge.DesignFor(0);
                what = LocHelper.GetLoc(design == BridgeDesign.Arch ? "HighwayEngineer_Contract_What_ArchBridge"
                    : design == BridgeDesign.Beam ? "HighwayEngineer_Contract_What_BeamBridge"
                    : "HighwayEngineer_Contract_What_Bridge");
            }
            else if (record.OnlyBridges)
                what = LocHelper.Format("HighwayEngineer_Contract_What_Bridges", bridges);
            else
                what = LocHelper.Format(bridges == 0 ? "HighwayEngineer_Contract_What_Road"
                    : bridges == 1 ? "HighwayEngineer_Contract_What_RoadWithBridge"
                    : "HighwayEngineer_Contract_What_RoadWithBridges", record.SegmentCount, bridges);
            var items = new List<string>();
            if (record.ItemAmount > 0.0)
                items.Add(LocHelper.Format("HighwayEngineer_Contract_ItemKg", record.ItemAmount, ItemName(record)));
            if (record.Item2Amount > 0.0)
                items.Add(LocHelper.Format("HighwayEngineer_Contract_ItemKg", record.Item2Amount, LocHelper.Resolve(record.Item2Name)));
            if (record.Item3Amount > 0.0)
                items.Add(LocHelper.Format("HighwayEngineer_Contract_ItemCount", record.Item3Amount, ConcreteMod.MixDisplayName()));
            return LocHelper.Format("HighwayEngineer_Contract_Description", what, LocHelper.JoinAnd(items));
        }

        /// <summary>Deck heights above the average radius, from a plan's deck radii at its nodes.</summary>
        static double[] Heights(double[] radii, double averageRadius)
        {
            var heights = new double[radii.Length];
            for (int i = 0; i < radii.Length; i++)
                heights[i] = radii[i] - averageRadius;
            return heights;
        }

        /// <summary>The "_Road" or "_Bridge" variant of a status message key.</summary>
        static string NounKey(string key, RoadContractRecord record)
        {
            return key + (record.OnlyBridges ? "_Bridge" : "_Road");
        }

        static string ItemName(RoadContractRecord record)
        {
            return ItemNames.Display(record.ItemType, record.ItemSubtype, record.ItemSubtype);
        }

        /// <summary>
        /// The delivery GPS name: "Deliver Gravel" in the game's own contract wording, followed by
        /// the progress, e.g. "Deliver Gravel 1234/2345kg".
        /// </summary>
        static string DeliveryGpsName(RoadContractRecord record)
        {
            var names = new List<string>();
            if (record.ItemAmount > 0.0 || record.Item2Amount <= 0.0 && record.Item3Amount <= 0.0)
                names.Add(ItemName(record));
            if (record.Item2Amount > 0.0) names.Add(LocHelper.Resolve(record.Item2Name));
            if (record.Item3Amount > 0.0) names.Add(ConcreteMod.MixDisplayName());
            string items = string.Join(" + ", names);
            return string.Format(MyTexts.GetString(MyStringId.GetOrCompute("Contract_Delivery_ItemGpsName")),
                items) + " " + DeliveryProgress(record);
        }

        /// <summary>A "Deliver Gravel 0/2345kg" GPS at the box, worded with the game's own contract texts.</summary>
        void AddDeliveryGps(RoadContractRecord record)
        {
            if (record.Zone == null || MyAPIGateway.Session.GPS == null) return;
            string name = DeliveryGpsName(record);
            string description = MyTexts.GetString(MyStringId.GetOrCompute("Contract_ObtainAndDelivery_GpsDescription"));
            IMyGps gps = MyAPIGateway.Session.GPS.Create(name, description, record.Zone.ToOrientedBox().Center, true);
            gps.GPSColor = Color.DarkOrange;
            MyAPIGateway.Session.GPS.AddGps(record.OwnerIdentityId, gps);
            record.GpsHash = gps.Hash;
        }

        /// <summary>
        /// Renames the delivery GPS to the current progress. A GPS hash depends on its name, so the
        /// marker is modified under its old hash and the new one is kept; a GPS the player deleted
        /// is added again.
        /// </summary>
        void UpdateDeliveryGps(RoadContractRecord record)
        {
            IMyGpsCollection gpss = MyAPIGateway.Session.GPS;
            if (record.Zone == null || gpss == null) return;
            IMyGps gps = null;
            if (record.GpsHash != 0)
                foreach (IMyGps candidate in gpss.GetGpsList(record.OwnerIdentityId))
                    if (candidate.Hash == record.GpsHash)
                    {
                        gps = candidate;
                        break;
                    }
            if (gps == null)
            {
                AddDeliveryGps(record);
                return;
            }
            gps.Name = DeliveryGpsName(record);
            gpss.ModifyGps(record.OwnerIdentityId, gps);
            gps.UpdateHash();
            record.GpsHash = gps.Hash;
        }

        void RemoveDeliveryGps(RoadContractRecord record)
        {
            if (record.GpsHash == 0 || MyAPIGateway.Session.GPS == null) return;
            MyAPIGateway.Session.GPS.RemoveGps(record.OwnerIdentityId, record.GpsHash);
            record.GpsHash = 0;
        }

        /// <summary>
        /// The Road Logistic Depot for a quote: an intact depot of <paramref name="factionId"/> on the
        /// planet within <see cref="RoadServerSettings.DepotReuseRadiusMeters"/> of a node of the road,
        /// or reachable along built roads within <see cref="RoadServerSettings.DepotReuseRoadDistanceMeters"/>
        /// of one. The closest wins; null when a new depot is needed.
        /// </summary>
        public RoadDepotRecord TryFindReusableDepot(long factionId, MyPlanet planet, double[] geometry)
        {
            MatrixD world = planet.WorldMatrix;
            double radius = planet.AverageRadius;
            List<Vector3D> nodes = RoadNetwork.Nodes(world, geometry);
            if (nodes.Count == 0) return null;
            Vector3D center = planet.PositionComp.GetPosition();
            RoadNetwork network = null;
            HashSet<int> roadNodes = null;
            RoadDepotRecord best = null;
            double bestMeters = double.PositiveInfinity;
            foreach (RoadDepotRecord depot in _store.Depots)
            {
                if (depot.FactionId != factionId || depot.PlanetId != planet.EntityId || depot.Zone == null ||
                    IsDestroyed(depot))
                    continue;
                if (network == null)
                {
                    network = new RoadNetwork(radius);
                    foreach (RoadContractRecord record in _store.Contracts)
                        if (record.PlanetId == planet.EntityId &&
                            (record.State == RoadContractState.Completed || record.State == RoadContractState.Building))
                            network.AddRoad(world, record.Geometry);
                    // The quoted road joins the network at its junctions (a node on an existing
                    // node or segment), so reaching any of its nodes means reaching the road.
                    roadNodes = new HashSet<int>(network.AddRoad(world, geometry));
                }
                Vector3D at = Vector3D.Normalize(depot.Zone.ToOrientedBox().Center - center);
                double meters = double.PositiveInfinity;
                foreach (Vector3D node in nodes)
                    meters = Math.Min(meters, network.ArcMeters(at, node));
                if (meters > _settings.DepotReuseRadiusMeters)
                    meters = network.DistanceAlongRoads(depot.Anchor, roadNodes, _settings.DepotReuseRoadDistanceMeters);
                if (meters < bestMeters)
                {
                    best = depot;
                    bestMeters = meters;
                }
            }
            return best;
        }

        /// <summary>
        /// The segments of every road on <paramref name="planet"/> that is built, being built or
        /// waiting to be (failed and cancelled contracts are left out), with their widths.
        /// </summary>
        public List<RoadCurve> RoadsOn(MyPlanet planet)
        {
            var roads = new List<RoadCurve>();
            if (planet == null) return roads;
            MatrixD world = planet.WorldMatrix;
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var values = new double[per];
            foreach (RoadContractRecord record in _store.Contracts)
            {
                if (record.PlanetId != planet.EntityId || record.Geometry == null ||
                    record.State == RoadContractState.Failed || record.State == RoadContractState.Cancelled)
                    continue;
                double width = record.Parameters == null ? 0.0 : record.Parameters.WidthMeters;
                for (int i = 0; i + per <= record.Geometry.Length; i += per)
                {
                    Array.Copy(record.Geometry, i, values, 0, per);
                    RoadCurve curve;
                    if (!RoadCurve.TryFromLocalGeometry(world, values, out curve)) continue;
                    curve.WidthMeters = width;
                    roads.Add(curve);
                }
            }
            return roads;
        }

        /// <summary>Every registered depot whose faction still exists, for the clients' maps.</summary>
        PacketRoadDepotList DepotList()
        {
            var list = new PacketRoadDepotList
            {
                ReuseRadiusMeters = _settings.DepotReuseRadiusMeters,
                ReuseRoadDistanceMeters = _settings.DepotReuseRoadDistanceMeters
            };
            foreach (RoadDepotRecord depot in _store.Depots)
            {
                IMyFaction faction = MyAPIGateway.Session.Factions == null
                    ? null
                    : MyAPIGateway.Session.Factions.TryGetFactionById(depot.FactionId);
                if (faction == null || depot.Zone == null) continue;
                Vector3D position = depot.Zone.ToOrientedBox().Center;
                list.Depots.Add(new RoadDepotInfo
                {
                    GridId = depot.GridId,
                    PlanetId = depot.PlanetId,
                    X = position.X, Y = position.Y, Z = position.Z,
                    Faction = ServiceProviders.Describe(faction, null)
                });
            }
            return list;
        }

        RoadDepotRecord FindDepot(long gridId)
        {
            if (gridId == 0) return null;
            foreach (RoadDepotRecord depot in _store.Depots)
                if (depot.GridId == gridId) return depot;
            return null;
        }

        /// <summary>
        /// Spawns a Road Logistic Depot where the quote planned it, owned by the provider faction.
        /// Once it exists it is registered for later contracts, and the delivery box is re-measured
        /// from its actual contract terminal. If another grid is in the way, no depot is spawned and
        /// the planned box is used as is.
        /// </summary>
        void SpawnDepot(RoadContractRecord record)
        {
            if (record.Zone == null || MyAPIGateway.PrefabManager == null) return;
            var planet = MyAPIGateway.Entities.GetEntityById(record.PlanetId) as MyPlanet;
            if (planet == null) return;
            List<Vector3D> nodes = RoadNetwork.Nodes(planet.WorldMatrix, record.Geometry);
            if (nodes.Count == 0) return;
            MatrixD pivot = RoadLogisticDepot.PivotFromZone(record.Zone);
            MyOrientedBoundingBoxD footprint = RoadLogisticDepot.Footprint(pivot);
            BoundingBoxD bounds = footprint.GetAABB();
            foreach (IMyEntity entity in MyAPIGateway.Entities.GetTopMostEntitiesInBox(ref bounds))
            {
                var grid = entity as IMyCubeGrid;
                if (grid == null || grid.MarkedForClose) continue;
                var gridBox = new MyOrientedBoundingBoxD(grid.LocalAABB, grid.WorldMatrix);
                if (footprint.Intersects(ref gridBox))
                    return;
            }

            var depot = new RoadDepotRecord
            {
                FactionId = record.Provider == null ? 0 : record.Provider.FactionId,
                PlanetId = record.PlanetId,
                Zone = record.Zone,
                Anchor = nodes[0]
            };
            var grids = new List<IMyCubeGrid>();
            long owner = ProviderOwner(record.Provider);
            MyAPIGateway.PrefabManager.SpawnPrefab(grids, RoadLogisticDepot.PrefabName, pivot.Translation,
                (Vector3)pivot.Forward, (Vector3)pivot.Up, Vector3.Zero, Vector3.Zero, null,
                SpawningOptions.UseGridOrigin, owner, false,
                () => OnDepotSpawned(record, depot, grids, planet, owner));
        }

        void OnDepotSpawned(RoadContractRecord record, RoadDepotRecord depot, List<IMyCubeGrid> grids,
            MyPlanet planet, long owner)
        {
            if (grids.Count == 0) return;
            IMyCubeGrid grid = grids[0];
            depot.GridId = grid.EntityId;
            // The site is only nearly flat: prop up the floor where it hangs above the terrain.
            StructureService.AddGroundSupports(grid, planet, owner);
            var blocks = new List<IMySlimBlock>();
            grid.GetBlocks(blocks, block => block.FatBlock != null &&
                block.BlockDefinition.Id.TypeId.ToString() == "MyObjectBuilder_ContractBlock");
            if (blocks.Count > 0)
            {
                IMyCubeBlock terminal = blocks[0].FatBlock;
                depot.TerminalId = terminal.EntityId;
                depot.Zone = RoadLogisticDepot.ZoneFromPivot(RoadLogisticDepot.PivotFromTerminal(terminal.WorldMatrix));
            }
            // Registered even if the contract ended meanwhile: the depot stays for later contracts.
            _store.Depots.Add(depot);
            _broadcastDepots(DepotList());
            if (record.State == RoadContractState.Pending)
            {
                record.DepotGridId = depot.GridId;
                record.Zone = depot.Zone;
                RemoveDeliveryGps(record);
                AddDeliveryGps(record);
                Send(record, 0, null);
            }
            _store.Save();
        }

        /// <summary>The provider faction's founder owns the depot (nobody when there is no provider).</summary>
        static long ProviderOwner(RoadServiceProvider provider)
        {
            if (provider == null || MyAPIGateway.Session.Factions == null) return 0;
            IMyFaction faction = MyAPIGateway.Session.Factions.TryGetFactionById(provider.FactionId);
            return faction == null ? 0 : faction.FounderId;
        }

        /// <summary>The depot's contract terminal (or, without one, the whole grid) is gone.</summary>
        static bool IsDestroyed(RoadDepotRecord depot)
        {
            IMyEntity entity = MyAPIGateway.Entities.GetEntityById(depot.TerminalId != 0 ? depot.TerminalId : depot.GridId);
            return entity == null || entity.MarkedForClose || entity.Closed;
        }

        /// <summary>
        /// Destroyed depots: every contract still waiting for delivery there fails, and what is
        /// left of the depot is removed with its registration.
        /// </summary>
        void CheckDepots()
        {
            foreach (RoadDepotRecord depot in _store.Depots.ToArray())
            {
                if (!IsDestroyed(depot)) continue;
                foreach (RoadContractRecord record in _store.Contracts.ToArray())
                    if (record.State == RoadContractState.Pending && record.DepotGridId == depot.GridId)
                        FailDepotDestroyed(record);
                IMyEntity grid = MyAPIGateway.Entities.GetEntityById(depot.GridId);
                if (grid != null && !grid.MarkedForClose)
                    grid.Close();
                _store.Depots.Remove(depot);
                _store.Save();
                _broadcastDepots(DepotList());
            }
        }

        void CollectDelivery(RoadContractRecord record)
        {
            IMyPlayer owner = ServerPlayers.TryGetPlayer(record.OwnerSteamId);
            IMyCharacter character = owner == null ? null : owner.Character;
            double taken = 0.0, taken2 = 0.0, taken3 = 0.0;
            if (record.ItemAmount - record.Delivered > 0.001)
                taken = DeliveryCollector.Collect(record.Zone, record.OwnerIdentityId, character,
                    record.ItemType, record.ItemSubtype, record.ItemAmount - record.Delivered);
            if (record.Item2Amount - record.Delivered2 > 0.001)
                taken2 = DeliveryCollector.Collect(record.Zone, record.OwnerIdentityId, character,
                    record.Item2Type, record.Item2Subtype, record.Item2Amount - record.Delivered2);
            if (record.Item3Amount - record.Delivered3 > 0.001)
                taken3 = DeliveryCollector.Collect(record.Zone, record.OwnerIdentityId, character,
                    record.Item3Type, record.Item3Subtype, record.Item3Amount - record.Delivered3);
            if (taken <= 0.0 && taken2 <= 0.0 && taken3 <= 0.0) return;
            record.Delivered = Math.Min(record.ItemAmount, record.Delivered + taken);
            record.Delivered2 = Math.Min(record.Item2Amount, record.Delivered2 + taken2);
            record.Delivered3 = Math.Min(record.Item3Amount, record.Delivered3 + taken3);
            if (record.IsDelivered)
            {
                RemoveDeliveryGps(record);
                CompleteDelivery(record);
                return;
            }
            // Partial deliveries are kept; the player sees the running total.
            UpdateDeliveryGps(record);
            _store.Save();
            Send(record, 0, LocHelper.Message("HighwayEngineer_Contract_Received", DeliveryProgress(record)));
        }

        /// <summary>
        /// Delivered of total, e.g. "1234/2345kg" (the first two items are kilograms, the third
        /// counted); with more items, each, e.g. "1234/2345kg + 0/9000kg + 0/900".
        /// </summary>
        static string DeliveryProgress(RoadContractRecord record)
        {
            var parts = new List<string>();
            if (record.ItemAmount > 0.0 || record.Item2Amount <= 0.0 && record.Item3Amount <= 0.0)
                parts.Add(Progress(record.Delivered, record.ItemAmount, "kg"));
            if (record.Item2Amount > 0.0) parts.Add(Progress(record.Delivered2, record.Item2Amount, "kg"));
            if (record.Item3Amount > 0.0) parts.Add(Progress(record.Delivered3, record.Item3Amount, ""));
            return string.Join(" + ", parts);
        }

        static string Progress(double delivered, double amount, string unit)
        {
            return Math.Floor(delivered).ToString("0", CultureInfo.InvariantCulture) + "/" +
                   Math.Ceiling(amount).ToString("0", CultureInfo.InvariantCulture) + unit;
        }

        /// <summary>Charges the remaining credits, finishes the game contract and queues the build.</summary>
        void CompleteDelivery(RoadContractRecord record)
        {
            long due = record.Exempt ? 0 : Math.Max(0, record.Credits - record.CreditsPaid);
            if (due > 0)
            {
                string error;
                if (!TryCharge(record.OwnerSteamId, due, out error))
                {
                    if (record.State != RoadContractState.AwaitingPayment)
                    {
                        record.State = RoadContractState.AwaitingPayment;
                        _store.Save();
                        Send(record, 0, LocHelper.Message("HighwayEngineer_Contract_AwaitingPayment", error));
                    }
                    return;
                }
                record.CreditsPaid += due;
            }
            if (record.GameContractId != 0 && MyAPIGateway.ContractSystem != null)
                MyAPIGateway.ContractSystem.TryFinishCustomContract(record.GameContractId);
            record.State = RoadContractState.Building;
            _store.Save();
            Send(record, 0, LocHelper.Message(NounKey("HighwayEngineer_Contract_Delivered", record)));
        }

        /// <summary>
        /// Set by the server: drops the planet's voxel edits along road curves, then calls back
        /// (<see cref="RoadRepairService.ResetVoxelEdits"/>), so a new road is not left under old holes and dirt.
        /// </summary>
        public Action<MyPlanet, List<RoadCurve>, Action> ResetVoxelEdits;

        void StartBuild(RoadContractRecord record)
        {
            _building = record;
            string modificationId = null;
            var built = new List<BuiltBridge>();
            Action<bool, string> done = (success, message) =>
            {
                _building = null;
                if (success)
                    record.State = RoadContractState.Completed;
                else
                {
                    Refund(record);
                    record.State = RoadContractState.Failed;
                }
                // Whatever was built goes in the library, so it can be bulldozed (a refunded part for nothing).
                if (modificationId != null || built.Count > 0)
                    Library.AddBuilt(record, modificationId, built);
                _store.Save();
                Send(record, 0, success
                    ? LocHelper.Message(NounKey("HighwayEngineer_Contract_Built", record))
                    : LocHelper.Message(NounKey("HighwayEngineer_Contract_BuildFailed", record), message));
            };
            // The road first: the bridges are planned on the terrain it leaves.
            Action<bool, string, string> bridges = (success, message, modification) =>
            {
                if (success)
                {
                    modificationId = modification;
                    ResetEditsAlongRoad(record, () => BuildBridges(record, 0, built, done));
                }
                else
                    done(false, message);
            };
            if (record.OnlyBridges)
                bridges(true, null, null);
            else
                RoadConstruction.Build(record, bridges);
        }

        /// <summary>The contract's road segments (not its bridges), with the planet's voxel edits along them dropped.</summary>
        void ResetEditsAlongRoad(RoadContractRecord record, Action next)
        {
            var planet = MyAPIGateway.Entities.GetEntityById(record.PlanetId) as MyPlanet;
            if (planet == null || record.Geometry == null || ResetVoxelEdits == null || record.OnlyBridges)
            {
                next();
                return;
            }
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var curves = new List<RoadCurve>();
            for (int offset = 0; offset + per <= record.Geometry.Length; offset += per)
            {
                if (record.IsBridged(offset / per)) continue;
                var values = new double[per];
                Array.Copy(record.Geometry, offset, values, 0, per);
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(planet.WorldMatrix, values, out curve)) continue;
                curve.WidthMeters = record.Parameters.WidthMeters;
                curves.Add(curve);
            }
            if (curves.Count == 0)
                next();
            else
                ResetVoxelEdits(planet, curves, next);
        }

        /// <summary>
        /// Plans each bridge again with the design that was priced, then builds it, one after the
        /// other from <paramref name="run"/>.
        /// </summary>
        void BuildBridges(RoadContractRecord record, int run, List<BuiltBridge> built, Action<bool, string> done)
        {
            List<BridgeRun> runs = record.BridgeRuns();
            if (run >= runs.Count)
            {
                done(true, null);
                return;
            }
            var planet = MyAPIGateway.Entities.GetEntityById(record.PlanetId) as MyPlanet;
            var curves = new List<RoadCurve>();
            Vector3D start, end;
            double[] pins;
            if (planet == null || !BridgePlanner.TryReadRun(planet, record.Geometry, runs[run], record.Bridge.NodeLifts,
                    curves, out start, out end, out pins))
            {
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Bridge: contract " + record.ContractId +
                    " run " + run + ": its ends could not be read");
                done(false, LocHelper.Message("HighwayEngineer_Contract_BridgeEndsMissing"));
                return;
            }
            BridgeBuildParameters bridge = record.Bridge;
            RoadBuildParameters road = record.Parameters;
            BridgeOptions options = RoadQuoteJob.BridgeOptionsFor(road, bridge, bridge.DesignFor(run));
            BridgePlan plan = null;
            string error = null;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try
                {
                    plan = BridgePlanner.Plan(planet, curves, start, end, pins, options, out error);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            }, () =>
            {
                if (plan == null)
                {
                    MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Bridge: contract " + record.ContractId +
                        " run " + run + " could not be planned: " + LocHelper.Resolve(error));
                    done(false, error);
                    return;
                }
                string pavement = string.IsNullOrWhiteSpace(bridge.PavementMaterial) ? road.Material : bridge.PavementMaterial;
                string mapName = BridgeBuilder.NewMapName();
                Vector3D center = planet.PositionComp.GetPosition();
                _bridges.Build(plan, bridge.StructureMaterial, pavement, (success, message) =>
                {
                    if (!success)
                    {
                        done(false, message);
                        return;
                    }
                    built.Add(new BuiltBridge
                    {
                        Run = runs[run],
                        MapName = mapName,
                        Design = plan.Design,
                        StructureCredits = (long)Math.Ceiling(plan.StructureCubicMeters *
                                                              _quotes.Prices.ConcreteCreditsPerCubicMeter()),
                        StartHeight = Vector3D.Distance(start, center) - planet.AverageRadius,
                        EndHeight = Vector3D.Distance(end, center) - planet.AverageRadius,
                        NodeHeights = Heights(plan.NodeRadii, planet.AverageRadius)
                    });
                    BuildBridges(record, run + 1, built, done);
                }, mapName);
            });
        }

        void OnGameContractFailed(long contractId, long identityId, bool isAbandon)
        {
            foreach (RoadContractRecord record in _store.Contracts)
                if (record.GameContractId == contractId &&
                    (record.State == RoadContractState.Pending || record.State == RoadContractState.AwaitingPayment))
                {
                    Cancel(record, LocHelper.Message(isAbandon ? "HighwayEngineer_Contract_Abandoned" : "HighwayEngineer_Contract_Failed"));
                    return;
                }
        }

        void Cancel(RoadContractRecord record, string message)
        {
            RemoveDeliveryGps(record);
            Refund(record);
            record.State = RoadContractState.Cancelled;
            _store.Save();
            Send(record, 0, LocHelper.Message("HighwayEngineer_Contract_Refunded", message));
        }

        /// <summary>The depot was destroyed while waiting for delivery: the contract fails, without refund.</summary>
        void FailDepotDestroyed(RoadContractRecord record)
        {
            RemoveDeliveryGps(record);
            record.State = RoadContractState.Failed;
            _store.Save();
            // Set Failed first: the game's fail event then finds nothing left to cancel.
            if (record.GameContractId != 0 && MyAPIGateway.ContractSystem != null)
                MyAPIGateway.ContractSystem.TryFailCustomContract(record.GameContractId);
            Send(record, 0, LocHelper.Message("HighwayEngineer_Contract_DepotDestroyed"));
        }

        /// <summary>
        /// Returns the credits paid, except the paid share of a new depot's fee: the depot stays
        /// in the world for later contracts.
        /// </summary>
        static void Refund(RoadContractRecord record)
        {
            if (record.CreditsPaid <= 0) return;
            long kept = record.Credits <= 0 ? 0 : (long)Math.Round(
                (double)record.CreditsPaid * record.InfrastructureCredits / record.Credits);
            long refund = record.CreditsPaid - Math.Min(record.CreditsPaid, kept);
            if (refund > 0)
                MyAPIGateway.Players.RequestChangeBalance(record.OwnerIdentityId, refund);
            record.CreditsPaid = 0;
        }

        static bool TryCharge(ulong steamId, long amount, out string error)
        {
            error = null;
            IMyPlayer player = ServerPlayers.TryGetPlayer(steamId);
            long balance;
            if (player == null || !player.TryGetBalanceInfo(out balance))
            {
                error = LocHelper.Message("HighwayEngineer_Contract_PayOffline");
                return false;
            }
            if (balance < amount)
            {
                error = LocHelper.Message("HighwayEngineer_Contract_PayNotEnough", amount);
                return false;
            }
            player.RequestChangeBalance(-amount);
            return true;
        }

        void Reject(ulong steamId, int requestId, RoadRejectReason reason, string message, float retryAfter)
        {
            _send(steamId, new PacketRoadContractStatus
            {
                RequestId = requestId,
                Reason = reason,
                Message = message,
                RetryAfterSeconds = retryAfter
            });
        }

        void Send(RoadContractRecord record, int requestId, string message)
        {
            _send(record.OwnerSteamId, new PacketRoadContractStatus
            {
                RequestId = requestId,
                Contract = record.ToInfo(),
                Message = message
            });
        }
    }
}

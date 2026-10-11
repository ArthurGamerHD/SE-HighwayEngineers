using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Api;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Server.Helpers;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRageMath;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>A bridge as the server keeps it: which voxel maps are its, for bulldozing.</summary>
    public sealed class BuiltBridgeRecord
    {
        public long BridgeId;
        public long PlanetId;
        public long ContractId;
        /// <summary>Start of its voxel maps' storage names (see <see cref="Structures.BridgeBuilder.NewMapName"/>).</summary>
        public string MapName;
        /// <summary>Road fill on its deck when a repair changed it; null for its contract's.</summary>
        public string PavementMaterial;
    }

    /// <summary>A bridge a contract just built, for <see cref="RoadLibraryService.AddBuilt"/>.</summary>
    public struct BuiltBridge
    {
        public BridgeRun Run;
        public string MapName;
        public BridgeDesign Design;
        /// <summary>The concrete's price, used to share the contract's credits between its parts.</summary>
        public long StructureCredits;
        /// <summary>Deck-top height above the planet's average radius at the bridge's ends.</summary>
        public double StartHeight, EndHeight;
        /// <summary>Deck-top height above the average radius at each node of its run (segments + 1).</summary>
        public double[] NodeHeights;
    }

    /// <summary>
    /// Every road and bridge segment the contracts built, saved with the contracts. Clients get a
    /// copy (a snapshot on request, then the changes), and the intermod API reads it. Each segment
    /// keeps its share of the contract's credits for bulldozing refunds and costs, and a road
    /// segment its voxel API modification and line, so it can be removed exactly.
    /// </summary>
    public sealed class RoadLibraryService : IRoadRouteSource
    {
        readonly RoadContractStore _store;
        readonly Action<PacketRoadLibraryChanged> _broadcast;
        readonly Action<ulong, PacketRoadLibrarySnapshot> _send;
        readonly RateLimiter _snapshotLimiter = new RateLimiter(2, 10.0);
        Dictionary<long, RoadSegmentRecord> _byId;

        public RoadLibraryService(RoadContractStore store, Action<PacketRoadLibraryChanged> broadcast,
            Action<ulong, PacketRoadLibrarySnapshot> send)
        {
            _store = store;
            _broadcast = broadcast;
            _send = send;
        }

        public int Revision => _store.LibraryRevision;
        public bool IsReady => true;

        public IEnumerable<long> PlanetIds
        {
            get
            {
                var planets = new HashSet<long>();
                foreach (RoadSegmentRecord segment in _store.Segments)
                    if (planets.Add(segment.PlanetId))
                        yield return segment.PlanetId;
            }
        }

        public IEnumerable<RoadSegmentRecord> SegmentsOn(long planetId)
        {
            foreach (RoadSegmentRecord segment in _store.Segments)
                if (segment.PlanetId == planetId)
                    yield return segment;
        }

        public RoadSegmentRecord FindSegment(long segmentId)
        {
            if (_byId == null)
            {
                _byId = new Dictionary<long, RoadSegmentRecord>();
                foreach (RoadSegmentRecord segment in _store.Segments)
                    _byId[segment.SegmentId] = segment;
            }
            RoadSegmentRecord found;
            return _byId.TryGetValue(segmentId, out found) ? found : null;
        }

        public BuiltBridgeRecord FindBridge(long bridgeId)
        {
            return _store.Bridges.Find(bridge => bridge.BridgeId == bridgeId);
        }

        /// <summary>Every segment of a bridge, in order along it.</summary>
        public List<RoadSegmentRecord> BridgeSegments(long bridgeId)
        {
            return _store.Segments.FindAll(segment => segment.BridgeId == bridgeId);
        }

        /// <summary>
        /// Records a contract's built segments: its road (line k of <paramref name="modificationId"/>
        /// is its k-th segment built as road; null when the road was not pushed) and the bridges in
        /// <paramref name="bridges"/>; a bridged segment whose bridge is not among them was not
        /// built and is left out. The contract's credits, less a new depot's fee, are shared out:
        /// each bridge its concrete, the road the rest, each by length.
        /// </summary>
        public void AddBuilt(RoadContractRecord record, string modificationId, List<BuiltBridge> bridges)
        {
            var planet = MyAPIGateway.Entities.GetEntityById(record.PlanetId) as MyPlanet;
            if (planet == null || record.Geometry == null) return;
            int count = record.SegmentCount;
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var curves = new RoadCurve[count];
            var values = new double[per];
            var bridgeOf = new int[count];
            for (int i = 0; i < count; i++)
            {
                Array.Copy(record.Geometry, i * per, values, 0, per);
                RoadCurve.TryFromLocalGeometry(planet.WorldMatrix, values, out curves[i]);
                bridgeOf[i] = -1;
            }
            for (int b = 0; b < bridges.Count; b++)
                for (int i = bridges[b].Run.First; i <= bridges[b].Run.Last && i < count; i++)
                    bridgeOf[i] = b;

            // Credits: the bridges' concrete first (scaled down if more than was paid), the road the rest.
            long paid = Math.Max(0, record.CreditsPaid - record.InfrastructureCredits);
            long concrete = 0;
            foreach (BuiltBridge bridge in bridges)
                concrete += Math.Max(0, bridge.StructureCredits);
            double scale = concrete > paid ? paid / (double)concrete : 1.0;
            long roadCredits = Math.Max(0, paid - (long)Math.Round(concrete * scale));
            double roadArc = 0.0;
            var bridgeArcs = new double[bridges.Count];
            for (int i = 0; i < count; i++)
                if (bridgeOf[i] >= 0) bridgeArcs[bridgeOf[i]] += curves[i].Arc;
                else if (!record.IsBridged(i)) roadArc += curves[i].Arc;

            var bridgeIds = new long[bridges.Count];
            for (int b = 0; b < bridges.Count; b++)
            {
                bridgeIds[b] = ++_store.NextBridgeId;
                _store.Bridges.Add(new BuiltBridgeRecord
                {
                    BridgeId = bridgeIds[b],
                    PlanetId = record.PlanetId,
                    ContractId = record.ContractId,
                    MapName = bridges[b].MapName
                });
            }

            double now = PlaySeconds;
            var changed = new PacketRoadLibraryChanged { PlanetId = record.PlanetId };
            int line = 0;
            var along = new double[bridges.Count];
            for (int i = 0; i < count; i++)
            {
                bool road = !record.IsBridged(i);
                if (road ? modificationId == null : bridgeOf[i] < 0) continue;
                var segment = new RoadSegmentRecord
                {
                    SegmentId = ++_store.NextSegmentId,
                    PlanetId = record.PlanetId,
                    Geometry = SegmentGeometry(record.Geometry, i),
                    WidthMeters = record.Parameters == null ? 0.0 : record.Parameters.WidthMeters,
                    BuiltAtPlaySeconds = now,
                    OwnerIdentityId = record.OwnerIdentityId,
                    ContractId = record.ContractId,
                    GuideLines = record.Parameters != null && record.Parameters.GuideLines
                };
                int b = bridgeOf[i];
                if (b < 0)
                {
                    segment.ModificationId = modificationId;
                    segment.LineIndex = line++;
                    segment.CreditsPaid = Share(roadCredits, curves[i].Arc, roadArc);
                }
                else
                {
                    BuiltBridge bridge = bridges[b];
                    double total = Math.Max(1e-12, bridgeArcs[b]);
                    segment.BridgeId = bridgeIds[b];
                    segment.Design = bridge.Design;
                    int node = i - bridge.Run.First;
                    if (bridge.NodeHeights != null && node + 1 < bridge.NodeHeights.Length)
                    {
                        segment.StartHeight = bridge.NodeHeights[node];
                        segment.EndHeight = bridge.NodeHeights[node + 1];
                    }
                    else
                    {
                        segment.StartHeight = bridge.StartHeight + (bridge.EndHeight - bridge.StartHeight) * along[b] / total;
                        segment.EndHeight = bridge.StartHeight +
                                            (bridge.EndHeight - bridge.StartHeight) * (along[b] + curves[i].Arc) / total;
                    }
                    along[b] += curves[i].Arc;
                    segment.CreditsPaid = Share((long)Math.Round(bridge.StructureCredits * scale), curves[i].Arc, total);
                }
                _store.Segments.Add(segment);
                changed.Added.Add(segment);
            }
            _byId = null;
            changed.Revision = ++_store.LibraryRevision;
            _store.Save();
            _broadcast(changed);
        }

        static long Share(long credits, double arc, double total)
        {
            return total > 0.0 ? (long)Math.Round(credits * arc / total) : 0;
        }

        static double[] SegmentGeometry(double[] geometry, int index)
        {
            int per = Constants.GEOMETRY_VALUES_PER_SEGMENT;
            var values = new double[per];
            Array.Copy(geometry, index * per, values, 0, per);
            return values;
        }

        /// <summary>The identity that accepted a contract; 0 when it is unknown.</summary>
        public long ContractOwner(long contractId)
        {
            RoadContractRecord record = FindContract(contractId);
            return record == null ? 0 : record.OwnerIdentityId;
        }

        /// <summary>The contract that built a segment, with its road and bridge settings; null when it is gone.</summary>
        public RoadContractRecord FindContract(long contractId)
        {
            return _store.Contracts.Find(contract => contract.ContractId == contractId);
        }

        /// <summary>
        /// Records road lines a repair redrew: every line in <paramref name="removed"/> (as listed
        /// before) left its modification, and the library segments in <paramref name="moved"/> are
        /// now the lines given. The other segments' line indices follow the removals.
        /// </summary>
        public void ReplaceLines(long planetId, ICollection<RoadLineRef> removed,
            Dictionary<RoadSegmentRecord, RoadLineRef> moved)
        {
            ShiftLineIndices(removed, moved.Keys);
            var changed = new PacketRoadLibraryChanged { PlanetId = planetId };
            foreach (KeyValuePair<RoadSegmentRecord, RoadLineRef> pair in moved)
            {
                pair.Key.ModificationId = pair.Value.ModificationId;
                pair.Key.LineIndex = pair.Value.LineIndex;
                changed.Added.Add(pair.Key);
            }
            if (changed.Added.Count > 0)
            {
                changed.Revision = ++_store.LibraryRevision;
                _broadcast(changed);
            }
            _store.Save();
        }

        /// <summary>Marks segments (one planet's) to have their guide lines painted, and tells every client.</summary>
        public void PaintGuideLines(long planetId, ICollection<RoadSegmentRecord> segments)
        {
            var changed = new PacketRoadLibraryChanged { PlanetId = planetId };
            foreach (RoadSegmentRecord segment in segments)
            {
                if (segment.GuideLines) continue;
                segment.GuideLines = true;
                changed.Added.Add(segment);
            }
            if (changed.Added.Count == 0) return;
            changed.Revision = ++_store.LibraryRevision;
            _store.Save();
            _broadcast(changed);
        }

        /// <summary>Records a bridge rebuilt by a repair: its new voxel maps, and the road fill on its deck.</summary>
        public void RebuiltBridge(long bridgeId, string mapName, string pavementMaterial)
        {
            BuiltBridgeRecord bridge = FindBridge(bridgeId);
            if (bridge == null) return;
            bridge.MapName = mapName;
            if (pavementMaterial != null) bridge.PavementMaterial = pavementMaterial;
            _store.Save();
        }

        /// <summary>
        /// Removing a voxel API line moves the later lines of its modification up: the line
        /// indices of the library's segments (other than <paramref name="skipped"/>) follow.
        /// </summary>
        void ShiftLineIndices(ICollection<RoadLineRef> removed, ICollection<RoadSegmentRecord> skipped)
        {
            var skip = new HashSet<RoadSegmentRecord>(skipped);
            foreach (RoadSegmentRecord segment in _store.Segments)
            {
                if (skip.Contains(segment) || segment.ModificationId == null) continue;
                int before = 0;
                foreach (RoadLineRef line in removed)
                    if (line.ModificationId == segment.ModificationId && line.LineIndex < segment.LineIndex)
                        before++;
                segment.LineIndex -= before;
            }
        }

        /// <summary>
        /// Forgets bulldozed segments (one planet's) and their bridges. Removing a voxel API line
        /// moves the later lines of its modification up, so the line indices of the segments kept
        /// follow, for the library's removed lines and the other <paramref name="lines"/> alike.
        /// </summary>
        public void Remove(long planetId, ICollection<RoadSegmentRecord> removed, ICollection<RoadLineRef> lines)
        {
            var gone = new HashSet<RoadSegmentRecord>(removed);
            var removedLines = new List<RoadLineRef>(lines);
            foreach (RoadSegmentRecord segment in removed)
                if (segment.ModificationId != null)
                    removedLines.Add(new RoadLineRef { ModificationId = segment.ModificationId, LineIndex = segment.LineIndex });
            ShiftLineIndices(removedLines, removed);
            if (removed.Count == 0)
            {
                // Only lines the library does not hold: its segments' indices may still have moved.
                if (lines.Count > 0) _store.Save();
                return;
            }
            _store.Segments.RemoveAll(gone.Contains);
            _store.Bridges.RemoveAll(bridge => !_store.Segments.Exists(segment => segment.BridgeId == bridge.BridgeId));
            _byId = null;
            var ids = new List<long>();
            foreach (RoadSegmentRecord segment in removed)
                ids.Add(segment.SegmentId);
            var changed = new PacketRoadLibraryChanged
            {
                PlanetId = planetId,
                Revision = ++_store.LibraryRevision,
                RemovedSegmentIds = ids.ToArray()
            };
            _store.Save();
            _broadcast(changed);
        }

        /// <summary>Sends a player the segments on a planet (every planet for 0); rate limited.</summary>
        public void Snapshot(ulong steamId, PacketRoadLibraryRequest request)
        {
            if (request == null) return;
            var reply = new PacketRoadLibrarySnapshot
            {
                RequestId = request.RequestId,
                PlanetId = request.PlanetId,
                Revision = _store.LibraryRevision
            };
            float retryAfter;
            if (!ServerPlayers.BypassesLimits(steamId) && !_snapshotLimiter.TryTake(steamId, out retryAfter))
                reply.RetryAfterSeconds = Math.Max(0.1f, retryAfter);
            else
                foreach (RoadSegmentRecord segment in _store.Segments)
                    if (request.PlanetId == 0 || segment.PlanetId == request.PlanetId)
                        reply.Segments.Add(segment);
            _send(steamId, reply);
        }

        public void ForgetPlayer(ulong steamId)
        {
            _snapshotLimiter.Forget(steamId);
        }

        /// <summary>Session play time, which bulldozing's refund window is measured in.</summary>
        public static double PlaySeconds => MyAPIGateway.Session.ElapsedPlayTime.TotalSeconds;
    }
}

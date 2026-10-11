using System;
using System.Collections.Generic;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Common.Structures;
using HighwayEngineer.Server.Helpers;
using HighwayEngineer.Server.Quotes;
using HighwayEngineer.Server.Structures;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRageMath;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// Repairs built roads and bridges the player marked, checked first like a bulldoze (the
    /// player's own, any for admins and the host):
    /// <list type="number">
    /// <item>New material: each road line is drawn again through the voxel API exactly as the API
    /// lists it, with the new material, in place of the old one. Lines go in one push per owner
    /// tag, so each keeps its contract's tag.</item>
    /// <item>Holes: the planet's voxel edits along the roads are dropped (<see cref="RoadFootprint"/>),
    /// here and on every client (<see cref="PacketVoxelRepair"/>).</item>
    /// <item>Bridges are rebuilt from their library heights and design, paved with the new material
    /// if one was chosen, and then their old voxel maps removed.</item>
    /// </list>
    /// One repair runs at a time.
    /// TODO: price repairs (material delivery like a contract?); they are free for now.
    /// </summary>
    public sealed class RoadRepairService
    {
        const double QuoteLifetimeSeconds = 60.0;
        const int MaxSegmentsPerQuote = 512;
        // Blocks per PacketVoxelRepair, to keep each packet small.
        const int BlocksPerPacket = 2000;

        sealed class IssuedRepair
        {
            public long QuoteId;
            public ulong SteamId;
            public long PlanetId;
            public List<long> SegmentIds;
            public List<RoadLineRef> Lines;
            public string Material;
            public bool RepairHoles;
            public bool RepaintGuideLines;
            public long ExpiresUtcTicks;
        }

        /// <summary>A road line to repair, as the voxel API lists it.</summary>
        sealed class RepairLine
        {
            public RoadLineRef Ref;
            public RuntimeProceduralLineOperation Listed;
            public string CustomData;
            /// <summary>The library segment it is; null for a line built some other way.</summary>
            public RoadSegmentRecord Segment;
        }

        sealed class RepairPlan
        {
            public readonly List<RepairLine> Lines = new List<RepairLine>();
            public readonly List<long> BridgeIds = new List<long>();
        }

        readonly RoadLibraryService _library;
        readonly RoadSelection _selection;
        readonly VoxelRepairQueue _voxels;
        readonly Action<ulong, PacketRepairQuoteResponse> _sendQuote;
        readonly Action<ulong, PacketRepairStatus> _sendStatus;
        readonly Action<PacketVoxelRepair> _broadcastVoxels;
        readonly RateLimiter _quoteLimiter = new RateLimiter(3, 4.0);
        readonly RateLimiter _requestLimiter = new RateLimiter(2, 10.0);
        readonly Dictionary<long, IssuedRepair> _issued = new Dictionary<long, IssuedRepair>();
        readonly BridgeBuilder _bridges = new BridgeBuilder();
        long _nextQuoteId;
        bool _busy;

        public RoadRepairService(RoadLibraryService library, VoxelRepairQueue voxels,
            Action<ulong, PacketRepairQuoteResponse> sendQuote, Action<ulong, PacketRepairStatus> sendStatus,
            Action<PacketVoxelRepair> broadcastVoxels)
        {
            _library = library;
            _selection = new RoadSelection(library);
            _voxels = voxels;
            _sendQuote = sendQuote;
            _sendStatus = sendStatus;
            _broadcastVoxels = broadcastVoxels;
        }

        public void Quote(ulong steamId, PacketRepairQuoteRequest request)
        {
            if (request == null) return;
            var reply = new PacketRepairQuoteResponse { RequestId = request.RequestId };
            float retryAfter;
            if (!ServerPlayers.BypassesLimits(steamId) && !_quoteLimiter.TryTake(steamId, out retryAfter))
            {
                reply.Reason = RoadRejectReason.RateLimited;
                reply.RetryAfterSeconds = retryAfter;
                _sendQuote(steamId, reply);
                return;
            }
            string material = string.IsNullOrWhiteSpace(request.Material) ? null : request.Material.Trim();
            List<RoadLineRef> asked = request.Lines ?? new List<RoadLineRef>();
            long[] ids = request.SegmentIds ?? new long[0];
            RoadRejectReason reason = RoadRejectReason.InvalidGeometry;
            string error = null;
            List<RoadSegmentRecord> segments = null;
            if (ids.Length + asked.Count == 0)
                error = LocHelper.Message("HighwayEngineer_Selection_NoneMarked");
            else if (ids.Length + asked.Count > MaxSegmentsPerQuote)
            {
                reason = RoadRejectReason.OutOfLimits;
                error = LocHelper.Message("HighwayEngineer_Repair_TooMany", MaxSegmentsPerQuote);
            }
            else if (material == null && !request.RepairHoles && !request.RepaintGuideLines)
                error = LocHelper.Message("HighwayEngineer_Repair_NothingChosen");
            else if (material == null || CheckMaterial(steamId, ref material, out reason, out error))
                segments = _selection.Resolve(steamId, request.PlanetId, ids, "repair", out reason, out error);
            if (segments == null)
            {
                reply.Reason = reason;
                reply.Message = error;
                _sendQuote(steamId, reply);
                return;
            }
            Gather(steamId, request.PlanetId, segments, asked, (plan, planReason, planError) =>
            {
                if (plan == null)
                {
                    reply.Reason = planReason;
                    reply.Message = planError;
                    _sendQuote(steamId, reply);
                    return;
                }
                RemoveIssued(steamId);
                var quote = new IssuedRepair
                {
                    QuoteId = ++_nextQuoteId,
                    SteamId = steamId,
                    PlanetId = request.PlanetId,
                    SegmentIds = segments.ConvertAll(segment => segment.SegmentId),
                    Lines = asked,
                    Material = material,
                    RepairHoles = request.RepairHoles,
                    RepaintGuideLines = request.RepaintGuideLines,
                    ExpiresUtcTicks = DateTime.UtcNow.Ticks + (long)(QuoteLifetimeSeconds * TimeSpan.TicksPerSecond)
                };
                _issued[quote.QuoteId] = quote;
                reply.QuoteId = quote.QuoteId;
                reply.ExpiresSeconds = (float)QuoteLifetimeSeconds;
                reply.Message = Describe(plan, material, request.RepairHoles, request.RepaintGuideLines);
                _sendQuote(steamId, reply);
            });
        }

        public void Request(ulong steamId, PacketRepairRequest request)
        {
            if (request == null) return;
            var status = new PacketRepairStatus { RequestId = request.RequestId, State = RoadJobState.Failed };
            float retryAfter;
            IssuedRepair quote;
            if (!ServerPlayers.BypassesLimits(steamId) && !_requestLimiter.TryTake(steamId, out retryAfter))
            {
                status.Reason = RoadRejectReason.RateLimited;
                status.Message = LocHelper.Message("HighwayEngineer_Repair_Wait", Math.Ceiling(retryAfter));
                _sendStatus(steamId, status);
                return;
            }
            if (!_issued.TryGetValue(request.QuoteId, out quote) || quote.SteamId != steamId)
            {
                status.Reason = RoadRejectReason.QuoteExpired;
                status.Message = LocHelper.Message("HighwayEngineer_Repair_UnknownQuote");
                _sendStatus(steamId, status);
                return;
            }
            _issued.Remove(quote.QuoteId);
            if (quote.ExpiresUtcTicks < DateTime.UtcNow.Ticks)
            {
                status.Reason = RoadRejectReason.QuoteExpired;
                status.Message = LocHelper.Message("HighwayEngineer_Repair_Expired");
                _sendStatus(steamId, status);
                return;
            }
            if (_busy)
            {
                status.Reason = RoadRejectReason.ServerBusy;
                status.Message = LocHelper.Message("HighwayEngineer_Repair_Busy");
                _sendStatus(steamId, status);
                return;
            }
            // The roads may have gone (or changed hands) since the quote.
            string error = null;
            RoadRejectReason reason = RoadRejectReason.None;
            List<RoadSegmentRecord> segments = quote.SegmentIds.Count == 0
                ? new List<RoadSegmentRecord>()
                : _selection.Resolve(steamId, quote.PlanetId, quote.SegmentIds.ToArray(), "repair", out reason, out error);
            if (segments == null || segments.Count != quote.SegmentIds.Count)
            {
                status.Reason = segments == null ? reason : RoadRejectReason.QuoteExpired;
                status.Message = error ?? LocHelper.Message("HighwayEngineer_Selection_MarksChanged");
                _sendStatus(steamId, status);
                return;
            }
            _busy = true;
            Gather(steamId, quote.PlanetId, segments, quote.Lines, (plan, planReason, planError) =>
            {
                if (plan == null)
                {
                    _busy = false;
                    status.Reason = planReason;
                    status.Message = planError;
                    _sendStatus(steamId, status);
                    return;
                }
                _sendStatus(steamId, new PacketRepairStatus
                {
                    RequestId = request.RequestId,
                    State = RoadJobState.Running,
                    Message = LocHelper.Message("HighwayEngineer_Repair_Running")
                });
                Run(quote, plan, (success, message) =>
                {
                    _busy = false;
                    if (success && quote.RepaintGuideLines)
                        _library.PaintGuideLines(quote.PlanetId, segments);
                    _sendStatus(steamId, new PacketRepairStatus
                    {
                        RequestId = request.RequestId,
                        State = success ? RoadJobState.Completed : RoadJobState.Failed,
                        Reason = success ? RoadRejectReason.None : RoadRejectReason.PushFailed,
                        Message = message
                    });
                });
            });
        }

        public void ForgetPlayer(ulong steamId)
        {
            _quoteLimiter.Forget(steamId);
            _requestLimiter.Forget(steamId);
            RemoveIssued(steamId);
        }

        void RemoveIssued(ulong steamId)
        {
            var mine = new List<long>();
            foreach (KeyValuePair<long, IssuedRepair> pair in _issued)
                if (pair.Value.SteamId == steamId || pair.Value.ExpiresUtcTicks < DateTime.UtcNow.Ticks)
                    mine.Add(pair.Key);
            foreach (long id in mine)
                _issued.Remove(id);
        }

        /// <summary>
        /// The material must exist, and outside creative yield stone like a road contract's.
        /// <paramref name="material"/> becomes its exact subtype.
        /// </summary>
        static bool CheckMaterial(ulong steamId, ref string material, out RoadRejectReason reason, out string error)
        {
            reason = RoadRejectReason.MaterialNotAllowed;
            error = null;
            MyVoxelMaterialDefinition found = null;
            foreach (MyVoxelMaterialDefinition definition in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                if (definition != null && string.Equals(definition.Id.SubtypeName, material, StringComparison.OrdinalIgnoreCase))
                    found = definition;
            if (found == null)
            {
                error = LocHelper.Message("HighwayEngineer_Error_UnknownMaterial", VoxelMaterialNames.Message(material));
                return false;
            }
            if (!ServerPlayers.IsCostExempt(steamId) &&
                !string.Equals(found.MinedOre, "Stone", StringComparison.OrdinalIgnoreCase))
            {
                error = found.Id.SubtypeName + " does not yield stone; it can only be used in creative.";
                return false;
            }
            material = found.Id.SubtypeName;
            reason = RoadRejectReason.None;
            return true;
        }

        /// <summary>
        /// Every road line to repair as the voxel API lists it (the library's road segments and the
        /// other lines, each once) and the bridges; null with the reason when one cannot be.
        /// <paramref name="done"/> runs on the game thread.
        /// </summary>
        void Gather(ulong steamId, long planetId, List<RoadSegmentRecord> segments, List<RoadLineRef> asked,
            Action<RepairPlan, RoadRejectReason, string> done)
        {
            _selection.VerifyLines(steamId, planetId, asked, "repair", (lines, listing, reason, error) =>
            {
                if (lines == null)
                {
                    done(null, reason, error);
                    return;
                }
                bool roads = lines.Count > 0 || segments.Exists(segment => !segment.IsBridge);
                if (listing != null || !roads)
                {
                    done(Plan(segments, lines, listing, out reason, out error), reason, error);
                    return;
                }
                RoadSelection.ReadListing(planetId, (read, readReason, readError) =>
                {
                    if (read == null)
                    {
                        done(null, readReason, readError);
                        return;
                    }
                    done(Plan(segments, lines, read, out reason, out error), reason, error);
                });
            });
        }

        static RepairPlan Plan(List<RoadSegmentRecord> segments, List<RoadLineRef> lines,
            PlanetModificationListing listing, out RoadRejectReason reason, out string error)
        {
            reason = RoadRejectReason.InvalidGeometry;
            error = null;
            var plan = new RepairPlan();
            var seen = new HashSet<string>();
            foreach (RoadSegmentRecord segment in segments)
            {
                if (segment.IsBridge)
                {
                    if (!plan.BridgeIds.Contains(segment.BridgeId)) plan.BridgeIds.Add(segment.BridgeId);
                    continue;
                }
                if (segment.ModificationId == null)
                {
                    error = LocHelper.Message("HighwayEngineer_Repair_NotListedYet");
                    return null;
                }
                var line = new RoadLineRef
                {
                    ModificationId = segment.ModificationId,
                    LineIndex = segment.LineIndex,
                    Geometry = segment.Geometry
                };
                if (!Add(plan, listing, line, segment, seen, out error)) return null;
            }
            foreach (RoadLineRef line in lines)
                if (!Add(plan, listing, line, null, seen, out error)) return null;
            reason = RoadRejectReason.None;
            return plan;
        }

        static bool Add(RepairPlan plan, PlanetModificationListing listing, RoadLineRef line, RoadSegmentRecord segment,
            HashSet<string> seen, out string error)
        {
            error = null;
            if (!seen.Add(line.ModificationId + "#" + line.LineIndex)) return true;
            PlanetModificationInfo modification;
            RuntimeProceduralLineOperation listed = RoadSelection.FindLine(listing, line, out modification, out error);
            if (listed == null) return false;
            plan.Lines.Add(new RepairLine
            {
                Ref = line,
                Listed = listed,
                CustomData = modification.CustomData,
                Segment = segment
            });
            return true;
        }

        static string Describe(RepairPlan plan, string material, bool holes, bool guideLines)
        {
            var parts = new List<string>();
            if (plan.Lines.Count > 0)
                parts.Add(LocHelper.Message(plan.Lines.Count == 1
                    ? "HighwayEngineer_Repair_Desc_Segments_One"
                    : "HighwayEngineer_Repair_Desc_Segments_Many", plan.Lines.Count));
            if (plan.BridgeIds.Count > 0)
                parts.Add(LocHelper.Message(plan.BridgeIds.Count == 1
                    ? "HighwayEngineer_Repair_Desc_Bridges_One"
                    : "HighwayEngineer_Repair_Desc_Bridges_Many", plan.BridgeIds.Count));
            var work = new List<string>();
            if (material != null) work.Add(LocHelper.Message("HighwayEngineer_Repair_Desc_Paved", VoxelMaterialNames.Message(material)));
            if (holes) work.Add(LocHelper.Message("HighwayEngineer_Repair_Desc_Holes"));
            if (guideLines) work.Add(LocHelper.Message("HighwayEngineer_Repair_Desc_GuideLines"));
            return LocHelper.Message("HighwayEngineer_Repair_Desc", LocHelper.MessageJoinAnd(parts), LocHelper.MessageJoinAnd(work));
        }

        /// <summary>The material first (it changes the planet's storage), then the holes, then the bridges.</summary>
        void Run(IssuedRepair quote, RepairPlan plan, Action<bool, string> done)
        {
            var planet = MyAPIGateway.Entities.GetEntityById(quote.PlanetId) as MyPlanet;
            if (planet == null)
            {
                done(false, LocHelper.Message("HighwayEngineer_Error_PlanetGone"));
                return;
            }
            Action bridges = () => RebuildBridges(planet, plan.BridgeIds, 0, quote.Material, (success, message) =>
                done(success, success ? LocHelper.Message("HighwayEngineer_Repair_Done", Describe(plan, quote.Material, quote.RepairHoles, quote.RepaintGuideLines)) : message));
            Action holes = () =>
            {
                if (!quote.RepairHoles || plan.Lines.Count == 0)
                {
                    bridges();
                    return;
                }
                RepairHoles(planet, plan.Lines, bridges);
            };
            if (quote.Material == null || plan.Lines.Count == 0)
            {
                holes();
                return;
            }
            var groups = new Dictionary<string, List<RepairLine>>();
            foreach (RepairLine line in plan.Lines)
            {
                string key = line.CustomData ?? string.Empty;
                List<RepairLine> group;
                if (!groups.TryGetValue(key, out group)) groups[key] = group = new List<RepairLine>();
                group.Add(line);
            }
            Repave(planet, new List<List<RepairLine>>(groups.Values), 0, quote.Material, (success, message) =>
            {
                if (success) holes();
                else done(false, message);
            });
        }

        /// <summary>
        /// Draws one owner tag's lines again with <paramref name="material"/> in place of the old
        /// ones, in one voxel API push, then the next group's.
        /// </summary>
        void Repave(MyPlanet planet, List<List<RepairLine>> groups, int index, string material, Action<bool, string> done)
        {
            if (index >= groups.Count)
            {
                done(true, null);
                return;
            }
            List<RepairLine> lines = groups[index];
            ModificationTemplate template = null;
            try
            {
                ApiProvider api;
                string error;
                if (!RoadApi.TryValidate(out api, out error))
                {
                    done(false, error);
                    return;
                }
                template = api.GetModificationTemplate(planet.EntityId);
                if (template == null)
                {
                    done(false, LocHelper.Message("HighwayEngineer_Error_NoTemplate"));
                    return;
                }
                var materials = new Dictionary<double, byte>();
                var removed = new List<RoadLineRef>();
                var moved = new Dictionary<RoadSegmentRecord, RoadLineRef>();
                foreach (RepairLine line in lines)
                {
                    template.RemoveModificationLine(line.Ref.ModificationId, line.Ref.LineIndex);
                    removed.Add(line.Ref);
                }
                string modificationId = template.GetModificationId();
                for (int i = 0; i < lines.Count; i++)
                {
                    RuntimeProceduralLineOperation listed = lines[i].Listed;
                    // The new material fills as deep as the old one did.
                    double depth = RoadConstruction.MaterialDepth(template, listed.Material, RoadLimits.MinDepthMeters);
                    byte value;
                    if (!materials.TryGetValue(depth, out value))
                        materials[depth] = value = RoadConstruction.FindOrAddMaterial(template, material, depth);
                    template.DrawLineSmooth("material", value, (double[])listed.Geometry.Clone(),
                        listed.RadiusMeters > 0 ? listed.RadiusMeters : planet.AverageRadius, listed.WidthMeters,
                        listed.AverageHeight, listed.FillAngleDegrees, listed.MaximumSlopeDegrees,
                        listed.SegmentDistanceMeters);
                    if (lines[i].Segment != null)
                        moved[lines[i].Segment] = new RoadLineRef { ModificationId = modificationId, LineIndex = i };
                }
                if (!string.IsNullOrEmpty(lines[0].CustomData)) template.SetCustomData(lines[0].CustomData);
                ModificationTemplate pending = template;
                template = null;
                pending.Push((success, message) =>
                {
                    try { pending.Close(); }
                    catch (Exception) { }
                    if (!success)
                    {
                        done(false, LocHelper.Message("HighwayEngineer_Repair_ApiFailed", message));
                        return;
                    }
                    _library.ReplaceLines(planet.EntityId, removed, moved);
                    Repave(planet, groups, index + 1, material, done);
                });
            }
            catch (Exception error)
            {
                done(false, error.Message);
            }
            finally
            {
                if (template != null)
                    template.Close();
            }
        }

        /// <summary>Drops the voxel edits along the lines (see <see cref="ResetVoxelEdits"/>).</summary>
        void RepairHoles(MyPlanet planet, List<RepairLine> lines, Action done)
        {
            var curves = new List<RoadCurve>();
            foreach (RepairLine line in lines)
            {
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(planet.WorldMatrix, line.Listed.Geometry, out curve)) continue;
                curve.WidthMeters = line.Listed.WidthMeters;
                curves.Add(curve);
            }
            ResetVoxelEdits(planet, curves, done);
        }

        /// <summary>
        /// The only change made to a planet's voxels: its edits (holes dug, dirt piled) along the
        /// curves are dropped, so the terrain falls back to the voxel API's, roads included. Finds the
        /// storage blocks on a worker thread, then drops the edits here and on every client.
        /// </summary>
        public void ResetVoxelEdits(MyPlanet planet, List<RoadCurve> curves, Action done)
        {
            int[] blocks = null;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try { blocks = RoadFootprint.Blocks(planet, curves); }
                catch (Exception) { blocks = null; }
            }, () =>
            {
                if (blocks != null && blocks.Length > 0)
                {
                    _voxels.Enqueue(planet.EntityId, blocks, RoadFootprint.BlockSize);
                    int per = BlocksPerPacket * 3;
                    for (int offset = 0; offset < blocks.Length; offset += per)
                    {
                        var part = new int[Math.Min(per, blocks.Length - offset)];
                        Array.Copy(blocks, offset, part, 0, part.Length);
                        _broadcastVoxels(new PacketVoxelRepair
                        {
                            PlanetId = planet.EntityId,
                            Blocks = part,
                            BlockSize = RoadFootprint.BlockSize
                        });
                    }
                }
                done();
            });
        }

        /// <summary>
        /// Plans each bridge again from its library deck heights and design, builds it, then
        /// removes its old voxel maps; one after the other from <paramref name="index"/>.
        /// </summary>
        void RebuildBridges(MyPlanet planet, List<long> bridgeIds, int index, string material, Action<bool, string> done)
        {
            if (index >= bridgeIds.Count)
            {
                done(true, null);
                return;
            }
            long bridgeId = bridgeIds[index];
            BuiltBridgeRecord bridge = _library.FindBridge(bridgeId);
            RoadContractRecord contract = bridge == null ? null : _library.FindContract(bridge.ContractId);
            List<RoadSegmentRecord> segments = _library.BridgeSegments(bridgeId);
            if (bridge == null || contract == null || contract.Parameters == null || contract.Bridge == null ||
                segments.Count == 0)
            {
                done(false, LocHelper.Message("HighwayEngineer_Repair_BridgeNoRecord"));
                return;
            }
            var curves = new List<RoadCurve>();
            foreach (RoadSegmentRecord segment in segments)
            {
                RoadCurve curve;
                if (!RoadCurve.TryFromLocalGeometry(planet.WorldMatrix, segment.Geometry, out curve))
                {
                    done(false, LocHelper.Message("HighwayEngineer_Repair_BridgeUnreadable"));
                    return;
                }
                curves.Add(curve);
            }
            Vector3D center = planet.PositionComp.GetPosition();
            Vector3D start = center + curves[0].Start * (planet.AverageRadius + segments[0].StartHeight);
            Vector3D end = center + curves[curves.Count - 1].End *
                           (planet.AverageRadius + segments[segments.Count - 1].EndHeight);
            // Every node keeps the deck height it was built with.
            var pins = new double[segments.Count + 1];
            for (int i = 0; i < segments.Count; i++)
                pins[i] = planet.AverageRadius + segments[i].StartHeight;
            pins[segments.Count] = planet.AverageRadius + segments[segments.Count - 1].EndHeight;
            BridgeOptions options = RoadQuoteJob.BridgeOptionsFor(contract.Parameters, contract.Bridge, segments[0].Design);
            // A repair rebuilds what was built: only the general limit, so bridges from before the
            // road's slope applied still rebuild.
            options.MaxGradeDegrees = 0.0;
            BridgePlan plan = null;
            string error = null;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try { plan = BridgePlanner.Plan(planet, curves, start, end, pins, options, out error); }
                catch (Exception e) { error = e.Message; }
            }, () =>
            {
                if (plan == null)
                {
                    done(false, LocHelper.Message("HighwayEngineer_Repair_BridgeReplanFailed", error));
                    return;
                }
                string pavement = material ?? bridge.PavementMaterial ??
                                  (string.IsNullOrWhiteSpace(contract.Bridge.PavementMaterial)
                                      ? contract.Parameters.Material
                                      : contract.Bridge.PavementMaterial);
                string mapName = BridgeBuilder.NewMapName();
                string oldMapName = bridge.MapName;
                _bridges.Build(plan, contract.Bridge.StructureMaterial, pavement, (success, message) =>
                {
                    if (!success)
                    {
                        done(false, message);
                        return;
                    }
                    BridgeBuilder.Remove(oldMapName);
                    _library.RebuiltBridge(bridgeId, mapName, material);
                    RebuildBridges(planet, bridgeIds, index + 1, material, done);
                }, mapName);
            });
        }
    }
}

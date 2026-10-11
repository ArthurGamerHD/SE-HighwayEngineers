using System;
using System.Collections.Generic;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Common.Config;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Server.Helpers;
using HighwayEngineer.Server.Structures;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// Removes built road and bridge segments from the road library, priced first:
    /// <list type="number">
    /// <item>Quote: the segments must be the player's (admins and the host may remove any); a
    /// bridge goes whole. Within the refund window after building, each refunds a share of its
    /// credits; after it, removing costs a share. Creative worlds pay and get nothing. Road lines
    /// of the voxel API that are not in the library (built some other way) can be removed too,
    /// for nothing: each is checked against the API's own listing, and is the player's when its
    /// modification carries one of their contracts' tags, else only an admin's or the host's.</item>
    /// <item>Request: the quote's segments are removed (road lines through the voxel API in one
    /// push, then the bridges' voxel maps), and only then are the credits settled. One bulldoze
    /// runs at a time.</item>
    /// </list>
    /// </summary>
    public sealed class RoadBulldozeService
    {
        const double QuoteLifetimeSeconds = 60.0;
        const int MaxSegmentsPerQuote = 512;

        sealed class IssuedBulldoze
        {
            public long QuoteId;
            public ulong SteamId;
            public long PlanetId;
            public List<long> SegmentIds;
            public List<RoadLineRef> Lines;
            public long CreditDelta;
            public bool Exempt;
            public long ExpiresUtcTicks;
        }

        readonly RoadLibraryService _library;
        readonly RoadSelection _selection;
        readonly Func<RoadCostSettings> _prices;
        readonly Action<ulong, PacketBulldozeQuoteResponse> _sendQuote;
        readonly Action<ulong, PacketBulldozeStatus> _sendStatus;
        readonly RateLimiter _quoteLimiter = new RateLimiter(3, 4.0);
        readonly RateLimiter _requestLimiter = new RateLimiter(2, 10.0);
        readonly Dictionary<long, IssuedBulldoze> _issued = new Dictionary<long, IssuedBulldoze>();
        long _nextQuoteId;
        bool _busy;

        public RoadBulldozeService(RoadLibraryService library, Func<RoadCostSettings> prices,
            Action<ulong, PacketBulldozeQuoteResponse> sendQuote, Action<ulong, PacketBulldozeStatus> sendStatus)
        {
            _library = library;
            _selection = new RoadSelection(library);
            _prices = prices;
            _sendQuote = sendQuote;
            _sendStatus = sendStatus;
        }

        public void Quote(ulong steamId, PacketBulldozeQuoteRequest request)
        {
            if (request == null) return;
            var reply = new PacketBulldozeQuoteResponse { RequestId = request.RequestId };
            float retryAfter;
            if (!ServerPlayers.BypassesLimits(steamId) && !_quoteLimiter.TryTake(steamId, out retryAfter))
            {
                reply.Reason = RoadRejectReason.RateLimited;
                reply.RetryAfterSeconds = retryAfter;
                _sendQuote(steamId, reply);
                return;
            }
            string error;
            RoadRejectReason reason;
            List<RoadLineRef> asked = request.Lines ?? new List<RoadLineRef>();
            long[] ids = request.SegmentIds ?? new long[0];
            List<RoadSegmentRecord> segments = null;
            if (ids.Length + asked.Count == 0)
            {
                reason = RoadRejectReason.InvalidGeometry;
                error = LocHelper.Message("HighwayEngineer_Selection_NoneMarked");
            }
            else if (ids.Length + asked.Count > MaxSegmentsPerQuote)
            {
                reason = RoadRejectReason.OutOfLimits;
                error = LocHelper.Message("HighwayEngineer_Bulldoze_TooMany", MaxSegmentsPerQuote);
            }
            else
                segments = _selection.Resolve(steamId, request.PlanetId, ids, "bulldoze", out reason, out error);
            if (segments == null)
            {
                reply.Reason = reason;
                reply.Message = error;
                _sendQuote(steamId, reply);
                return;
            }
            _selection.VerifyLines(steamId, request.PlanetId, asked, "bulldoze", (lines, listing, lineReason, lineError) =>
            {
                if (lines == null)
                {
                    reply.Reason = lineReason;
                    reply.Message = lineError;
                    _sendQuote(steamId, reply);
                    return;
                }
                IssueQuote(steamId, request.PlanetId, segments, lines, reply);
            });
        }

        void IssueQuote(ulong steamId, long planetId, List<RoadSegmentRecord> segments, List<RoadLineRef> lines,
            PacketBulldozeQuoteResponse reply)
        {
            bool exempt = ServerPlayers.IsCostExempt(steamId);
            long delta = exempt ? 0 : CreditDelta(segments, _prices());
            RemoveIssued(steamId);
            var quote = new IssuedBulldoze
            {
                QuoteId = ++_nextQuoteId,
                SteamId = steamId,
                PlanetId = planetId,
                SegmentIds = segments.ConvertAll(segment => segment.SegmentId),
                Lines = lines,
                CreditDelta = delta,
                Exempt = exempt,
                ExpiresUtcTicks = DateTime.UtcNow.Ticks + (long)(QuoteLifetimeSeconds * TimeSpan.TicksPerSecond)
            };
            _issued[quote.QuoteId] = quote;
            reply.QuoteId = quote.QuoteId;
            reply.CreditDelta = delta;
            reply.Exempt = exempt;
            reply.ExpiresSeconds = (float)QuoteLifetimeSeconds;
            reply.Message = Describe(segments, lines.Count);
            _sendQuote(steamId, reply);
        }

        public void Request(ulong steamId, PacketBulldozeRequest request)
        {
            if (request == null) return;
            var status = new PacketBulldozeStatus { RequestId = request.RequestId, State = RoadJobState.Failed };
            float retryAfter;
            IssuedBulldoze quote;
            if (!ServerPlayers.BypassesLimits(steamId) && !_requestLimiter.TryTake(steamId, out retryAfter))
            {
                status.Reason = RoadRejectReason.RateLimited;
                status.Message = LocHelper.Message("HighwayEngineer_Bulldoze_Wait", Math.Ceiling(retryAfter));
                _sendStatus(steamId, status);
                return;
            }
            if (!_issued.TryGetValue(request.QuoteId, out quote) || quote.SteamId != steamId)
            {
                status.Reason = RoadRejectReason.QuoteExpired;
                status.Message = LocHelper.Message("HighwayEngineer_Bulldoze_UnknownQuote");
                _sendStatus(steamId, status);
                return;
            }
            _issued.Remove(quote.QuoteId);
            if (quote.ExpiresUtcTicks < DateTime.UtcNow.Ticks)
            {
                status.Reason = RoadRejectReason.QuoteExpired;
                status.Message = LocHelper.Message("HighwayEngineer_Bulldoze_Expired");
                _sendStatus(steamId, status);
                return;
            }
            if (_busy)
            {
                status.Reason = RoadRejectReason.ServerBusy;
                status.Message = LocHelper.Message("HighwayEngineer_Bulldoze_Busy");
                _sendStatus(steamId, status);
                return;
            }
            // The segments and lines may have gone (or changed hands) since the quote.
            string error = null;
            RoadRejectReason reason = RoadRejectReason.None;
            List<RoadSegmentRecord> segments = quote.SegmentIds.Count == 0
                ? new List<RoadSegmentRecord>()
                : _selection.Resolve(steamId, quote.PlanetId, quote.SegmentIds.ToArray(), "bulldoze", out reason, out error);
            if (segments == null || segments.Count != quote.SegmentIds.Count)
            {
                status.Reason = segments == null ? reason : RoadRejectReason.QuoteExpired;
                status.Message = error ?? LocHelper.Message("HighwayEngineer_Selection_MarksChanged");
                _sendStatus(steamId, status);
                return;
            }
            _busy = true;
            _selection.VerifyLines(steamId, quote.PlanetId, quote.Lines, "bulldoze", (lines, listing, lineReason, lineError) =>
            {
                if (lines == null || lines.Count != quote.Lines.Count)
                {
                    _busy = false;
                    status.Reason = lines == null ? lineReason : RoadRejectReason.QuoteExpired;
                    status.Message = lineError ?? LocHelper.Message("HighwayEngineer_Selection_MarksChanged");
                    _sendStatus(steamId, status);
                    return;
                }
                Run(steamId, request, quote, segments, lines, status);
            });
        }

        void Run(ulong steamId, PacketBulldozeRequest request, IssuedBulldoze quote, List<RoadSegmentRecord> segments,
            List<RoadLineRef> lines, PacketBulldozeStatus status)
        {
            IMyPlayer player = ServerPlayers.TryGetPlayer(steamId);
            long balance;
            if (quote.CreditDelta < 0 && (player == null || !player.TryGetBalanceInfo(out balance) ||
                                          balance < -quote.CreditDelta))
            {
                _busy = false;
                status.Reason = RoadRejectReason.InsufficientCredits;
                status.Message = LocHelper.Message("HighwayEngineer_Bulldoze_Costs", -quote.CreditDelta);
                _sendStatus(steamId, status);
                return;
            }

            _sendStatus(steamId, new PacketBulldozeStatus
            {
                RequestId = request.RequestId,
                State = RoadJobState.Running,
                Message = LocHelper.Message("HighwayEngineer_Bulldoze_Running")
            });
            Remove(quote.PlanetId, segments, lines, (success, message) =>
            {
                _busy = false;
                var done = new PacketBulldozeStatus
                {
                    RequestId = request.RequestId,
                    State = success ? RoadJobState.Completed : RoadJobState.Failed,
                    Reason = success ? RoadRejectReason.None : RoadRejectReason.PushFailed,
                    Message = message
                };
                if (success)
                {
                    long identity = player != null ? player.IdentityId : MyAPIGateway.Players.TryGetIdentityId(steamId);
                    if (quote.CreditDelta != 0 && identity != 0)
                        MyAPIGateway.Players.RequestChangeBalance(identity, quote.CreditDelta);
                    done.CreditDelta = quote.CreditDelta;
                }
                _sendStatus(steamId, done);
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
            foreach (KeyValuePair<long, IssuedBulldoze> pair in _issued)
                if (pair.Value.SteamId == steamId || pair.Value.ExpiresUtcTicks < DateTime.UtcNow.Ticks)
                    mine.Add(pair.Key);
            foreach (long id in mine)
                _issued.Remove(id);
        }

        /// <summary>Positive: refunded; negative: charged. Each segment by its age and the credits paid for it.</summary>
        static long CreditDelta(List<RoadSegmentRecord> segments, RoadCostSettings prices)
        {
            double now = RoadLibraryService.PlaySeconds;
            double window = prices.BulldozeRefundWindowHours * 3600.0;
            double delta = 0.0;
            foreach (RoadSegmentRecord segment in segments)
                delta += now - segment.BuiltAtPlaySeconds <= window
                    ? segment.CreditsPaid * prices.BulldozeEarlyRefundShare
                    : -segment.CreditsPaid * prices.BulldozeLateCostShare;
            return (long)Math.Round(delta);
        }

        static string Describe(List<RoadSegmentRecord> segments, int lines)
        {
            int roads = lines;
            var bridges = new HashSet<long>();
            foreach (RoadSegmentRecord segment in segments)
                if (segment.IsBridge) bridges.Add(segment.BridgeId);
                else roads++;
            var parts = new List<string>();
            if (roads > 0)
                parts.Add(LocHelper.Message(roads == 1
                    ? "HighwayEngineer_Repair_Desc_Segments_One"
                    : "HighwayEngineer_Repair_Desc_Segments_Many", roads));
            if (bridges.Count > 0)
                parts.Add(LocHelper.Message(bridges.Count == 1
                    ? "HighwayEngineer_Repair_Desc_Bridges_One"
                    : "HighwayEngineer_Repair_Desc_Bridges_Many", bridges.Count));
            return LocHelper.MessageJoinAnd(parts);
        }

        /// <summary>
        /// Removes the road lines (the library's road segments and the other lines) in one voxel
        /// API push, then (if it worked) the bridges' voxel maps, and forgets them all.
        /// <paramref name="done"/> runs on the game thread.
        /// </summary>
        void Remove(long planetId, List<RoadSegmentRecord> segments, List<RoadLineRef> lines, Action<bool, string> done)
        {
            var roads = segments.FindAll(segment => !segment.IsBridge);
            Action<bool, string> bridges = (success, message) =>
            {
                if (!success)
                {
                    done(false, message);
                    return;
                }
                var removed = new HashSet<long>();
                foreach (RoadSegmentRecord segment in segments)
                {
                    if (!segment.IsBridge || !removed.Add(segment.BridgeId)) continue;
                    BuiltBridgeRecord bridge = _library.FindBridge(segment.BridgeId);
                    if (bridge != null) BridgeBuilder.Remove(bridge.MapName);
                }
                _library.Remove(planetId, segments, lines);
                done(true, LocHelper.Message("HighwayEngineer_Bulldoze_Done", Describe(segments, lines.Count)));
            };
            if (roads.Count == 0 && lines.Count == 0)
            {
                bridges(true, null);
                return;
            }

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
                template = api.GetModificationTemplate(planetId);
                if (template == null)
                {
                    done(false, LocHelper.Message("HighwayEngineer_Error_NoTemplate"));
                    return;
                }
                foreach (RoadSegmentRecord road in roads)
                {
                    if (road.ModificationId == null)
                    {
                        done(false, LocHelper.Message("HighwayEngineer_Bulldoze_NoRecord"));
                        return;
                    }
                    template.RemoveModificationLine(road.ModificationId, road.LineIndex);
                }
                foreach (RoadLineRef line in lines)
                    template.RemoveModificationLine(line.ModificationId, line.LineIndex);
                ModificationTemplate pending = template;
                template = null;
                pending.Push((success, message) =>
                {
                    try { pending.Close(); }
                    catch (Exception) { }
                    bridges(success, success ? null : LocHelper.Message("HighwayEngineer_Bulldoze_ApiFailed", message));
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
    }
}

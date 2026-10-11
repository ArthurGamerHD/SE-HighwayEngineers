using System;
using System.Collections.Generic;
using Generated;
using HighwayEngineer.Common.Api;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client
{
    /// <summary>
    /// This client's copy of the server's road library: a snapshot of every planet on join, then
    /// the changes the server broadcasts. A gap in their revisions means one was missed, so a new
    /// snapshot is asked for.
    /// </summary>
    public sealed class ClientRoadLibrary : IRoadRouteSource
    {
        readonly Dictionary<long, RoadSegmentRecord> _segments = new Dictionary<long, RoadSegmentRecord>();

        public int Revision { get; private set; }
        public bool IsReady { get; private set; }

        public IEnumerable<long> PlanetIds
        {
            get
            {
                var planets = new HashSet<long>();
                foreach (RoadSegmentRecord segment in _segments.Values)
                    if (planets.Add(segment.PlanetId))
                        yield return segment.PlanetId;
            }
        }

        public IEnumerable<RoadSegmentRecord> SegmentsOn(long planetId)
        {
            var segments = new List<RoadSegmentRecord>();
            foreach (RoadSegmentRecord segment in _segments.Values)
                if (segment.PlanetId == planetId)
                    segments.Add(segment);
            // In the order they were built, so a bridge's segments come in order along it.
            segments.Sort((a, b) => a.SegmentId.CompareTo(b.SegmentId));
            return segments;
        }

        public RoadSegmentRecord FindSegment(long segmentId)
        {
            RoadSegmentRecord segment;
            return _segments.TryGetValue(segmentId, out segment) ? segment : null;
        }

        internal void Replace(PacketRoadLibrarySnapshot snapshot)
        {
            if (snapshot.PlanetId == 0)
                _segments.Clear();
            else
                foreach (RoadSegmentRecord segment in SegmentsOn(snapshot.PlanetId))
                    _segments.Remove(segment.SegmentId);
            foreach (RoadSegmentRecord segment in snapshot.Segments)
                if (segment != null)
                    _segments[segment.SegmentId] = segment;
            Revision = snapshot.Revision;
            IsReady = true;
        }

        /// <summary>False when the change does not follow the copy's revision (one was missed).</summary>
        internal bool Apply(PacketRoadLibraryChanged changed)
        {
            if (!IsReady || changed.Revision != Revision + 1) return false;
            if (changed.RemovedSegmentIds != null)
                foreach (long id in changed.RemovedSegmentIds)
                    _segments.Remove(id);
            if (changed.Added != null)
                foreach (RoadSegmentRecord segment in changed.Added)
                    if (segment != null)
                        _segments[segment.SegmentId] = segment;
            Revision = changed.Revision;
            return true;
        }

        internal void Clear()
        {
            _segments.Clear();
            Revision = 0;
            IsReady = false;
        }
    }

    public partial class HighwayEngineerClientComponent
    {
        readonly Dictionary<int, Action<PacketBulldozeQuoteResponse>> _bulldozeQuoteCallbacks =
            new Dictionary<int, Action<PacketBulldozeQuoteResponse>>();
        readonly Dictionary<int, Action<PacketBulldozeStatus>> _bulldozeCallbacks =
            new Dictionary<int, Action<PacketBulldozeStatus>>();
        // Frames left before asking again for a snapshot the server turned down (rate limit).
        int _libraryRetryFrames = -1;

        /// <summary>This client's copy of the built roads and bridges.</summary>
        public ClientRoadLibrary Library { get; } = new ClientRoadLibrary();

        /// <summary>Raised on the game thread when the library copy changed (a planet's id, or 0 for all).</summary>
        public static event Action<long> RoadLibraryChanged;

        /// <summary>Asks the server for every planet's built roads and bridges.</summary>
        public void RequestRoadLibrary()
        {
            _libraryRetryFrames = -1;
            var packet = new PacketRoadLibraryRequest { RequestId = ++_nextRequestId };
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalRoadLibraryRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        /// <summary>Asks again for a snapshot the server turned down, once its wait is over.</summary>
        void UpdateRoadLibrary()
        {
            if (_libraryRetryFrames > 0 && --_libraryRetryFrames == 0)
                RequestRoadLibrary();
        }

        [NetworkCallback(typeof(PacketRoadLibrarySnapshot), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRoadLibrarySnapshot(PacketRoadLibrarySnapshot packet)
        {
            HandleLocalRoadLibrarySnapshot(packet);
        }

        public void HandleLocalRoadLibrarySnapshot(PacketRoadLibrarySnapshot packet)
        {
            if (packet == null) return;
            if (packet.RetryAfterSeconds > 0f)
            {
                _libraryRetryFrames = Math.Max(1, (int)Math.Ceiling(packet.RetryAfterSeconds * 60f));
                return;
            }
            Library.Replace(packet);
            RaiseLibraryChanged(packet.PlanetId);
        }

        [NetworkCallback(typeof(PacketRoadLibraryChanged), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRoadLibraryChanged(PacketRoadLibraryChanged packet)
        {
            HandleLocalRoadLibraryChanged(packet);
        }

        public void HandleLocalRoadLibraryChanged(PacketRoadLibraryChanged packet)
        {
            if (packet == null) return;
            if (!Library.Apply(packet))
            {
                // A missed change (or none yet): start over from a snapshot, unless one is on its way.
                if (Library.IsReady && _libraryRetryFrames < 0)
                    RequestRoadLibrary();
                return;
            }
            RaiseLibraryChanged(packet.PlanetId);
        }

        static void RaiseLibraryChanged(long planetId)
        {
            Action<long> changed = RoadLibraryChanged;
            if (changed != null) changed(planetId);
        }

        /// <summary>
        /// Asks the server to price bulldozing segments of the road library and road lines of the
        /// voxel API it does not hold.
        /// </summary>
        public void RequestBulldozeQuote(long planetId, long[] segmentIds, List<RoadLineRef> lines,
            Action<PacketBulldozeQuoteResponse> callback)
        {
            var packet = new PacketBulldozeQuoteRequest
            {
                RequestId = ++_nextRequestId,
                PlanetId = planetId,
                SegmentIds = segmentIds,
                Lines = lines
            };
            _bulldozeQuoteCallbacks[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalBulldozeQuoteRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        [NetworkCallback(typeof(PacketBulldozeQuoteResponse), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleBulldozeQuoteResponse(PacketBulldozeQuoteResponse packet)
        {
            HandleLocalBulldozeQuoteResponse(packet);
        }

        public void HandleLocalBulldozeQuoteResponse(PacketBulldozeQuoteResponse packet)
        {
            Action<PacketBulldozeQuoteResponse> callback;
            if (packet == null || !_bulldozeQuoteCallbacks.TryGetValue(packet.RequestId, out callback)) return;
            _bulldozeQuoteCallbacks.Remove(packet.RequestId);
            callback(packet);
        }

        /// <summary>
        /// Carries out a bulldoze quote; <paramref name="callback"/> gets every status (running,
        /// then completed or failed).
        /// </summary>
        public void RequestBulldoze(long quoteId, Action<PacketBulldozeStatus> callback)
        {
            var packet = new PacketBulldozeRequest { RequestId = ++_nextRequestId, QuoteId = quoteId };
            _bulldozeCallbacks[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalBulldozeRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        [NetworkCallback(typeof(PacketBulldozeStatus), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleBulldozeStatus(PacketBulldozeStatus packet)
        {
            HandleLocalBulldozeStatus(packet);
        }

        public void HandleLocalBulldozeStatus(PacketBulldozeStatus packet)
        {
            Action<PacketBulldozeStatus> callback;
            if (packet == null || !_bulldozeCallbacks.TryGetValue(packet.RequestId, out callback)) return;
            if (packet.State == RoadJobState.Completed || packet.State == RoadJobState.Failed)
                _bulldozeCallbacks.Remove(packet.RequestId);
            callback(packet);
        }
    }
}

using System;
using System.Collections.Generic;
using Generated;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client
{
    public partial class HighwayEngineerClientComponent
    {
        readonly Dictionary<int, Action<PacketRepairQuoteResponse>> _repairQuoteCallbacks =
            new Dictionary<int, Action<PacketRepairQuoteResponse>>();
        readonly Dictionary<int, Action<PacketRepairStatus>> _repairCallbacks =
            new Dictionary<int, Action<PacketRepairStatus>>();
        // Voxel edits a repair drops on this machine; on a host the server's own queue does it.
        readonly VoxelRepairQueue _voxelRepairs = new VoxelRepairQueue();

        /// <summary>
        /// Asks the server to check repairing road library segments and other road lines of the
        /// voxel API: paving them with <paramref name="material"/> (null keeps theirs) and/or
        /// filling holes dug into them, and/or painting the library segments' guide lines.
        /// </summary>
        public void RequestRepairQuote(long planetId, long[] segmentIds, List<RoadLineRef> lines, string material,
            bool repairHoles, bool repaintGuideLines, Action<PacketRepairQuoteResponse> callback)
        {
            var packet = new PacketRepairQuoteRequest
            {
                RequestId = ++_nextRequestId,
                PlanetId = planetId,
                SegmentIds = segmentIds,
                Lines = lines,
                Material = material,
                RepairHoles = repairHoles,
                RepaintGuideLines = repaintGuideLines
            };
            _repairQuoteCallbacks[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalRepairQuoteRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        [NetworkCallback(typeof(PacketRepairQuoteResponse), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRepairQuoteResponse(PacketRepairQuoteResponse packet)
        {
            HandleLocalRepairQuoteResponse(packet);
        }

        public void HandleLocalRepairQuoteResponse(PacketRepairQuoteResponse packet)
        {
            Action<PacketRepairQuoteResponse> callback;
            if (packet == null || !_repairQuoteCallbacks.TryGetValue(packet.RequestId, out callback)) return;
            _repairQuoteCallbacks.Remove(packet.RequestId);
            callback(packet);
        }

        /// <summary>
        /// Carries out a repair quote; <paramref name="callback"/> gets every status (running,
        /// then completed or failed).
        /// </summary>
        public void RequestRepair(long quoteId, Action<PacketRepairStatus> callback)
        {
            var packet = new PacketRepairRequest { RequestId = ++_nextRequestId, QuoteId = quoteId };
            _repairCallbacks[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalRepairRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        [NetworkCallback(typeof(PacketRepairStatus), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRepairStatus(PacketRepairStatus packet)
        {
            HandleLocalRepairStatus(packet);
        }

        public void HandleLocalRepairStatus(PacketRepairStatus packet)
        {
            Action<PacketRepairStatus> callback;
            if (packet == null || !_repairCallbacks.TryGetValue(packet.RequestId, out callback)) return;
            if (packet.State == RoadJobState.Completed || packet.State == RoadJobState.Failed)
                _repairCallbacks.Remove(packet.RequestId);
            callback(packet);
        }

        [NetworkCallback(typeof(PacketVoxelRepair), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleVoxelRepair(PacketVoxelRepair packet)
        {
            if (packet == null || MyAPIGateway.Session.IsServer) return;
            _voxelRepairs.Enqueue(packet.PlanetId, packet.Blocks, packet.BlockSize);
        }
    }
}

using System;
using System.Collections.Generic;
using Generated;
using HighwayEngineer.Client.Helpers;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client
{
    public partial class HighwayEngineerClientComponent
    {
        readonly Dictionary<int, Action<PacketRoadQuoteResponse>> _quoteCallbacks =
            new Dictionary<int, Action<PacketRoadQuoteResponse>>();
        readonly Dictionary<int, Action<PacketRoadContractStatus>> _contractCallbacks =
            new Dictionary<int, Action<PacketRoadContractStatus>>();
        readonly Dictionary<long, RoadContractInfo> _openContracts = new Dictionary<long, RoadContractInfo>();

        /// <summary>Raised for every contract update from the server (accepted, delivery, built, cancelled).</summary>
        public static event Action<RoadContractInfo> RoadContractChanged;

        /// <summary>This player's open road contracts (waiting for delivery, payment or building).</summary>
        public IEnumerable<RoadContractInfo> OpenContracts => _openContracts.Values;
        int _nextRequestId;

        /// <summary>
        /// Asks the server to price <paramref name="packet"/>; <paramref name="callback"/>
        /// receives the reply. Returns the request ID for <see cref="CancelRoadQuote"/>.
        /// On a host or in single player the server code is called directly.
        /// </summary>
        public int RequestRoadQuote(PacketRoadQuoteRequest packet, Action<PacketRoadQuoteResponse> callback)
        {
            packet.RequestId = ++_nextRequestId;
            _quoteCallbacks[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalRoadQuoteRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
            return packet.RequestId;
        }

        /// <summary>Drops the callback of a quote nobody is waiting for any more.</summary>
        public void CancelRoadQuote(int requestId)
        {
            _quoteCallbacks.Remove(requestId);
        }

        [NetworkCallback(typeof(PacketRoadQuoteResponse), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRoadQuoteResponse(PacketRoadQuoteResponse packet)
        {
            HandleLocalRoadQuoteResponse(packet);
        }

        public void HandleLocalRoadQuoteResponse(PacketRoadQuoteResponse packet)
        {
            Action<PacketRoadQuoteResponse> callback;
            if (packet == null || !_quoteCallbacks.TryGetValue(packet.RequestId, out callback))
                return;
            _quoteCallbacks.Remove(packet.RequestId);
            callback(packet);
        }

        /// <summary>Accepts a server quote, creating the road contract; <paramref name="callback"/> gets the outcome.</summary>
        public void AcceptRoadContract(long quoteId, Action<PacketRoadContractStatus> callback)
        {
            var packet = new PacketRoadContractAccept { RequestId = ++_nextRequestId, QuoteId = quoteId };
            _contractCallbacks[packet.RequestId] = callback;
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalRoadContractAccept(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        /// <summary>Asks for this player's open contracts (on join), so their delivery boxes show.</summary>
        public void RequestRoadContracts()
        {
            var packet = new PacketRoadContractListRequest { RequestId = ++_nextRequestId };
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalRoadContractListRequest(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        [NetworkCallback(typeof(PacketRoadContractStatus), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRoadContractStatus(PacketRoadContractStatus packet)
        {
            HandleLocalRoadContractStatus(packet);
        }

        public void HandleLocalRoadContractStatus(PacketRoadContractStatus packet)
        {
            if (packet == null) return;
            if (packet.Contract != null)
            {
                if (IsOpen(packet.Contract.State))
                    _openContracts[packet.Contract.ContractId] = packet.Contract;
                else
                    _openContracts.Remove(packet.Contract.ContractId);
                Action<RoadContractInfo> changed = RoadContractChanged;
                if (changed != null) changed(packet.Contract);
            }
            Action<PacketRoadContractStatus> callback;
            if (packet.RequestId != 0 && _contractCallbacks.TryGetValue(packet.RequestId, out callback))
            {
                _contractCallbacks.Remove(packet.RequestId);
                callback(packet);
            }
            else if (!string.IsNullOrWhiteSpace(packet.Message))
            {
                // Updates nobody asked for (delivery done, road built) appear as notifications.
                MyAPIGateway.Utilities.ShowNotification(LocHelper.Format("HighwayEngineer_Contract_Notification", LocHelper.Resolve(packet.Message)), 6000,
                    packet.Contract != null && packet.Contract.State == RoadContractState.Failed ? "Red" : "White");
            }
        }

        /// <summary>Road Logistic Depots known from the server, for the planner map.</summary>
        public static PacketRoadDepotList Depots { get; private set; } = new PacketRoadDepotList();

        [NetworkCallback(typeof(PacketRoadDepotList), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRoadDepotList(PacketRoadDepotList packet)
        {
            HandleLocalRoadDepotList(packet);
        }

        /// <summary>Statics outlive the session; forget the last world's depots.</summary>
        static void ResetRoadDepots()
        {
            Depots = new PacketRoadDepotList();
        }

        public void HandleLocalRoadDepotList(PacketRoadDepotList packet)
        {
            if (packet != null)
                Depots = packet;
        }

        [NetworkCallback(typeof(PacketRoadContractList), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleRoadContractList(PacketRoadContractList packet)
        {
            HandleLocalRoadContractList(packet);
        }

        public void HandleLocalRoadContractList(PacketRoadContractList packet)
        {
            if (packet == null || packet.RetryAfterSeconds > 0f) return;
            _openContracts.Clear();
            foreach (RoadContractInfo contract in packet.Contracts)
                if (contract != null && IsOpen(contract.State))
                    _openContracts[contract.ContractId] = contract;
        }

        static bool IsOpen(RoadContractState state)
        {
            return state == RoadContractState.Pending || state == RoadContractState.AwaitingPayment ||
                   state == RoadContractState.Building;
        }

        /// <summary>Sends a /road admin command; the server checks the sender's admin level and answers.</summary>
        public void RunRoadAdminCommand(string command, string argument)
        {
            var packet = new PacketAdminCommandRequest
            {
                RequestId = ++_nextRequestId,
                Command = command,
                Argument = argument
            };
            if (MyAPIGateway.Session.IsServer && HighwayEngineerSessionComponent.Server != null)
                HighwayEngineerSessionComponent.Server.HandleLocalAdminCommand(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToServer(packet, false);
        }

        [NetworkCallback(typeof(PacketAdminCommandResult), NetworkCallbackFilter.FromServer | NetworkCallbackFilter.IsClient)]
        public void HandleAdminCommandResult(PacketAdminCommandResult packet)
        {
            HandleLocalAdminCommandResult(packet);
        }

        public void HandleLocalAdminCommandResult(PacketAdminCommandResult packet)
        {
            if (packet != null && !string.IsNullOrWhiteSpace(packet.Message))
                RoadChat.Show(packet.Message);
        }
    }
}

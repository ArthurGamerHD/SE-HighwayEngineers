using System;
using System.Collections.Generic;
using System.Globalization;
using Generated;
using HighwayEngineer.Common.Config;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Server.Config;
using HighwayEngineer.Server.Contracts;
using HighwayEngineer.Server.Helpers;
using HighwayEngineer.Server.Quotes;
using HighwayEngineer.Server.Structures;
using Sandbox.Game;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRageMath;

namespace HighwayEngineer.Server
{
    /// <summary>
    /// Server side: authoritative road quotes, contracts, delivery zones and voxel
    /// pushes. Every request is answered to its sender's Steam ID only; requests from
    /// the local player (host / single player) arrive through the HandleLocal* methods.
    /// </summary>
    public partial class HighwayEngineerServerComponent : ISingleton<HighwayEngineerServerComponent>
    {
        readonly HighwayEngineerSessionComponent _session;
        RoadQuoteService _quotes;
        RoadContractService _contracts;
        RoadBulldozeService _bulldozer;
        RoadRepairService _repairs;
        // Drops voxel edits for repairs on this machine; clients run their own copy.
        readonly VoxelRepairQueue _voxelRepairs = new VoxelRepairQueue();
        readonly BridgeBuilder _bridges = new BridgeBuilder();

        public HighwayEngineerServerComponent(HighwayEngineerSessionComponent session)
        {
            RegisterSingleton();
            _session = session;
        }

        /// <summary>The world's prices, read from HighwayEngineerCosts.xml on load.</summary>
        public RoadCostSettings Costs { get; private set; }

        /// <summary>Server limits, read from HighwayEngineerServer.xml on load.</summary>
        public RoadServerSettings Settings { get; private set; }

        internal RoadQuoteService Quotes => _quotes;

        /// <summary>Every road and bridge segment built, for the intermod API.</summary>
        internal RoadLibraryService Library => _contracts == null ? null : _contracts.Library;

        public void LoadData()
        {
            Costs = RoadCostSettings.Load();
            Settings = RoadServerSettings.Load();
            _quotes = new RoadQuoteService(Settings, () => Costs, Deliver);
            _contracts = new RoadContractService(Settings, _quotes, Deliver, Deliver, Deliver, Broadcast, Broadcast,
                Deliver);
            _bulldozer = new RoadBulldozeService(_contracts.Library, () => Costs, Deliver, Deliver);
            _repairs = new RoadRepairService(_contracts.Library, _voxelRepairs, Deliver, Deliver, Broadcast);
            _contracts.ResetVoxelEdits = _repairs.ResetVoxelEdits;
            _quotes.DepotFinder = _contracts.TryFindReusableDepot;
            _quotes.ExistingRoads = _contracts.RoadsOn;
            MyVisualScriptLogicProvider.PlayerDisconnected += PlayerDisconnected;
        }

        public void BeforeStart()
        {
            _contracts.Start();
        }

        public void UpdateBeforeSimulation()
        {
            _quotes.Update();
            _contracts.Update();
            _voxelRepairs.Update();
        }

        public void SaveData()
        {
            _contracts.Save();
        }

        public void UnloadData()
        {
            MyVisualScriptLogicProvider.PlayerDisconnected -= PlayerDisconnected;
            _contracts.Unload();
            _voxelRepairs.Clear();
            UnregisterSingleton();
        }

        [NetworkCallback(typeof(PacketRoadQuoteRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRoadQuoteRequest(ReceivedPacketEventArgs args)
        {
            _quotes.Request(args.SenderId, args.UnWrap<PacketRoadQuoteRequest>());
        }

        public void HandleLocalRoadQuoteRequest(PacketRoadQuoteRequest packet)
        {
            _quotes.Request(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketRoadContractAccept), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRoadContractAccept(ReceivedPacketEventArgs args)
        {
            _contracts.Accept(args.SenderId, args.UnWrap<PacketRoadContractAccept>());
        }

        public void HandleLocalRoadContractAccept(PacketRoadContractAccept packet)
        {
            _contracts.Accept(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketRoadContractListRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRoadContractListRequest(ReceivedPacketEventArgs args)
        {
            _contracts.List(args.SenderId, args.UnWrap<PacketRoadContractListRequest>());
        }

        public void HandleLocalRoadContractListRequest(PacketRoadContractListRequest packet)
        {
            _contracts.List(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketRoadLibraryRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRoadLibraryRequest(ReceivedPacketEventArgs args)
        {
            _contracts.Library.Snapshot(args.SenderId, args.UnWrap<PacketRoadLibraryRequest>());
        }

        public void HandleLocalRoadLibraryRequest(PacketRoadLibraryRequest packet)
        {
            _contracts.Library.Snapshot(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketBulldozeQuoteRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleBulldozeQuoteRequest(ReceivedPacketEventArgs args)
        {
            _bulldozer.Quote(args.SenderId, args.UnWrap<PacketBulldozeQuoteRequest>());
        }

        public void HandleLocalBulldozeQuoteRequest(PacketBulldozeQuoteRequest packet)
        {
            _bulldozer.Quote(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketBulldozeRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleBulldozeRequest(ReceivedPacketEventArgs args)
        {
            _bulldozer.Request(args.SenderId, args.UnWrap<PacketBulldozeRequest>());
        }

        public void HandleLocalBulldozeRequest(PacketBulldozeRequest packet)
        {
            _bulldozer.Request(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketRepairQuoteRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRepairQuoteRequest(ReceivedPacketEventArgs args)
        {
            _repairs.Quote(args.SenderId, args.UnWrap<PacketRepairQuoteRequest>());
        }

        public void HandleLocalRepairQuoteRequest(PacketRepairQuoteRequest packet)
        {
            _repairs.Quote(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketRepairRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleRepairRequest(ReceivedPacketEventArgs args)
        {
            _repairs.Request(args.SenderId, args.UnWrap<PacketRepairRequest>());
        }

        public void HandleLocalRepairRequest(PacketRepairRequest packet)
        {
            _repairs.Request(LocalSteamId, packet);
        }

        [NetworkCallback(typeof(PacketAdminCommandRequest), NetworkCallbackFilter.FromClient | NetworkCallbackFilter.IsServer)]
        public void HandleAdminCommand(ReceivedPacketEventArgs args)
        {
            RunAdminCommand(args.SenderId, args.UnWrap<PacketAdminCommandRequest>());
        }

        public void HandleLocalAdminCommand(PacketAdminCommandRequest packet)
        {
            RunAdminCommand(LocalSteamId, packet);
        }

        /// <summary>/road chat commands; the sender's admin level is checked here, never on the client.</summary>
        void RunAdminCommand(ulong steamId, PacketAdminCommandRequest packet)
        {
            if (packet == null) return;
            string command = (packet.Command ?? string.Empty).Trim().ToLowerInvariant();
            var result = new PacketAdminCommandResult { RequestId = packet.RequestId };
            if (!ServerPlayers.IsAdmin(steamId) && !ServerPlayers.IsLocal(steamId))
                result.Message = LocHelper.Message("HighwayEngineer_Admin_NotAdmin", command);
            else if (command == "forcecomplete")
            {
                int count = _contracts.ForceCompleteAll();
                result.Success = true;
                result.Message = count == 0
                    ? LocHelper.Message("HighwayEngineer_Admin_ForceComplete_None")
                    : LocHelper.Message(count == 1 ? "HighwayEngineer_Admin_ForceComplete_One" : "HighwayEngineer_Admin_ForceComplete_Many", count);
            }
            else if (command == "free")
            {
                bool enabled;
                string argument = (packet.Argument ?? string.Empty).Trim().ToLowerInvariant();
                if (argument == "on" || argument == "true") enabled = true;
                else if (argument == "off" || argument == "false") enabled = false;
                else enabled = !Settings.FreeRoads;
                Settings.FreeRoads = enabled;
                Settings.Save();
                result.Success = true;
                result.Message = enabled
                    ? LocHelper.Message("HighwayEngineer_Admin_FreeOn")
                    : LocHelper.Message("HighwayEngineer_Admin_FreeOff");
            }
            else if (command == "bridge")
            {
                Vector3D start, end;
                string pavement, structure, design;
                double width, fillDepth, segmentLength;
                if (!TryParseBridge(packet.Argument, out start, out end, out pavement, out structure,
                        out width, out fillDepth, out segmentLength, out design))
                    result.Message = LocHelper.Message("HighwayEngineer_Admin_BridgeMalformed");
                else if (_bridges.Busy)
                    result.Message = LocHelper.Message("HighwayEngineer_Bridge_Busy");
                else
                {
                    // Answered when the background build finishes.
                    _bridges.Build(start, end, structure, pavement, width, fillDepth, segmentLength, design,
                        (success, message) =>
                        Deliver(steamId, new PacketAdminCommandResult
                        {
                            RequestId = packet.RequestId,
                            Success = success,
                            Message = message
                        }));
                    if (_bridges.Busy)
                    {
                        result.Success = true;
                        result.Message = LocHelper.Message("HighwayEngineer_Admin_BridgeBuilding");
                    }
                    else
                        return;
                }
            }
            else if (command == "bridgeclear")
            {
                int count = BridgeBuilder.ClearAll();
                result.Success = true;
                result.Message = count == 0
                    ? LocHelper.Message("HighwayEngineer_Admin_BridgeClear_None")
                    : LocHelper.Message(count == 1 ? "HighwayEngineer_Admin_BridgeClear_One" : "HighwayEngineer_Admin_BridgeClear_Many", count);
            }
            else
                result.Message = LocHelper.Message("HighwayEngineer_Admin_UnknownCommand", command);
            Deliver(steamId, result);
        }

        /// <summary>
        /// "x1,y1,z1,x2,y2,z2[,pavement[,structure[,width[,fillDepth[,segmentLength[,design]]]]]]",
        /// numbers in the invariant culture; an empty field takes the default.
        /// </summary>
        static bool TryParseBridge(string argument, out Vector3D start, out Vector3D end, out string pavement,
            out string structure, out double width, out double fillDepth, out double segmentLength, out string design)
        {
            start = end = Vector3D.Zero;
            pavement = structure = design = null;
            width = RoadLimits.MinWidthMeters;
            fillDepth = RoadLimits.MinDepthMeters;
            segmentLength = RoadLimits.MaxSegmentDistanceMeters;
            string[] parts = (argument ?? string.Empty).Split(',');
            if (parts.Length < 6) return false;
            var values = new double[6];
            for (int i = 0; i < 6; i++)
                if (!TryParseFinite(parts[i], out values[i]))
                    return false;
            start = new Vector3D(values[0], values[1], values[2]);
            end = new Vector3D(values[3], values[4], values[5]);
            if (parts.Length > 6) pavement = parts[6];
            if (parts.Length > 7) structure = parts[7];
            double number;
            if (parts.Length > 8 && TryParseFinite(parts[8], out number)) width = number;
            if (parts.Length > 9 && TryParseFinite(parts[9], out number)) fillDepth = number;
            if (parts.Length > 10 && TryParseFinite(parts[10], out number)) segmentLength = number;
            if (parts.Length > 11) design = parts[11];
            return true;
        }

        static bool TryParseFinite(string text, out double value)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out value) &&
                !double.IsNaN(value) && !double.IsInfinity(value);
        }

        static ulong LocalSteamId
        {
            get
            {
                IMyPlayer player = MyAPIGateway.Session.Player;
                return player != null ? player.SteamUserId : MyAPIGateway.Multiplayer.MyId;
            }
        }

        void Deliver(ulong steamId, PacketRoadQuoteResponse packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRoadQuoteResponse(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketRoadContractStatus packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRoadContractStatus(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketRoadContractList packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRoadContractList(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketRoadDepotList packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRoadDepotList(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        /// <summary>Sends the depot list to every connected player (rare: a depot was spawned or destroyed).</summary>
        void Broadcast(PacketRoadDepotList packet)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players)
                if (player != null && !player.IsBot)
                    Deliver(player.SteamUserId, packet);
        }

        void Deliver(ulong steamId, PacketRoadLibrarySnapshot packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRoadLibrarySnapshot(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketBulldozeQuoteResponse packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalBulldozeQuoteResponse(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketBulldozeStatus packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalBulldozeStatus(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketRepairQuoteResponse packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRepairQuoteResponse(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        void Deliver(ulong steamId, PacketRepairStatus packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalRepairStatus(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        /// <summary>
        /// Sends a repair's voxel blocks to every other machine; this one (and a host's own
        /// client, which shares its planet) is repaired by <see cref="_voxelRepairs"/>.
        /// </summary>
        void Broadcast(PacketVoxelRepair packet)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players)
                if (player != null && !player.IsBot && !IsLocalClient(player.SteamUserId))
                    HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, player.SteamUserId);
        }


        /// <summary>Sends a road library change to every connected player.</summary>
        void Broadcast(PacketRoadLibraryChanged packet)
        {
            var players = new List<IMyPlayer>();
            MyAPIGateway.Players.GetPlayers(players);
            foreach (IMyPlayer player in players)
            {
                if (player == null || player.IsBot) continue;
                if (IsLocalClient(player.SteamUserId))
                    HighwayEngineerSessionComponent.Client.HandleLocalRoadLibraryChanged(packet);
                else
                    HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, player.SteamUserId);
            }
        }

        void Deliver(ulong steamId, PacketAdminCommandResult packet)
        {
            if (IsLocalClient(steamId))
                HighwayEngineerSessionComponent.Client.HandleLocalAdminCommandResult(packet);
            else
                HighwayEngineerSessionComponent.NetworkManager.TransmitToPlayer(packet, steamId);
        }

        /// <summary>The recipient plays on this machine (host or single player): skip the network.</summary>
        static bool IsLocalClient(ulong steamId)
        {
            IMyPlayer local = MyAPIGateway.Session.Player;
            return HighwayEngineerSessionComponent.Client != null && local != null && local.SteamUserId == steamId;
        }

        void PlayerDisconnected(long identityId)
        {
            ulong steamId = MyAPIGateway.Players.TryGetSteamId(identityId);
            if (steamId == 0) return;
            _quotes.ForgetPlayer(steamId);
            _contracts.ForgetPlayer(steamId);
            _contracts.Library.ForgetPlayer(steamId);
            _bulldozer.ForgetPlayer(steamId);
            _repairs.ForgetPlayer(steamId);
        }
    }
}

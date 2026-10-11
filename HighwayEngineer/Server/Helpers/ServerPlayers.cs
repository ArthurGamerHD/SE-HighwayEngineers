using Sandbox.ModAPI;
using VRage.Game.ModAPI;

namespace HighwayEngineer.Server.Helpers
{
    /// <summary>Who a request comes from, judged only from the sender's Steam ID.</summary>
    public static class ServerPlayers
    {
        /// <summary>The player on this machine (host or single player), if any.</summary>
        public static bool IsLocal(ulong steamId)
        {
            IMyPlayer local = MyAPIGateway.Session.Player;
            return local != null && local.SteamUserId == steamId;
        }

        public static bool IsAdmin(ulong steamId)
        {
            return MyAPIGateway.Session.GetUserPromoteLevel(steamId) >= MyPromoteLevel.Admin;
        }

        /// <summary>Admins and the local player bypass per-player rate limits and queue caps.</summary>
        public static bool BypassesLimits(ulong steamId)
        {
            return IsLocal(steamId) || IsAdmin(steamId);
        }

        /// <summary>
        /// Creative worlds pay nothing. A player's own creative tools cannot be checked:
        /// MySession.CreativeToolsEnabled is prohibited for mods.
        /// </summary>
        public static bool IsCostExempt(ulong steamId)
        {
            return MyAPIGateway.Session.CreativeMode;
        }

        static readonly System.Collections.Generic.List<IMyPlayer> Players = new System.Collections.Generic.List<IMyPlayer>();

        /// <summary>The online player with this Steam ID, or null.</summary>
        public static IMyPlayer TryGetPlayer(ulong steamId)
        {
            Players.Clear();
            MyAPIGateway.Players.GetPlayers(Players, player => player.SteamUserId == steamId);
            IMyPlayer found = Players.Count > 0 ? Players[0] : null;
            Players.Clear();
            return found;
        }
    }
}

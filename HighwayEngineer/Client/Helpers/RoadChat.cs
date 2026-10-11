using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client.Helpers
{
    /// <summary>Chat lines and HUD notifications from the mod; server messages are resolved to this player's language.</summary>
    internal static class RoadChat
    {
        public static string Sender => LocHelper.GetLoc("HighwayEngineer_Chat_Sender");

        public static void Show(string message)
        {
            MyAPIGateway.Utilities.ShowMessage(Sender, LocHelper.Resolve(message));
        }

        public static void Notify(string message, int milliseconds, string font)
        {
            MyAPIGateway.Utilities.ShowNotification(LocHelper.Resolve(message), milliseconds, font);
        }
    }
}

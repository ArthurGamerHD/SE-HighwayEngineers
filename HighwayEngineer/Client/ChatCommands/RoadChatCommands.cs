using System.Globalization;
using Generated;
using HighwayEngineer.Client.Contracts;
using HighwayEngineer.Client.Helpers;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using Sandbox.ModAPI;
using VRage.Game.ModAPI;
using VRage.ModAPI;
using VRageMath;

namespace HighwayEngineer.Client.ChatCommands
{
    /// <summary>
    /// /road chat commands. The admin ones run on the typing player's client, so each only asks
    /// the server, which checks the sender's admin level before doing anything.
    /// </summary>
    internal static class RoadChatCommands
    {
        const double BridgeAimRange = 2000.0;
        static Vector3D? _bridgeStart;

        /// <summary>Shows or hides the delivery boxes of the player's contracts (client only).</summary>
        /// <loc>HighwayEngineer_Command_DebugDropoff</loc>
        [ChatCommand("debugDropoff", "road")]
        public static void DebugDropoff()
        {
            DeliveryZoneRenderer.Visible = !DeliveryZoneRenderer.Visible;
            RoadChat.Show(LocHelper.GetLoc(DeliveryZoneRenderer.Visible
                ? "HighwayEngineer_Command_DropoffShown"
                : "HighwayEngineer_Command_DropoffHidden"));
        }

        /// <summary>Completes every road contract still waiting for delivery or payment (admin).</summary>
        /// <loc>HighwayEngineer_Command_ForceComplete</loc>
        [ChatCommand("forcecomplete", "road")]
        public static void ForceComplete()
        {
            Send("forcecomplete", null);
        }

        /// <summary>Turns free roads on or off, or toggles them without an argument (admin).</summary>
        /// <loc>HighwayEngineer_Command_Free</loc>
        [ChatCommand("free", "road")]
        public static void Free(bool? enabled)
        {
            Send("free", enabled.HasValue ? (enabled.Value ? "on" : "off") : null);
        }

        /// <summary>
        /// Test bridge (admin): the first call marks the aimed-at point as one end, the second marks
        /// the other end and asks the server to build a voxel bridge between them. The design is
        /// beam, arch, or auto (the default: whichever needs less concrete). Its width, fill depth,
        /// segment length and fill material come from the planner's road settings unless given;
        /// the structure defaults to concrete.
        /// </summary>
        /// <loc>HighwayEngineer_Command_BridgeMark</loc>
        [ChatCommand("bridgemark", "road")]
        public static void BridgeMark(string design = null, string pavement = null, string structure = null)
        {
            Vector3D point;
            if (!TryGetAimPoint(out point))
            {
                RoadChat.Show(LocHelper.Format("HighwayEngineer_Command_AimGround", BridgeAimRange));
                return;
            }
            if (!_bridgeStart.HasValue)
            {
                _bridgeStart = point;
                RoadChat.Show(LocHelper.GetLoc("HighwayEngineer_Command_BridgeStartMarked"));
                return;
            }
            Vector3D start = _bridgeStart.Value;
            _bridgeStart = null;
            RoadSettingsConfig road = MinimapLocalConfig.Current.Road;
            if (string.IsNullOrWhiteSpace(pavement))
                pavement = road.Material;
            string argument = Format(start) + "," + Format(point) + "," + Clean(pavement) + "," + Clean(structure) +
                "," + road.WidthMeters.ToString("R", CultureInfo.InvariantCulture) +
                "," + road.DepthMeters.ToString("R", CultureInfo.InvariantCulture) +
                "," + road.SegmentDistanceMeters.ToString("R", CultureInfo.InvariantCulture) + "," + Clean(design);
            RoadChat.Show(LocHelper.Format("HighwayEngineer_Command_BridgeEndMarked", Vector3D.Distance(start, point)));
            Send("bridge", argument);
        }

        /// <summary>Removes every test bridge (admin) and forgets a half-marked one.</summary>
        /// <loc>HighwayEngineer_Command_BridgeClear</loc>
        [ChatCommand("bridgeclear", "road")]
        public static void BridgeClear()
        {
            _bridgeStart = null;
            Send("bridgeclear", null);
        }

        static bool TryGetAimPoint(out Vector3D point)
        {
            point = Vector3D.Zero;
            IMyCamera camera = MyAPIGateway.Session.Camera;
            if (camera == null) return false;
            MatrixD view = camera.WorldMatrix;
            IHitInfo hit;
            if (!MyAPIGateway.Physics.CastRay(view.Translation, view.Translation + view.Forward * BridgeAimRange, out hit))
                return false;
            point = hit.Position;
            return true;
        }

        /// <summary>A material subtype for the comma-separated argument; empty for the server's default.</summary>
        static string Clean(string subtype)
        {
            return string.IsNullOrWhiteSpace(subtype) ? string.Empty : subtype.Trim().Replace(",", string.Empty);
        }

        static string Format(Vector3D point)
        {
            return point.X.ToString("R", CultureInfo.InvariantCulture) + "," +
                point.Y.ToString("R", CultureInfo.InvariantCulture) + "," +
                point.Z.ToString("R", CultureInfo.InvariantCulture);
        }

        static void Send(string command, string argument)
        {
            HighwayEngineerClientComponent client = HighwayEngineerSessionComponent.Client;
            if (client != null)
                client.RunRoadAdminCommand(command, argument);
        }
    }
}

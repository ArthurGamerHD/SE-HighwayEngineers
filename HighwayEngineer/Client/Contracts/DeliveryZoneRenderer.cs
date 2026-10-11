using System.Collections.Generic;
using HighwayEngineer.Common.Roads;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Utils;
using VRageMath;

namespace HighwayEngineer.Client.Contracts
{
    /// <summary>
    /// Draws the delivery box of each of the player's contracts still waiting for delivery:
    /// a faint green box where accessible inventories are drawn from. Hidden unless turned on
    /// with the /road debugDropoff chat command.
    /// </summary>
    internal static class DeliveryZoneRenderer
    {
        const double DrawDistanceMeters = 2000.0;
        const float Opacity = 0.25f;
        static readonly MyStringId LineMaterial = MyStringId.GetOrCompute("Square");

        /// <summary>Whether the boxes are drawn (client only, not saved).</summary>
        public static bool Visible;

        public static void Draw(IEnumerable<RoadContractInfo> contracts)
        {
            if (!Visible || MyAPIGateway.Session == null || MyAPIGateway.Session.Camera == null) return;
            Vector3D camera = MyAPIGateway.Session.Camera.WorldMatrix.Translation;
            foreach (RoadContractInfo contract in contracts)
            {
                if (contract.State != RoadContractState.Pending || contract.Zone == null) continue;
                MatrixD world = contract.Zone.WorldMatrix;
                if (Vector3D.DistanceSquared(world.Translation, camera) > DrawDistanceMeters * DrawDistanceMeters) continue;
                Vector3D half = contract.Zone.HalfExtents;
                var box = new BoundingBoxD(-half, half);
                Color color = Color.LimeGreen * Opacity;
                MySimpleObjectDraw.DrawTransparentBox(ref world, ref box, ref color, MySimpleObjectRasterizer.SolidAndWireframe,
                    1, 0.01f, null, LineMaterial);
            }
        }
    }
}

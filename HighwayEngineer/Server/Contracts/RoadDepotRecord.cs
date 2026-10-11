using HighwayEngineer.Common.Roads;
using VRageMath;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>
    /// A spawned Road Logistic Depot (saved with the contracts). Depots stay in the world after
    /// their contracts end, so later contracts of the same faction nearby deliver to them too.
    /// </summary>
    public sealed class RoadDepotRecord
    {
        public long GridId;
        /// <summary>Its contract terminal; destroying it fails every contract delivering here.</summary>
        public long TerminalId;
        public long FactionId;
        public long PlanetId;
        /// <summary>Delivery box around the terminal.</summary>
        public DeliveryZone Zone;
        /// <summary>Unit direction from the planet centre to the first node of the road it was built for.</summary>
        public double AnchorX;
        public double AnchorY;
        public double AnchorZ;

        public Vector3D Anchor
        {
            get { return new Vector3D(AnchorX, AnchorY, AnchorZ); }
            set { AnchorX = value.X; AnchorY = value.Y; AnchorZ = value.Z; }
        }
    }
}

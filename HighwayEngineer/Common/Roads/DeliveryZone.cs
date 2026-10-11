using ProtoBuf;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// The delivery box: an oriented box the server places beside the road's first
    /// node. Inventories inside it (that the contract owner may access) are drawn from.
    /// </summary>
    [ProtoContract]
    public sealed class DeliveryZone
    {
        [ProtoMember(1)] public double CenterX;
        [ProtoMember(2)] public double CenterY;
        [ProtoMember(3)] public double CenterZ;
        [ProtoMember(4)] public double ForwardX;
        [ProtoMember(5)] public double ForwardY;
        [ProtoMember(6)] public double ForwardZ;
        [ProtoMember(7)] public double UpX;
        [ProtoMember(8)] public double UpY;
        [ProtoMember(9)] public double UpZ;
        [ProtoMember(10)] public float HalfExtentX;
        [ProtoMember(11)] public float HalfExtentY;
        [ProtoMember(12)] public float HalfExtentZ;

        public static DeliveryZone Create(Vector3D center, Vector3D forward, Vector3D up, Vector3 halfExtents)
        {
            return new DeliveryZone
            {
                CenterX = center.X, CenterY = center.Y, CenterZ = center.Z,
                ForwardX = forward.X, ForwardY = forward.Y, ForwardZ = forward.Z,
                UpX = up.X, UpY = up.Y, UpZ = up.Z,
                HalfExtentX = halfExtents.X, HalfExtentY = halfExtents.Y, HalfExtentZ = halfExtents.Z
            };
        }

        public MatrixD WorldMatrix => MatrixD.CreateWorld(new Vector3D(CenterX, CenterY, CenterZ),
            new Vector3D(ForwardX, ForwardY, ForwardZ), new Vector3D(UpX, UpY, UpZ));

        public Vector3 HalfExtents => new Vector3(HalfExtentX, HalfExtentY, HalfExtentZ);

        public MyOrientedBoundingBoxD ToOrientedBox()
        {
            MatrixD world = WorldMatrix;
            return new MyOrientedBoundingBoxD(world.Translation, HalfExtents,
                Quaternion.CreateFromRotationMatrix(world));
        }
    }
}

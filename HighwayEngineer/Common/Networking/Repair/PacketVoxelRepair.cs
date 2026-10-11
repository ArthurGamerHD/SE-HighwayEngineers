using Generated;
using ProtoBuf;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>
    /// Server to all clients: storage blocks of a planet whose voxel edits (holes dug, dirt piled)
    /// are dropped, back to the planet's generated shape. Mods' storage writes are not synced by
    /// the game, so every machine drops them itself (<see cref="Roads.VoxelRepairQueue"/>).
    /// </summary>
    [ProtoContract]
    [NetworkPayload(17)]
    public sealed partial class PacketVoxelRepair
    {
        [ProtoMember(1)] public long PlanetId;
        /// <summary>Block coordinates, three values (x, y, z) per block of <see cref="BlockSize"/> voxels.</summary>
        [ProtoMember(2)] public int[] Blocks;
        [ProtoMember(3)] public int BlockSize;
    }
}

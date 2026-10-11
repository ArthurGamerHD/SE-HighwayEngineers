using ProtoBuf;
using VRageMath;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>
    /// The faction that takes a road contract: its nearest station posts the game contract.
    /// Colours are resolved on the server from the faction's colour masks.
    /// </summary>
    [ProtoContract]
    public sealed class RoadServiceProvider
    {
        [ProtoMember(1)] public long FactionId;
        [ProtoMember(2)] public long StationId;
        [ProtoMember(3)] public string FactionName;
        [ProtoMember(4)] public string FactionTag;
        [ProtoMember(5)] public string StationName;
        /// <summary>Faction logo texture path, drawn tinted with <see cref="IconColor"/>.</summary>
        [ProtoMember(6)] public string Icon;
        [ProtoMember(7)] public uint IconColorPacked;
        [ProtoMember(8)] public uint BackgroundColorPacked;
        [ProtoMember(9)] public double StationX;
        [ProtoMember(10)] public double StationY;
        [ProtoMember(11)] public double StationZ;

        public Color IconColor => new Color(IconColorPacked);
        public Color BackgroundColor => new Color(BackgroundColorPacked);
        public Vector3D StationPosition => new Vector3D(StationX, StationY, StationZ);
    }
}

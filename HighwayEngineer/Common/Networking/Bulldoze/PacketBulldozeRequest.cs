using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: carry out a bulldoze quote.</summary>
    [ProtoContract]
    [NetworkPayload(7)]
    public sealed partial class PacketBulldozeRequest
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public long QuoteId;
    }
}

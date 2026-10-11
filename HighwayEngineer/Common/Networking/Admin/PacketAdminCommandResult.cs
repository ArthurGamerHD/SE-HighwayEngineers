using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Server to requester: the admin command's outcome.</summary>
    [ProtoContract]
    [NetworkPayload(51)]
    public sealed partial class PacketAdminCommandResult
    {
        [ProtoMember(1)] public int RequestId;
        [ProtoMember(2)] public bool Success;
        [ProtoMember(3)] public string Message;
    }
}

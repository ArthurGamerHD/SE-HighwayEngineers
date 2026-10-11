using Generated;
using ProtoBuf;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Common.Networking
{
    /// <summary>Client to server: an admin chat command (the server checks the sender's promote level).</summary>
    [ProtoContract]
    [NetworkPayload(50)]
    public sealed partial class PacketAdminCommandRequest
    {
        [ProtoMember(1)] public int RequestId;
        /// <summary>e.g. "forcecomplete".</summary>
        [ProtoMember(2)] public string Command;
        [ProtoMember(3)] public string Argument;
    }
}

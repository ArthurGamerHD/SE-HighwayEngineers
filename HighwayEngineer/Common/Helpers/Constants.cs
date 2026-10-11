namespace HighwayEngineer.Common.Helpers
{
    public static class Constants
    {
        /// <summary>
        /// Secure message channel; unique to this mod. Packet IDs on it: 1–19 build and
        /// bulldoze, 20–39 road library, 40–49 settings, 50–59 admin; 127 is reserved by
        /// the ADK transport. Requests carry a client-chosen RequestId that the reply
        /// echoes. The server identifies the sender only from ReceivedPacketEventArgs.SenderId,
        /// and clients send with sendToAllPlayers: false so requests are never relayed unchecked.
        /// </summary>
        public const ushort PORT = 52437;
        /// <summary>Bumped on any incompatible packet change; sent with the settings sync.</summary>
        public const int PROTOCOL_VERSION = 1;
        public const int MAX_WIRE_PACKET_BYTES = 32000;
        public const int MAX_FRAGMENTED_PACKET_BYTES = 4 * 1024 * 1024;
        public const int MAX_FRAGMENT_CHUNKS = 160;
        /// <summary>Values per segment in planet-local road geometry (see RoadCurve.ToLocalGeometry).</summary>
        public const int GEOMETRY_VALUES_PER_SEGMENT = 12;
        /// <summary>Subtype of the custom contract type in ContractTypes_HighwayEngineer.sbc.</summary>
        public const string ROAD_CONTRACT_TYPE = "HighwayEngineer_RoadConstruction";
    }
}

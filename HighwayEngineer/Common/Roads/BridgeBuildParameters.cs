using System.Collections.Generic;
using ProtoBuf;

namespace HighwayEngineer.Common.Roads
{
    /// <summary>How a bridge is carried: whichever needs less concrete, or a design the player chose.</summary>
    public enum BridgeDesign
    {
        Auto = 0,
        Beam = 1,
        Arch = 2
    }

    /// <summary>Consecutive bridged segments of a road, built as one bridge (indices into its geometry).</summary>
    public struct BridgeRun
    {
        public int First;
        public int Last;

        public int Count => Last - First + 1;
    }

    /// <summary>
    /// The bridge part of a quote request: which of the request's segments are bridged and how.
    /// Each run of consecutive bridged segments is one bridge whose deck follows their curves. Its
    /// width and fill depth are the request's road parameters.
    /// </summary>
    [ProtoContract]
    public sealed class BridgeBuildParameters
    {
        /// <summary>Voxel material subtype of the deck, parapets and structure.</summary>
        [ProtoMember(1)] public string StructureMaterial;
        /// <summary>The design asked for (any run may use it).</summary>
        [ProtoMember(2)] public BridgeDesign Design;
        /// <summary>Length of each straight, gravity-levelled deck segment (between expansion joints).</summary>
        [ProtoMember(3)] public double SegmentMeters;
        /// <summary>Per geometry segment, whether it is bridged; null when every segment is (older contracts).</summary>
        [ProtoMember(4)] public bool[] Segments;
        /// <summary>On a quote the server issued, the design it chose for each run (never Auto).</summary>
        [ProtoMember(5)] public BridgeDesign[] RunDesigns;
        /// <summary>Voxel material subtype of the road fill on the decks; null for the road's material.</summary>
        [ProtoMember(6)] public string PavementMaterial;
        /// <summary>
        /// Per geometry node (segments + 1), how far above the terrain the deck passes there; a
        /// lifted node pins the deck, which otherwise runs straight between pins (a bridge's ends
        /// are always pinned). Null when no node is lifted.
        /// </summary>
        [ProtoMember(7)] public double[] NodeLifts;

        /// <summary>Node <paramref name="node"/>'s lift; 0 when it has none.</summary>
        public double LiftAt(int node)
        {
            return NodeLifts != null && node >= 0 && node < NodeLifts.Length ? NodeLifts[node] : 0.0;
        }

        public bool IsBridged(int segment)
        {
            return Segments == null || segment >= 0 && segment < Segments.Length && Segments[segment];
        }

        /// <summary>True when every one of <paramref name="count"/> segments is bridged.</summary>
        public bool BridgesAll(int count)
        {
            for (int i = 0; i < count; i++)
                if (!IsBridged(i)) return false;
            return true;
        }

        /// <summary>The runs of bridged segments among <paramref name="count"/>, in order.</summary>
        public List<BridgeRun> Runs(int count)
        {
            var runs = new List<BridgeRun>();
            for (int i = 0; i < count; i++)
            {
                if (!IsBridged(i)) continue;
                int first = i;
                while (i + 1 < count && IsBridged(i + 1)) i++;
                runs.Add(new BridgeRun { First = first, Last = i });
            }
            return runs;
        }

        /// <summary>The design a run is built with: the one priced for it, else the one asked for.</summary>
        public BridgeDesign DesignFor(int run)
        {
            return RunDesigns != null && run >= 0 && run < RunDesigns.Length ? RunDesigns[run] : Design;
        }

        public BridgeBuildParameters Clone()
        {
            return new BridgeBuildParameters
            {
                StructureMaterial = StructureMaterial,
                Design = Design,
                SegmentMeters = SegmentMeters,
                Segments = Segments == null ? null : (bool[])Segments.Clone(),
                RunDesigns = RunDesigns == null ? null : (BridgeDesign[])RunDesigns.Clone(),
                PavementMaterial = PavementMaterial,
                NodeLifts = NodeLifts == null ? null : (double[])NodeLifts.Clone()
            };
        }
    }
}

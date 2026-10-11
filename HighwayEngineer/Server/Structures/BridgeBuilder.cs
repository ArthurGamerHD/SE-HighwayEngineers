using System;
using System.Collections.Generic;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Common.Structures;
using Sandbox.Definitions;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;
using VRage.Game.ModAPI;
using VRage.Utils;
using VRage.ModAPI;
using VRage.Voxels;
using VRageMath;

namespace HighwayEngineer.Server.Structures
{
    /// <summary>
    /// Builds a bridge as free-standing voxel maps, one per deck segment, each rotated to its segment
    /// so the deck lines up with the voxel grid (an unaligned grid cuts its edges into spikes) and
    /// the bridge can follow the road's curves and the planet's at any length.
    /// <para>
    /// The mesher builds each face from the cells on both sides, so two maps meeting at a plane leave
    /// holes, and two maps drawing the same surface z-fight. So at each joint the next segment starts
    /// <see cref="JointOverlap"/> early as a closed solid, and inside that overlap (measured in the
    /// next segment's frame, so it holds at a bend) the previous segment sinks up to
    /// <see cref="JointTaper"/> into it: it stays hidden, and never draws the same surface. Both
    /// sample the same <see cref="BridgeShape"/>, so the next segment always covers it.
    /// </para>
    /// Rasterising runs on a background thread; one bridge builds at a time.
    /// </summary>
    public sealed class BridgeBuilder
    {
        public const string StoragePrefix = "HighwayEngineerBridge_";
        // A whole number of metres, so with the grid's half-voxel X shift the next segment's
        // starting face falls between samples.
        const double JointOverlap = 2.0;
        const double JointTaper = 0.5;
        // Registered by this mod (Data/VoxelMaterials_HighwayEngineer.sbc) or the Concrete Tool mod
        // (see ConcreteMod); Stone_01 if it is missing.
        const string FallbackStructureMaterial = "Stone_01";
        // The planner's default road material.
        const string DefaultPavementMaterial = "Soil";

        // Voxel maps are power-of-two cubes. Coarser LODs keep only every 2^k-th sample of the
        // storage, so its Y origin sits on a multiple of this to keep the deck's mid-plane in all of them.
        const int LodAlignment = 128;
        // X and Z only need to start on an octree leaf.
        const int LeafAlignment = 16;
        const int MaxStorageSize = 4096;
        const int ChunkSize = 32;

        bool _busy;

        public bool Busy => _busy;

        /// <summary>A straight bridge's single curve: the great circle between two points.</summary>
        static RoadCurve StraightCurve(Vector3D center, Vector3D start, Vector3D end)
        {
            Vector3D from = Vector3D.Normalize(start - center), to = Vector3D.Normalize(end - center);
            double cos = MathHelper.Clamp(Vector3D.Dot(from, to), -1.0, 1.0);
            Vector3D startTangent = to - from * cos, endTangent = to * cos - from;
            if (startTangent.Normalize() < 1e-12) startTangent = Vector3D.CalculatePerpendicularVector(from);
            if (endTangent.Normalize() < 1e-12) endTangent = startTangent;
            return new RoadCurve
            {
                Start = from,
                End = to,
                StartTangent = startTangent,
                EndTangent = endTangent,
                Arc = Math.Acos(cos)
            };
        }

        /// <summary>
        /// Plans and builds a straight bridge (chat command; game thread). <paramref name="done"/>
        /// runs on the game thread with the outcome, also when the build could not start.
        /// </summary>
        /// <param name="design">"beam", "arch", or null / "auto" for whichever needs less concrete.</param>
        public void Build(Vector3D start, Vector3D end, string structureMaterial, string pavementMaterial,
            double width, double fillDepth, double segmentLength, string design, Action<bool, string> done)
        {
            if (_busy)
            {
                done(false, LocHelper.Message("HighwayEngineer_Bridge_Busy"));
                return;
            }
            BridgeDesign parsed;
            if (!TryParseDesign(design, out parsed))
            {
                done(false, LocHelper.Message("HighwayEngineer_Bridge_UnknownDesign", design));
                return;
            }
            MyPlanet planet = MyGamePruningStructure.GetClosestPlanet((start + end) * 0.5);
            if (planet == null)
            {
                done(false, LocHelper.Message("HighwayEngineer_Bridge_NoPlanet"));
                return;
            }
            var curves = new List<RoadCurve> { StraightCurve(planet.PositionComp.GetPosition(), start, end) };
            var options = new BridgeOptions
            {
                Design = parsed,
                Width = width,
                FillDepth = fillDepth,
                SegmentLength = segmentLength,
                RampGradeDegrees = 10.0
            };
            _busy = true;
            BridgePlan plan = null;
            string error = null;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try
                {
                    plan = BridgePlanner.Plan(planet, curves, start, end, null, options, out error);
                }
                catch (Exception e)
                {
                    error = LocHelper.Message("HighwayEngineer_Bridge_PlanFailed", e.Message);
                }
            }, () =>
            {
                _busy = false;
                if (plan == null)
                    done(false, error);
                else
                    Build(plan, structureMaterial, pavementMaterial, (success, message) =>
                        done(success, success ? LocHelper.Message("HighwayEngineer_Bridge_BuiltWithSummary", message, plan.Summary) : message));
            });
        }

        /// <summary>
        /// Builds a planned bridge (game thread); the voxels are made on a background thread.
        /// <paramref name="done"/> runs on the game thread with the outcome.
        /// </summary>
        /// <param name="structureMaterial">Voxel material of the deck, parapets and structure; null for concrete.</param>
        /// <param name="pavementMaterial">Voxel material of the road fill; null for the planner's default.</param>
        /// <param name="mapName">Start of its voxel maps' storage names (<see cref="NewMapName"/>), to remove them by later.</param>
        public void Build(BridgePlan plan, string structureMaterial, string pavementMaterial, Action<bool, string> done,
            string mapName = null)
        {
            if (_busy)
            {
                done(false, LocHelper.Message("HighwayEngineer_Bridge_Busy"));
                return;
            }
            MyVoxelMaterialDefinition structure = FindMaterial(structureMaterial, ConcreteMod.StructureMaterial,
                FallbackStructureMaterial);
            if (structure == null)
            {
                done(false, LocHelper.Message("HighwayEngineer_Bridge_NoMaterial"));
                return;
            }
            MyVoxelMaterialDefinition pavement = FindMaterial(pavementMaterial, DefaultPavementMaterial, null) ?? structure;

            _busy = true;
            string error = null;
            BridgeShape shape = plan.Shape;
            var tiles = new List<Tile>();
            byte structureIndex = structure.Index;
            byte pavementIndex = pavement.Index;
            MyAPIGateway.Parallel.StartBackground(() =>
            {
                try
                {
                    for (int i = 0; i < shape.SegmentCount; i++)
                    {
                        Tile tile = CreateTile(shape, i);
                        if (tile.Size > MaxStorageSize)
                            throw new InvalidOperationException("a segment is too large for one voxel map (over " +
                                MaxStorageSize + " m)");
                        tiles.Add(tile);
                    }
                    foreach (Tile tile in tiles)
                        Rasterize(shape, tile, structureIndex, pavementIndex);
                }
                catch (Exception e)
                {
                    error = e.Message;
                }
            }, () =>
            {
                _busy = false;
                if (error != null)
                {
                    MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Bridge: build failed: " + error);
                    done(false, LocHelper.Message("HighwayEngineer_Bridge_BuildFailed", error));
                    return;
                }
                done(true, Spawn(tiles, shape, mapName ?? NewMapName(), structure.Id.SubtypeName,
                    pavement.Id.SubtypeName));
            });
        }


        public static bool TryParseDesign(string text, out BridgeDesign design)
        {
            switch ((text ?? string.Empty).Trim().ToLowerInvariant())
            {
                case "":
                case "auto":
                    design = BridgeDesign.Auto;
                    return true;
                case "beam":
                    design = BridgeDesign.Beam;
                    return true;
                case "arch":
                    design = BridgeDesign.Arch;
                    return true;
                default:
                    design = BridgeDesign.Auto;
                    return false;
            }
        }

        /// <summary>Removes every bridge voxel map (game thread); returns how many were removed.</summary>
        public static int ClearAll()
        {
            var maps = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps, map =>
                map.StorageName != null && map.StorageName.StartsWith(StoragePrefix, StringComparison.Ordinal));
            foreach (IMyVoxelBase map in maps)
                map.Close();
            return maps.Count;
        }

        /// <summary>
        /// The voxel map of one segment: from the overlap before its start to the overlap past its
        /// end, which at a bend is measured in the next segment's frame and so reaches a little
        /// further out on the bend's outside.
        /// </summary>
        static Tile CreateTile(BridgeShape shape, int index)
        {
            BridgeShape.Segment segment = shape.GetSegment(index);
            var tile = new Tile
            {
                Segment = index,
                First = index == 0,
                Last = index == shape.SegmentCount - 1,
                Grid = new BridgeGrid(segment, shape.DeckThickness)
            };
            double from = tile.First ? 0.0 : -JointOverlap;
            double to = segment.Length + (tile.Last ? 0.0 : JointOverlap);
            BoundingBoxD bounds = shape.SegmentBounds(index, from, to);
            if (!tile.Last)
            {
                BridgeShape.Segment next = shape.GetSegment(index + 1);
                double across = Math.Max(Math.Abs(bounds.Min.Z), Math.Abs(bounds.Max.Z));
                double height = Math.Max(Math.Abs(bounds.Min.Y), Math.Abs(bounds.Max.Y));
                double reach = (JointOverlap + across * Math.Abs(Vector3D.Dot(segment.Side, next.Forward)) +
                                height * Math.Abs(Vector3D.Dot(segment.Up, next.Forward))) /
                               Math.Max(0.5, Vector3D.Dot(segment.Forward, next.Forward));
                bounds = shape.SegmentBounds(index, from, segment.Length + reach + 1.0);
            }
            bounds.Translate(tile.Grid.FromLocal);
            // Keeps the surface off the storage's outer layer, where the mesher has no neighbours.
            bounds.Inflate(2.0);
            tile.Bounds = bounds;
            tile.Min = new Vector3I(AlignDown(bounds.Min.X, LeafAlignment), AlignDown(bounds.Min.Y, LodAlignment),
                AlignDown(bounds.Min.Z, LeafAlignment));
            Vector3D extent = bounds.Max - (Vector3D)tile.Min;
            tile.Size = MathHelper.GetNearestBiggerPowerOfTwo((int)Math.Ceiling(extent.Max()));
            return tile;
        }

        static void Rasterize(BridgeShape shape, Tile tile, byte structure, byte pavement)
        {
            BridgeShape.Segment segment = shape.GetSegment(tile.Segment);
            BridgeShape.Segment next = tile.Last ? segment : shape.GetSegment(tile.Segment + 1);
            var data = new MyStorageData(MyStorageDataTypeFlags.ContentAndMaterial);
            data.Resize(new Vector3I(ChunkSize));
            // Only the chunks the bounds touch: the arch's distance is approximate away from its
            // surface, so skipping chunks by distance could cut holes.
            int chunks = tile.Size / ChunkSize;
            Vector3I first = Vector3I.Max(Vector3I.Zero, ChunkIndex(tile.Bounds.Min - (Vector3D)tile.Min));
            Vector3I last = Vector3I.Min(new Vector3I(chunks - 1), ChunkIndex(tile.Bounds.Max - (Vector3D)tile.Min));
            for (int cx = first.X; cx <= last.X; cx++)
            for (int cy = first.Y; cy <= last.Y; cy++)
            for (int cz = first.Z; cz <= last.Z; cz++)
            {
                var min = new Vector3I(cx, cy, cz) * ChunkSize;
                bool any = false;
                var p = new Vector3I();
                for (p.Z = 0; p.Z < ChunkSize; p.Z++)
                for (p.Y = 0; p.Y < ChunkSize; p.Y++)
                for (p.X = 0; p.X < ChunkSize; p.X++)
                {
                    Vector3D world = tile.Grid.ToWorld(tile.Min + min + p);
                    BridgePoint point = shape.Locate(world);
                    double distance = shape.Distance(point);
                    // Closed where the overlap before this segment starts.
                    if (!tile.First)
                        distance = Math.Max(distance, -(Vector3D.Dot(world - segment.Start, segment.Forward) + JointOverlap));
                    // Sinking into the next segment, then closed where its overlap ends (in its frame).
                    if (!tile.Last)
                    {
                        double along = Vector3D.Dot(world - next.Start, next.Forward);
                        double sink = MathHelper.Clamp((along + JointOverlap) / (2.0 * JointOverlap), 0.0, 1.0) *
                                      JointTaper;
                        distance = Math.Max(distance + sink, along - JointOverlap);
                    }
                    byte content = Content(distance);
                    int index = data.ComputeLinear(ref p);
                    data.Content(index, content);
                    data.Material(index, shape.IsPavement(point) ? pavement : structure);
                    any |= content != 0;
                }
                if (!any) continue;
                if (tile.Storage == null)
                    tile.Storage = MyAPIGateway.Session.VoxelMaps.CreateStorage(new Vector3I(tile.Size));
                tile.Storage.WriteRange(data, MyStorageDataTypeFlags.ContentAndMaterial,
                    min, min + (ChunkSize - 1), false);
            }
        }

        /// <summary>
        /// Voxel content is the engine's signed distance: 0..255 maps to +1..-1 voxel from the
        /// surface, saturated beyond. Matching it lets the mesher place the surface between samples
        /// instead of snapping it to them.
        /// </summary>
        static byte Content(double distance)
        {
            return (byte)((MathHelper.Clamp(-distance, -1.0, 1.0) * 0.5 + 0.5) * 255.0 + 0.5);
        }

        /// <summary>A new bridge's voxel map name: every segment's map is named this plus its index.</summary>
        public static string NewMapName()
        {
            return StoragePrefix + DateTime.UtcNow.Ticks + "_";
        }

        /// <summary>Removes a bridge's voxel maps (game thread); returns how many were removed.</summary>
        public static int Remove(string mapName)
        {
            if (string.IsNullOrEmpty(mapName) || !mapName.StartsWith(StoragePrefix, StringComparison.Ordinal)) return 0;
            var maps = new List<IMyVoxelBase>();
            MyAPIGateway.Session.VoxelMaps.GetInstances(maps, map =>
                map.StorageName != null && map.StorageName.StartsWith(mapName, StringComparison.Ordinal));
            foreach (IMyVoxelBase map in maps)
                map.Close();
            return maps.Count;
        }

        static string Spawn(List<Tile> tiles, BridgeShape shape, string name, string structure, string pavement)
        {
            int spawned = 0;
            foreach (Tile tile in tiles)
            {
                if (tile.Storage == null) continue;
                BridgeShape.Segment segment = shape.GetSegment(tile.Segment);
                IMyVoxelMap map = MyAPIGateway.Session.VoxelMaps.CreateVoxelMap(name + tile.Segment, tile.Storage,
                    Vector3D.Zero, 0);
                // Voxel v of a map sits at WorldMatrix * (v - size / 2); the orientation is saved and
                // replicated with the map.
                MatrixD matrix = MatrixD.Identity;
                matrix.Right = segment.Forward;
                matrix.Up = segment.Up;
                matrix.Backward = segment.Side;
                matrix.Translation = tile.Grid.ToWorld((Vector3D)tile.Min + new Vector3D(tile.Size * 0.5));
                map.SetWorldMatrix(matrix);
                spawned++;
                MyLog.Default.WriteLineAndConsole("[HighwayEngineer] Bridge: spawned voxel map " + map.StorageName +
                    " (entity " + map.EntityId + ") at " + matrix.Translation + ", " + tile.Size + " m");
            }
            bool arch = shape.Structure.Name == "arch";
            string key = arch
                ? spawned == 1 ? "HighwayEngineer_Bridge_Built_Arch_One" : "HighwayEngineer_Bridge_Built_Arch_Many"
                : spawned == 1 ? "HighwayEngineer_Bridge_Built_Beam_One" : "HighwayEngineer_Bridge_Built_Beam_Many";
            return LocHelper.Message(key, Math.Round(shape.Length), Math.Round(shape.Width), structure, pavement,
                Math.Round(shape.DeepestFoot), spawned);
        }

        static Vector3I ChunkIndex(Vector3D voxel)
        {
            return new Vector3I((int)Math.Floor(voxel.X / ChunkSize), (int)Math.Floor(voxel.Y / ChunkSize),
                (int)Math.Floor(voxel.Z / ChunkSize));
        }

        static int AlignDown(double value, int alignment)
        {
            return (int)Math.Floor(value / alignment) * alignment;
        }

        /// <summary>
        /// The first defined of <paramref name="subtype"/>, <paramref name="preferred"/> and
        /// <paramref name="fallback"/>; with a null fallback, null when neither of the others exists,
        /// otherwise any material as a last resort.
        /// </summary>
        static MyVoxelMaterialDefinition FindMaterial(string subtype, string preferred, string fallback)
        {
            MyVoxelMaterialDefinition definition = null;
            if (!string.IsNullOrWhiteSpace(subtype))
                definition = MyDefinitionManager.Static.GetVoxelMaterialDefinition(subtype.Trim());
            if (!ConcreteMod.IsOffered(definition))
                definition = null;
            if (definition == null)
                definition = MyDefinitionManager.Static.GetVoxelMaterialDefinition(preferred);
            if (definition != null || fallback == null)
                return definition;
            definition = MyDefinitionManager.Static.GetVoxelMaterialDefinition(fallback);
            if (definition == null)
                foreach (MyVoxelMaterialDefinition any in MyDefinitionManager.Static.GetVoxelMaterialDefinitions())
                    return any;
            return definition;
        }

        /// <summary>
        /// Voxel coordinates in a segment's frame. The deck's mid-plane is on Y = 0, a multiple of
        /// every LOD's sample spacing, so coarse LODs keep a row of samples inside the deck instead
        /// of stepping over it. X and Z are shifted half a voxel so the deck's ends and sides fall
        /// between samples: a sample exactly on a face is ambiguous and meshes into slivers.
        /// </summary>
        struct BridgeGrid
        {
            readonly Vector3D _origin, _x, _y, _z;
            readonly double _halfThickness;

            public BridgeGrid(BridgeShape.Segment segment, double deckThickness)
            {
                _x = segment.Forward;
                _y = segment.Up;
                _z = segment.Side;
                _halfThickness = deckThickness * 0.5;
                _origin = segment.Start - _x * 0.5 - _y * _halfThickness - _z * 0.5;
            }

            /// <summary>Offset from <see cref="BridgeShape.SegmentBounds"/> coordinates to grid coordinates.</summary>
            public Vector3D FromLocal => new Vector3D(0.5, _halfThickness, 0.5);

            public Vector3D ToWorld(Vector3D grid)
            {
                return _origin + _x * grid.X + _y * grid.Y + _z * grid.Z;
            }
        }

        sealed class Tile
        {
            public int Segment;
            public bool First, Last;
            public BridgeGrid Grid;
            /// <summary>Grid bounds of the solid parts.</summary>
            public BoundingBoxD Bounds;
            /// <summary>Grid position of the storage's voxel 0.</summary>
            public Vector3I Min;
            /// <summary>Storage edge in voxels (a power of two).</summary>
            public int Size;
            /// <summary>Created when the first non-empty chunk is written.</summary>
            public IMyStorage Storage;
        }
    }
}

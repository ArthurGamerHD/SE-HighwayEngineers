using System;
using System.Collections.Generic;
using Acrux.Cartography.Maps;
using Acrux.Render3D;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using HighwayEngineer.Common.Roads;
using HighwayEngineer.Common.Structures;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Utils;
using VRageMath;

namespace HighwayEngineer.Client.Roads
{
    /// <summary>
    /// The draft's bridges as real shapes in the planner's 3D view: each run of bridged segments is planned exactly
    /// as the server would plan it for a quote (<see cref="BridgePlanner"/>, on this client's own terrain) and
    /// meshed (<see cref="BridgePreviewMesher"/>), on a background thread, into a model the map renderer draws.
    /// One job runs at a time; a draft that changes meanwhile is planned again when it finishes.
    /// </summary>
    internal sealed class BridgePreview
    {
        static readonly Color StructureColor = new Color(198, 200, 206);
        static readonly Color PavementColor = new Color(84, 86, 94);

        readonly List<SceneModel> _models = new List<SceneModel>();
        CartographyMapRenderer _renderer;
        bool _running;
        bool _hasShown;
        long _shownKey;
        long _runningKey;
        bool _hasPending;
        long _pendingKey;
        MyPlanet _pendingPlanet;
        PacketRoadQuoteRequest _pendingRequest;
        // Bumped by Detach: a job that finishes for an earlier renderer is dropped.
        int _generation;

        /// <summary>Game thread: the models changed, so the map needs a new frame.</summary>
        public Action Changed;

        public void Attach(CartographyMapRenderer renderer)
        {
            Detach();
            _renderer = renderer;
        }

        public void Detach()
        {
            _generation++;
            if (_renderer != null)
            {
                for (int i = 0; i < _models.Count; i++)
                    _renderer.RemoveModel(_models[i]);
            }
            _models.Clear();
            _renderer = null;
            _hasShown = false;
            _hasPending = false;
            _pendingPlanet = null;
            _pendingRequest = null;
        }

        /// <summary>
        /// Game thread: shows the bridges of <paramref name="request"/> (the draft as a quote request); null, or a
        /// request without bridges, hides them. Cheap when the draft has not changed.
        /// </summary>
        public void Update(MyPlanet planet, PacketRoadQuoteRequest request)
        {
            if (_renderer == null)
                return;
            bool any = planet != null && !planet.MarkedForClose && request != null && request.Bridge != null &&
                       request.Geometry != null && request.Parameters != null;
            long key = any ? Key(planet, request) : 0L;
            if (_running)
            {
                _hasPending = key != _runningKey;
                _pendingKey = key;
                _pendingPlanet = any ? planet : null;
                _pendingRequest = any ? request : null;
                return;
            }
            if (_hasShown && key == _shownKey)
                return;
            if (!any)
            {
                Apply(null, key);
                return;
            }
            Start(planet, request, key);
        }

        void Start(MyPlanet planet, PacketRoadQuoteRequest request, long key)
        {
            _running = true;
            _runningKey = key;
            int generation = _generation;
            List<BridgePreviewMesher.Mesh> meshes = null;
            MyAPIGateway.Parallel.StartBackground(
                () => meshes = Compute(planet, request),
                () =>
                {
                    _running = false;
                    if (generation != _generation)
                        return;
                    Apply(meshes, key);
                    if (!_hasPending)
                        return;
                    _hasPending = false;
                    MyPlanet nextPlanet = _pendingPlanet;
                    PacketRoadQuoteRequest nextRequest = _pendingRequest;
                    _pendingPlanet = null;
                    _pendingRequest = null;
                    if (nextRequest == null)
                        Apply(null, _pendingKey);
                    else
                        Start(nextPlanet, nextRequest, _pendingKey);
                });
        }

        // Background thread. A run the planner refuses (too short, too steep, no ground) has no preview; the
        // draft's own checks and the quote say why.
        static List<BridgePreviewMesher.Mesh> Compute(MyPlanet planet, PacketRoadQuoteRequest request)
        {
            var meshes = new List<BridgePreviewMesher.Mesh>();
            try
            {
                int segments = request.Geometry.Length / Constants.GEOMETRY_VALUES_PER_SEGMENT;
                List<BridgeRun> runs = request.Bridge.Runs(segments);
                var curves = new List<RoadCurve>();
                for (int run = 0; run < runs.Count; run++)
                {
                    if (planet.MarkedForClose)
                        break;
                    Vector3D start;
                    Vector3D end;
                    double[] pins;
                    if (!BridgePlanner.TryReadRun(planet, request.Geometry, runs[run], request.Bridge.NodeLifts,
                            curves, out start, out end, out pins))
                        continue;
                    string error;
                    BridgePlan plan = BridgePlanner.Plan(planet, curves, start, end, pins,
                        BridgePlanner.OptionsFor(request.Parameters, request.Bridge, request.Bridge.DesignFor(run)),
                        out error);
                    if (plan == null)
                        continue;
                    BridgePreviewMesher.Mesh mesh = BridgePreviewMesher.Build(plan.Shape, StructureColor,
                        PavementColor);
                    if (mesh != null)
                        meshes.Add(mesh);
                }
            }
            catch (Exception error)
            {
                MyLog.Default.WriteLine("[HighwayEngineer] Bridge preview: " + error);
            }
            return meshes;
        }

        void Apply(List<BridgePreviewMesher.Mesh> meshes, long key)
        {
            _hasShown = true;
            _shownKey = key;
            if (_renderer == null)
                return;
            int count = meshes == null ? 0 : meshes.Count;
            for (int i = 0; i < count; i++)
            {
                if (i == _models.Count)
                {
                    var model = new SceneModel();
                    _models.Add(model);
                    _renderer.AddModel(model);
                }
                BridgePreviewMesher.Mesh mesh = meshes[i];
                _models[i].Visible = _models[i].SetMesh(mesh.Vertices, mesh.Indices, mesh.TriangleColors);
                _models[i].WorldMatrix = MatrixD.CreateTranslation(mesh.Origin);
            }
            for (int i = count; i < _models.Count; i++)
            {
                _models[i].Visible = false;
                _models[i].ClearMesh();
            }
            Action changed = Changed;
            if (changed != null)
                changed();
        }

        static long Key(MyPlanet planet, PacketRoadQuoteRequest request)
        {
            unchecked
            {
                long hash = planet.EntityId;
                double[] geometry = request.Geometry;
                for (int i = 0; i < geometry.Length; i++)
                    hash = hash * 31 + BitConverter.DoubleToInt64Bits(geometry[i]);
                RoadBuildParameters road = request.Parameters;
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(road.WidthMeters);
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(road.DepthMeters);
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(road.MaxSlopeDegrees);
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(road.ShoulderAngleDegrees);
                BridgeBuildParameters bridge = request.Bridge;
                hash = hash * 31 + (int)bridge.Design;
                hash = hash * 31 + BitConverter.DoubleToInt64Bits(bridge.SegmentMeters);
                if (bridge.Segments != null)
                {
                    for (int i = 0; i < bridge.Segments.Length; i++)
                        hash = hash * 31 + (bridge.Segments[i] ? 1 : 2);
                }
                if (bridge.NodeLifts != null)
                {
                    for (int i = 0; i < bridge.NodeLifts.Length; i++)
                        hash = hash * 31 + BitConverter.DoubleToInt64Bits(bridge.NodeLifts[i]);
                }
                return hash;
            }
        }
    }
}

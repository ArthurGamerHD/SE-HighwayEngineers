using System;
using Acrux.Cartography.Maps;
using Sandbox.Game.Entities;
using VRageMath;

namespace HighwayEngineer.Client.Maps
{
    /// <summary>
    /// Projects planet surface points onto the Planet Map, using the projection the Acrux renderer reports for
    /// the frame (<see cref="CartographyMapRenderer.TryGetPlanetSurfaceProjection"/>). It is built once and then
    /// projects locally, so the planner's road curves (thousands of points a frame) cost one call into the UI
    /// mod. Points on the hidden hemisphere, or far off the surface, are not projected.
    /// <para>
    /// In the 3D surface view the map is a perspective camera over displaced terrain
    /// (<see cref="CartographyMapRenderer.TryGetPlanetSurfaceViewCamera"/>): a point is then stood on the ground
    /// the view draws in its direction, which costs one more call into the UI mod per point.
    /// </para>
    /// </summary>
    public struct PlanetSurfaceProjector
    {
        const float VectorEpsilon = 0.000001f;
        const double SurfaceTargetToleranceMeters = 10000d;
        const double SurfaceTerrainMarginMeters = 5000d;
        const double SurfaceMaximumRadiusRatio = 0.25d;
        const double SurfaceMinimumAllowedToleranceMeters = 1000d;

        readonly MyPlanet _planet;
        readonly Vector3 _view;
        readonly Vector3 _right;
        readonly Vector3 _up;
        readonly Vector2 _center;
        readonly Vector2 _fallback;
        // The 3D surface view: a pinhole camera in planet-local metres, and what stands a point on its ground.
        readonly bool _perspective;
        readonly CartographyMapRenderer _renderer;
        readonly MapData _data;
        readonly MapViewState _state;
        readonly Vector3D _cameraPosition;
        readonly Vector3D _cameraRight;
        readonly Vector3D _cameraUp;
        readonly Vector3D _cameraForward;
        readonly double _focalLengthPixels;
        readonly double _nearDepth;

        public float DisplayRadius { get; }

        PlanetSurfaceProjector(MyPlanet planet, Vector3 view, Vector3 right, Vector3 up, Vector2 center,
            float displayRadius, Vector2 fallback)
        {
            _planet = planet;
            _view = view;
            _right = right;
            _up = up;
            _center = center;
            _fallback = fallback;
            DisplayRadius = displayRadius;
            _perspective = false;
            _renderer = null;
            _data = null;
            _state = null;
            _cameraPosition = _cameraRight = _cameraUp = _cameraForward = Vector3D.Zero;
            _focalLengthPixels = _nearDepth = 0d;
        }

        PlanetSurfaceProjector(MyPlanet planet, CartographyMapRenderer renderer, MapData data, MapViewState state,
            Vector3D position, Vector3D right, Vector3D up, Vector3D forward, double focalLengthPixels,
            Vector2 center, double nearDepth, Vector2 fallback)
        {
            _planet = planet;
            _view = _right = _up = Vector3.Zero;
            _center = center;
            _fallback = fallback;
            _perspective = true;
            _renderer = renderer;
            _data = data;
            _state = state;
            _cameraPosition = position;
            _cameraRight = right;
            _cameraUp = up;
            _cameraForward = forward;
            _focalLengthPixels = focalLengthPixels;
            _nearDepth = nearDepth;
            // The globe radius that would show the ground under the camera at the same scale: callers size
            // their curve sampling by it.
            double height = Math.Max(1d, position.Length() - planet.AverageRadius);
            DisplayRadius = (float)Math.Min(1e9, focalLengthPixels * planet.AverageRadius / height);
        }

        /// <summary>True in the 3D surface view, where <see cref="DisplayRadius"/> is only an equivalent scale.</summary>
        public bool IsPerspective => _perspective;

        /// <summary>The projector for the planet <paramref name="renderer"/> shows in <paramref name="mapBounds"/> this frame.</summary>
        public static bool TryCreate(CartographyMapRenderer renderer, RectangleF mapBounds, MapData data,
            MapViewState state, out PlanetSurfaceProjector projector)
        {
            projector = default(PlanetSurfaceProjector);
            MyPlanet planet;
            Vector3 view;
            Vector3 right;
            Vector3 up;
            Vector2 center;
            float displayRadius;
            if (renderer == null || state == null)
                return false;
            if (state.SurfaceViewActive)
            {
                Vector3D position;
                Vector3D cameraRight;
                Vector3D cameraUp;
                Vector3D forward;
                double focalLengthPixels;
                double nearDepth;
                if (!renderer.TryGetPlanetSurfaceViewCamera(mapBounds, data, state, out planet, out position,
                        out cameraRight, out cameraUp, out forward, out focalLengthPixels, out center,
                        out nearDepth) || planet == null)
                    return false;
                projector = new PlanetSurfaceProjector(planet, renderer, data, state, position, cameraRight,
                    cameraUp, forward, focalLengthPixels, center, nearDepth, mapBounds.Center);
                return true;
            }
            if (!renderer.TryGetPlanetSurfaceProjection(mapBounds, data, state,
                    out planet, out view, out right, out up, out center, out displayRadius) || planet == null)
                return false;
            projector = new PlanetSurfaceProjector(planet, view, right, up, center, displayRadius, mapBounds.Center);
            return true;
        }

        /// <summary>One point; for many, create a projector once and call <see cref="TryProject"/>.</summary>
        public static bool TryProject(CartographyMapRenderer renderer, RectangleF mapBounds, Vector3D worldPosition,
            MapData data, MapViewState state, out Vector2 screenPosition)
        {
            PlanetSurfaceProjector projector;
            if (!TryCreate(renderer, mapBounds, data, state, out projector))
            {
                screenPosition = mapBounds.Center;
                return false;
            }
            return projector.TryProject(worldPosition, out screenPosition);
        }

        public bool TryProject(Vector3D worldPosition, out Vector2 screenPosition)
        {
            screenPosition = _fallback;
            if (_planet == null || !IsPositionNearPlanetSurface(_planet, worldPosition))
                return false;

            Vector3D radialWorld = worldPosition - _planet.WorldMatrix.Translation;
            if (radialWorld.Normalize() <= 1e-9)
                return false;

            MatrixD matrix = _planet.WorldMatrix;
            var radialLocal = new Vector3(
                (float)Vector3D.Dot(radialWorld, matrix.Right),
                (float)Vector3D.Dot(radialWorld, matrix.Up),
                (float)Vector3D.Dot(radialWorld, matrix.Backward));
            if (radialLocal.Normalize() <= VectorEpsilon)
                return false;

            if (_perspective)
                return TryProjectPerspective(radialLocal, out screenPosition);

            // Points on the hidden hemisphere are not projected.
            if (Vector3.Dot(radialLocal, _view) < 0f)
                return false;

            screenPosition = new Vector2(
                _center.X + Vector3.Dot(radialLocal, _right) * DisplayRadius,
                _center.Y - Vector3.Dot(radialLocal, _up) * DisplayRadius);
            return !float.IsNaN(screenPosition.X) && !float.IsInfinity(screenPosition.X) &&
                   !float.IsNaN(screenPosition.Y) && !float.IsInfinity(screenPosition.Y);
        }

        bool TryProjectPerspective(Vector3 radialLocal, out Vector2 screenPosition)
        {
            screenPosition = _fallback;
            double cameraRadius = _cameraPosition.Length();
            if (cameraRadius <= 1d)
                return false;
            // Past the horizon the ground is behind the planet's own curve. The lowest terrain sets the horizon,
            // with a little slack for hills that rise back into view.
            double floor = Math.Max(1d, _planet.MinimumRadius);
            double horizon = Math.Min(1d, floor / cameraRadius);
            double facing = Vector3D.Dot((Vector3D)radialLocal, _cameraPosition) / cameraRadius;
            if (facing < horizon - 0.02d)
                return false;

            double radius;
            if (!_renderer.TryGetPlanetSurfaceViewGroundRadius(_data, _state, radialLocal, out radius))
                return false;
            Vector3D offset = (Vector3D)radialLocal * radius - _cameraPosition;
            double depth = Vector3D.Dot(offset, _cameraForward);
            if (!(depth > _nearDepth))
                return false;
            double scale = _focalLengthPixels / depth;
            screenPosition = new Vector2(
                _center.X + (float)(Vector3D.Dot(offset, _cameraRight) * scale),
                _center.Y - (float)(Vector3D.Dot(offset, _cameraUp) * scale));
            return !float.IsNaN(screenPosition.X) && !float.IsInfinity(screenPosition.X) &&
                   !float.IsNaN(screenPosition.Y) && !float.IsInfinity(screenPosition.Y);
        }

        static bool IsPositionNearPlanetSurface(MyPlanet planet, Vector3D position)
        {
            double averageRadius = planet.AverageRadius > 0d ? planet.AverageRadius : planet.MaximumRadius;
            if (averageRadius <= 0d)
                return false;
            double maximumRadius = planet.MaximumRadius > 0d ? planet.MaximumRadius : averageRadius;
            double terrainVariation = Math.Abs(maximumRadius - averageRadius);
            double desiredTolerance = Math.Max(SurfaceTargetToleranceMeters,
                terrainVariation + SurfaceTerrainMarginMeters);
            double tolerance = Math.Max(SurfaceMinimumAllowedToleranceMeters,
                Math.Min(desiredTolerance, averageRadius * SurfaceMaximumRadiusRatio));
            double radialDistance = Vector3D.Distance(position, planet.WorldMatrix.Translation);
            return Math.Abs(radialDistance - averageRadius) <= tolerance;
        }
    }
}

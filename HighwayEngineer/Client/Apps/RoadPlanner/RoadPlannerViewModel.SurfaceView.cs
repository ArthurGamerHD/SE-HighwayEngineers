using System;
using Acrux.Cartography.Maps;
using HighwayEngineer.Client.Roads;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Networking;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadPlanner
{
    // The planner's 3D view: the Acrux map's surface view (a camera orbiting displaced terrain), switched on and
    // off by the player instead of by zooming, because the planner's flat globe has no maximum zoom to hand over at.
    internal sealed partial class RoadPlannerViewModel
    {
        // The globe scale the surface view's farthest camera distance stands for (MapViewState.MaximumZoom while
        // the view is open): about the whole planet.
        const float SURFACE_VIEW_FARTHEST_ZOOM = 1f;
        // Camera distance = planet radius / (this * globe zoom): the perspective view then shows the ground at
        // the scale the globe had (the map's 60 degree field of view over the globe's 0.47 short-side radius).
        const float SURFACE_VIEW_SCALE = 0.5427f;
        const int BRIDGE_PREVIEW_FRAMES = 15;

        readonly BridgePreview _bridgePreview = new BridgePreview();

        public bool IsSurfaceViewActive => _viewState.SurfaceViewActive;

        void ResetSurfaceView()
        {
            _viewState.SurfaceViewActive = false;
            _viewState.SurfaceViewOnly = false;
            _viewState.MaximumZoom = float.MaxValue;
            _viewState.BlocksVisible = true;
            _bridgePreview.Changed = MarkTextureDirty;
        }

        /// <summary>Switches between the flat map and the 3D view of the ground under its center.</summary>
        public void ToggleSurfaceView()
        {
            if (_mapRenderer == null)
                return;
            if (_viewState.SurfaceViewActive)
            {
                ExitSurfaceView();
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_SurfaceViewOff");
            }
            else if (EnterSurfaceView())
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_SurfaceViewOn");
            else
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_SurfaceViewLoading");
        }

        bool EnterSurfaceView()
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            if (planet == null || planet.Radius <= 0f || !MapPlanetsReady())
                return false;
            float zoom = Math.Max(_viewState.Zoom, 0.0001f);
            _viewState.MaximumZoom = SURFACE_VIEW_FARTHEST_ZOOM;
            // Whole relief at every distance, so the draft and the bridge preview stand on the ground drawn.
            _viewState.SurfaceViewOnly = true;
            if (!_mapRenderer.EnterPlanetSurfaceView(_mapData, _viewState))
            {
                // The planet's terrain heights are still loading.
                _viewState.SurfaceViewOnly = false;
                _viewState.MaximumZoom = float.MaxValue;
                return false;
            }
            // Opened looking straight down from where the flat map showed the same ground at the same size; the
            // renderer keeps the distance between its closest and the farthest above.
            _viewState.SurfaceViewDistance = Math.Max(CartographyMapRenderer.SURFACE_VIEW_MINIMUM_DISTANCE,
                planet.Radius / (SURFACE_VIEW_SCALE * zoom));
            _viewState.SurfaceViewYaw = 0f;
            _viewState.SurfaceViewPitch = MathHelper.PiOver2;
            _planetGrabActive = false;
            _overlayPresentation.Reset();
            _textureDirty = true;
            return true;
        }

        void ExitSurfaceView()
        {
            PlanetSnapshot planet = ResolveFocusedPlanet();
            float distance = _viewState.SurfaceViewDistance;
            _mapRenderer.ExitPlanetSurfaceView(_viewState);
            _viewState.SurfaceViewOnly = false;
            _viewState.MaximumZoom = float.MaxValue;
            if (planet != null && planet.Radius > 0f && distance > 0f && !float.IsInfinity(distance))
                _viewState.Zoom = Math.Max(PLANET_MINIMUM_ZOOM, planet.Radius / (SURFACE_VIEW_SCALE * distance));
            _overlayPresentation.Reset();
            _textureDirty = true;
        }

        /// <summary>Middle drag: turns and tilts the 3D view, opening it first when the map is flat.</summary>
        public void MiddleDragMap(Vector2 delta)
        {
            if (_mapRenderer == null || delta == Vector2.Zero)
                return;
            if (!_viewState.SurfaceViewActive)
            {
                if (!EnterSurfaceView())
                    return;
                RoadEditorStatus = LocHelper.GetLoc("HighwayEngineer_Status_SurfaceViewOn");
            }
            if (_mapRenderer.OrbitPlanetSurfaceView(_viewState, delta))
                _textureDirty = true;
        }

        void PanSurfaceView(Vector2 delta)
        {
            if (_mapRenderer == null)
                return;
            UnlockPlanetOrientation();
            if (_viewState.FollowPlayer)
                SetFollowPlayer(false);
            if (_mapRenderer.PanPlanetSurfaceView(_mapData, _viewState, delta,
                    Math.Min(_mapBounds.Width, _mapBounds.Height)))
                _textureDirty = true;
        }

        void ZoomSurfaceView(int wheelDelta)
        {
            if (_mapRenderer != null && _mapRenderer.ScrollPlanetSurfaceView(_mapData, _viewState, wheelDelta))
                _textureDirty = true;
        }

        // The roll keys turn the camera around its target in the 3D view.
        void TurnSurfaceView(float angle)
        {
            _viewState.SurfaceViewYaw = MathHelper.WrapAngle(_viewState.SurfaceViewYaw + angle);
            _textureDirty = true;
        }

        // The flat globe's radius, in canvas pixels, that draws the ground under the camera at the 3D view's scale:
        // the road overlay sizes its strokes by it.
        float SurfaceViewGlobeRadius(PlanetSnapshot planet, int canvasShortSide)
        {
            double focal = canvasShortSide * 0.5d / Math.Tan(Math.PI / 6d);
            double distance = Math.Max(1d, _viewState.SurfaceViewDistance);
            return (float)Math.Min(1e9d, focal * planet.Radius / distance);
        }

        void UpdateSurfaceView()
        {
            if (_mapRenderer == null)
                return;
            if (!_viewState.SurfaceViewActive)
            {
                // The renderer closed the view itself (the planet's terrain went away under it).
                if (_viewState.SurfaceViewOnly)
                    ExitSurfaceView();
                return;
            }
            // The scene refines over several frames while terrain, voxels and block models load.
            if (_mapRenderer.IsSceneLoading)
                _textureDirty = true;
            if (_frame % BRIDGE_PREVIEW_FRAMES != 0)
                return;
            // Another planet took the focus, or this one's terrain is no longer loaded: back to the flat map,
            // which always draws.
            if (!_mapRenderer.IsPlanetSurfaceViewAvailable(_mapData, _viewState))
            {
                ExitSurfaceView();
                return;
            }
            PlanetSnapshot planet = ResolveFocusedPlanet();
            PacketRoadQuoteRequest request = null;
            if (planet != null && planet.Planet != null && RoadsVisible && _roadNodes.Count >= 2 &&
                !TryBuildRoadQuoteRequest(out request))
                request = null;
            _bridgePreview.Update(planet == null ? null : planet.Planet, request);
        }
    }
}

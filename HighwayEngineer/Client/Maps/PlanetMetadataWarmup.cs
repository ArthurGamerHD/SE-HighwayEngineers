using System;
using System.Collections.Generic;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using ProceduralCubemapApi.Api;
using Sandbox.ModAPI;

namespace HighwayEngineer.Client.Maps
{
    /// <summary>
    /// Loads the voxel cubemap API's metadata for runtime planets on a worker thread. The first
    /// request for a planet unpacks its data archive, which takes seconds; the map renderer would
    /// make that request on the game thread and freeze the game, so the planner waits for
    /// <see cref="IsReady"/> before it asks the renderer for anything.
    /// The handle is kept until the session unloads: the API drops its cached metadata when the
    /// last handle to it closes, and the renderer opens its own handle only once it draws.
    /// </summary>
    static class PlanetMetadataWarmup
    {
        static readonly Dictionary<long, bool> Ready = new Dictionary<long, bool>();
        static readonly List<PlanetMetadataProvider> Held = new List<PlanetMetadataProvider>();
        static int _generation;

        /// <summary>
        /// True once the planet's metadata is loaded (or there is none to load, or loading failed,
        /// so the renderer reports it). The first call for a planet starts loading it. Game thread.
        /// </summary>
        public static bool IsReady(long planetEntityId)
        {
            if (planetEntityId == 0L) return true;
            bool ready;
            if (Ready.TryGetValue(planetEntityId, out ready)) return ready;

            ApiProvider api;
            string error;
            if (MyAPIGateway.Parallel == null || !RoadApi.TryValidate(out api, out error))
            {
                // No API (the renderer draws without it) or no worker threads: nothing to wait for.
                Ready[planetEntityId] = true;
                return true;
            }

            Ready[planetEntityId] = false;
            int generation = _generation;
            PlanetMetadataProvider provider = null;
            Exception failure = null;
            MyAPIGateway.Parallel.Start(() =>
            {
                // Same request as the renderer's, so it then finds the API's cached metadata.
                try { provider = api.GetPlanetMetadata(planetEntityId, false); }
                catch (Exception e) { failure = e; }
            }, () =>
            {
                if (generation != _generation)
                {
                    if (provider != null) provider.Close();
                    return;
                }
                if (failure != null)
                    ErrorHandlerHelper.LogError(failure, typeof(PlanetMetadataWarmup));
                if (provider != null)
                    Held.Add(provider);
                Ready[planetEntityId] = true;
            });
            return false;
        }

        public static void Unload()
        {
            ++_generation;
            foreach (PlanetMetadataProvider provider in Held)
            {
                try { provider.Close(); }
                catch (Exception) { }
            }
            Held.Clear();
            Ready.Clear();
        }
    }
}

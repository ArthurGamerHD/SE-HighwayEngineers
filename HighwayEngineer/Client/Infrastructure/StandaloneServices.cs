using System;
using System.Collections.Generic;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.ModAPI;
using VRage.Utils;

namespace HighwayEngineer.Client.Infrastructure
{
    internal static class StandaloneConstants
    {
        // "MINI". Used only to derive this mod's private inter-mod API reply channel.
        public const long COMMUNICATION_ID = 0x52444352L;
    }

    internal static class PlanetRegistry
    {
        static readonly HashSet<IMyEntity> Entities = new HashSet<IMyEntity>();
        static readonly Dictionary<long, MyPlanet> Planets = new Dictionary<long, MyPlanet>();

        public static Dictionary<long, MyPlanet> PlanetsById
        {
            get
            {
                Refresh();
                return Planets;
            }
        }

        static void Refresh()
        {
            Entities.Clear();
            Planets.Clear();
            if (MyAPIGateway.Entities == null)
                return;

            MyAPIGateway.Entities.GetEntities(Entities, IsPlanet);
            foreach (IMyEntity entity in Entities)
            {
                MyPlanet planet = entity as MyPlanet;
                if (planet != null && !planet.MarkedForClose)
                    Planets[planet.EntityId] = planet;
            }
        }

        static bool IsPlanet(IMyEntity entity)
        {
            return entity is MyPlanet;
        }
    }

    internal static class ErrorHandlerHelper
    {
        public static void LogError(Exception error, Type source)
        {
            MyLog.Default.WriteLineAndConsole(string.Format(
                "[Minimap] {0}: {1}",
                source == null ? "Unknown" : source.FullName,
                error));
        }
    }
}

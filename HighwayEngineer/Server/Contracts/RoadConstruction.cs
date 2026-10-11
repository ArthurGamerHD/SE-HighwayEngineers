using System;
using ProceduralCubemapApi.Api;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using Sandbox.Game.Entities;
using Sandbox.ModAPI;
using VRage.Game;

namespace HighwayEngineer.Server.Contracts
{
    /// <summary>Builds a paid road through the voxel cubemap API, on the server (its bridges are built apart).</summary>
    public static class RoadConstruction
    {
        /// <summary>Tags a contract's voxel API modification, so it can be recognised in the API's listing.</summary>
        public static string CustomData(long contractId) => "HighwayEngineer:contract=" + contractId;

        /// <summary>
        /// Queues every segment that is not bridged and pushes; <paramref name="done"/> gets the
        /// outcome (also when the push could not start) and the id of the API modification the
        /// segments became, line k being the k-th segment built as road. The API syncs the change
        /// to clients.
        /// </summary>
        public static void Build(RoadContractRecord record, Action<bool, string, string> done)
        {
            ModificationTemplate template = null;
            try
            {
                ApiProvider api;
                string error;
                if (!RoadApi.TryValidate(out api, out error))
                {
                    done(false, error, null);
                    return;
                }
                var planet = MyAPIGateway.Entities.GetEntityById(record.PlanetId) as MyPlanet;
                if (planet == null)
                {
                    done(false, LocHelper.Message("HighwayEngineer_Error_PlanetGone"), null);
                    return;
                }
                template = api.GetModificationTemplate(planet.EntityId);
                if (template == null)
                {
                    done(false, LocHelper.Message("HighwayEngineer_Error_NoTemplate"), null);
                    return;
                }
                RoadBuildParameters parameters = record.Parameters;
                byte material = FindOrAddMaterial(template, parameters.Material, parameters.DepthMeters);
                int perSegment = Constants.GEOMETRY_VALUES_PER_SEGMENT;
                var segment = new double[perSegment];
                for (int offset = 0; offset + perSegment <= record.Geometry.Length; offset += perSegment)
                {
                    if (record.IsBridged(offset / perSegment)) continue;
                    Array.Copy(record.Geometry, offset, segment, 0, perSegment);
                    template.DrawLineSmooth("material", material, (double[])segment.Clone(), planet.AverageRadius,
                        parameters.WidthMeters, parameters.AverageHeight, parameters.ShoulderAngleDegrees,
                        parameters.MaxSlopeDegrees, parameters.SegmentDistanceMeters);
                }
                template.SetCustomData(CustomData(record.ContractId));
                string modificationId = template.GetModificationId();
                ModificationTemplate pending = template;
                template = null;
                pending.Push((success, message) =>
                {
                    try { pending.Close(); }
                    catch (Exception) { }
                    done(success, message, modificationId);
                });
            }
            catch (Exception error)
            {
                done(false, error.Message, null);
            }
            finally
            {
                if (template != null)
                    template.Close();
            }
        }

        /// <summary>The fill depth of planet material table value <paramref name="value"/>; <paramref name="fallback"/> when unknown.</summary>
        internal static double MaterialDepth(ModificationTemplate template, int value, double fallback)
        {
            MyObjectBuilder_PlanetGeneratorDefinition generator = template.GetPlanetGenerator();
            if (generator != null && generator.CustomMaterialTable != null)
                foreach (MyPlanetMaterialDefinition material in generator.CustomMaterialTable)
                    if (material != null && material.Value == value)
                        return material.MaxDepth;
            return fallback;
        }

        internal static byte FindOrAddMaterial(ModificationTemplate template, string subtype, double depth)
        {
            MyObjectBuilder_PlanetGeneratorDefinition generator = template.GetPlanetGenerator();
            if (generator != null && generator.CustomMaterialTable != null)
                foreach (MyPlanetMaterialDefinition material in generator.CustomMaterialTable)
                    if (material != null && material.Value != 0 && material.Value != 255 &&
                        string.Equals(material.Material, subtype, StringComparison.OrdinalIgnoreCase) &&
                        Math.Abs(material.MaxDepth - depth) < 0.001f)
                        return material.Value;
            return template.AddMaterial(subtype, (float)depth);
        }
    }
}

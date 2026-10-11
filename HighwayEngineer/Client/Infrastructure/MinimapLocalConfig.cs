using System;
using System.IO;
using Acrux.Core.Input;
using Sandbox.ModAPI;
using VRage.Input;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;

namespace HighwayEngineer.Client.Infrastructure
{
    public enum MinimapStyleTheme
    {
        Fluent,
        SpaceEngineers
    }

    /// <summary>GPS and radio signal visibility for one map scope.</summary>
    public sealed class MapDisplayConfig
    {
        public bool DisplayMyGps;
        public bool IncludeRadioSignals = true;
    }

    /// <summary>
    /// The planner's road settings and auto-road weights, kept between sessions on this
    /// machine. Field initializers are the defaults a right click resets to.
    /// </summary>
    public sealed class RoadSettingsConfig
    {
        public string Material = "Soil";
        public double WidthMeters = 50.0;
        public double DepthMeters = 2.5f;
        public float MaxSlopeDegrees = 8f;
        public float ShoulderAngleDegrees = 35f;
        public float SegmentDistanceMeters = 100f;
        public bool AverageHeight = true;
        public bool FillShoulders = true;
        /// <summary>Paint guide lines on the roads built.</summary>
        public bool GuideLines;
        public float AutoRouteDistance = 1f;
        public float AutoRouteInclination = 1f;
        /// <summary>Per 1000 m³ of cut and fill, as the auto-road slider shows it.</summary>
        public float AutoRouteEarthwork = 2f;

        public static readonly RoadSettingsConfig Defaults = new RoadSettingsConfig();

        /// <summary>Clamps values to the planner controls' ranges.</summary>
        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(Material)) Material = Defaults.Material;
            WidthMeters = Clamp(WidthMeters, RoadLimits.MinWidthMeters, RoadLimits.MaxWidthMeters,
                Defaults.WidthMeters);
            DepthMeters = Clamp(DepthMeters, RoadLimits.MinDepthMeters, RoadLimits.MaxDepthMeters,
                Defaults.DepthMeters);
            MaxSlopeDegrees = (float)Clamp(MaxSlopeDegrees, RoadLimits.MinSlopeDegrees, RoadLimits.MaxSlopeDegrees,
                Defaults.MaxSlopeDegrees);
            ShoulderAngleDegrees = (float)Clamp(ShoulderAngleDegrees, RoadLimits.MinShoulderAngleDegrees,
                RoadLimits.MaxShoulderAngleDegrees, Defaults.ShoulderAngleDegrees);
            SegmentDistanceMeters = (float)Clamp(SegmentDistanceMeters, RoadLimits.MinSegmentDistanceMeters,
                RoadLimits.MaxSegmentDistanceMeters, Defaults.SegmentDistanceMeters);
            AutoRouteDistance = (float)Clamp(AutoRouteDistance, RoadLimits.MinAutoRouteWeight,
                RoadLimits.MaxAutoRouteDistance, Defaults.AutoRouteDistance);
            AutoRouteInclination = (float)Clamp(AutoRouteInclination, RoadLimits.MinAutoRouteWeight,
                RoadLimits.MaxAutoRouteInclination, Defaults.AutoRouteInclination);
            AutoRouteEarthwork = (float)Clamp(AutoRouteEarthwork, RoadLimits.MinAutoRouteWeight,
                RoadLimits.MaxAutoRouteEarthwork, Defaults.AutoRouteEarthwork);
        }

        static double Clamp(double value, double minimum, double maximum, double fallback)
        {
            return double.IsNaN(value) || double.IsInfinity(value)
                ? fallback
                : Math.Max(minimum, Math.Min(maximum, value));
        }
    }

    /// <summary>
    /// The planner's bridge settings, kept between sessions on this machine. Width and fill depth
    /// are the road settings'. Field initializers are the defaults a right click resets to.
    /// </summary>
    public sealed class BridgeSettingsConfig
    {
        /// <summary>Voxel material of the deck, parapets and structure (the Concrete Tool mod's when loaded).</summary>
        public string StructureMaterial = ConcreteMod.StructureMaterial;
        /// <summary>Voxel material of the road fill on the deck.</summary>
        public string PavementMaterial = "Soil";
        /// <summary>Length of each straight deck segment, between expansion joints.</summary>
        public float SegmentMeters = 100f;
        public BridgeDesign Design = BridgeDesign.Auto;
        /// <summary>Draft segments that cross a gap become bridges unless set otherwise.</summary>
        public bool AutoDetect = true;

        // Built on use: its structure material depends on the mods of the world loaded.
        public static BridgeSettingsConfig Defaults => new BridgeSettingsConfig();

        /// <summary>Clamps values to the planner controls' ranges.</summary>
        public void Normalize()
        {
            // Our concrete is disabled beside the Concrete Tool mod's.
            if (string.IsNullOrWhiteSpace(StructureMaterial) ||
                ConcreteMod.Present && string.Equals(StructureMaterial, ConcreteMod.OwnMaterial, StringComparison.OrdinalIgnoreCase))
                StructureMaterial = Defaults.StructureMaterial;
            if (string.IsNullOrWhiteSpace(PavementMaterial)) PavementMaterial = Defaults.PavementMaterial;
            SegmentMeters = float.IsNaN(SegmentMeters) || float.IsInfinity(SegmentMeters)
                ? Defaults.SegmentMeters
                : (float)Math.Max(RoadLimits.MinSegmentDistanceMeters,
                    Math.Min(RoadLimits.MaxSegmentDistanceMeters, SegmentMeters));
            if (!Enum.IsDefined(typeof(BridgeDesign), Design)) Design = BridgeDesign.Auto;
        }
    }

    public sealed class MinimapLocalConfig
    {
        const string FileName = "HighwayEngineerConfig.xml";
        // 9: the Minimap mod's HUD settings (anchor, scale, offsets, minimap keys) were removed.
        // 10: the Star Map mode and its settings were removed.
        const int CurrentConfigVersion = 10;
        static MinimapLocalConfig _current;

        public int ConfigVersion;

        public MinimapStyleTheme Theme = MinimapStyleTheme.Fluent;

        // The full-screen switch is the master; PlanetaryMap is the map's own
        // GPS/radio visibility (its waypoint button edits it).
        public MapDisplayConfig FullScreen = new MapDisplayConfig();
        public MapDisplayConfig PlanetaryMap = new MapDisplayConfig { DisplayMyGps = true };

        /// <summary>Road settings and auto-road weights (older files have none: defaults).</summary>
        public RoadSettingsConfig Road = new RoadSettingsConfig();

        /// <summary>Bridge settings (older files have none: defaults).</summary>
        public BridgeSettingsConfig Bridge = new BridgeSettingsConfig();

        /// <summary>Paints a double yellow centre line on built roads near the camera.</summary>
        // TODO: default to off once the markings are tested in game; on for now so test maps show them.
        public bool RoadMarkings = true;

        public Key ZoomInKey = Key.Add;
        public Key ZoomOutKey = Key.Subtract;
        public Key RollLeftKey = Key.Q;
        public Key RollRightKey = Key.E;
        public Key OpenMapKey = Key.U;
        public Key CenterMapKey = Key.C;
        /// <summary>Lifts the selected road node 10 m as a bridge; lower takes it back down.</summary>
        public Key BridgeRaiseKey = Key.OemPlus;
        public Key BridgeLowerKey = Key.OemMinus;
        /// <summary>Switches the planner between the flat map and the 3D view.</summary>
        public Key SurfaceViewKey = Key.V;

        public static MinimapLocalConfig Current
        {
            get
            {
                if (_current == null)
                    _current = LoadCore();
                return _current;
            }
        }

        public static MinimapLocalConfig Load()
        {
            return Current;
        }

        static MinimapLocalConfig CreateDefault()
        {
            var config = new MinimapLocalConfig
            {
                ConfigVersion = CurrentConfigVersion
            };
            config.Normalize();
            return config;
        }

        static MinimapLocalConfig LoadCore()
        {
            MinimapLocalConfig fallback = CreateDefault();
            if (MyAPIGateway.Utilities == null ||
                !MyAPIGateway.Utilities.FileExistsInLocalStorage(
                    FileName,
                    typeof(MinimapLocalConfig)))
            {
                return fallback;
            }

            try
            {
                using (TextReader reader = MyAPIGateway.Utilities.ReadFileInLocalStorage(
                           FileName,
                           typeof(MinimapLocalConfig)))
                {
                    MinimapLocalConfig config = MyAPIGateway.Utilities
                        .SerializeFromXML<MinimapLocalConfig>(reader.ReadToEnd());
                    if (config == null)
                        return fallback;

                    config.NormalizeAndUpgrade();
                    return config;
                }
            }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(MinimapLocalConfig));
                return fallback;
            }
        }

        void NormalizeAndUpgrade()
        {
            // Fields of older versions that no longer exist are ignored when the XML is read.
            ConfigVersion = CurrentConfigVersion;
            Normalize();
        }

        void EnsureDisplayConfigs()
        {
            if (Road == null)
                Road = new RoadSettingsConfig();
            if (FullScreen == null)
                FullScreen = new MapDisplayConfig();
            if (PlanetaryMap == null)
                PlanetaryMap = new MapDisplayConfig { DisplayMyGps = true };
        }

        void Normalize()
        {
            EnsureDisplayConfigs();
            Road.Normalize();
            if (Bridge == null) Bridge = new BridgeSettingsConfig();
            Bridge.Normalize();
            if (!Enum.IsDefined(typeof(MinimapStyleTheme), Theme))
                Theme = MinimapStyleTheme.Fluent;
            if (ConfigVersion < CurrentConfigVersion)
                ConfigVersion = CurrentConfigVersion;
        }

        public bool IsFullScreenGpsVisible()
        {
            return FullScreen.DisplayMyGps && PlanetaryMap.DisplayMyGps;
        }

        public bool IsFullScreenRadioVisible()
        {
            return FullScreen.IncludeRadioSignals && PlanetaryMap.IncludeRadioSignals;
        }

        // Sliders change many times a second while dragged; write once they settle.
        const int SaveDelayFrames = 60;
        static int _pendingSaveFrames;

        /// <summary>Saves <see cref="Current"/> about a second after the last call.</summary>
        public static void RequestSave()
        {
            _pendingSaveFrames = SaveDelayFrames;
        }

        /// <summary>Call once per frame; writes a requested save once it is due.</summary>
        public static void UpdatePendingSave()
        {
            if (_pendingSaveFrames > 0 && --_pendingSaveFrames == 0)
                Current.Save();
        }

        /// <summary>Writes a requested save now (on unload).</summary>
        public static void FlushPendingSave()
        {
            if (_pendingSaveFrames <= 0) return;
            _pendingSaveFrames = 0;
            Current.Save();
        }

        public void Save()
        {
            if (MyAPIGateway.Utilities == null)
                return;

            Normalize();
            ConfigVersion = CurrentConfigVersion;

            try
            {
                using (TextWriter writer = MyAPIGateway.Utilities.WriteFileInLocalStorage(
                           FileName,
                           typeof(MinimapLocalConfig)))
                {
                    writer.Write(MyAPIGateway.Utilities.SerializeToXML(this));
                }
            }
            catch (Exception error)
            {
                ErrorHandlerHelper.LogError(error, typeof(MinimapLocalConfig));
            }
        }
    }
}

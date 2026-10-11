using Adk.Mvvm;
using System;
using Acrux.Core.Binding;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using VRageMath;

namespace HighwayEngineer.Client.Apps.BridgeSettings
{
    /// <summary>
    /// The bridge settings flyout, two-way bound to its controls. Values come from and are saved
    /// to this machine's config, and each control resets to its default on a right click. Width
    /// and fill depth are the road settings'.
    /// </summary>
    internal sealed class BridgeSettingsViewModel : ObservableObject
    {
        readonly BridgeSettingsConfig _config;
        float _width = 330f;
        float _height = 128f;
        float _scale = 1f;

        public BridgeSettingsViewModel(BridgeSettingsConfig config)
        {
            _config = config;
            ResetStructureMaterial = () => StructureMaterial = BridgeSettingsConfig.Defaults.StructureMaterial;
            ResetPavementMaterial = () => PavementMaterial = BridgeSettingsConfig.Defaults.PavementMaterial;
            ResetSegment = () => SegmentMeters = BridgeSettingsConfig.Defaults.SegmentMeters;
            ResetDesign = () => Design = BridgeSettingsConfig.Defaults.Design;
        }

        public string StructureMaterial
        {
            get { return _config.StructureMaterial; }
            set
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    string.Equals(_config.StructureMaterial, value, StringComparison.Ordinal)) return;
                _config.StructureMaterial = value;
                Changed(nameof(StructureMaterial), value);
            }
        }

        public string PavementMaterial
        {
            get { return _config.PavementMaterial; }
            set
            {
                if (string.IsNullOrWhiteSpace(value) ||
                    string.Equals(_config.PavementMaterial, value, StringComparison.Ordinal)) return;
                _config.PavementMaterial = value;
                Changed(nameof(PavementMaterial), value);
            }
        }

        public float SegmentMeters
        {
            get { return _config.SegmentMeters; }
            set
            {
                if (Math.Abs(_config.SegmentMeters - value) < 1e-4f) return;
                _config.SegmentMeters = value;
                Changed(nameof(SegmentMeters), value);
                RaisePropertyChanged<string>(nameof(SegmentLabel));
            }
        }

        public BridgeDesign Design
        {
            get { return _config.Design; }
            set
            {
                if (_config.Design == value) return;
                _config.Design = value;
                Changed(nameof(Design), value);
            }
        }

        public float MinSegment => (float)RoadLimits.MinSegmentDistanceMeters;
        public float MaxSegment => (float)RoadLimits.MaxSegmentDistanceMeters;
        public string SegmentLabel => LocHelper.Format("HighwayEngineer_Settings_SegmentLabel", Math.Round(SegmentMeters));

        /// <summary>The style list's label for a design.</summary>
        public static string DesignName(BridgeDesign design)
        {
            switch (design)
            {
                case BridgeDesign.Beam: return LocHelper.GetLoc("HighwayEngineer_BridgeDesign_Beam");
                case BridgeDesign.Arch: return LocHelper.GetLoc("HighwayEngineer_BridgeDesign_Arch");
                default: return LocHelper.GetLoc("HighwayEngineer_BridgeDesign_Auto");
            }
        }

        // Right click on a control restores its default.
        public Action ResetStructureMaterial { get; private set; }
        public Action ResetPavementMaterial { get; private set; }
        public Action ResetSegment { get; private set; }
        public Action ResetDesign { get; private set; }

        public float Width { get { return _width; } private set { RaiseAndSetIfChanged(ref _width, value, nameof(Width)); } }
        public float Height { get { return _height; } private set { RaiseAndSetIfChanged(ref _height, value, nameof(Height)); } }
        public Vector4 Padding => new Vector4(8f * _scale);

        public void ApplyScale(float width, float height, float scale)
        {
            Width = width;
            Height = height;
            if (RaiseAndSetIfChanged(ref _scale, scale, "Scale"))
                RaisePropertyChanged<Vector4>(nameof(Padding));
        }

        void Changed<T>(string property, T value)
        {
            RaisePropertyChanged<T>(property);
            MinimapLocalConfig.RequestSave();
        }
    }
}

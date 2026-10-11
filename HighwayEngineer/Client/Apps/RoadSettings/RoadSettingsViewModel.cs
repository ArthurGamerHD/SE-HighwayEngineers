using Adk.Mvvm;
using System;
using Acrux.Core.Binding;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using VRageMath;

namespace HighwayEngineer.Client.Apps.RoadSettings
{
    /// <summary>
    /// The road settings flyout, two-way bound to its controls. Values come from and are
    /// saved to this machine's config, are applied to the planner as they change, and each
    /// control resets to its default on a right click.
    /// </summary>
    internal sealed class RoadSettingsViewModel : ObservableObject
    {
        readonly RoadSettingsConfig _config;
        readonly Action<RoadSettingsConfig> _apply;
        float _width = 330f;
        float _height = 256f;
        float _scale = 1f;
        double _depthStep = 1.0;

        public RoadSettingsViewModel(RoadSettingsConfig config, Action<RoadSettingsConfig> apply)
        {
            _config = config;
            _apply = apply;
            ResetMaterial = () => Material = RoadSettingsConfig.Defaults.Material;
            ResetWidth = () => WidthMeters = RoadSettingsConfig.Defaults.WidthMeters;
            ResetDepth = () => DepthMeters = RoadSettingsConfig.Defaults.DepthMeters;
            ResetSlope = () => MaxSlope = RoadSettingsConfig.Defaults.MaxSlopeDegrees;
            ResetShoulder = () => ShoulderAngle = RoadSettingsConfig.Defaults.ShoulderAngleDegrees;
            ResetSegment = () => SegmentDistance = RoadSettingsConfig.Defaults.SegmentDistanceMeters;
            ResetAverageHeight = () => AverageHeight = RoadSettingsConfig.Defaults.AverageHeight;
            ResetFillShoulders = () => FillShoulders = RoadSettingsConfig.Defaults.FillShoulders;
            ResetGuideLines = () => GuideLines = RoadSettingsConfig.Defaults.GuideLines;
            _apply(_config);
        }

        public string Material
        {
            get { return _config.Material; }
            set
            {
                if (string.IsNullOrWhiteSpace(value) || string.Equals(_config.Material, value, StringComparison.Ordinal)) return;
                _config.Material = value;
                Changed(nameof(Material), value);
            }
        }

        public double WidthMeters
        {
            get { return _config.WidthMeters; }
            set { if (Set(ref _config.WidthMeters, value)) Changed(nameof(WidthMeters), value); }
        }

        /// <summary>Depth in 0.1 m steps (Ctrl), so repeated small steps stay exact.</summary>
        public double DepthMeters
        {
            get { return _config.DepthMeters; }
            set
            {
                double rounded = Math.Round(value, 1);
                if (Set(ref _config.DepthMeters, rounded)) Changed(nameof(DepthMeters), rounded);
            }
        }

        /// <summary>Depth +/− and wheel step: 0.1 m with Ctrl held, 10 m with Shift, otherwise 1 m.</summary>
        public double DepthStep
        {
            get { return _depthStep; }
            private set { RaiseAndSetIfChanged(ref _depthStep, value, nameof(DepthStep)); }
        }

        /// <summary>Call once per frame with the modifier keys held.</summary>
        public void UpdateModifiers(bool control, bool shift)
        {
            DepthStep = control ? 0.1 : shift ? 10.0 : 1.0;
        }

        public float MaxSlope
        {
            get { return _config.MaxSlopeDegrees; }
            set
            {
                if (!Set(ref _config.MaxSlopeDegrees, value)) return;
                Changed(nameof(MaxSlope), value);
                RaisePropertyChanged<string>(nameof(SlopeLabel));
            }
        }

        public float ShoulderAngle
        {
            get { return _config.ShoulderAngleDegrees; }
            set
            {
                if (!Set(ref _config.ShoulderAngleDegrees, value)) return;
                Changed(nameof(ShoulderAngle), value);
                RaisePropertyChanged<string>(nameof(ShoulderLabel));
            }
        }

        public float SegmentDistance
        {
            get { return _config.SegmentDistanceMeters; }
            set
            {
                if (!Set(ref _config.SegmentDistanceMeters, value)) return;
                Changed(nameof(SegmentDistance), value);
                RaisePropertyChanged<string>(nameof(SegmentLabel));
            }
        }

        public bool? AverageHeight
        {
            get { return _config.AverageHeight; }
            set
            {
                bool next = value == true;
                if (_config.AverageHeight == next) return;
                _config.AverageHeight = next;
                Changed(nameof(AverageHeight), (bool?)next);
            }
        }

        public bool? GuideLines
        {
            get { return _config.GuideLines; }
            set
            {
                bool next = value == true;
                if (_config.GuideLines == next) return;
                _config.GuideLines = next;
                Changed(nameof(GuideLines), (bool?)next);
            }
        }

        public bool? FillShoulders
        {
            get { return _config.FillShoulders; }
            set
            {
                bool next = value == true;
                if (_config.FillShoulders == next) return;
                _config.FillShoulders = next;
                Changed(nameof(FillShoulders), (bool?)next);
                RaisePropertyChanged<bool>(nameof(ShoulderEnabled));
            }
        }

        // Control ranges, from RoadLimits (XAML has no x:Static).
        public double MinWidth => RoadLimits.MinWidthMeters;
        public double MaxWidth => RoadLimits.MaxWidthMeters;
        public double MinDepth => RoadLimits.MinDepthMeters;
        public double MaxDepth => RoadLimits.MaxDepthMeters;
        public float MinSlope => (float)RoadLimits.MinSlopeDegrees;
        public float MaxSlopeLimit => (float)RoadLimits.MaxSlopeDegrees;
        public float MinShoulder => (float)RoadLimits.MinShoulderAngleDegrees;
        public float MaxShoulder => (float)RoadLimits.MaxShoulderAngleDegrees;
        public float MinSegment => (float)RoadLimits.MinSegmentDistanceMeters;
        public float MaxSegment => (float)RoadLimits.MaxSegmentDistanceMeters;

        /// <summary>The shoulder angle only matters when the sides are cut/filled.</summary>
        public bool ShoulderEnabled => _config.FillShoulders;

        public string SlopeLabel => LocHelper.Format("HighwayEngineer_Settings_SlopeLabel", Math.Round(MaxSlope));
        public string ShoulderLabel => LocHelper.Format("HighwayEngineer_Settings_ShoulderLabel", Math.Round(ShoulderAngle));
        public string SegmentLabel => LocHelper.Format("HighwayEngineer_Settings_SegmentLabel", Math.Round(SegmentDistance));

        // Right click on a control restores its default.
        public Action ResetMaterial { get; private set; }
        public Action ResetWidth { get; private set; }
        public Action ResetDepth { get; private set; }
        public Action ResetSlope { get; private set; }
        public Action ResetShoulder { get; private set; }
        public Action ResetSegment { get; private set; }
        public Action ResetAverageHeight { get; private set; }
        public Action ResetFillShoulders { get; private set; }
        public Action ResetGuideLines { get; private set; }

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
            _apply(_config);
            MinimapLocalConfig.RequestSave();
        }

        static bool Set(ref double field, double value)
        {
            if (Math.Abs(field - value) < 1e-6) return false;
            field = value;
            return true;
        }

        static bool Set(ref float field, float value)
        {
            if (Math.Abs(field - value) < 1e-4f) return false;
            field = value;
            return true;
        }
    }
}

using Adk.Mvvm;
using System;
using Acrux.Core.Binding;
using HighwayEngineer.Client.Infrastructure;
using HighwayEngineer.Client.Maps;
using Acrux.Cartography.Maps;
using HighwayEngineer.Common.Helpers;
using HighwayEngineer.Common.Roads;
using VRageMath;

namespace HighwayEngineer.Client.Apps.AutoRoute
{
    /// <summary>
    /// The auto-route flyout: the A* weights (two-way bound to its sliders) and the
    /// outcome of the latest route. Changing a weight asks the planner to reroute and is
    /// saved on this machine; a right click on a slider row resets it to its default.
    /// </summary>
    internal sealed class AutoRoutePanelViewModel : ObservableObject
    {
        // The earthwork slider shows cost per 1000 m³, so its useful range stays readable.
        const double EarthworkScale = 1000.0;

        readonly RoadRouteWeights _weights;
        readonly RoadSettingsConfig _config;
        readonly Action _weightsChanged;
        string _status = string.Empty;
        float _width = 330f;
        float _height = 128f;
        float _scale = 1f;

        public AutoRoutePanelViewModel(RoadRouteWeights weights, RoadSettingsConfig config, Action weightsChanged)
        {
            _weights = weights;
            _config = config;
            _weightsChanged = weightsChanged;
            _weights.Distance = config.AutoRouteDistance;
            _weights.Inclination = config.AutoRouteInclination;
            _weights.Earthwork = config.AutoRouteEarthwork / EarthworkScale;
            ResetDistance = () => Distance = RoadSettingsConfig.Defaults.AutoRouteDistance;
            ResetInclination = () => Inclination = RoadSettingsConfig.Defaults.AutoRouteInclination;
            ResetEarthwork = () => Earthwork = RoadSettingsConfig.Defaults.AutoRouteEarthwork;
        }

        // Right click on a slider row restores its default.
        public Action ResetDistance { get; private set; }
        public Action ResetInclination { get; private set; }
        public Action ResetEarthwork { get; private set; }

        public float Distance
        {
            get { return (float)_weights.Distance; }
            set { SetWeight(ref _weights.Distance, value, nameof(Distance), nameof(DistanceLabel), () => DistanceLabel); }
        }

        public float Inclination
        {
            get { return (float)_weights.Inclination; }
            set { SetWeight(ref _weights.Inclination, value, nameof(Inclination), nameof(InclinationLabel), () => InclinationLabel); }
        }

        /// <summary>Space Credits-equivalent cost per 1000 m³ of cut and fill.</summary>
        public float Earthwork
        {
            get { return (float)(_weights.Earthwork * EarthworkScale); }
            set
            {
                double weight = value / EarthworkScale;
                SetWeight(ref _weights.Earthwork, weight, nameof(Earthwork), nameof(EarthworkLabel), () => EarthworkLabel);
            }
        }

        // Slider ranges, from RoadLimits (XAML has no x:Static).
        public float MinWeight => (float)RoadLimits.MinAutoRouteWeight;
        public float MaxDistance => (float)RoadLimits.MaxAutoRouteDistance;
        public float MaxInclination => (float)RoadLimits.MaxAutoRouteInclination;
        public float MaxEarthwork => (float)RoadLimits.MaxAutoRouteEarthwork;

        public string DistanceLabel => LocHelper.Format("HighwayEngineer_AutoRoute_Distance", Distance);
        public string InclinationLabel => LocHelper.Format("HighwayEngineer_AutoRoute_Inclination", Inclination);
        public string EarthworkLabel => LocHelper.Format("HighwayEngineer_AutoRoute_Earthwork", Earthwork);

        /// <summary>The latest route's outcome (lengths and volumes, or why it was rejected).</summary>
        public string Status
        {
            get { return _status; }
            set { RaiseAndSetIfChanged(ref _status, value ?? string.Empty, nameof(Status)); }
        }

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

        void SetWeight(ref double field, double value, string property, string label, Func<string> labelText)
        {
            if (Math.Abs(field - value) < 1e-9) return;
            field = value;
            _config.AutoRouteDistance = (float)_weights.Distance;
            _config.AutoRouteInclination = (float)_weights.Inclination;
            _config.AutoRouteEarthwork = (float)(_weights.Earthwork * EarthworkScale);
            MinimapLocalConfig.RequestSave();
            RaisePropertyChanged<float>(property);
            RaisePropertyChanged<string>(label);
            _weightsChanged();
        }
    }
}

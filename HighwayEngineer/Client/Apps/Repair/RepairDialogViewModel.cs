using Adk.Mvvm;
using System;
using Acrux.Core.Binding;
using Acrux.Core.Input;
using Acrux.Styling;
using HighwayEngineer.Common.Helpers;
using VRageMath;

namespace HighwayEngineer.Client.Apps.Repair
{
    /// <summary>
    /// The repair tool's options for the marked roads: a new material and/or filling holes. Repair
    /// closes the dialog; the planner then asks the server and confirms what it will do.
    /// </summary>
    internal sealed class RepairDialogViewModel : ObservableObject
    {
        bool _replaceMaterial;
        string _material;
        bool _repairHoles = true;
        bool _repaintGuideLines;
        string _summary;
        bool _canRepair = true;
        BackgroundBrush _repairBackground;
        float _dialogWidth = 420f;
        float _scale = 1f;

        public RepairDialogViewModel()
        {
            RepairCommand = new DelegateCommand(_ => Finish(true), _ => CanRepair);
            CancelCommand = new DelegateCommand(_ => Finish(false));
        }

        /// <summary>Raised with true for Repair and false for Cancel.</summary>
        public Action<bool> Closed { get; set; }

        public ICommand RepairCommand { get; private set; }
        public ICommand CancelCommand { get; private set; }

        /// <summary>Pave the roads (and bridge decks) with <see cref="Material"/>.</summary>
        public bool ReplaceMaterial
        {
            get { return _replaceMaterial; }
            set
            {
                if (RaiseAndSetIfChanged(ref _replaceMaterial, value, nameof(ReplaceMaterial)))
                    UpdateCanRepair();
            }
        }

        public string Material
        {
            get { return _material; }
            set { RaiseAndSetIfChanged(ref _material, value, nameof(Material)); }
        }

        /// <summary>Drop the voxel edits along the roads and rebuild their bridges.</summary>
        public bool RepairHoles
        {
            get { return _repairHoles; }
            set
            {
                if (RaiseAndSetIfChanged(ref _repairHoles, value, nameof(RepairHoles)))
                    UpdateCanRepair();
            }
        }

        /// <summary>Paint the guide lines on the roads (off by default).</summary>
        public bool RepaintGuideLines
        {
            get { return _repaintGuideLines; }
            set
            {
                if (RaiseAndSetIfChanged(ref _repaintGuideLines, value, nameof(RepaintGuideLines)))
                    UpdateCanRepair();
            }
        }

        /// <summary>The material to send: null keeps each road's own.</summary>
        public string ChosenMaterial => ReplaceMaterial && !string.IsNullOrWhiteSpace(Material) ? Material : null;

        public string Summary { get { return _summary; } private set { RaiseAndSetIfChanged(ref _summary, value, nameof(Summary)); } }
        public bool CanRepair { get { return _canRepair; } private set { RaiseAndSetIfChanged(ref _canRepair, value, nameof(CanRepair)); } }
        public BackgroundBrush RepairBackground { get { return _repairBackground; } private set { RaiseAndSetIfChanged(ref _repairBackground, value, nameof(RepairBackground)); } }

        public float DialogWidth { get { return _dialogWidth; } private set { RaiseAndSetIfChanged(ref _dialogWidth, value, nameof(DialogWidth)); } }
        public float Scale { get { return _scale; } private set { RaiseAndSetIfChanged(ref _scale, value, nameof(Scale)); } }
        public float Spacing => 6f * _scale;
        public Vector4 Padding => new Vector4(12f * _scale);
        public float RowHeight => 30f * _scale;
        public float ButtonWidth => 110f * _scale;
        public float ButtonHeight => 30f * _scale;

        public void ApplyScale(float width, float scale)
        {
            DialogWidth = width;
            if (!RaiseAndSetIfChanged(ref _scale, scale, nameof(Scale))) return;
            RaisePropertyChanged<float>(nameof(Spacing));
            RaisePropertyChanged<Vector4>(nameof(Padding));
            RaisePropertyChanged<float>(nameof(RowHeight));
            RaisePropertyChanged<float>(nameof(ButtonWidth));
            RaisePropertyChanged<float>(nameof(ButtonHeight));
        }

        /// <summary>Opens the dialog for <paramref name="marked"/> marked segments; the options are kept from last time.</summary>
        public void Show(int marked, Color accent)
        {
            RepairBackground = new SolidColorBrush(accent);
            Summary = LocHelper.Format(marked == 1 ? "HighwayEngineer_Repair_Summary_One" : "HighwayEngineer_Repair_Summary_Many", marked);
            UpdateCanRepair();
        }

        void UpdateCanRepair()
        {
            CanRepair = ReplaceMaterial || RepairHoles || RepaintGuideLines;
        }

        void Finish(bool repair)
        {
            if (Closed != null)
                Closed(repair);
        }
    }
}

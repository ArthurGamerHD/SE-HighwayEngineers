using System;
using System.Collections.Generic;
using Acrux.Controls;
using HighwayEngineer.Client.Apps.BridgeSettings;
using HighwayEngineer.Client.Apps.RoadSettings;

namespace HighwayEngineer.Client.Apps.Repair
{
    /// <summary>
    /// The repair tool's options; its state lives in <see cref="RepairDialogViewModel"/>. Only the
    /// material ComboBox&lt;T&gt; (not creatable from XAML) is built and synchronised here.
    /// </summary>
    internal sealed partial class RepairDialog : UserControl
    {
        readonly ComboBox _material;

        public RepairDialog(RepairDialogViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();

            List<string> materials = RoadSettingsPanel.GetRoadMaterialOptions();
            _material = _materialSlot.Add(BridgeSettingsPanel.MaterialBox(materials, material => viewModel.Material = material));
            // Asphalt when it is there, else the first material.
            string initial = materials.Find(material =>
                                 string.Equals(material, "HighwayEngineer_Asphalt", StringComparison.OrdinalIgnoreCase)) ??
                             (materials.Count > 0 ? materials[0] : null);
            if (initial != null)
            {
                _material.SelectedItem = initial;
                viewModel.Material = initial;
            }
        }

        public RepairDialogViewModel ViewModel { get; private set; }

        public void ApplyScale(float width, float scale)
        {
            ViewModel.ApplyScale(width, scale);
            _material.Width = Math.Max(1f, width - 24f * scale);
            _material.Height = ViewModel.RowHeight;
            _material.MaxDropDownHeight = 320f * scale;
        }
    }
}

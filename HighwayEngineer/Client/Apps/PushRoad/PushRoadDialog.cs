using Acrux.Controls;

namespace HighwayEngineer.Client.Apps.PushRoad
{
    /// <summary>Push confirmation view; all of its state lives in <see cref="PushRoadDialogViewModel"/>.</summary>
    internal sealed partial class PushRoadDialog : UserControl
    {
        public PushRoadDialog(PushRoadDialogViewModel viewModel)
        {
            ViewModel = viewModel;
            DataContext = viewModel;
            InitializeComponent();
        }

        public PushRoadDialogViewModel ViewModel { get; private set; }
    }
}

using Acrux.Controls;

namespace HighwayEngineer.Client.Apps.AutoRoute
{
    /// <summary>Auto-route flyout view; its state lives in <see cref="AutoRoutePanelViewModel"/>.</summary>
    internal sealed partial class AutoRoutePanel : UserControl
    {
        public AutoRoutePanel(AutoRoutePanelViewModel viewModel)
        {
            DataContext = viewModel;
            InitializeComponent();
        }
    }
}

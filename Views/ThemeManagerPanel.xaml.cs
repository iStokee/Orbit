using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class ThemeManagerPanel : UserControl
{
    public ThemeManagerPanel() : this(null)
    {
    }

    public ThemeManagerPanel(ThemeManagerViewModel? viewModel)
    {
        InitializeComponent();
        DataContext = viewModel ?? DataContext ?? new ThemeManagerViewModel();
    }
}

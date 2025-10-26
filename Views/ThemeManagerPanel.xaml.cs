using System.Windows.Controls;
using Orbit.ViewModels;
using UserControl = System.Windows.Controls.UserControl;

namespace Orbit.Views;

public partial class ThemeManagerPanel : UserControl
{
    public ThemeManagerPanel()
    {
        InitializeComponent();
        if (DataContext == null)
        {
            DataContext = new ThemeManagerViewModel();
        }
    }
}


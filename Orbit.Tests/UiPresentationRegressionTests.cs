using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;
using Orbit.Converters;
using Orbit.Models;
using Orbit.Services;
using Orbit.Views;
using Xunit;

namespace Orbit.Tests;

public sealed class UiPresentationRegressionTests
{
    [Theory]
    [InlineData("#252525", "#B22222")]
    [InlineData("#252525", "#228B22")]
    [InlineData("#252525", "#647687")]
    [InlineData("#FFFFFF", "#DAA520")]
    [InlineData("#FFFFFF", "#B22222")]
    [InlineData("#FFFFFF", "#80647687")]
    public void StatusColorsRemainReadableOnBothBaseThemes(string background, string status)
    {
        var surface = (Color)ColorConverter.ConvertFromString(background)!;
        var candidate = ThemeService.GetReadableStatusColor((Color)ColorConverter.ConvertFromString(status)!, surface);
        Assert.Equal(255, candidate.A);
        Assert.True(ThemeService.GetContrastRatio(candidate, surface) >= 4.5);
    }

    [Theory]
    [InlineData(0d, 0d)]
    [InlineData(10d, 0d)]
    [InlineData(320d, 304d)]
    [InlineData(1000d, 380d)]
    public void DashboardCardWidthFitsViewportAndNeverBecomesNegative(double viewport, double expected)
        => Assert.Equal(expected, new ToolCardWidthConverter().Convert(viewport, typeof(double), null!, CultureInfo.InvariantCulture));

    [Fact]
    public void ScriptDoubleClickRequiresRowContentAndExcludesActionButtonsAndEditors()
        => ClientLifecycleRegressionTests.RunOnSta(() =>
        {
            var profile = new ScriptProfile { Name = "Chosen", FilePath = "synthetic.dll" };
            var label = new TextBlock { Text = profile.Name };
            var action = new Button { Content = new TextBlock { Text = "Favorite" } };
            var editor = new TextBox { Text = "Edit" };
            var content = new StackPanel();
            content.Children.Add(label); content.Children.Add(action); content.Children.Add(editor);
            var row = new ListBoxItem { Content = content, DataContext = profile };
            var browser = new ListBox(); browser.Items.Add(row);
            browser.Measure(new Size(400, 300)); browser.Arrange(new Rect(0, 0, 400, 300)); browser.UpdateLayout();
            Assert.Same(profile, ScriptManagerPanel.GetDoubleClickProfile(browser, label));
            Assert.Null(ScriptManagerPanel.GetDoubleClickProfile(browser, (DependencyObject)action.Content));
            Assert.Null(ScriptManagerPanel.GetDoubleClickProfile(browser, editor));
            Assert.Null(ScriptManagerPanel.GetDoubleClickProfile(browser, browser));
        });

    [Fact]
    public void SingleChoiceRadioBindingsCannotUncheckTheCurrentChoice()
        => ClientLifecycleRegressionTests.RunOnSta(() =>
        {
            var panel = new StackPanel();
            var source = new ChoiceState();
            var first = new TestRadio(); var second = new TestRadio();
            panel.Children.Add(first); panel.Children.Add(second);
            foreach (var (button, choice) in new[] { (first, ScriptBrowserLayout.List), (second, ScriptBrowserLayout.Tiles) })
                button.SetBinding(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty,
                    new Binding(nameof(ChoiceState.Value)) { Source = source, Mode = BindingMode.TwoWay,
                        Converter = new EnumToBooleanConverter(), ConverterParameter = choice });
            Assert.True(first.IsChecked);
            first.Press();
            Assert.True(first.IsChecked);
            second.Press();
            Assert.True(second.IsChecked);
            Assert.False(first.IsChecked);
            Assert.Equal(ScriptBrowserLayout.Tiles, source.Value);
        });

    private sealed class TestRadio : RadioButton { public void Press() => OnClick(); }
    private sealed class ChoiceState : System.ComponentModel.INotifyPropertyChanged
    {
        private ScriptBrowserLayout _value;
        public ScriptBrowserLayout Value
        {
            get => _value;
            set { _value = value; PropertyChanged?.Invoke(this, new(nameof(Value))); }
        }
        public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    }
}

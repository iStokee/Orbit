using System.Windows;
using MahApps.Metro.IconPacks;

namespace Orbit.Tooling;

public interface IOrbitTool
{
	string Key { get; }
	string DisplayName { get; }
	PackIconMaterialKind Icon { get; }
	FrameworkElement CreateView(object? context = null);
}

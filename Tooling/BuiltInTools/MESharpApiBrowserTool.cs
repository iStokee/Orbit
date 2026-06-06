using System.Windows;
using csharp_interop.Documentation;
using MahApps.Metro.IconPacks;

namespace Orbit.Tooling.BuiltInTools;

/// <summary>
/// Built-in tool that hosts the reflected MESharp scripting API browser.
/// </summary>
public sealed class MESharpApiBrowserTool : IOrbitTool
{
	public string Key => "MesharpApiBrowser";

	public string DisplayName => "MESharp API";

	public PackIconMaterialKind Icon => PackIconMaterialKind.BookSearch;

	public FrameworkElement CreateView(object? context = null)
		=> new ApiDocumentationBrowser();
}

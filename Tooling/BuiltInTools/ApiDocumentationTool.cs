using System.Windows;
using MahApps.Metro.IconPacks;

namespace Orbit.Tooling.BuiltInTools
{
	/// <summary>
	/// Built-in tool that hosts the MESharp API Documentation Browser from csharp_interop
	/// </summary>
	public sealed class ApiDocumentationTool : IOrbitTool
	{
		public string Key => "ApiDocumentation";

		public string DisplayName => "API Documentation";

		public PackIconMaterialKind Icon => PackIconMaterialKind.BookOpenPageVariant;

		public FrameworkElement CreateView(object? context = null)
		{
			// Create and return the API browser from csharp_interop
			return new csharp_interop.Documentation.ApiDocumentationBrowser();
		}
	}
}

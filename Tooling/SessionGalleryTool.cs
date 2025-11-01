using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Services;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling
{
	/// <summary>
	/// Tool for displaying sessions in a thumbnail gallery view
	/// </summary>
	public sealed class SessionGalleryTool : IOrbitTool
	{
		private readonly SessionCollectionService _sessionCollectionService;

		public SessionGalleryTool(SessionCollectionService sessionCollectionService)
		{
			_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
		}

		public string Key => "SessionGallery";

		public string DisplayName => "Gallery";

		public PackIconMaterialKind Icon => PackIconMaterialKind.ViewModule;

		public FrameworkElement CreateView(object? context = null)
		{
			var viewModel = new SessionGalleryViewModel(_sessionCollectionService);
			return new SessionGalleryView(viewModel);
		}
	}
}

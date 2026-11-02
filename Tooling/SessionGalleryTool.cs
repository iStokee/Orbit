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
			SessionGalleryView? view = null;
			var originalOwner = context is MainWindowViewModel owner ? new WeakReference<MainWindowViewModel>(owner) : null;
			var viewModel = new SessionGalleryViewModel(_sessionCollectionService, session =>
			{
				var shell = (view == null ? null : Window.GetWindow(view)?.DataContext as MainWindowViewModel)
					?? (originalOwner?.TryGetTarget(out var current) == true ? current : null);
				shell?.FocusSession(session);
			});
			view = new SessionGalleryView(viewModel);
			return view;
		}
	}
}

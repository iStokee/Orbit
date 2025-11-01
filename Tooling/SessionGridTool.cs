using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Services;
using Orbit.ViewModels;
using Orbit.Views;
using Application = System.Windows.Application;

namespace Orbit.Tooling
{
	/// <summary>
	/// Tool for grid-based session window management (corner/edge snapping)
	/// </summary>
	public sealed class SessionGridTool : IOrbitTool
	{
		private readonly SessionCollectionService _sessionCollectionService;
		private readonly SessionGridManager _gridManager;

		public SessionGridTool(SessionCollectionService sessionCollectionService, SessionGridManager gridManager)
		{
			_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
			_gridManager = gridManager ?? throw new ArgumentNullException(nameof(gridManager));
		}

		public string Key => "SessionGrid";

		public string DisplayName => "Grid Layout";

		public PackIconMaterialKind Icon => PackIconMaterialKind.GridLarge;

		public FrameworkElement CreateView(object? context = null)
		{
			// Provide a callback to get viewport size from MainWindowViewModel
			var mainVm = context as MainWindowViewModel;
			if (mainVm == null)
			{
				if (Application.Current?.MainWindow is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel fromWindow)
				{
					mainVm = fromWindow;
				}
				else
				{
					throw new InvalidOperationException("SessionGridTool requires the MainWindowViewModel context to operate.");
				}
			}

			var viewModel = new SessionGridViewModel(
				_sessionCollectionService,
				_gridManager,
				(defaultWidth, defaultHeight) =>
				{
					var width = (int)Math.Max(0, mainVm.hostViewportWidth);
					var height = (int)Math.Max(0, mainVm.hostViewportHeight);
					return width > 0 && height > 0
						? (width, height)
						: (defaultWidth, defaultHeight);
				},
				mainVm.InterTabClient,
				"OrbitMainShell");

			return new SessionGridView(viewModel);
		}
	}
}

using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Services;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling
{
	/// <summary>
	/// Orbit View: Live session grid layout - Mission Control for RuneScape
	/// </summary>
	public sealed class OrbitViewTool : IOrbitTool
	{
		private readonly SessionCollectionService _sessionCollectionService;
		private readonly OrbitLayoutStateService _layoutStateService;

		public OrbitViewTool(SessionCollectionService sessionCollectionService, OrbitLayoutStateService layoutStateService)
		{
			_sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
			_layoutStateService = layoutStateService ?? throw new ArgumentNullException(nameof(layoutStateService));
		}

		public string Key => "OrbitView";

		public string DisplayName => "Orbit View";

		public PackIconMaterialKind Icon => PackIconMaterialKind.ViewGrid;

		public FrameworkElement CreateView(object? context = null)
		{
			// Get the InterTabClient from MainWindowViewModel
			var mainVm = context as MainWindowViewModel;
			if (mainVm == null)
			{
				if (System.Windows.Application.Current?.MainWindow is MainWindow mainWindow && mainWindow.DataContext is MainWindowViewModel fromWindow)
				{
					mainVm = fromWindow;
				}
				else
				{
					throw new InvalidOperationException("OrbitViewTool requires the MainWindowViewModel context to operate.");
				}
			}

		// Create the layout-based implementation (Dragablz Layout control)
		var viewModel = new OrbitGridLayoutViewModel(
			_sessionCollectionService,
			_layoutStateService,
			mainVm.InterTabClient,
			mainVm.CloseSession);

			return new OrbitGridLayoutView { DataContext = viewModel };
		}
	}
}

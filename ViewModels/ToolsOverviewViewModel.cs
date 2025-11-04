using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows;
using Orbit.Tooling;
using Application = System.Windows.Application;

namespace Orbit.ViewModels
{
	/// <summary>
	/// ViewModel for the Tools Overview page - provides a management surface for all Orbit tools
	/// </summary>
	public class ToolsOverviewViewModel : INotifyPropertyChanged
	{
		private readonly IToolRegistry _toolRegistry;
		private readonly MainWindowViewModel? _mainWindowViewModel;

		public ToolsOverviewViewModel(IToolRegistry toolRegistry, MainWindowViewModel? mainWindowViewModel = null)
		{
			_toolRegistry = toolRegistry ?? throw new ArgumentNullException(nameof(toolRegistry));
			_mainWindowViewModel = mainWindowViewModel;
			LoadTools();
		}

		#region Properties

		/// <summary>
		/// Collection of all registered tools
		/// </summary>
		public ObservableCollection<ToolItemViewModel> Tools { get; } = new();

		#endregion

		#region Methods

		private void LoadTools()
		{
			foreach (var tool in _toolRegistry.Tools)
			{
				Tools.Add(new ToolItemViewModel(tool, _mainWindowViewModel));
			}
		}

		#endregion

		#region INotifyPropertyChanged

		public event PropertyChangedEventHandler? PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion
	}

	/// <summary>
	/// Represents a single tool in the overview
	/// </summary>
		public class ToolItemViewModel : INotifyPropertyChanged
		{
			private readonly IOrbitTool _tool;
			private readonly MainWindowViewModel? _mainWindowViewModel;

			public ToolItemViewModel(IOrbitTool tool, MainWindowViewModel? mainWindowViewModel = null)
			{
				_tool = tool ?? throw new ArgumentNullException(nameof(tool));
				_mainWindowViewModel = mainWindowViewModel;
			}

		#region Properties

		public string Key => _tool.Key;
		public string DisplayName => _tool.DisplayName;
		public MahApps.Metro.IconPacks.PackIconMaterialKind Icon => _tool.Icon;

		/// <summary>
		/// Whether this tool is visible in the floating menu
		/// </summary>
		public bool IsVisibleInMenu
		{
			get => GetVisibilitySetting();
			set
			{
				SetVisibilitySetting(value);
				OnPropertyChanged();
			}
		}

		/// <summary>
		/// Description of what this tool does
		/// </summary>
		public string Description => GetToolDescription();

		#endregion

		#region Methods

		private bool GetVisibilitySetting()
		{
			// Map tool keys to settings properties
			// Tools without a corresponding setting default to true (always visible)
			return Key switch
			{
				"Sessions" => Settings.Default.ShowMenuSessions,
				"AccountManager" => Settings.Default.ShowMenuAccountManager,
				"ThemeManager" => Settings.Default.ShowMenuThemeManager,
				"Console" => Settings.Default.ShowMenuConsole,
				"ApiDocumentation" => Settings.Default.ShowMenuApiDocumentation,
				"Settings" => Settings.Default.ShowMenuSettings,
				// Tools that don't appear in floating menu or don't have settings
				_ => true
			};
		}

		private void SetVisibilitySetting(bool value)
		{
			if (_mainWindowViewModel != null)
			{
				void Apply()
				{
					switch (Key)
					{
						case "Sessions":
							_mainWindowViewModel.ShowMenuSessions = value;
							break;
						case "AccountManager":
							_mainWindowViewModel.ShowMenuAccountManager = value;
							break;
							_mainWindowViewModel.ShowMenuThemeManager = value;
							break;
						case "Console":
							_mainWindowViewModel.ShowMenuConsole = value;
							break;
						case "ApiDocumentation":
							_mainWindowViewModel.ShowMenuGuide = value;
							break;
						case "Settings":
							_mainWindowViewModel.ShowMenuSettings = value;
							break;
						// Tools without settings - no-op
					}
				}

				var dispatcher = Application.Current?.Dispatcher;
				if (dispatcher?.CheckAccess() == true)
				{
					Apply();
				}
				else
				{
					dispatcher?.Invoke(Apply);
				}
				Settings.Default.Save();
				return;
			}

			switch (Key)
			{
				case "Sessions":
					Settings.Default.ShowMenuSessions = value;
					break;
				case "AccountManager":
					Settings.Default.ShowMenuAccountManager = value;
					break;
					Settings.Default.ShowMenuThemeManager = value;
					break;
				case "Console":
					Settings.Default.ShowMenuConsole = value;
					break;
				case "ApiDocumentation":
					Settings.Default.ShowMenuApiDocumentation = value;
					break;
				case "Settings":
					Settings.Default.ShowMenuSettings = value;
					break;
				// Tools without settings - no-op
			}
			Settings.Default.Save();
		}

		private string GetToolDescription()
		{
			return Key switch
			{
				"SessionsOverview" => "Overview and management of all RuneScape 3 sessions",
				"Sessions" => "Manage RuneScape 3 sessions and embedded client windows",
				"AccountManager" => "Manage account credentials and quick login",
				"ScriptControls" => "Load and control C# scripts via hot reload",
				"ThemeManager" => "Customize themes, accents, and appearance",
				"ScriptManager" => "Browse and manage script library",
				"Console" => "View unified logs from Orbit, ME, and scripts",
				"ApiDocumentation" => "Open the Orbiters Guide documentation hub",
				"ToolsOverview" => "Manage registered tools and their visibility",
				"Settings" => "Configure Orbit application settings",
				_ => "Custom tool - no description available"
			};
		}

		#endregion

		#region INotifyPropertyChanged

		public event PropertyChangedEventHandler? PropertyChanged;

		protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
		{
			PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
		}

		#endregion
	}
}

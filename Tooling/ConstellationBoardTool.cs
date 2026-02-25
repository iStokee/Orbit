using System;
using System.Windows;
using MahApps.Metro.IconPacks;
using Orbit.Services;
using Orbit.ViewModels;
using Orbit.Views;

namespace Orbit.Tooling;

public sealed class ConstellationBoardTool : IOrbitTool
{
    private readonly SessionCollectionService _sessionCollectionService;

    public ConstellationBoardTool(SessionCollectionService sessionCollectionService)
    {
        _sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
    }

    public string Key => "ConstellationBoard";

    public string DisplayName => "Constellation";

    public PackIconMaterialKind Icon => PackIconMaterialKind.StarFourPoints;

    public FrameworkElement CreateView(object? context = null)
    {
        if (context is not MainWindowViewModel mainWindowViewModel)
        {
            throw new InvalidOperationException("ConstellationBoardTool requires MainWindowViewModel context.");
        }

        return new ConstellationBoardView
        {
            DataContext = new ConstellationBoardViewModel(_sessionCollectionService, mainWindowViewModel)
        };
    }
}

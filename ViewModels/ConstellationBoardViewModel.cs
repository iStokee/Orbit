using System;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using MahApps.Metro.IconPacks;
using Orbit.Models;
using Orbit.Services;

namespace Orbit.ViewModels;

public sealed class ConstellationBoardViewModel : ObservableObject, IDisposable
{
    private readonly SessionCollectionService _sessionCollectionService;
    private readonly MainWindowViewModel _mainWindowViewModel;
    private double _zoom = 1.0;

    public ConstellationBoardViewModel(SessionCollectionService sessionCollectionService, MainWindowViewModel mainWindowViewModel)
    {
        _sessionCollectionService = sessionCollectionService ?? throw new ArgumentNullException(nameof(sessionCollectionService));
        _mainWindowViewModel = mainWindowViewModel ?? throw new ArgumentNullException(nameof(mainWindowViewModel));

        SessionNodes = new ObservableCollection<ConstellationSessionNodeViewModel>();
        Title = "Constellation Board";
        Subtitle = "Infinite orbit canvas - each session is a sun, tools are expandable constellations.";

        _sessionCollectionService.Sessions.CollectionChanged += OnSessionsChanged;
        _mainWindowViewModel.PropertyChanged += OnMainWindowPropertyChanged;

        RebuildNodes();
    }

    public ObservableCollection<ConstellationSessionNodeViewModel> SessionNodes { get; }

    public string Title { get; }

    public string Subtitle { get; }

    public double CanvasWidth => 20000;

    public double CanvasHeight => 20000;

    public double CanvasCenterX => CanvasWidth / 2.0;

    public double CanvasCenterY => CanvasHeight / 2.0;

    public double Zoom
    {
        get => _zoom;
        set
        {
            var clamped = Math.Clamp(value, 0.35, 2.4);
            if (Math.Abs(_zoom - clamped) < 0.001)
            {
                return;
            }

            _zoom = clamped;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ZoomPercent));
        }
    }

    public string ZoomPercent => $"{Zoom * 100:0}%";

    public void NudgeZoom(double delta)
    {
        Zoom += delta;
    }

    public void ExpandNode(ConstellationSessionNodeViewModel? node, bool expanded)
    {
        if (node == null)
        {
            return;
        }

        node.IsHovered = expanded;
    }

    public void TogglePinned(ConstellationSessionNodeViewModel? node)
    {
        if (node == null)
        {
            return;
        }

        node.IsPinned = !node.IsPinned;
    }

    public void FocusSession(ConstellationSessionNodeViewModel? node)
    {
        if (node?.Session == null)
        {
            return;
        }

        _mainWindowViewModel.SelectedSession = node.Session;
        _mainWindowViewModel.SelectedTab = node.Session;
        _mainWindowViewModel.FocusSession(node.Session);

        foreach (var candidate in SessionNodes)
        {
            candidate.IsSelected = ReferenceEquals(candidate.Session, node.Session);
        }
    }

    public void InvokeTool(ConstellationToolNodeViewModel? tool)
    {
        tool?.Invoke();
    }

    private void OnSessionsChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        RebuildNodes();
    }

    private void OnMainWindowPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!string.Equals(e.PropertyName, nameof(MainWindowViewModel.SelectedSession), StringComparison.Ordinal))
        {
            return;
        }

        var selected = _mainWindowViewModel.SelectedSession;
        foreach (var node in SessionNodes)
        {
            node.IsSelected = selected != null && ReferenceEquals(node.Session, selected);
        }
    }

    private void RebuildNodes()
    {
        SessionNodes.Clear();

        var orderedSessions = _sessionCollectionService.Sessions
            .OrderBy(s => s.CreatedAt)
            .ToList();

        const double angleStep = 0.78;
        const double radiusGrowth = 190;

        for (var index = 0; index < orderedSessions.Count; index++)
        {
            var session = orderedSessions[index];
            var arm = Math.Sqrt(index + 1) * radiusGrowth;
            var angle = index * angleStep;

            var centerX = CanvasCenterX + Math.Cos(angle) * arm;
            var centerY = CanvasCenterY + Math.Sin(angle) * arm;
            var size = ResolveSunSize(session);

            var node = new ConstellationSessionNodeViewModel(session, centerX, centerY, size)
            {
                IsSelected = ReferenceEquals(session, _mainWindowViewModel.SelectedSession)
            };

            BuildToolNodes(node);
            SessionNodes.Add(node);
        }

        OnPropertyChanged(nameof(SessionNodes));
    }

    private void BuildToolNodes(ConstellationSessionNodeViewModel node)
    {
        // Tool nodes are positioned as a constellation arc around each session sun.
        var toolSpecs = new (string Label, string Hint, PackIconMaterialKind Icon, Action Action)[]
        {
            ("Focus", "Select this session", PackIconMaterialKind.CrosshairsGps, () =>
            {
                _mainWindowViewModel.SelectedSession = node.Session;
                _mainWindowViewModel.SelectedTab = node.Session;
            }),
            ("Inject", "Inject selected session", PackIconMaterialKind.Needle, () =>
            {
                _mainWindowViewModel.SelectedSession = node.Session;
                _mainWindowViewModel.SelectedTab = node.Session;
                if (_mainWindowViewModel.InjectCommand.CanExecute(null))
                {
                    _mainWindowViewModel.InjectCommand.Execute(null);
                }
            }),
            ("Scripts", "Open script manager", PackIconMaterialKind.FileCodeOutline, () => _mainWindowViewModel.OpenScriptManagerCommand.Execute(null)),
            ("Orbit", "Open Orbit grid view", PackIconMaterialKind.ViewGrid, () => _mainWindowViewModel.OpenOrbitViewCommand.Execute(null)),
            ("Console", "Open runtime console", PackIconMaterialKind.Console, () => _mainWindowViewModel.ToggleConsoleCommand.Execute(null)),
            ("Close", "Close this session", PackIconMaterialKind.CloseCircleOutline, () => _mainWindowViewModel.CloseSession(node.Session))
        };

        var count = toolSpecs.Length;
        const double orbitRadius = 146;
        const double startAngle = -1.75;
        const double sweep = 3.5;

        for (var i = 0; i < count; i++)
        {
            var t = count == 1 ? 0.5 : i / (double)(count - 1);
            var angle = startAngle + sweep * t;
            var toolCenterX = node.CenterX + Math.Cos(angle) * orbitRadius;
            var toolCenterY = node.CenterY + Math.Sin(angle) * orbitRadius;

            var spec = toolSpecs[i];
            node.Tools.Add(new ConstellationToolNodeViewModel(
                spec.Label,
                spec.Hint,
                spec.Icon,
                spec.Action,
                node.CenterX,
                node.CenterY,
                toolCenterX,
                toolCenterY));
        }
    }

    private static double ResolveSunSize(SessionModel session)
    {
        return session.InjectionState switch
        {
            InjectionState.Injected => 100,
            InjectionState.Ready => 90,
            InjectionState.Failed => 88,
            _ => 84
        };
    }

    public void Dispose()
    {
        _sessionCollectionService.Sessions.CollectionChanged -= OnSessionsChanged;
        _mainWindowViewModel.PropertyChanged -= OnMainWindowPropertyChanged;
    }
}

public sealed class ConstellationSessionNodeViewModel : ObservableObject
{
    private bool _isHovered;
    private bool _isPinned;
    private bool _isSelected;

    public ConstellationSessionNodeViewModel(SessionModel session, double centerX, double centerY, double size)
    {
        Session = session ?? throw new ArgumentNullException(nameof(session));
        CenterX = centerX;
        CenterY = centerY;
        Size = size;
        Tools = new ObservableCollection<ConstellationToolNodeViewModel>();
    }

    public SessionModel Session { get; }

    public ObservableCollection<ConstellationToolNodeViewModel> Tools { get; }

    public double CenterX { get; }

    public double CenterY { get; }

    public double Size { get; }

    public double Left => CenterX - (Size / 2.0);

    public double Top => CenterY - (Size / 2.0);

    public bool IsHovered
    {
        get => _isHovered;
        set
        {
            if (SetProperty(ref _isHovered, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
    }

    public bool IsPinned
    {
        get => _isPinned;
        set
        {
            if (SetProperty(ref _isPinned, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetProperty(ref _isSelected, value))
            {
                OnPropertyChanged(nameof(IsExpanded));
            }
        }
    }

    public bool IsExpanded => IsHovered || IsPinned || IsSelected;
}

public sealed class ConstellationToolNodeViewModel
{
    private readonly Action _action;

    public ConstellationToolNodeViewModel(
        string label,
        string hint,
        PackIconMaterialKind icon,
        Action action,
        double anchorX,
        double anchorY,
        double centerX,
        double centerY)
    {
        Label = label;
        Hint = hint;
        Icon = icon;
        _action = action ?? throw new ArgumentNullException(nameof(action));
        AnchorX = anchorX;
        AnchorY = anchorY;
        CenterX = centerX;
        CenterY = centerY;
    }

    public string Label { get; }

    public string Hint { get; }

    public PackIconMaterialKind Icon { get; }

    public double AnchorX { get; }

    public double AnchorY { get; }

    public double CenterX { get; }

    public double CenterY { get; }

    public double Left => CenterX - 16;

    public double Top => CenterY - 16;

    public double LineEndX => CenterX - AnchorX;

    public double LineEndY => CenterY - AnchorY;

    public void Invoke()
    {
        _action();
    }
}

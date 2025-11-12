using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using MahApps.Metro.IconPacks;
using Markdig;
using Markdig.Wpf;

namespace Orbit.ViewModels;

/// <summary>
/// View model backing the Orbiters Guide browser. Handles document navigation,
/// markdown rendering, and lightweight status messaging.
/// </summary>
public class GuideBrowserViewModel : INotifyPropertyChanged
{
	private readonly MarkdownPipeline _markdownPipeline;
	private readonly string _docsRoot;
	private FlowDocument? _currentDocument;
	private GuideSection? _selectedSection;
	private string _statusMessage = "Select a chapter to begin.";
	private bool _isLoading;

	public event PropertyChangedEventHandler? PropertyChanged;

	public ObservableCollection<GuideSection> Sections { get; } = new();

	public GuideSection? SelectedSection
	{
		get => _selectedSection;
		set
		{
			if (_selectedSection == value) return;
			_selectedSection = value;
			OnPropertyChanged();
			_ = LoadCurrentSectionAsync();
		}
	}

	public FlowDocument? CurrentDocument
	{
		get => _currentDocument;
		private set
		{
			if (_currentDocument == value) return;
			_currentDocument = value;
			OnPropertyChanged();
		}
	}

	public string StatusMessage
	{
		get => _statusMessage;
		private set
		{
			if (_statusMessage == value) return;
			_statusMessage = value;
			OnPropertyChanged();
		}
	}

	public bool IsLoading
	{
		get => _isLoading;
		private set
		{
			if (_isLoading == value) return;
			_isLoading = value;
			OnPropertyChanged();
		}
	}

	public IRelayCommand RefreshCommand { get; }
	public IRelayCommand OpenDocsFolderCommand { get; }

	public GuideBrowserViewModel()
	{
		_docsRoot = ResolveDocsRoot();
		_markdownPipeline = CreatePipeline();

		RefreshCommand = new RelayCommand(() => _ = LoadCurrentSectionAsync());
		OpenDocsFolderCommand = new RelayCommand(OpenDocsFolder);

		SeedSections();
		SelectedSection = Sections.FirstOrDefault();
	}

	private void SeedSections()
	{
		Sections.Clear();

		Sections.Add(new GuideSection(
			"overview",
			"Launchpad",
			"Start here for the complete tour of Orbit.",
			"README.md",
			PackIconMaterialKind.BookOpenPageVariant));

		Sections.Add(new GuideSection(
			"flight-school",
			"Flight School",
			"Pilot Orbit day-to-day: setup, layouts, script workflows.",
			"UserGuide.md",
			PackIconMaterialKind.RocketLaunch));

		Sections.Add(new GuideSection(
			"contributors",
			"Contributor Field Manual",
			"Environment setup, coding style, testing, and releases.",
			"ContributorGuide.md",
			PackIconMaterialKind.Tools));

		Sections.Add(new GuideSection(
			"api",
			"API Reference",
			"Script APIs, Orbit services, plugin surfaces, and contracts.",
			"APIReference.md",
			PackIconMaterialKind.ApplicationBracketsOutline));

		Sections.Add(new GuideSection(
			"systems",
			"Systems Primer",
			"Architectural overviews and maps to deeper research.",
			"SystemsPrimer.md",
			PackIconMaterialKind.ChartTimelineVariant));
    }

	private static MarkdownPipeline CreatePipeline()
	{
		// Advanced extensions mirror GitHub-style Markdown (tables, lists, alerts, etc.).
		return new MarkdownPipelineBuilder()
			.UseAdvancedExtensions()
			.Build();
	}

	private async System.Threading.Tasks.Task LoadCurrentSectionAsync()
	{
		if (SelectedSection is null)
		{
			CurrentDocument = BuildMessageDocument("No section selected.");
			return;
		}

		IsLoading = true;
		StatusMessage = $"Loading “{SelectedSection.Title}”…";

		try
		{
			var path = Path.Combine(_docsRoot, SelectedSection.FileName);

			if (!File.Exists(path))
			{
				StatusMessage = "Document missing.";
				CurrentDocument = BuildMissingDocument(path);
				return;
			}

			var markdownText = await File.ReadAllTextAsync(path);
			var flowDocument = Markdig.Wpf.Markdown.ToFlowDocument(markdownText, _markdownPipeline);

			ApplyDocumentStyling(flowDocument);
			CurrentDocument = flowDocument;
			StatusMessage = $"Showing {SelectedSection.Title}";
		}
		catch (Exception ex)
		{
			StatusMessage = "Failed to load guide.";
			CurrentDocument = BuildErrorDocument(ex);
		}
		finally
		{
			IsLoading = false;
		}
	}

	private void ApplyDocumentStyling(FlowDocument document)
	{
        document.PagePadding = new Thickness(32, 24, 32, 48);
        document.FontSize = 14;
        document.FontFamily = new System.Windows.Media.FontFamily("Segoe UI");
        document.TextAlignment = TextAlignment.Left;
        document.Background = System.Windows.Media.Brushes.Transparent;

        if (TryFindResource("MahApps.Brushes.Text") is System.Windows.Media.Brush textBrush)
        {
            document.SetValue(TextElement.ForegroundProperty, textBrush);
        }

		if (TryFindResource("MahApps.Brushes.Accent") is System.Windows.Media.Brush accentBrush)
		{
			foreach (var block in document.Blocks.OfType<Paragraph>())
			{
				if (block.FontSize > document.FontSize + 1)
				{
					block.Foreground = accentBrush;
				}
			}
		}

		foreach (var block in document.Blocks)
		{
			switch (block)
			{
				case Paragraph paragraph:
					paragraph.Margin = new Thickness(0, 0, 0, 12);
					break;
				case List list:
					list.Margin = new Thickness(20, 4, 0, 12);
					break;
				case Section section:
					section.Margin = new Thickness(0, 12, 0, 18);
					break;
				case Table table:
					table.Margin = new Thickness(0, 12, 0, 18);
					break;
			}
		}
	}

    private object? TryFindResource(object key)
    {
        return System.Windows.Application.Current?.TryFindResource(key);
	}

	private FlowDocument BuildMessageDocument(string message)
	{
		var document = new FlowDocument(new Paragraph(new Run(message)))
		{
			PagePadding = new Thickness(32),
			FontSize = 14,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };

        if (TryFindResource("MahApps.Brushes.Text") is System.Windows.Media.Brush textBrush)
        {
            document.SetValue(TextElement.ForegroundProperty, textBrush);
        }

		return document;
	}

	private FlowDocument BuildMissingDocument(string path)
	{
		var paragraph = new Paragraph
		{
			Inlines =
			{
				new Run("The requested guide chapter could not be found.") { FontWeight = FontWeights.SemiBold },
				new LineBreak(),
				new LineBreak(),
				new Run(path)
			}
		};

        return BuildMessageDocument(paragraph);
	}

	private FlowDocument BuildErrorDocument(Exception ex)
	{
		var paragraph = new Paragraph
		{
			Inlines =
			{
				new Run("An error occurred while parsing the guide.") { FontWeight = FontWeights.SemiBold },
				new LineBreak(),
				new LineBreak(),
				new Run(ex.Message)
			}
		};

		return BuildMessageDocument(paragraph);
	}

	private FlowDocument BuildMessageDocument(Paragraph paragraph)
	{
		var document = new FlowDocument(paragraph)
		{
			PagePadding = new Thickness(32),
			FontSize = 14,
            FontFamily = new System.Windows.Media.FontFamily("Segoe UI")
        };

        if (TryFindResource("MahApps.Brushes.Text") is System.Windows.Media.Brush textBrush)
        {
            document.SetValue(TextElement.ForegroundProperty, textBrush);
        }

        return document;
	}

	private void OpenDocsFolder()
	{
		try
		{
			if (!Directory.Exists(_docsRoot))
			{
				StatusMessage = "Docs folder not found on disk.";
				return;
			}

			Process.Start(new ProcessStartInfo
			{
				FileName = _docsRoot,
				UseShellExecute = true
			});
		}
		catch (Exception ex)
		{
			StatusMessage = $"Unable to open folder: {ex.Message}";
		}
	}

	private static string ResolveDocsRoot()
	{
		var baseDir = AppDomain.CurrentDomain.BaseDirectory;
		var candidate = Path.Combine(baseDir, "docs", "OrbitersGuide");

		if (Directory.Exists(candidate))
		{
			return candidate;
		}

		var current = baseDir;

		for (var i = 0; i < 6; i++)
		{
			current = Path.GetFullPath(Path.Combine(current, ".."));
			candidate = Path.Combine(current, "docs", "OrbitersGuide");

			if (Directory.Exists(candidate))
			{
				return candidate;
			}
		}

		return Path.Combine(baseDir, "docs", "OrbitersGuide");
	}

    protected void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

public sealed class GuideSection
{
    public GuideSection(string key, string title, string description, string fileName, PackIconMaterialKind icon)
    {
        Key = key;
        Title = title;
        Description = description;
        FileName = fileName;
        Icon = icon;
    }

    public string Key { get; }
    public string Title { get; }
    public string Description { get; }
    public string FileName { get; }
    public PackIconMaterialKind Icon { get; }
}

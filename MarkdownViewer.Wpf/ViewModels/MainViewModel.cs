using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MarkdownViewer.Wpf.Models;
using MarkdownViewer.Wpf.NativeHost;
using MarkdownViewer.Wpf.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Threading;
using MarkdownRenderDocument = MarkdownViewer.Wpf.Services.MarkdownRenderDocument;

namespace MarkdownViewer.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject, IMainViewModel
{
    private readonly IMarkdownDocumentService _markdownDocumentService;
    private CancellationTokenSource? _loadCts;
    private IMarkdownRendererBridge? _renderer;
    private MarkdownRenderDocument? _currentDocument;
    private readonly List<int> _searchParagraphMatches = [];
    private int _searchMatchIndex = -1;
    private readonly DispatcherTimer _contentHeightTimer;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _currentFilePath = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private int _paragraphCount;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _parseStats = "";

    private double _contentHeight = 0.0;

    [ObservableProperty]
    private double _scrollPosition = 0.0;

    public bool HasDocument => _currentDocument is not null;

    public bool IsLoading => IsBusy;

    public double ContentHeight
    {
        get => _contentHeight;
        set => SetProperty(ref _contentHeight, value);
    }

    public Theme CurrentTheme => IsDarkTheme 
        ? new Theme(0xFF1E1E1E, 0xFFF1F1F1, 0xFF66AFFF, 0xFF2A2A2A, 0xFF4F7D9F, 0x664F9EE3)
        : new Theme(0xFFFDFDFD, 0xFF1E1E1E, 0xFF0A66CC, 0xFFF0F0F0, 0xFF7090B0, 0x66318CE7);

    public string CurrentFileName => string.IsNullOrWhiteSpace(CurrentFilePath) ? "No file" : Path.GetFileName(CurrentFilePath);

    public MainViewModel(IMarkdownDocumentService markdownDocumentService)
    {
        _markdownDocumentService = markdownDocumentService;
        
        _contentHeightTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(100)
        };
        _contentHeightTimer.Tick += (_, _) => UpdateContentHeight();
    }

    public void AttachRenderer(IMarkdownRendererBridge renderer)
    {
        _renderer = renderer;
        _renderer.LinkActivated += OnRendererLinkActivated;
        _renderer.SetTheme(CurrentTheme);

        if (_currentDocument is not null)
        {
            _renderer.SetDocument(_currentDocument);
        }

        // Initialize scroll position from renderer
        ScrollPosition = _renderer.GetScrollY();
        ContentHeight = _renderer.GetContentHeight();
        
        // Start content height monitoring
        _contentHeightTimer.Start();
    }

    public void OpenFile(string filePath)
    {
        _ = LoadFileAsync(filePath);
    }

    public void SearchNext(string query)
    {
        SearchQuery = query;
        SearchNext();
    }

    public void SearchPrevious(string query)
    {
        SearchQuery = query;
        SearchPrevious();
    }

    [RelayCommand]
    private async Task OpenFileAsync()
    {
        var dialog = new OpenFileDialog
        {
            Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|Text files (*.txt)|*.txt|All files (*.*)|*.*",
            CheckFileExists = true,
            Multiselect = false
        };

        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await LoadFileAsync(dialog.FileName);
    }

    [RelayCommand]
    private async Task ReloadAsync()
    {
        if (string.IsNullOrWhiteSpace(CurrentFilePath) || !File.Exists(CurrentFilePath))
        {
            return;
        }

        await LoadFileAsync(CurrentFilePath);
    }

    [RelayCommand]
    public void ToggleTheme()
    {
        IsDarkTheme = !IsDarkTheme;

        // TODO: Implement proper theme switching for .NET 10
        // ApplicationThemeManager.Apply(IsDarkTheme ? ApplicationTheme.Dark : ApplicationTheme.Light);

        _renderer?.SetTheme(CurrentTheme);
        StatusMessage = IsDarkTheme ? "Dark theme enabled" : "Light theme enabled";
    }

    [RelayCommand]
    private void SearchNext()
    {
        if (_currentDocument is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        if (_searchParagraphMatches.Count == 0)
        {
            RecomputeSearchMatches();
        }

        if (_searchParagraphMatches.Count == 0)
        {
            StatusMessage = "No match found";
            return;
        }

        _searchMatchIndex = (_searchMatchIndex + 1) % _searchParagraphMatches.Count;
        JumpToSearchMatch(_searchMatchIndex);
    }

    [RelayCommand]
    private void SearchPrevious()
    {
        if (_currentDocument is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        if (_searchParagraphMatches.Count == 0)
        {
            RecomputeSearchMatches();
        }

        if (_searchParagraphMatches.Count == 0)
        {
            StatusMessage = "No match found";
            return;
        }

        _searchMatchIndex = (_searchMatchIndex - 1 + _searchParagraphMatches.Count) % _searchParagraphMatches.Count;
        JumpToSearchMatch(_searchMatchIndex);
    }

    partial void OnSearchQueryChanged(string value)
    {
        RecomputeSearchMatches();
    }

    partial void OnCurrentFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentFileName));
    }

    private async Task LoadFileAsync(string path)
    {
        _loadCts?.Cancel();
        _loadCts?.Dispose();
        _loadCts = new CancellationTokenSource();

        try
        {
            IsBusy = true;
            StatusMessage = "Loading markdown...";
            CurrentFilePath = path;

            var sw = Stopwatch.StartNew();
            var markdown = await File.ReadAllTextAsync(path, _loadCts.Token);
            var parseDoc = await _markdownDocumentService.ParseAsync(markdown, _loadCts.Token);
            sw.Stop();

            _currentDocument = parseDoc;
            ParagraphCount = parseDoc.Paragraphs.Length;
            ParseStats = $"Parsed in {sw.ElapsedMilliseconds} ms";
            _renderer?.SetDocument(parseDoc);
            RecomputeSearchMatches();

            StatusMessage = $"Loaded {CurrentFileName}";
        }
        catch (OperationCanceledException)
        {
            StatusMessage = "Load canceled";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void RecomputeSearchMatches()
    {
        _searchParagraphMatches.Clear();
        _searchMatchIndex = -1;

        if (_currentDocument is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        var comparison = StringComparison.OrdinalIgnoreCase;
        for (var i = 0; i < _currentDocument.Paragraphs.Length; i++)
        {
            if (_currentDocument.Paragraphs[i].Text.Contains(SearchQuery, comparison))
            {
                _searchParagraphMatches.Add(i);
            }
        }

        StatusMessage = _searchParagraphMatches.Count == 0
            ? "No search matches"
            : $"{_searchParagraphMatches.Count} matches";
    }

    private void JumpToSearchMatch(int matchIndex)
    {
        if (_renderer is null || matchIndex < 0 || matchIndex >= _searchParagraphMatches.Count)
        {
            return;
        }

        var paragraphIndex = (uint)_searchParagraphMatches[matchIndex];
        var y = _renderer.GetParagraphY(paragraphIndex);
        _renderer.SetScrollY(Math.Max(0, y - 24));

        StatusMessage = $"Match {matchIndex + 1}/{_searchParagraphMatches.Count}";
    }

    private void OnRendererLinkActivated(uint linkId)
    {
        if (_currentDocument is null || !_currentDocument.LinkTargets.TryGetValue(linkId, out var url))
        {
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusMessage = $"Opened link: {url}";
        }
        catch (Exception ex)
        {
            StatusMessage = $"Failed to open link: {ex.Message}";
        }
    }

    private void UpdateContentHeight()
    {
        if (_renderer is not null)
        {
            ContentHeight = _renderer.GetContentHeight();
        }
    }

    partial void OnScrollPositionChanged(double value)
    {
        _renderer?.SetScrollY((float)value);
    }
}

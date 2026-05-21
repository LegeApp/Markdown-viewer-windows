using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Markdig.Syntax;
using MarkdownViewer.Wpf.Services;
using Microsoft.Win32;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Documents;
using System.Windows.Threading;
using Block = System.Windows.Documents.Block;
using Table = System.Windows.Documents.Table;
using List = System.Windows.Documents.List;

namespace MarkdownViewer.Wpf.ViewModels;

public partial class MainViewModel : ObservableObject, IMainViewModel
{
    private readonly IMarkdownDocumentService _markdownDocumentService;
    private CancellationTokenSource? _loadCts;
    private MarkdownDocument? _currentAst;
    private List<Block>? _searchMatches;
    private int _searchMatchIndex = -1;
    private string _lastSearchQuery = "";
    private string? _requestedFilePath;
    private readonly DispatcherTimer _searchDebounceTimer;

    [ObservableProperty]
    private string _statusMessage = "Ready";

    [ObservableProperty]
    private string _currentFilePath = "";

    [ObservableProperty]
    private bool _isBusy;

    [ObservableProperty]
    private string _searchQuery = "";

    [ObservableProperty]
    private bool _isDarkTheme;

    [ObservableProperty]
    private string _parseStats = "";

    [ObservableProperty]
    private FlowDocument? _document;

    public bool HasDocument => Document is not null;

    public bool IsLoading => IsBusy;

    public string CurrentFileName => string.IsNullOrWhiteSpace(CurrentFilePath) ? "No file" : Path.GetFileName(CurrentFilePath);

    public string WindowTitle
    {
        get
        {
            var path = string.IsNullOrWhiteSpace(_requestedFilePath) ? CurrentFilePath : _requestedFilePath;
            return string.IsNullOrWhiteSpace(path) ? "Markdown Viewer" : $"{Path.GetFileName(path)} - Markdown Viewer";
        }
    }

    public MainViewModel(IMarkdownDocumentService markdownDocumentService)
    {
        _markdownDocumentService = markdownDocumentService;
        _searchDebounceTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(180),
        };
        _searchDebounceTimer.Tick += (_, _) =>
        {
            _searchDebounceTimer.Stop();
            InvalidateSearchMatches();
            UpdateSearchStatus();
        };
    }

    public void OpenFile(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            StatusMessage = "No file path was provided";
            return;
        }

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
            Multiselect = false,
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
        RebuildFlowDocument();
        StatusMessage = IsDarkTheme ? "Dark theme enabled" : "Light theme enabled";
    }

    [RelayCommand]
    private void SearchNext() => Step(+1);

    [RelayCommand]
    private void SearchPrevious() => Step(-1);

    private void Step(int direction)
    {
        if (_currentAst is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        EnsureSearchMatches();
        if (_searchMatches is null || _searchMatches.Count == 0)
        {
            StatusMessage = "No match found";
            return;
        }

        _searchMatchIndex = ((_searchMatchIndex + direction) % _searchMatches.Count + _searchMatches.Count) % _searchMatches.Count;
        _searchMatches[_searchMatchIndex].BringIntoView();
        StatusMessage = $"Match {_searchMatchIndex + 1}/{_searchMatches.Count}";
    }

    partial void OnSearchQueryChanged(string value)
    {
        _searchDebounceTimer.Stop();
        _searchDebounceTimer.Start();
    }

    partial void OnCurrentFilePathChanged(string value)
    {
        OnPropertyChanged(nameof(CurrentFileName));
        OnPropertyChanged(nameof(WindowTitle));
    }

    partial void OnIsBusyChanged(bool value)
    {
        OnPropertyChanged(nameof(IsLoading));
    }

    partial void OnDocumentChanged(FlowDocument? value)
    {
        OnPropertyChanged(nameof(HasDocument));
        InvalidateSearchMatches();
    }

    private async Task LoadFileAsync(string path)
    {
        var normalizedPath = NormalizeFilePath(path);
        var loadCts = new CancellationTokenSource();
        var previousLoad = Interlocked.Exchange(ref _loadCts, loadCts);
        previousLoad?.Cancel();
        previousLoad?.Dispose();

        try
        {
            if (!File.Exists(normalizedPath))
            {
                throw new FileNotFoundException("The selected file does not exist.", normalizedPath);
            }

            _requestedFilePath = normalizedPath;
            OnPropertyChanged(nameof(WindowTitle));
            IsBusy = true;
            StatusMessage = $"Opening {Path.GetFileName(normalizedPath)}...";

            var sw = Stopwatch.StartNew();
            var ast = await _markdownDocumentService.ParseFileAsync(normalizedPath, loadCts.Token);
            ast.SetData("BaseUri", new Uri(normalizedPath));
            sw.Stop();

            _currentAst = ast;
            CurrentFilePath = normalizedPath;
            ParseStats = $"Parsed in {sw.ElapsedMilliseconds} ms";
            RebuildFlowDocument();

            StatusMessage = $"Loaded {CurrentFileName}";
        }
        catch (OperationCanceledException)
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                StatusMessage = "Load canceled";
            }
        }
        catch (Exception ex)
        {
            StatusMessage = $"Load failed: {ex.Message}";
        }
        finally
        {
            if (ReferenceEquals(_loadCts, loadCts))
            {
                _requestedFilePath = null;
                OnPropertyChanged(nameof(WindowTitle));
                _loadCts = null;
                IsBusy = false;
            }

            loadCts.Dispose();
        }
    }

    private void RebuildFlowDocument()
    {
        if (_currentAst is null)
        {
            Document = null;
            return;
        }

        Document = _markdownDocumentService.BuildFlowDocument(_currentAst, IsDarkTheme);
    }

    private void InvalidateSearchMatches()
    {
        _searchMatches = null;
        _searchMatchIndex = -1;
        _lastSearchQuery = "";
    }

    private void EnsureSearchMatches()
    {
        if (_searchMatches is not null && string.Equals(_lastSearchQuery, SearchQuery, StringComparison.Ordinal))
        {
            return;
        }

        _searchMatches = new List<Block>();
        _searchMatchIndex = -1;
        _lastSearchQuery = SearchQuery;

        if (Document is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        foreach (var block in EnumerateBlocks(Document.Blocks))
        {
            if (ExtractText(block).Contains(SearchQuery, StringComparison.OrdinalIgnoreCase))
            {
                _searchMatches.Add(block);
            }
        }
    }

    private void UpdateSearchStatus()
    {
        if (Document is null || string.IsNullOrWhiteSpace(SearchQuery))
        {
            return;
        }

        EnsureSearchMatches();
        StatusMessage = _searchMatches!.Count == 0
            ? "No search matches"
            : $"{_searchMatches.Count} matches";
    }

    private static IEnumerable<Block> EnumerateBlocks(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            yield return block;

            switch (block)
            {
                case Section section:
                    foreach (var child in EnumerateBlocks(section.Blocks)) yield return child;
                    break;
                case List list:
                    foreach (var item in list.ListItems)
                    foreach (var child in EnumerateBlocks(item.Blocks)) yield return child;
                    break;
                case Table table:
                    foreach (var group in table.RowGroups)
                    foreach (var row in group.Rows)
                    foreach (var cell in row.Cells)
                    foreach (var child in EnumerateBlocks(cell.Blocks)) yield return child;
                    break;
            }
        }
    }

    private static string ExtractText(Block block)
    {
        var range = new TextRange(block.ContentStart, block.ContentEnd);
        return range.Text;
    }

    private static string NormalizeFilePath(string path)
    {
        return Path.GetFullPath(Environment.ExpandEnvironmentVariables(path.Trim()));
    }
}

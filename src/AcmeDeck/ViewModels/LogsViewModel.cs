using System.Collections.ObjectModel;
using AcmeDeck.Infrastructure;
using AcmeDeck.Services;

namespace AcmeDeck.ViewModels;

public sealed record LogFile(string Name, string Path, long Size, DateTime Modified)
{
    public string Info => $"{Modified:dd.MM.yyyy HH:mm} · {Size / 1024.0:0.#} KB";
}

/// <summary>
/// Shows the daily win-acme log files (log-yyyyMMdd.txt).
/// </summary>
public sealed class LogsViewModel : ObservableObject
{
    private readonly string _folder;
    private LogFile? _selected;
    private bool _onlyProblems;
    private string? _search;
    private string _content = "";
    private string[] _lines = [];

    public LogsViewModel(string folder)
    {
        _folder = folder;
        RefreshCommand = new RelayCommand(Refresh);
        OpenFolderCommand = new RelayCommand(() => MainViewModel.OpenInExplorer(_folder));
        Refresh();
    }

    public ObservableCollection<LogFile> Files { get; } = [];
    public string Folder => _folder;

    public LogFile? Selected
    {
        get => _selected;
        set
        {
            if (SetProperty(ref _selected, value))
            {
                Load();
            }
        }
    }

    public bool OnlyProblems
    {
        get => _onlyProblems;
        set
        {
            if (SetProperty(ref _onlyProblems, value))
            {
                ApplyFilter();
            }
        }
    }

    public string? Search
    {
        get => _search;
        set
        {
            if (SetProperty(ref _search, value))
            {
                ApplyFilter();
            }
        }
    }

    public string Content
    {
        get => _content;
        private set => SetProperty(ref _content, value);
    }

    public RelayCommand RefreshCommand { get; }
    public RelayCommand OpenFolderCommand { get; }

    private void Refresh()
    {
        var current = Selected?.Path;
        Files.Clear();
        if (Directory.Exists(_folder))
        {
            foreach (var info in new DirectoryInfo(_folder).GetFiles("*.txt").OrderByDescending(f => f.LastWriteTime))
            {
                Files.Add(new LogFile(info.Name, info.FullName, info.Length, info.LastWriteTime));
            }
        }
        _selected = null;
        Selected = Files.FirstOrDefault(f => f.Path == current) ?? Files.FirstOrDefault();
        if (Selected == null)
        {
            Content = Directory.Exists(_folder) ? "No log files yet." : $"Log folder not found: {_folder}";
        }
    }

    private void Load()
    {
        if (Selected == null)
        {
            _lines = [];
            Content = "";
            return;
        }
        try
        {
            _lines = RenewalStore.ReadShared(Selected.Path).Split('\n').Select(l => l.TrimEnd('\r')).ToArray();
        }
        catch (Exception ex)
        {
            _lines = [$"Could not read {Selected.Path}: {ex.Message}"];
        }
        ApplyFilter();
    }

    /// <summary>
    /// "Only warnings and errors" keeps [WRN]/[ERR]/[FTL] entries together with
    /// their continuation lines (stack traces, multi-line messages).
    /// </summary>
    private void ApplyFilter()
    {
        IEnumerable<string> lines = _lines;
        if (OnlyProblems)
        {
            var result = new List<string>();
            var keep = false;
            foreach (var line in _lines)
            {
                var isEntry = line.Length > 20 && char.IsDigit(line[0]) && line.Contains(" [");
                if (isEntry)
                {
                    keep = line.Contains("[WRN]") || line.Contains("[ERR]") || line.Contains("[FTL]");
                }
                if (keep)
                {
                    result.Add(line);
                }
            }
            lines = result;
        }
        if (!string.IsNullOrWhiteSpace(Search))
        {
            lines = lines.Where(l => l.Contains(Search, StringComparison.OrdinalIgnoreCase));
        }
        Content = string.Join(Environment.NewLine, lines);
    }
}

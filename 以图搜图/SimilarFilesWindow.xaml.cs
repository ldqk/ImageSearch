using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using 以图搜图.Models;
using 以图搜图.Services;

namespace 以图搜图;

public partial class SimilarFilesWindow : INotifyPropertyChanged
{
    private static SimilarFilesWindow? _instance;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".gif",
        ".jpg",
        ".jpeg",
        ".png",
        ".bmp",
        ".webp"
    };

    private readonly ImageIndexService _indexService = ImageIndexService.Instance;
    private readonly ImageSearchService _searchService = new();
    private CancellationTokenSource? _scanCancellationTokenSource;
    private bool _isUpdatingMarks;
    private bool _isDeleting;
    private bool _filterRefreshPending;
    private readonly HashSet<SimilarFileGroup> _pendingFilterGroups = [];
    private string[] _scannedImagePaths = [];
    private List<SimilarImagePair> _similarImagePairs = [];

    public ObservableCollection<string> Directories { get; } = [];
    public BulkObservableCollection<SimilarFileGroup> Groups { get; } = [];
    public BulkObservableCollection<SimilarFileGroup> VisibleGroups { get; } = [];
    public BulkObservableCollection<SimilarDirectoryGroup> DuplicateDirectoryGroups { get; } = [];
    public event PropertyChangedEventHandler? PropertyChanged;

    public int DuplicateGroupCount => Groups.Count;
    public int DuplicateFileCount => Groups.Sum(group => group.Files.Count);
    public int MarkedFileCount => Groups.Sum(group => group.Files.Count(file => file.IsMarked));

    private int _scannedImageCount;

    public int ScannedImageCount
    {
        get => _scannedImageCount;
        private set
        {
            if (_scannedImageCount != value)
            {
                _scannedImageCount = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ScannedImageCount)));
            }
        }
    }

    public bool HasDuplicateDirectories => DuplicateDirectoryGroups.Count > 0;

    private bool _isThumbnailMode;

    public bool IsThumbnailMode
    {
        get => _isThumbnailMode;
        private set
        {
            if (_isThumbnailMode != value)
            {
                _isThumbnailMode = value;
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsThumbnailMode)));
            }
        }
    }

    private double _thumbnailSize = 220;

    public double ThumbnailSize
    {
        get => _thumbnailSize;
        set
        {
            if (Math.Abs(_thumbnailSize - value) < 0.1)
            {
                return;
            }

            _thumbnailSize = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(ThumbnailSize)));
        }
    }

    private SimilarFilesWindow()
    {
        InitializeComponent();
        DataContext = this;
        SimilaritySlider.ValueChanged += (_, _) => SimilarityLabel.Text = $"{SimilaritySlider.Value:F0}%";
    }

    private void ResultsList_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ListBox listBox || e.Delta == 0)
        {
            return;
        }

        var scrollViewer = FindVisualChild<ScrollViewer>(listBox);
        if (scrollViewer == null)
        {
            return;
        }

        var scrollLines = SystemParameters.WheelScrollLines;
        if (scrollLines == 0)
        {
            e.Handled = true;
            return;
        }

        var scrollDistance = scrollLines < 0 ? scrollViewer.ViewportHeight : scrollLines * 16d;
        scrollViewer.ScrollToVerticalOffset(scrollViewer.VerticalOffset - e.Delta / 120d * scrollDistance);
        e.Handled = true;
    }

    private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                return match;
            }

            if (FindVisualChild<T>(child) is { } descendant)
            {
                return descendant;
            }
        }

        return null;
    }

    public static void ShowSingle()
    {
        if (_instance is {IsVisible: true} existingWindow)
        {
            if (existingWindow.WindowState == WindowState.Minimized)
            {
                existingWindow.WindowState = WindowState.Normal;
            }

            existingWindow.Activate();
            return;
        }

        var window = new SimilarFilesWindow
        {
            WindowStartupLocation = WindowStartupLocation.CenterScreen
        };
        _instance = window;
        window.Closed += (_, _) =>
        {
            if (ReferenceEquals(_instance, window))
            {
                _instance = null;
            }
        };
        window.Show();
    }

    private void AddDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "选择文件夹",
            ValidateNames = false
        };

        if (dialog.ShowDialog(this) == true)
        {
            var path = Path.GetDirectoryName(dialog.FileName);
            if (!string.IsNullOrWhiteSpace(path) && !Directories.Contains(path, StringComparer.OrdinalIgnoreCase))
            {
                Directories.Add(path);
            }
        }
    }

    private void RemoveDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button {Tag: string path})
        {
            Directories.Remove(path);
        }
    }

    private void AddPath_Click(object sender, RoutedEventArgs e) => AddPathsFromTextBox();

    private void DirectoryPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddPathsFromTextBox();
            e.Handled = true;
        }
    }

    private void AddPathsFromTextBox()
    {
        var paths = DirectoryPathBox.Text.Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(path => path.Trim('"'));
        var added = 0;
        foreach (var path in paths.Where(path => Directory.Exists(path) && !Directories.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            Directories.Add(path);
            added++;
        }

        SetStatus(added > 0 ? $"已添加 {added} 个扫描位置。" : "未添加位置，请粘贴有效的文件夹路径。", added == 0);
        DirectoryPathBox.Clear();
    }

    private void ClearDirectories_Click(object sender, RoutedEventArgs e)
    {
        Directories.Clear();
        SetStatus("扫描位置已清空。");
    }

    private void DirectoryList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        foreach (var directory in ((string[]) e.Data.GetData(DataFormats.FileDrop)).Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)).Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && !Directories.Contains(directory, StringComparer.OrdinalIgnoreCase)))
        {
            Directories.Add(directory);
        }
    }

    private void ResultView_Checked(object sender, RoutedEventArgs e)
    {
        IsThumbnailMode = sender is RadioButton {Content: "缩略图"};
    }

    private void StopScan_Click(object sender, RoutedEventArgs e)
    {
        var cancellation = _scanCancellationTokenSource;
        if (cancellation == null || cancellation.IsCancellationRequested)
        {
            return;
        }

        StopScanButton.IsEnabled = false;
        SetStatus("正在停止扫描…");
        cancellation.Cancel();
        if (_indexService.IsIndexing)
        {
            _indexService.StopIndexing();
        }
    }

    private async void RefreshResults_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            SetStatus("当前操作尚未完成，请稍后再刷新结果。", true);
            return;
        }

        var files = Groups.SelectMany(group => group.Files).ToArray();
        if (files.Length == 0)
        {
            SetStatus("当前没有重复文件结果可刷新。");
            return;
        }

        ScanButton.IsEnabled = false;
        DeleteMarkedButton.IsEnabled = false;
        RefreshResultsButton.IsEnabled = false;
        try
        {
            SetStatus("正在检查重复文件是否仍然存在…");
            var missingFiles = await Task.Run(() => files.Where(file => !File.Exists(file.FilePath)).ToHashSet());
            var remainingGroups = new List<SimilarFileGroup>(Groups.Count);
            var removedGroups = 0;
            foreach (var group in Groups)
            {
                foreach (var file in group.Files.Where(missingFiles.Contains).ToArray())
                {
                    group.Files.Remove(file);
                    group.VisibleFiles.Remove(file);
                    file.PropertyChanged -= FileItem_PropertyChanged;
                }

                if (group.Files.Count >= 2)
                {
                    remainingGroups.Add(group);
                }
                else
                {
                    foreach (var file in group.Files)
                    {
                        file.PropertyChanged -= FileItem_PropertyChanged;
                    }

                    removedGroups++;
                }
            }

            Groups.ReplaceAll(remainingGroups);
            RefreshVisibleGroups();
            await RefreshDuplicateDirectoryGroupsAsync(missingFiles.Select(file => file.FilePath));
            SetStatus($"刷新完成：移除 {missingFiles.Count:N0} 个不存在的文件，移除 {removedGroups:N0} 个不足两项的组。");
        }
        catch (Exception ex)
        {
            SetStatus($"刷新结果失败：{ex.Message}", true);
        }
        finally
        {
            ScanButton.IsEnabled = true;
            DeleteMarkedButton.IsEnabled = true;
            RefreshResultsButton.IsEnabled = true;
        }
    }

    private async void Scan_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting)
        {
            SetStatus("批量操作尚未完成，请稍后再扫描。", true);
            return;
        }

        if (_indexService.IsIndexing)
        {
            SetStatus("当前有其他索引任务正在运行，请完成后再扫描。", true);
            return;
        }

        var scanDirectories = Directories.Where(Directory.Exists).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (scanDirectories.Length == 0)
        {
            SetStatus("请先添加有效的扫描文件夹。", true);
            return;
        }

        var cancellationSource = new CancellationTokenSource();
        _scanCancellationTokenSource = cancellationSource;
        var cancellationToken = cancellationSource.Token;
        ScanButton.IsEnabled = false;
        StopScanButton.Visibility = Visibility.Visible;
        StopScanButton.IsEnabled = true;
        Groups.Clear();
        VisibleGroups.Clear();
        DuplicateDirectoryGroups.Clear();
        _scannedImagePaths = [];
        _similarImagePairs = [];
        ScannedImageCount = 0;
        NotifyResultSummary();
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuplicateDirectories)));
        try
        {
            SetStatus("正在检查扫描位置和索引…");
            var files = GetFiles(scanDirectories);
            cancellationToken.ThrowIfCancellationRequested();
            var missing = await Task.Run(() => files.Where(path => !_indexService.Index.ContainsKey(path) && !_indexService.FrameIndex.ContainsKey(path)).ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (missing.Length > 0)
            {
                SetStatus($"发现 {missing.Length:N0} 个图片尚未建立索引，正在建立索引…");
                await Task.Run(() => _indexService.UpdateIndexAsync(scanDirectories, false), cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var indexedPaths = await Task.Run(() => files.Where(path => _indexService.Index.ContainsKey(path) || _indexService.FrameIndex.ContainsKey(path)).ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            ScannedImageCount = indexedPaths.Length;
            if (indexedPaths.Length < 2)
            {
                SetStatus($"扫描位置共有 {files.Length:N0} 张图片，已索引 {indexedPaths.Length:N0} 张，至少需要两张图片。", true);
                MainTabControl.SelectedIndex = 1;
                return;
            }

            var algorithm = GetSelectedAlgorithm();
            var threshold = (float) (SimilaritySlider.Value / 100d);
            SetStatus($"正在准备并比较 {indexedPaths.Length:N0} 张图片…");
            var progress = new Progress<int>(completed => SetStatus($"正在比较图片：{completed:N0}/{indexedPaths.Length:N0}，请稍候…"));
            var pairs = await _searchService.FindSimilarPairsAsync(indexedPaths, _indexService.Index, _indexService.FrameIndex, algorithm, threshold, IgnoreSameFolderCheckBox.IsChecked == true, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var groups = await Task.Run(() => BuildGroups(indexedPaths, pairs, cancellationToken), cancellationToken);
            var duplicateDirectoryGroups = await Task.Run(() => BuildDuplicateDirectoryGroups(indexedPaths, pairs, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Groups.ReplaceAll(groups);
            _scannedImagePaths = indexedPaths;
            _similarImagePairs = pairs;
            DuplicateDirectoryGroups.ReplaceAll(duplicateDirectoryGroups);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuplicateDirectories)));
            SetStatus($"扫描完成：检查 {indexedPaths.Length:N0} 张图片，找到 {Groups.Count:N0} 组相似文件、{Groups.Sum(group => group.Files.Count):N0} 个文件。");
            RefreshVisibleGroups();
            MainTabControl.SelectedIndex = 1;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("扫描已停止。");
        }
        catch (Exception ex)
        {
            SetStatus($"扫描失败：{ex.Message}", true);
        }
        finally
        {
            if (ReferenceEquals(_scanCancellationTokenSource, cancellationSource))
            {
                _scanCancellationTokenSource = null;
            }

            cancellationSource.Dispose();
            ScanButton.IsEnabled = true;
            StopScanButton.IsEnabled = false;
            StopScanButton.Visibility = Visibility.Collapsed;
        }
    }

    private static string[] GetFiles(string[] directories)
    {
        if (File.Exists("Everything64.dll") && Process.GetProcessesByName("Everything").Length > 0)
        {
            return directories.SelectMany(s =>
            {
                var array = EverythingHelper.EnumerateFiles(s).ToArray();
                return array.Length == 0 ? Directory.GetFiles(s, "*", SearchOption.AllDirectories) : array;
            }).ToArray();
        }

        return directories.SelectMany(static s =>
        {
            try
            {
                return Directory.GetFiles(s, "*", SearchOption.AllDirectories);
            }
            catch
            {
                return [];
            }
        }).ToArray();
    }

    private MatchAlgorithm GetSelectedAlgorithm()
    {
        return (AlgorithmCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() switch
        {
            "Difference Hash" => MatchAlgorithm.DifferenceHash,
            "DCT Hash 32" => MatchAlgorithm.DctHash32,
            "DCT Hash 64" => MatchAlgorithm.DctHash64,
            _ => MatchAlgorithm.All
        };
    }

    private List<SimilarFileGroup> BuildGroups(string[] paths, List<SimilarImagePair> matches, CancellationToken cancellationToken)
    {
        var groups = new List<SimilarFileGroup>();
        var neighbors = paths.ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var scores = new Dictionary<(string First, string Second), float>();
        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            neighbors[match.FirstPath].Add(match.SecondPath);
            neighbors[match.SecondPath].Add(match.FirstPath);
            var pair = string.Compare(match.FirstPath, match.SecondPath, StringComparison.OrdinalIgnoreCase) < 0 ? (match.FirstPath, match.SecondPath) : (match.SecondPath, match.FirstPath);
            scores[pair] = match.Similarity;
        }

        var remaining = new HashSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var groupNumber = 1;
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = remaining.First();
            remaining.Remove(first);
            var component = new List<string>
            {
                first
            };
            var pending = new Queue<string>();
            pending.Enqueue(first);
            while (pending.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var neighbor in neighbors[current])
                {
                    if (remaining.Remove(neighbor))
                    {
                        component.Add(neighbor);
                        pending.Enqueue(neighbor);
                    }
                }
            }

            if (component.Count < 2)
            {
                continue;
            }

            var fileItems = component.Select(path => CreateFileItem(path, component, scores)).OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
            var group = new SimilarFileGroup(groupNumber++, fileItems);
            foreach (var file in fileItems)
            {
                file.PropertyChanged += FileItem_PropertyChanged;
            }

            groups.Add(group);
        }

        return groups;
    }

    private static List<SimilarDirectoryGroup> BuildDuplicateDirectoryGroups(string[] paths, List<SimilarImagePair> matches, CancellationToken cancellationToken)
    {
        var filesByDirectory = paths.GroupBy(path => Path.GetDirectoryName(path)!, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.ToHashSet(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var pairCoverage = new Dictionary<(string FirstDirectory, string SecondDirectory), (HashSet<string> FirstFiles, HashSet<string> SecondFiles)>();

        foreach (var match in matches)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var firstDirectory = Path.GetDirectoryName(match.FirstPath);
            var secondDirectory = Path.GetDirectoryName(match.SecondPath);
            if (string.IsNullOrWhiteSpace(firstDirectory) || string.IsNullOrWhiteSpace(secondDirectory) || string.Equals(firstDirectory, secondDirectory, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var firstPath = match.FirstPath;
            var secondPath = match.SecondPath;
            if (string.Compare(firstDirectory, secondDirectory, StringComparison.OrdinalIgnoreCase) > 0)
            {
                (firstDirectory, secondDirectory) = (secondDirectory, firstDirectory);
                (firstPath, secondPath) = (secondPath, firstPath);
            }

            var key = (firstDirectory, secondDirectory);
            if (!pairCoverage.TryGetValue(key, out var coverage))
            {
                coverage = (new HashSet<string>(StringComparer.OrdinalIgnoreCase), new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                pairCoverage.Add(key, coverage);
            }

            coverage.FirstFiles.Add(firstPath);
            coverage.SecondFiles.Add(secondPath);
        }

        var neighbors = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var (pair, coverage) in pairCoverage)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!filesByDirectory.TryGetValue(pair.FirstDirectory, out var firstFiles) || !filesByDirectory.TryGetValue(pair.SecondDirectory, out var secondFiles) || firstFiles.Count != secondFiles.Count || coverage.FirstFiles.Count != firstFiles.Count || coverage.SecondFiles.Count != secondFiles.Count)
            {
                continue;
            }

            if (!neighbors.TryGetValue(pair.FirstDirectory, out var firstNeighbors))
            {
                firstNeighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                neighbors.Add(pair.FirstDirectory, firstNeighbors);
            }

            if (!neighbors.TryGetValue(pair.SecondDirectory, out var secondNeighbors))
            {
                secondNeighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                neighbors.Add(pair.SecondDirectory, secondNeighbors);
            }

            firstNeighbors.Add(pair.SecondDirectory);
            secondNeighbors.Add(pair.FirstDirectory);
        }

        var groups = new List<SimilarDirectoryGroup>();
        var remaining = new HashSet<string>(neighbors.Keys, StringComparer.OrdinalIgnoreCase);
        var groupNumber = 1;
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = remaining.First();
            remaining.Remove(first);
            var directories = new List<string>
            {
                first
            };
            var pending = new Queue<string>();
            pending.Enqueue(first);
            while (pending.TryDequeue(out var current))
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var neighbor in neighbors[current])
                {
                    if (remaining.Remove(neighbor))
                    {
                        directories.Add(neighbor);
                        pending.Enqueue(neighbor);
                    }
                }
            }

            if (directories.Count < 2)
            {
                continue;
            }

            var items = directories.Order(StringComparer.OrdinalIgnoreCase).Select(directory =>
            {
                var filePaths = filesByDirectory[directory];
                var totalSize = filePaths.Sum(path =>
                {
                    try
                    {
                        return File.Exists(path) ? new FileInfo(path).Length : 0;
                    }
                    catch
                    {
                        return 0;
                    }
                });
                return new SimilarDirectoryItem(directory, filePaths.Count, totalSize);
            }).ToList();
            groups.Add(new SimilarDirectoryGroup(groupNumber++, items));
        }

        return groups;
    }

    private static SimilarFileItem CreateFileItem(string path, List<string> group, Dictionary<(string First, string Second), float> matches)
    {
        var file = new FileInfo(path);
        var similarity = group.Where(other => !other.Equals(path, StringComparison.OrdinalIgnoreCase)).Select(other =>
        {
            var pair = string.Compare(path, other, StringComparison.OrdinalIgnoreCase) < 0 ? (path, other) : (other, path);
            return matches.GetValueOrDefault(pair, 0);
        }).DefaultIfEmpty(0).Max();
        return new SimilarFileItem(path, file.Length, file.LastWriteTime, similarity);
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshVisibleGroups();

    private async void DuplicateDirectoryMark_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled || sender is not CheckBox {DataContext: SimilarDirectoryItem directory} checkBox)
        {
            return;
        }

        var directoryFiles = Groups.SelectMany(group => group.Files).Where(file => string.Equals(Path.GetDirectoryName(file.FilePath), directory.DirectoryPath, StringComparison.OrdinalIgnoreCase)).ToArray();
        var shouldMark = checkBox.IsChecked == true;
        SetStatus($"正在{(shouldMark ? "标记" : "取消标记")}目录中的 {directoryFiles.Length:N0} 个文件…");
        _isUpdatingMarks = true;
        try
        {
            const int batchSize = 256;
            for (var offset = 0; offset < directoryFiles.Length; offset += batchSize)
            {
                var end = Math.Min(offset + batchSize, directoryFiles.Length);
                for (var index = offset; index < end; index++)
                {
                    directoryFiles[index].IsMarked = shouldMark;
                }

                if (end < directoryFiles.Length)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                    }, DispatcherPriority.Background);
                }
            }
        }
        finally
        {
            _isUpdatingMarks = false;
        }

        NotifyResultSummary();
        if (GetCurrentFilter() != "全部文件")
        {
            RefreshVisibleGroups();
        }

        SetStatus($"目录“{directory.DirectoryPath}”的 {directoryFiles.Length:N0} 个文件已{(shouldMark ? "标记" : "取消标记")}。", false);
    }

    private void FileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isUpdatingMarks && e.PropertyName == nameof(SimilarFileItem.IsMarked) && sender is SimilarFileItem file)
        {
            NotifyResultSummary();
            if (GetCurrentFilter() != "全部文件")
            {
                ScheduleFilterRefresh(file.Group);
            }
        }
    }

    private void ScheduleFilterRefresh(SimilarFileGroup group)
    {
        _pendingFilterGroups.Add(group);
        if (_filterRefreshPending)
        {
            return;
        }

        _filterRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Background, () =>
        {
            _filterRefreshPending = false;
            var groups = _pendingFilterGroups.ToArray();
            _pendingFilterGroups.Clear();
            foreach (var pendingGroup in groups)
            {
                RefreshVisibleGroup(pendingGroup);
            }
        });
    }

    private void RefreshVisibleGroup(SimilarFileGroup group)
    {
        if (!Groups.Contains(group))
        {
            return;
        }

        var filter = GetCurrentFilter();
        if (filter is "仅已标记文件" or "仅未标记文件")
        {
            group.VisibleFiles.ReplaceAll(filter == "仅已标记文件" ? group.Files.Where(file => file.IsMarked) : group.Files.Where(file => !file.IsMarked));
        }

        var markedCount = group.Files.Count(file => file.IsMarked);
        var shouldShow = filter switch
        {
            "仅已标记文件" => group.VisibleFiles.Count > 0,
            "仅未标记文件" => group.VisibleFiles.Count > 0,
            "含已标记文件的组" => markedCount > 0,
            "没有标记文件的组" => markedCount == 0,
            "组内全部已标记" => markedCount == group.Files.Count,
            _ => true
        };
        var isVisible = VisibleGroups.Contains(group);
        if (shouldShow && !isVisible)
        {
            var groupIndex = Groups.IndexOf(group);
            var visibleIndex = Groups.Take(groupIndex).Count(VisibleGroups.Contains);
            VisibleGroups.Insert(visibleIndex, group);
        }
        else if (!shouldShow && isVisible)
        {
            VisibleGroups.Remove(group);
        }
    }

    private void RefreshVisibleGroups()
    {
        if (FilterCombo == null)
        {
            return;
        }

        NotifyResultSummary();
        var filter = GetCurrentFilter();
        if (filter == "全部文件")
        {
            VisibleGroups.ReplaceAll(Groups);
            return;
        }

        var visibleGroups = new List<SimilarFileGroup>();
        foreach (var group in Groups)
        {
            if (filter is "仅已标记文件" or "仅未标记文件")
            {
                group.VisibleFiles.ReplaceAll(filter == "仅已标记文件" ? group.Files.Where(file => file.IsMarked) : group.Files.Where(file => !file.IsMarked));
            }
            else if (group.VisibleFiles.Count != group.Files.Count)
            {
                group.VisibleFiles.ReplaceAll(group.Files);
            }

            var markedCount = group.Files.Count(file => file.IsMarked);
            var showGroup = filter switch
            {
                "含已标记文件的组" => markedCount > 0,
                "没有标记文件的组" => markedCount == 0,
                "组内全部已标记" => markedCount == group.Files.Count,
                _ => true
            };
            if (showGroup)
            {
                visibleGroups.Add(group);
            }
        }

        VisibleGroups.ReplaceAll(visibleGroups);
    }

    private string GetCurrentFilter() => (FilterCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "全部文件";

    private void FileRow_RightButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGridRow row)
        {
            row.IsSelected = true;
            if (ItemsControl.ItemsControlFromItemContainer(row) is DataGrid grid)
            {
                grid.SelectedItem = row.Item;
            }
        }
    }

    private void FileContextMenu_Opened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu contextMenu)
        {
            return;
        }

        contextMenu.DataContext = contextMenu.PlacementTarget switch
        {
            DataGrid {SelectedItem: SimilarFileItem selectedFile} => selectedFile,
            FrameworkElement {DataContext: SimilarFileItem targetFile} => targetFile,
            _ => null
        };
    }

    private void FileList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (sender is DataGrid {SelectedItem: SimilarFileItem file})
        {
            OpenFile(file.FilePath);
        }
    }

    private void Thumbnail_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2 && sender is FrameworkElement {DataContext: SimilarFileItem file})
        {
            OpenFile(file.FilePath);
            e.Handled = true;
        }
    }

    private void OpenFile_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextFile(sender) is { } file)
        {
            OpenFile(file.FilePath);
        }
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextFile(sender) is { } file && File.Exists(file.FilePath))
        {
            FileExplorerHelper.ExplorerFile(file.FilePath);
        }
    }

    private async void IgnoreDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled || GetContextFile(sender) is not { } file)
        {
            return;
        }

        var directory = Path.GetDirectoryName(file.FilePath);
        if (string.IsNullOrWhiteSpace(directory))
        {
            return;
        }

        _isUpdatingMarks = true;
        try
        {
            SetStatus($"正在从结果中移除目录：{directory}");
            var affectedGroups = await Task.Run(() => Groups.Select(group => (Group: group, Files: group.Files.Where(item => string.Equals(Path.GetDirectoryName(item.FilePath), directory, StringComparison.OrdinalIgnoreCase)).ToArray())).Where(item => item.Files.Length > 0).ToArray());

            if (affectedGroups.Length == 0)
            {
                SetStatus("该目录没有可从结果中移除的文件。", true);
                return;
            }

            var removedFiles = 0;
            foreach (var affected in affectedGroups)
            {
                foreach (var item in affected.Files)
                {
                    affected.Group.Files.Remove(item);
                    affected.Group.VisibleFiles.Remove(item);
                    item.PropertyChanged -= FileItem_PropertyChanged;
                    removedFiles++;
                }
            }

            var remainingGroups = new List<SimilarFileGroup>(Groups.Count);
            var removedGroups = 0;
            foreach (var group in Groups)
            {
                if (group.Files.Count < 2)
                {
                    foreach (var item in group.Files)
                    {
                        item.PropertyChanged -= FileItem_PropertyChanged;
                    }

                    removedGroups++;
                    continue;
                }

                group.VisibleFiles.ReplaceAll(group.Files);
                remainingGroups.Add(group);
            }

            Groups.ReplaceAll(remainingGroups);
            RefreshVisibleGroups();
            RefreshDuplicateDirectoryGroups();
            SetStatus($"已从结果中移除目录“{directory}”的 {removedFiles:N0} 个文件，并移除 {removedGroups:N0} 个不足两项的组。");
        }
        finally
        {
            _isUpdatingMarks = false;
        }
    }

    private void RefreshDuplicateDirectoryGroups()
    {
        var remainingFileCounts = Groups.SelectMany(group => group.Files).GroupBy(item => Path.GetDirectoryName(item.FilePath) ?? string.Empty, StringComparer.OrdinalIgnoreCase).ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase);
        var updatedGroups = DuplicateDirectoryGroups.Select(group => group.Directories.Where(directory => remainingFileCounts.GetValueOrDefault(directory.DirectoryPath) == directory.FileCount).ToList()).Where(directories => directories.Count > 1).Select((directories, index) => new SimilarDirectoryGroup(index + 1, directories)).ToArray();
        DuplicateDirectoryGroups.ReplaceAll(updatedGroups);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuplicateDirectories)));
    }

    private async Task RefreshDuplicateDirectoryGroupsAsync(IEnumerable<string> removedPaths)
    {
        var removed = removedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        if (removed.Count == 0)
        {
            return;
        }

        _scannedImagePaths = _scannedImagePaths.Where(path => !removed.Contains(path)).ToArray();
        _similarImagePairs = _similarImagePairs.Where(pair => !removed.Contains(pair.FirstPath) && !removed.Contains(pair.SecondPath)).ToList();

        var paths = _scannedImagePaths;
        var pairs = _similarImagePairs;
        var updatedGroups = await Task.Run(() => BuildDuplicateDirectoryGroups(paths, pairs, CancellationToken.None));
        DuplicateDirectoryGroups.ReplaceAll(updatedGroups);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuplicateDirectories)));
    }

    private void OpenFile(string path)
    {
        if (!File.Exists(path))
        {
            SetStatus("文件不存在，可能已被移动或删除。", true);
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = path,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"无法打开图片：{ex.Message}", true);
        }
    }

    private void MarkFile_Click(object sender, RoutedEventArgs e) => SetContextFileMark(sender, true);
    private void UnmarkFile_Click(object sender, RoutedEventArgs e) => SetContextFileMark(sender, false);

    private void SetContextFileMark(object sender, bool marked)
    {
        if (!_isUpdatingMarks && !_isDeleting && ScanButton.IsEnabled && GetContextFile(sender) is { } file)
        {
            file.IsMarked = marked;
        }
    }

    private async void MarkOtherFiles_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextFile(sender) is { } file)
        {
            await UpdateContextGroupAsync(file, item => !ReferenceEquals(item, file));
        }
    }

    private async void MarkDirectoryFiles_Click(object sender, RoutedEventArgs e) => await MarkFilesInContextScopeAsync(sender, (root, candidate) => string.Equals(Path.GetDirectoryName(root.FilePath), Path.GetDirectoryName(candidate.FilePath), StringComparison.OrdinalIgnoreCase));

    private async void MarkDirectoryTreeFiles_Click(object sender, RoutedEventArgs e) => await MarkFilesInContextScopeAsync(sender, (root, candidate) => IsSameDirectoryOrDescendant(Path.GetDirectoryName(root.FilePath)!, Path.GetDirectoryName(candidate.FilePath)!));

    private async void MarkDriveFiles_Click(object sender, RoutedEventArgs e) => await MarkFilesInContextScopeAsync(sender, (root, candidate) => string.Equals(Path.GetPathRoot(root.FilePath), Path.GetPathRoot(candidate.FilePath), StringComparison.OrdinalIgnoreCase));

    private async Task MarkFilesInContextScopeAsync(object sender, Func<SimilarFileItem, SimilarFileItem, bool> inScope)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        if (GetContextFile(sender) is not { } root)
        {
            SetStatus("无法获取右键菜单对应的文件，请在文件行或缩略图上重新打开菜单。", true);
            return;
        }

        var matchingFiles = Groups.SelectMany(group => group.Files).Where(file => inScope(root, file)).ToArray();
        if (matchingFiles.Length == 0)
        {
            SetStatus("当前目录范围内没有找到相似文件。", true);
            return;
        }

        SetStatus($"正在标记范围内的 {matchingFiles.Length:N0} 个文件…");
        _isUpdatingMarks = true;
        var changed = 0;
        try
        {
            const int batchSize = 256;
            for (var offset = 0; offset < matchingFiles.Length; offset += batchSize)
            {
                var end = Math.Min(offset + batchSize, matchingFiles.Length);
                for (var index = offset; index < end; index++)
                {
                    var file = matchingFiles[index];
                    if (!file.IsMarked)
                    {
                        file.IsMarked = true;
                        changed++;
                    }
                }

                if (end < matchingFiles.Length)
                {
                    await Dispatcher.InvokeAsync(() =>
                    {
                    }, DispatcherPriority.Background);
                }
            }
        }
        finally
        {
            _isUpdatingMarks = false;
        }

        NotifyResultSummary();
        if (GetCurrentFilter() != "全部文件")
        {
            RefreshVisibleGroups();
        }

        SetStatus($"范围匹配 {matchingFiles.Length:N0} 个文件，新增标记 {changed:N0} 个。");
    }

    private static SimilarFileItem? GetContextFile(object sender)
    {
        if (sender is FrameworkElement {DataContext: SimilarFileItem file})
        {
            return file;
        }

        if (sender is not MenuItem menuItem)
        {
            return null;
        }

        if (menuItem.DataContext is SimilarFileItem menuFile)
        {
            return menuFile;
        }

        var contextMenu = menuItem.Parent as ContextMenu ?? menuItem.TemplatedParent as ContextMenu ?? ItemsControl.ItemsControlFromItemContainer(menuItem) as ContextMenu;
        if (contextMenu?.DataContext is SimilarFileItem contextFile)
        {
            return contextFile;
        }

        return contextMenu?.PlacementTarget switch
        {
            DataGrid {SelectedItem: SimilarFileItem selectedFile} => selectedFile,
            FrameworkElement {DataContext: SimilarFileItem targetFile} => targetFile,
            _ => null
        };
    }

    private static bool IsSameDirectoryOrDescendant(string rootDirectory, string candidateDirectory)
    {
        var root = Path.GetFullPath(rootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var candidate = Path.GetFullPath(candidateDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return string.Equals(root, candidate, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) || candidate.StartsWith(root + Path.AltDirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
    }

    private async void UnmarkGroup_Click(object sender, RoutedEventArgs e) => await UpdateContextGroupAsync(GetContextFile(sender), _ => false);
    private async void InvertGroup_Click(object sender, RoutedEventArgs e) => await UpdateContextGroupAsync(GetContextFile(sender), file => !file.IsMarked);

    private async Task UpdateContextGroupAsync(SimilarFileItem? file, Func<SimilarFileItem, bool> selector)
    {
        if (file != null && !_isUpdatingMarks && !_isDeleting && ScanButton.IsEnabled)
        {
            SetStatus("正在更新组内标记…");
            _isUpdatingMarks = true;
            try
            {
                await Task.Run(() =>
                {
                    foreach (var item in file.Group.Files)
                    {
                        item.IsMarked = selector(item);
                    }
                });
            }
            finally
            {
                _isUpdatingMarks = false;
            }

            NotifyResultSummary();
            if (GetCurrentFilter() != "全部文件")
            {
                RefreshVisibleGroups();
            }
        }
    }

    private async void DeleteMarked_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        var markedFiles = Groups.SelectMany(group => group.Files).Where(file => file.IsMarked).ToArray();
        if (markedFiles.Length == 0)
        {
            SetStatus("没有已标记的文件。", true);
            return;
        }

        var fullyMarkedGroups = Groups.Count(group => group.Files.Count > 0 && group.Files.All(file => file.IsMarked));
        if (fullyMarkedGroups > 0 && MessageBox.Show(this, $"警告：有 {fullyMarkedGroups} 组中的所有文件都已标记，删除后这些组将没有保留文件。\n仍要继续吗？", "全部重复文件已标记", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var mode = MessageBox.Show(this, $"准备删除 {markedFiles.Length} 个文件。\n选择“是”移入回收站，选择“否”永久删除，选择“取消”返回。", "选择删除方式", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
        if (mode == MessageBoxResult.Cancel)
        {
            return;
        }

        var toRecycleBin = mode == MessageBoxResult.Yes;
        if (!toRecycleBin && MessageBox.Show(this, "永久删除的文件无法从回收站恢复。确定继续吗？", "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetStatus($"正在删除 {markedFiles.Length:N0} 个文件…");
        _isDeleting = true;
        ScanButton.IsEnabled = false;
        DeleteMarkedButton.IsEnabled = false;
        HashSet<SimilarFileItem> deleted;
        List<string> errors;
        try
        {
            (deleted, errors) = await Task.Run(() =>
            {
                var completedFiles = new HashSet<SimilarFileItem>();
                var failures = new List<string>();
                foreach (var file in markedFiles)
                {
                    try
                    {
                        if (toRecycleBin)
                        {
                            RecycleBinHelper.Delete(file.FilePath);
                        }
                        else
                        {
                            File.Delete(file.FilePath);
                        }

                        _indexService.RemoveFromIndex(file.FilePath);
                        completedFiles.Add(file);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{file.Name}: {ex.Message}");
                    }
                }

                return (completedFiles, failures);
            });
        }
        finally
        {
            _isDeleting = false;
            ScanButton.IsEnabled = true;
            DeleteMarkedButton.IsEnabled = true;
        }

        var remainingGroups = new List<SimilarFileGroup>(Groups.Count);
        foreach (var group in Groups)
        {
            foreach (var file in group.Files.Where(deleted.Contains).ToArray())
            {
                group.Files.Remove(file);
                file.PropertyChanged -= FileItem_PropertyChanged;
            }

            if (group.Files.Count >= 2)
            {
                group.VisibleFiles.ReplaceAll(group.Files);
                remainingGroups.Add(group);
            }
            else
            {
                foreach (var file in group.Files)
                {
                    file.PropertyChanged -= FileItem_PropertyChanged;
                }
            }
        }

        Groups.ReplaceAll(remainingGroups);
        await RefreshDuplicateDirectoryGroupsAsync(deleted.Select(file => file.FilePath));

        RefreshVisibleGroups();
        SetStatus(errors.Count == 0 ? $"已{(toRecycleBin ? "移入回收站" : "永久删除")} {deleted.Count} 个文件。" : $"已处理 {deleted.Count} 个文件，{errors.Count} 个失败：{errors[0]}", errors.Count > 0);
    }

    private async void MarkByRule_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        var rule = GetKeepRule();
        SetStatus($"正在按“{rule}”批量标记…");
        _isUpdatingMarks = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var group in Groups)
                {
                    var keeper = GetKeeper(group.Files, rule);
                    foreach (var file in group.Files)
                    {
                        file.IsMarked = file.FilePath != keeper.FilePath;
                    }
                }
            });
        }
        finally
        {
            _isUpdatingMarks = false;
        }

        NotifyResultSummary();
        SetStatus($"已按“{rule}”标记 {Groups.Sum(group => group.Files.Count(file => file.IsMarked)):N0} 个待清理文件；文件尚未删除。");
        if (GetCurrentFilter() != "全部文件")
        {
            RefreshVisibleGroups();
        }
    }

    private async void ClearAllMarks_Click(object sender, RoutedEventArgs e) => await UpdateAllMarksAsync(_ => false, "取消所有标记");

    private async void InvertAllMarks_Click(object sender, RoutedEventArgs e) => await UpdateAllMarksAsync(isMarked => !isMarked, "反选所有标记");

    private async Task UpdateAllMarksAsync(Func<bool, bool> update, string operation)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        var files = Groups.SelectMany(group => group.Files).ToArray();
        if (files.Length == 0)
        {
            SetStatus("当前没有重复文件可操作。", true);
            return;
        }

        SetStatus($"正在{operation}…");
        _isUpdatingMarks = true;
        var changed = 0;
        try
        {
            await Task.Run(() =>
            {
                foreach (var file in files)
                {
                    var value = update(file.IsMarked);
                    if (file.IsMarked != value)
                    {
                        file.IsMarked = value;
                        Interlocked.Increment(ref changed);
                    }
                }
            });
        }
        finally
        {
            _isUpdatingMarks = false;
        }

        NotifyResultSummary();
        if (GetCurrentFilter() != "全部文件")
        {
            RefreshVisibleGroups();
        }

        SetStatus($"{operation}完成，共更新 {changed:N0} 个文件标记。", false);
    }

    private SimilarFileItem GetKeeper(List<SimilarFileItem> files, string keepRule)
    {
        return keepRule switch
        {
            "最小文件" => files.OrderBy(file => file.Length).ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).First(),
            "最新修改" => files.OrderByDescending(file => file.Modified).ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).First(),
            "最早修改" => files.OrderBy(file => file.Modified).ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).First(),
            "文件名最短" => files.OrderBy(file => file.FilePath.Length).ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).First(),
            "文件名最长" => files.OrderByDescending(file => file.FilePath.Length).ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).First(),
            _ => files.OrderByDescending(file => file.Length).ThenBy(file => file.FilePath, StringComparer.OrdinalIgnoreCase).First()
        };
    }

    private string GetKeepRule() => (KeepRuleCombo.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "最大文件";

    private void NotifyResultSummary()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DuplicateGroupCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DuplicateFileCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkedFileCount)));
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(0x59, 0x63, 0x6E));
    }
}

public sealed class SimilarFileGroup
{
    public SimilarFileGroup(int number, List<SimilarFileItem> files)
    {
        Title = $"第 {number} 组";
        Summary = $"{files.Count} 个文件";
        Files = files;
        VisibleFiles = new BulkObservableCollection<SimilarFileItem>(files);
        foreach (var file in files)
        {
            file.Group = this;
        }
    }

    public string Title { get; }
    public string Summary { get; }
    public List<SimilarFileItem> Files { get; }
    public BulkObservableCollection<SimilarFileItem> VisibleFiles { get; }
}

public sealed class SimilarDirectoryGroup(int number, List<SimilarDirectoryItem> directories)
{
    public string Title { get; } = $"目录组 {number}";
    public string Summary { get; } = $"{directories.Count} 个目录";
    public List<SimilarDirectoryItem> Directories { get; } = directories;
}

public partial class SimilarDirectoryItem : ObservableObject
{
    public string DirectoryPath { get; }
    public int FileCount { get; }
    public long TotalSize { get; }
    public string Summary => $"{FileCount:N0} 张图片 · {TotalSize / 1048576d:F2} MB";

    [ObservableProperty]private bool isMarked;

    public SimilarDirectoryItem(string directoryPath, int fileCount, long totalSize)
    {
        DirectoryPath = directoryPath;
        FileCount = fileCount;
        TotalSize = totalSize;
    }
}

public sealed class BulkObservableCollection<T> : ObservableCollection<T>
{
    public BulkObservableCollection()
    {
    }

    public BulkObservableCollection(IEnumerable<T> items) : base(items)
    {
    }

    public void ReplaceAll(IEnumerable<T> items)
    {
        CheckReentrancy();
        Items.Clear();
        foreach (var item in items)
        {
            Items.Add(item);
        }

        OnPropertyChanged(new PropertyChangedEventArgs(nameof(Count)));
        OnPropertyChanged(new PropertyChangedEventArgs("Item[]"));
        OnCollectionChanged(new NotifyCollectionChangedEventArgs(NotifyCollectionChangedAction.Reset));
    }
}

public partial class SimilarFileItem : ObservableObject
{
    public SimilarFileGroup Group { get; set; } = null!;
    public string FilePath { get; }
    public string Name { get; }
    public long Length { get; }
    public DateTime Modified { get; }
    public string SizeText { get; }
    public string ModifiedText { get; }
    public string SimilarityText { get; }

    [ObservableProperty]private bool isMarked;

    public SimilarFileItem(string filePath, long length, DateTime modified, float similarity)
    {
        FilePath = filePath;
        Name = Path.GetFileName(filePath);
        Length = length;
        Modified = modified;
        SizeText = $"{length / 1048576d:F2} MB";
        ModifiedText = modified.ToString("yyyy-MM-dd HH:mm");
        SimilarityText = $"{similarity * 100:F0}%";
    }
}
using CommunityToolkit.Mvvm.ComponentModel;
using Masuit.Tools;
using Masuit.Tools.Logging;
using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using 以图搜图.Models;
using 以图搜图.Services;

namespace 以图搜图;

public partial class SimilarFilesWindow : INotifyPropertyChanged
{
    private static SimilarFilesWindow? _instance;

    private readonly ImageIndexService _indexService = ImageIndexService.Instance;
    private readonly ImageSearchService _searchService = new();
    private CancellationTokenSource? _scanCancellationTokenSource;
    private bool _isUpdatingMarks;
    private bool _isDeleting;
    private bool _filterRefreshPending;
    private readonly DispatcherTimer _pathSearchDebounceTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(300)
    };
    private readonly HashSet<SimilarFileGroup> _pendingFilterGroups = [];
    private string[] _scannedImagePaths = [];
    private List<SimilarImagePair> _similarImagePairs = [];
    private bool SuppressConfirmations => SuppressConfirmationsCheckBox.IsChecked == true;

    public ObservableCollection<string> Directories { get; } = [];
    public ObservableCollection<string> ExcludedDirectories { get; } = [];
    public BulkObservableCollection<SimilarFileGroup> Groups { get; } = [];
    public BulkObservableCollection<SimilarFileGroup> VisibleGroups { get; } = [];
    public BulkObservableCollection<SimilarDirectoryGroup> DuplicateDirectoryGroups { get; } = [];
    public IReadOnlyList<MatchAlgorithm> MatchAlgorithms { get; } =
    [
        MatchAlgorithm.All,
        MatchAlgorithm.DifferenceHash,
        MatchAlgorithm.DctHash32,
        MatchAlgorithm.DctHash64
    ];

    private MatchAlgorithm _selectedAlgorithm = MatchAlgorithm.All;

    public MatchAlgorithm SelectedAlgorithm
    {
        get => _selectedAlgorithm;
        set
        {
            if (_selectedAlgorithm == value)
            {
                return;
            }

            _selectedAlgorithm = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(SelectedAlgorithm)));
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public int DuplicateGroupCount => Groups.Count;
    public int VisibleGroupCount => VisibleGroups.Count;
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
        _pathSearchDebounceTimer.Tick += (_, _) =>
        {
            _pathSearchDebounceTimer.Stop();
            RefreshVisibleGroups();
        };
        Closed += (_, _) => _pathSearchDebounceTimer.Stop();
        InitializeProgressChart();
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

    private void ResolutionText_Loaded(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: SimilarFileItem item })
        {
            item.RequestResolutionLoad();
        }
    }

    private void ResolutionText_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (e.NewValue is SimilarFileItem item)
        {
            item.RequestResolutionLoad();
        }
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

    private async void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.N && Keyboard.Modifiers == ModifierKeys.Control)
        {
            var window = new SimilarFilesWindow
            {
                WindowStartupLocation = WindowStartupLocation.CenterScreen
            };
            window.Show();
            e.Handled = true;
            return;
        }

        if (MainTabControl.SelectedIndex != 1)
        {
            return;
        }

        if (e.Key == Key.F5)
        {
            e.Handled = true;
            RefreshResults_Click(this, new RoutedEventArgs());
            return;
        }

        if (e.Key == Key.I && Keyboard.Modifiers == ModifierKeys.Control &&
            ResultsTabControl.SelectedIndex == 0)
        {
            e.Handled = true;
            await UpdateVisibleMarksAsync(isMarked => !isMarked, "反选当前列表中的标记");
            return;
        }

        var toRecycleBin = e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.None;
        var permanentlyDelete = e.Key == Key.Delete && Keyboard.Modifiers == ModifierKeys.Shift;
        if (!toRecycleBin && !permanentlyDelete)
        {
            return;
        }

        if (GetFocusedResultFile() is not { } file)
        {
            return;
        }

        e.Handled = true;
        if (!SuppressConfirmations)
        {
            var prompt = toRecycleBin
                ? $"确定将“{file.Name}”移入回收站吗？"
                : $"永久删除“{file.Name}”后无法从回收站恢复。确定继续吗？";
            var title = toRecycleBin ? "删除文件" : "确认永久删除";
            if (MessageBox.Show(this, prompt, title, MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await DeleteFileAsync(file, toRecycleBin);
    }

    private SimilarFileItem? GetFocusedResultFile()
    {
        DependencyObject? current = Keyboard.FocusedElement as DependencyObject;
        while (current != null)
        {
            if (current is FrameworkElement { DataContext: SimilarFileItem file })
            {
                return file;
            }

            if (current is DataGrid { SelectedItem: SimilarFileItem selectedFile })
            {
                return selectedFile;
            }

            if (current is ListBox { SelectedItem: SimilarFileItem selectedListFile })
            {
                return selectedListFile;
            }

            current = current switch
            {
                Visual => VisualTreeHelper.GetParent(current),
                ContentElement contentElement => ContentOperations.GetParent(contentElement) ??
                                                 (contentElement as FrameworkContentElement)?.Parent,
                _ => LogicalTreeHelper.GetParent(current)
            };
        }

        return null;
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
        AddDirectories(paths);
        DirectoryPathBox.Clear();
    }

    private void AddDirectories(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Where(path => Directory.Exists(path) && !Directories.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            Directories.Add(path);
            added++;
        }

        SetStatus(added > 0 ? $"已添加 {added} 个扫描位置。" : "未添加位置，请粘贴有效的文件夹路径。", added == 0);
    }

    private void PasteDirectories_Click(object sender, RoutedEventArgs e)
    {
        if (!Clipboard.ContainsText())
        {
            SetStatus("剪贴板中没有文本路径。", true);
            return;
        }

        var paths = Clipboard.GetText()
            .Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Trim('"'));
        AddDirectories(paths);
    }

    private void ClearDirectories_Click(object sender, RoutedEventArgs e)
    {
        Directories.Clear();
        SetStatus("扫描位置已清空。");
    }

    private void AddExcludedDirectory_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "选择要排除的文件夹",
            ValidateNames = false
        };

        if (dialog.ShowDialog(this) == true && Path.GetDirectoryName(dialog.FileName) is { } path)
        {
            AddExcludedDirectories([path]);
        }
    }

    private void AddExcludedPath_Click(object sender, RoutedEventArgs e) => AddExcludedPathsFromTextBox();

    private void ExcludedDirectoryPathBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            AddExcludedPathsFromTextBox();
            e.Handled = true;
        }
    }

    private void AddExcludedPathsFromTextBox()
    {
        var paths = ExcludedDirectoryPathBox.Text
            .Split(['\r', '\n', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(path => path.Trim('"'));
        AddExcludedDirectories(paths);
        ExcludedDirectoryPathBox.Clear();
    }

    private void AddExcludedDirectories(IEnumerable<string> paths)
    {
        var added = 0;
        foreach (var path in paths.Where(path => Directory.Exists(path) && !ExcludedDirectories.Contains(path, StringComparer.OrdinalIgnoreCase)))
        {
            ExcludedDirectories.Add(path);
            added++;
        }

        SetStatus(added > 0 ? $"已添加 {added} 个排除文件夹。" : "未添加排除文件夹，请输入有效的文件夹路径。", added == 0);
    }

    private void RemoveExcludedDirectory_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button {Tag: string path})
        {
            ExcludedDirectories.Remove(path);
        }
    }

    private void DirectoryList_Drop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(DataFormats.FileDrop))
        {
            return;
        }

        foreach (var directory in ((string[]) e.Data.GetData(DataFormats.FileDrop)).Select(path => Directory.Exists(path) ? path : Path.GetDirectoryName(path)).Where(directory => !string.IsNullOrWhiteSpace(directory) && Directory.Exists(directory) && !Directories.Contains(directory, StringComparer.OrdinalIgnoreCase)))
        {
            Directories.Add(directory!);
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

    private const int SpeedHistoryLimit = 5000;
    private const int SpeedSmoothingWindowSeconds = 2;
    private readonly List<double> _speedHistory = [];
    private readonly Queue<(long Ticks, long Done)> _speedSamples = [];
    private readonly DispatcherTimer _progressTimer = new(DispatcherPriority.Background) {Interval = TimeSpan.FromMilliseconds(200)};
    private System.Windows.Shapes.Polygon? _speedPolygon;
    private readonly Stopwatch _scanStopwatch = new();
    private string _scanPhase = "准备中…";
    private long _progressDone;
    private long _progressTotal;
    private double _lastSpeed;

    private void InitializeProgressChart()
    {
        _speedPolygon = new System.Windows.Shapes.Polygon
        {
            Stroke = CreateFrozenBrush(Color.FromRgb(0x00, 0x7A, 0xCC)),
            StrokeThickness = 2,
            Fill = CreateFrozenBrush(Color.FromArgb(0x40, 0x00, 0x7A, 0xCC))
        };
        ScanSpeedCanvas.Children.Add(_speedPolygon);
        _progressTimer.Tick += (_, _) => SampleProgress();
        ScanSpeedCanvas.SizeChanged += (_, _) => RenderSpeedChart();
    }

    private static SolidColorBrush CreateFrozenBrush(Color color)
    {
        var brush = new SolidColorBrush(color);
        brush.Freeze();
        return brush;
    }

    private void StartProgressTracking()
    {
        _speedHistory.Clear();
        if (_speedPolygon != null)
        {
            _speedPolygon.Points = [];
        }

        Volatile.Write(ref _progressDone, 0);
        Volatile.Write(ref _progressTotal, 0);
        _lastSpeed = 0;
        _scanPhase = "正在检查扫描位置和索引…";
        _scanStopwatch.Restart();
        _speedSamples.Clear();
        _speedSamples.Enqueue((_scanStopwatch.ElapsedTicks, 0));
        ScanProgressBar.IsIndeterminate = true;
        ScanPercentText.Text = "";
        ScanSpeedText.Text = "速度: -";
        ScanCountText.Text = "已处理: -";
        ScanElapsedText.Text = "用时: 00:00";
        ScanPhaseText.Text = _scanPhase;
        ScanProgressPanel.Visibility = Visibility.Visible;
        _progressTimer.Start();
    }

    private void StopProgressTracking()
    {
        _progressTimer.Stop();
        _scanStopwatch.Stop();
        ScanPhaseText.Text = "已完成";
        //ScanProgressPanel.Visibility = Visibility.Collapsed;
    }

    private void SetProgressPhase(string phase, long total)
    {
        _scanPhase = phase;
        Volatile.Write(ref _progressDone, 0);
        Volatile.Write(ref _progressTotal, total);
        _speedSamples.Clear();
        _speedSamples.Enqueue((_scanStopwatch.ElapsedTicks, 0));
    }

    private void ReportProgress(long done, long total)
    {
        Volatile.Write(ref _progressDone, done);
        Volatile.Write(ref _progressTotal, total);
    }

    private void SampleProgress()
    {
        var done = Volatile.Read(ref _progressDone);
        var total = Volatile.Read(ref _progressTotal);
        var ticks = _scanStopwatch.ElapsedTicks;
        _speedSamples.Enqueue((ticks, done));
        var cutoffTicks = ticks - SpeedSmoothingWindowSeconds * Stopwatch.Frequency;
        while (_speedSamples.Count > 2 && _speedSamples.ElementAt(1).Ticks < cutoffTicks)
        {
            _speedSamples.Dequeue();
        }

        var oldestSample = _speedSamples.Peek();
        var seconds = (ticks - oldestSample.Ticks) / (double) Stopwatch.Frequency;
        if (seconds > 0)
        {
            _lastSpeed = Math.Max(0, (done - oldestSample.Done) / seconds);
        }

        _speedHistory.Add(_lastSpeed);
        if (_speedHistory.Count > SpeedHistoryLimit)
        {
            _speedHistory.RemoveAt(0);
        }

        var elapsed = _scanStopwatch.Elapsed;
        ScanPhaseText.Text = _scanPhase;
        ScanElapsedText.Text = $"用时: {(int) elapsed.TotalMinutes:00}:{elapsed.Seconds:00}";
        if (total > 0)
        {
            var percent = Math.Min(100d, done * 100d / total);
            ScanProgressBar.IsIndeterminate = false;
            ScanProgressBar.Value = percent;
            ScanPercentText.Text = $"{percent:F1}%";
            ScanCountText.Text = $"已处理: {done:N0}/{total:N0}";
            ScanSpeedText.Text = $"速度: {_lastSpeed:N0} 项/秒";
        }
        else
        {
            ScanProgressBar.IsIndeterminate = true;
            ScanPercentText.Text = "";
        }

        RenderSpeedChart();
    }

    private void RenderSpeedChart()
    {
        if (_speedPolygon == null || _speedHistory.Count == 0)
        {
            return;
        }

        var width = ScanSpeedCanvas.ActualWidth;
        var height = ScanSpeedCanvas.ActualHeight;
        if (width <= 0 || height <= 0)
        {
            return;
        }

        var max = Math.Max(_speedHistory.Max(), 1d);
        var step = _speedHistory.Count > 1 ? width / (_speedHistory.Count - 1) : 0;
        var points = new PointCollection(_speedHistory.Count + 2) {new Point(0, height)};
        for (var i = 0; i < _speedHistory.Count; i++)
        {
            points.Add(new Point(i * step, height - _speedHistory[i] / max * height * 0.9));
        }

        points.Add(new Point((_speedHistory.Count - 1) * step, height));
        points.Freeze();
        _speedPolygon.Points = points;
    }

    private void IndexService_ProgressChanged(object? sender, IndexProgressEventArgs e)
    {
        ReportProgress(e.ProcessedFiles, e.TotalFiles);
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
        StartProgressTracking();
        try
        {
            SetStatus("正在检查扫描位置和索引…");
            var files = EverythingHelper.GetFiles(scanDirectories);
            cancellationToken.ThrowIfCancellationRequested();
            var missing = await Task.Run(() => files.Where(path => !_indexService.Index.ContainsKey(path)).ToArray(), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (missing.Length > 0)
            {
                SetStatus($"发现 {missing.Length:N0} 个图片尚未建立索引，正在建立索引…");
                SetProgressPhase("正在建立索引…", missing.Length);
                _indexService.ProgressChanged += IndexService_ProgressChanged;
                try
                {
                    await Task.Run(() => _indexService.UpdateIndexAsync(scanDirectories, false), cancellationToken);
                }
                finally
                {
                    _indexService.ProgressChanged -= IndexService_ProgressChanged;
                }

                cancellationToken.ThrowIfCancellationRequested();
            }

            cancellationToken.ThrowIfCancellationRequested();
            var index = _indexService.Index.Values.Where(x => x.FilePath.StartsWith(scanDirectories)).ToList();
            index.RemoveAll(t => t.FilePath.StartsWith(ExcludedDirectories));
            ScannedImageCount = index.Count;
            if (ScannedImageCount < 2)
            {
                SetStatus("扫描位置至少需要两张图片。", true);
                return;
            }

            var algorithm = SelectedAlgorithm;
            var threshold = (float) (SimilaritySlider.Value / 100d);
            SetStatus($"正在准备并比较 {ScannedImageCount:N0} 张图片…");
            StartProgressTracking();
            var progress = new Progress<int>(completed =>
            {
                ReportProgress(completed, ScannedImageCount);
                SetStatus($"正在比较图片：{completed:N0}/{ScannedImageCount:N0}，请稍候…");
            });
            SetProgressPhase("正在比较图片…", ScannedImageCount);
            var pairs = await _searchService.FindSimilarPairsAsync(index, algorithm, threshold, IgnoreSameFolderCheckBox.IsChecked == true, progress, cancellationToken);

            cancellationToken.ThrowIfCancellationRequested();
            var groups = await Task.Run(() => BuildGroups(files, pairs, cancellationToken), cancellationToken);
            _progressTimer.Stop();
            var duplicateDirectoryGroups = await Task.Run(() => BuildDuplicateDirectoryGroups(files, pairs, cancellationToken), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Groups.ReplaceAll(groups);
            _scannedImagePaths = files;
            _similarImagePairs = pairs;
            DuplicateDirectoryGroups.ReplaceAll(duplicateDirectoryGroups);
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(HasDuplicateDirectories)));
            SetStatus($"扫描完成：检查 {ScannedImageCount:N0} 张图片，找到 {Groups.Count:N0} 组相似文件、{Groups.Sum(group => group.Files.Count):N0} 个文件。");
            RefreshVisibleGroups();
            MainTabControl.SelectedIndex = Groups.Count > 0 ? 1 : 0;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus("扫描已停止。");
        }
        catch (Exception ex)
        {
            LogManager.Error(ex);
            SetStatus($"扫描失败：{ex.Message}", true);
        }
        finally
        {
            if (ReferenceEquals(_scanCancellationTokenSource, cancellationSource))
            {
                _scanCancellationTokenSource = null;
            }

            cancellationSource.Dispose();
            StopProgressTracking();
            ScanButton.IsEnabled = true;
            StopScanButton.IsEnabled = false;
            StopScanButton.Visibility = Visibility.Collapsed;
            FilterSimilaritySlider.Value = SimilaritySlider.Value;
        }
    }

    private List<SimilarFileGroup> BuildGroups(string[] paths, List<SimilarImagePair> matches, CancellationToken cancellationToken)
    {
        var groups = new List<SimilarFileGroup>();
        var neighbors = paths.ToDictionary(path => path, _ => new HashSet<string>(StringComparer.OrdinalIgnoreCase), StringComparer.OrdinalIgnoreCase);
        var scores = new Dictionary<(string First, string Second), float>();
        var algorithms = new Dictionary<(string First, string Second), MatchAlgorithm>();
        foreach (var match in matches.Where(p => paths.Contains(p.FirstPath)&& paths.Contains(p.SecondPath)))
        {
            cancellationToken.ThrowIfCancellationRequested();
            neighbors[match.FirstPath].Add(match.SecondPath);
            neighbors[match.SecondPath].Add(match.FirstPath);
            var pair = string.Compare(match.FirstPath, match.SecondPath, StringComparison.OrdinalIgnoreCase) < 0 ? (match.FirstPath, match.SecondPath) : (match.SecondPath, match.FirstPath);
            scores[pair] = match.Similarity;
            algorithms[pair] = match.Algorithms;
        }

        var remaining = new SortedSet<string>(paths, StringComparer.OrdinalIgnoreCase);
        var groupNumber = 1;
        while (remaining.Count > 0)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var first = remaining.Min!;
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

            var fileItems = component.Select(path => CreateFileItem(path, component, scores, algorithms)).OrderBy(item => item.FilePath, StringComparer.OrdinalIgnoreCase).ToList();
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

    private static SimilarFileItem CreateFileItem(string path, List<string> group, Dictionary<(string First, string Second), float> matches, Dictionary<(string First, string Second), MatchAlgorithm> algorithms)
    {
        var file = new FileInfo(path);
        var matchingPairs = group.Where(other => !other.Equals(path, StringComparison.OrdinalIgnoreCase)).Select(other =>
        {
            var pair = string.Compare(path, other, StringComparison.OrdinalIgnoreCase) < 0 ? (path, other) : (other, path);
            return (Similarity: matches.GetValueOrDefault(pair, 0), Algorithm: algorithms.GetValueOrDefault(pair));
        }).ToArray();
        var similarity = matchingPairs.Select(pair => pair.Similarity).DefaultIfEmpty(0).Max();
        var matchedAlgorithms = matchingPairs.Where(pair => pair.Similarity == similarity).Aggregate((MatchAlgorithm) 0, (combined, pair) => combined | pair.Algorithm);
        return new SimilarFileItem(path, file.Length, file.LastWriteTime, similarity, FormatMatchAlgorithms(matchedAlgorithms));
    }

    private static string FormatMatchAlgorithms(MatchAlgorithm algorithms)
    {
        var names = new List<string>(3);
        if (algorithms.HasFlag(MatchAlgorithm.DifferenceHash))
        {
            names.Add("DifferenceHash");
        }

        if (algorithms.HasFlag(MatchAlgorithm.DctHash32))
        {
            names.Add("DCTHash32");
        }

        if (algorithms.HasFlag(MatchAlgorithm.DctHash64))
        {
            names.Add("DCTHash64");
        }

        return names.Count == 0 ? "—" : string.Join(", ", names);
    }

    private void FilterCombo_SelectionChanged(object sender, SelectionChangedEventArgs e) => RefreshVisibleGroups();

    private void FilterPathSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _pathSearchDebounceTimer.Stop();
        _pathSearchDebounceTimer.Start();
    }

    private void FilterSimilarityCheck_Changed(object sender, RoutedEventArgs e) => RefreshVisibleGroups();

    private void FilterSimilaritySlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (FilterSimilarityCheck?.IsChecked == true)
        {
            RefreshVisibleGroups();
        }
    }

    private string GetAlgorithmFilter()
    {
        var index = FilterAlgorithmCombo?.SelectedIndex ?? 0;
        return index > 0 ? ((ComboBoxItem) FilterAlgorithmCombo!.SelectedItem).Content.ToString()! : string.Empty;
    }

    private bool IsFileLevelFilterActive(string filter) => filter is "仅已标记文件" or "仅未标记文件" || GetAlgorithmFilter().Length > 0 || FilterSimilarityCheck?.IsChecked == true;

    private bool IsAnyFilterActive() =>
        GetCurrentFilter() != "全部文件" ||
        IsFileLevelFilterActive(string.Empty) ||
        !string.IsNullOrWhiteSpace(FilterPathSearchBox?.Text);

    private bool MatchesPathSearch(SimilarFileGroup group)
    {
        var searchTerm = FilterPathSearchBox?.Text;
        return string.IsNullOrWhiteSpace(searchTerm) ||
               group.Files.Any(file => file.FilePath.Contains(searchTerm, StringComparison.OrdinalIgnoreCase));
    }

    private IEnumerable<SimilarFileItem> GetFilteredFiles(SimilarFileGroup group, string filter)
    {
        IEnumerable<SimilarFileItem> files = filter switch
        {
            "仅已标记文件" => group.Files.Where(file => file.IsMarked),
            "仅未标记文件" => group.Files.Where(file => !file.IsMarked),
            _ => group.Files
        };
        var algorithm = GetAlgorithmFilter();
        if (algorithm.Length > 0)
        {
            files = files.Where(file => file.MatchAlgorithmText.Split(", ").Contains(algorithm));
        }

        if (FilterSimilarityCheck.IsChecked == true)
        {
            var threshold = Math.Round(FilterSimilaritySlider.Value);
            files = files.Where(file => Math.Round(file.Similarity * 100) >= threshold);
        }

        return files;
    }

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
        if (IsAnyFilterActive())
        {
            RefreshVisibleGroups();
        }

        SetStatus($"目录“{directory.DirectoryPath}”的 {directoryFiles.Length:N0} 个文件已{(shouldMark ? "标记" : "取消标记")}。");
    }

    private void DuplicateDirectoryPath_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount != 2 || sender is not FrameworkElement {DataContext: SimilarDirectoryItem directory})
        {
            return;
        }

        if (!Directory.Exists(directory.DirectoryPath))
        {
            SetStatus("目录不存在，可能已被移动或删除。", true);
            e.Handled = true;
            return;
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = directory.DirectoryPath,
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            SetStatus($"无法打开目录：{ex.Message}", true);
        }

        e.Handled = true;
    }

    private void FileItem_PropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (!_isUpdatingMarks && e.PropertyName == nameof(SimilarFileItem.IsMarked) && sender is SimilarFileItem file)
        {
            NotifyResultSummary();
            if (IsAnyFilterActive())
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
        if (IsFileLevelFilterActive(filter))
        {
            group.VisibleFiles.Synchronize(GetFilteredFiles(group, filter));
        }
        else if (group.VisibleFiles.Count != group.Files.Count)
        {
            group.VisibleFiles.Synchronize(group.Files);
        }

        var markedCount = group.Files.Count(file => file.IsMarked);
        var passesGroupFilter = filter switch
        {
            "含已标记文件的组" => markedCount > 0,
            "没有标记文件的组" => markedCount == 0,
            "组内全部已标记" => markedCount == group.Files.Count,
            "相同文件夹" => GetParentDirectoryCount(group) == 1,
            "不同文件夹" => GetParentDirectoryCount(group) > 1,
            _ => true
        };
        var shouldShow = MatchesPathSearch(group) &&
                         passesGroupFilter &&
                         (!IsFileLevelFilterActive(filter) || group.VisibleFiles.Count > 1);
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

        var filter = GetCurrentFilter();
        if (!IsAnyFilterActive())
        {
            foreach (var group in Groups.Where(group => group.VisibleFiles.Count != group.Files.Count))
            {
                group.VisibleFiles.Synchronize(group.Files);
            }

            VisibleGroups.Synchronize(Groups.OrderBy(group => group.Files[0].FilePath, StringComparer.OrdinalIgnoreCase));
            NotifyResultSummary();
            return;
        }

        var visibleGroups = new List<SimilarFileGroup>();
        foreach (var group in Groups)
        {
            if (IsFileLevelFilterActive(filter))
            {
                group.VisibleFiles.Synchronize(GetFilteredFiles(group, filter));
            }
            else if (group.VisibleFiles.Count != group.Files.Count)
            {
                group.VisibleFiles.Synchronize(group.Files);
            }

            var markedCount = group.Files.Count(file => file.IsMarked);
            var passesGroupFilter = filter switch
            {
                "含已标记文件的组" => markedCount > 0,
                "没有标记文件的组" => markedCount == 0,
                "组内全部已标记" => markedCount == group.Files.Count,
                "相同文件夹" => GetParentDirectoryCount(group) == 1,
                "不同文件夹" => GetParentDirectoryCount(group) > 1,
                _ => true
            };
            var showGroup = MatchesPathSearch(group) && passesGroupFilter && (!IsFileLevelFilterActive(filter) || group.VisibleFiles.Count > 1);
            if (showGroup)
            {
                visibleGroups.Add(group);
            }
        }

        VisibleGroups.Synchronize(visibleGroups.OrderBy(group => group.Files[0].FilePath, StringComparer.OrdinalIgnoreCase));
        NotifyResultSummary();
    }

    private static int GetParentDirectoryCount(SimilarFileGroup group) =>
        group.Files
            .Select(file => Path.GetDirectoryName(file.FilePath) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(2)
            .Count();

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

    private void CopyFilePath_Click(object sender, RoutedEventArgs e)
    {
        if (GetContextFile(sender) is { } file)
        {
            Clipboard.SetText(file.FilePath);
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

        var matchingFiles = VisibleGroups
            .SelectMany(group => group.VisibleFiles)
            .Where(file => inScope(root, file))
            .ToArray();
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
        if (IsAnyFilterActive())
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
            if (IsAnyFilterActive())
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

        var markedFiles = VisibleGroups.SelectMany(group => group.VisibleFiles).Where(file => file.IsMarked).ToArray();
        if (markedFiles.Length == 0)
        {
            SetStatus("当前列表中没有已标记的文件。", true);
            return;
        }

        var markedFileSet = markedFiles.ToHashSet();
        var fullyMarkedGroups = Groups.Count(group => group.Files.Count > 0 && group.Files.All(markedFileSet.Contains));
        if (!SuppressConfirmations && fullyMarkedGroups > 0 && MessageBox.Show(this, $"警告：有 {fullyMarkedGroups} 组中的所有文件都已标记，删除后这些组将没有保留文件。\n仍要继续吗？", "全部重复文件已标记", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        var toRecycleBin = SuppressConfirmations;
        if (!SuppressConfirmations)
        {
            var mode = MessageBox.Show(this, $"准备删除 {markedFiles.Length} 个文件。\n选择“是”移入回收站，选择“否”永久删除，选择“取消”返回。", "选择删除方式", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (mode == MessageBoxResult.Cancel)
            {
                return;
            }

            toRecycleBin = mode == MessageBoxResult.Yes;
            if (!toRecycleBin && MessageBox.Show(this, "永久删除的文件无法从回收站恢复。确定继续吗？", "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        DeleteProgressBar.Value = 0;
        DeleteProgressText.Text = $"正在删除 0/{markedFiles.Length:N0}";
        DeleteProgressPanel.Visibility = Visibility.Visible;
        IProgress<int> progress = new Progress<int>(completed =>
        {
            if (!_isDeleting)
            {
                return;
            }

            DeleteProgressBar.Value = completed * 100d / markedFiles.Length;
            DeleteProgressText.Text = $"正在删除 {completed:N0}/{markedFiles.Length:N0}";
        });
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
                int count = 0;
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

                    progress.Report(++count);
                }

                foreach (var dir in completedFiles.Select(t => Path.GetDirectoryName(t.FilePath)).Distinct().Where(s => !Directory.EnumerateFiles(s,"*",SearchOption.AllDirectories).Any()))
                {
                    try
                    {
                        Directory.Delete(dir, true);
                    }
                    catch (Exception ex)
                    {
                        failures.Add($"{dir}: {ex.Message}");
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
            DeleteProgressPanel.Visibility = Visibility.Collapsed;
        }

        var remainingGroups = new List<SimilarFileGroup>(Groups.Count);
        foreach (var group in Groups)
        {
            foreach (var file in group.Files.Where(deleted.Contains).ToArray())
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
            }
        }

        Groups.ReplaceAll(remainingGroups);
        await RefreshDuplicateDirectoryGroupsAsync(deleted.Select(file => file.FilePath));

        RefreshVisibleGroups();
        SetStatus(errors.Count == 0 ? $"已{(toRecycleBin ? "移入回收站" : "永久删除")} {deleted.Count} 个文件。" : $"已处理 {deleted.Count} 个文件，{errors.Count} 个失败：{errors[0]}", errors.Count > 0);
    }

    private async void DeleteFile_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled || GetContextFile(sender) is not { } file)
        {
            return;
        }

        var toRecycleBin = SuppressConfirmations;
        if (!SuppressConfirmations)
        {
            var mode = MessageBox.Show(this, $"删除“{file.Name}”？\n选择“是”移入回收站，选择“否”永久删除，选择“取消”返回。", "删除文件", MessageBoxButton.YesNoCancel, MessageBoxImage.Warning, MessageBoxResult.Cancel);
            if (mode == MessageBoxResult.Cancel)
            {
                return;
            }

            toRecycleBin = mode == MessageBoxResult.Yes;
            if (!toRecycleBin && MessageBox.Show(this, $"永久删除“{file.Name}”后无法从回收站恢复。确定继续吗？", "确认永久删除", MessageBoxButton.YesNo, MessageBoxImage.Stop, MessageBoxResult.No) != MessageBoxResult.Yes)
            {
                return;
            }
        }

        await DeleteFileAsync(file, toRecycleBin);
    }

    private async Task DeleteFileAsync(SimilarFileItem file, bool toRecycleBin)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        _isDeleting = true;
        ScanButton.IsEnabled = false;
        DeleteMarkedButton.IsEnabled = false;
        SetStatus($"正在删除“{file.Name}”…");
        try
        {
            try
            {
                await Task.Run(() =>
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
                    var dir = Path.GetDirectoryName(file.FilePath);
                    if (!Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories).Any())
                    {
                        Directory.Delete(dir, true);
                    }
                });
            }
            catch (Exception ex)
            {
                LogManager.Error(ex);
                SetStatus($"删除“{file.Name}”失败：{ex.Message}", true);
                return;
            }

            var group = file.Group;
            group.Files.Remove(file);
            group.VisibleFiles.Remove(file);
            file.PropertyChanged -= FileItem_PropertyChanged;

            if (group.Files.Count < 2)
            {
                Groups.Remove(group);
                VisibleGroups.Remove(group);
                foreach (var remainingFile in group.Files)
                {
                    remainingFile.PropertyChanged -= FileItem_PropertyChanged;
                }
            }
            else
            {
                group.RefreshSummary();
            }

            await RefreshDuplicateDirectoryGroupsAsync([file.FilePath]);
            RefreshVisibleGroups();
            SetStatus($"已{(toRecycleBin ? "移入回收站" : "永久删除")}“{file.Name}”。");
        }
        finally
        {
            _isDeleting = false;
            ScanButton.IsEnabled = true;
            DeleteMarkedButton.IsEnabled = true;
        }
    }

    private async void ReplaceGroupFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled || GetContextFile(sender) is not { } source)
        {
            return;
        }

        var otherFiles = source.Group.Files.Where(file => !ReferenceEquals(file, source)).ToArray();
        if (otherFiles.Length == 0)
        {
            SetStatus("该组中没有其他文件可替换。", true);
            return;
        }

        if (!SuppressConfirmations && MessageBox.Show(this, $"将使用“{source.Name}”覆盖组内其他 {otherFiles.Length:N0} 个文件，保留其他文件名，并删除“{source.Name}”。此操作无法撤销，是否继续？", "确认替换组内文件", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
        {
            return;
        }

        SetStatus($"正在用“{source.Name}”替换组内其他文件…");
        _isDeleting = true;
        ScanButton.IsEnabled = false;
        DeleteMarkedButton.IsEnabled = false;
        RefreshResultsButton.IsEnabled = false;
        ReplacementResult result;
        try
        {
            result = await Task.Run(() => ReplaceGroupFiles(source.FilePath, otherFiles.Select(file => file.FilePath).ToArray()));
        }
        finally
        {
            _isDeleting = false;
            ScanButton.IsEnabled = true;
            DeleteMarkedButton.IsEnabled = true;
            RefreshResultsButton.IsEnabled = true;
        }

        foreach (var path in result.ReplacedPaths)
        {
            _indexService.RemoveFromIndex(path);
            var replacedFile = otherFiles.First(file => string.Equals(file.FilePath, path, StringComparison.OrdinalIgnoreCase));
            replacedFile.RefreshAfterReplacement(1f);
        }

        if (result.SourceDeleted)
        {
            _indexService.RemoveFromIndex(source.FilePath);
            var group = source.Group;
            group.Files.Remove(source);
            group.VisibleFiles.Remove(source);
            source.PropertyChanged -= FileItem_PropertyChanged;
            if (group.Files.Count < 2)
            {
                foreach (var remainingFile in group.Files)
                {
                    remainingFile.PropertyChanged -= FileItem_PropertyChanged;
                }

                Groups.Remove(group);
            }
            else
            {
                group.RefreshSummary();
            }

            await RefreshDuplicateDirectoryGroupsAsync([source.FilePath]);
        }

        RefreshVisibleGroups();
        SetStatus(result.Errors.Count == 0
            ? $"已替换 {result.ReplacedPaths.Count:N0} 个文件，并{(result.SourceDeleted ? "删除源文件" : "保留源文件")}。"
            : $"已替换 {result.ReplacedPaths.Count:N0} 个文件；源文件{(result.SourceDeleted ? "已删除" : "保留")}。操作未完全成功：{result.Errors[0]}",
            result.Errors.Count > 0);
    }

    private static ReplacementResult ReplaceGroupFiles(string sourcePath, string[] targetPaths)
    {
        var stagedFiles = new List<(string Target, string Temporary)>(targetPaths.Length);
        var replacedPaths = new List<string>(targetPaths.Length);
        var errors = new List<string>();
        try
        {
            foreach (var targetPath in targetPaths)
            {
                string? temporaryPath = null;
                try
                {
                    var directory = Path.GetDirectoryName(targetPath) ?? throw new IOException($"无法确定目标目录：{targetPath}");
                    temporaryPath = Path.Combine(directory, $".{Path.GetFileName(targetPath)}.{Guid.NewGuid():N}.tmp");
                    stagedFiles.Add((targetPath, temporaryPath));
                    File.Copy(sourcePath, temporaryPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(targetPath)}：{ex.Message}");
                    return new ReplacementResult(replacedPaths, false, errors);
                }
            }

            foreach (var (targetPath, temporaryPath) in stagedFiles)
            {
                try
                {
                    File.Move(temporaryPath, targetPath, true);
                    replacedPaths.Add(targetPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"{Path.GetFileName(targetPath)}：{ex.Message}");
                    return new ReplacementResult(replacedPaths, false, errors);
                }
            }

            try
            {
                File.Delete(sourcePath);
                return new ReplacementResult(replacedPaths, true, errors);
            }
            catch (Exception ex)
            {
                errors.Add($"无法删除源文件：{ex.Message}");
                return new ReplacementResult(replacedPaths, false, errors);
            }
        }
        finally
        {
            foreach (var (_, temporaryPath) in stagedFiles)
            {
                try
                {
                    File.Delete(temporaryPath);
                }
                catch (Exception ex)
                {
                    errors.Add($"无法清理暂存文件“{temporaryPath}”：{ex.Message}");
                }
            }
        }
    }

    private sealed record ReplacementResult(List<string> ReplacedPaths, bool SourceDeleted, List<string> Errors);

    private async void MarkByRule_Click(object sender, RoutedEventArgs e)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        var rule = GetKeepRule();
        var visibleGroupFiles = VisibleGroups.Select(group => group.VisibleFiles.ToArray()).ToArray();
        var visibleFiles = visibleGroupFiles.SelectMany(files => files).ToArray();
        SetStatus($"正在按“{rule}”批量标记…");
        _isUpdatingMarks = true;
        try
        {
            await Task.Run(() =>
            {
                foreach (var visibleGroup in visibleGroupFiles)
                {
                    if (visibleGroup.Length == 0)
                    {
                        continue;
                    }

                    var keeper = GetKeeper(visibleGroup.ToList(), rule);
                    foreach (var file in visibleGroup)
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
        SetStatus($"已按“{rule}”标记当前列表中的 {visibleFiles.Count(file => file.IsMarked):N0} 个待清理文件；文件尚未删除。");
        if (IsAnyFilterActive())
        {
            RefreshVisibleGroups();
        }
    }

    private async void ClearAllMarks_Click(object sender, RoutedEventArgs e) => await UpdateVisibleMarksAsync(_ => false, "取消当前列表中的标记");

    private async void InvertAllMarks_Click(object sender, RoutedEventArgs e) => await UpdateVisibleMarksAsync(isMarked => !isMarked, "反选当前列表中的标记");

    private async Task UpdateVisibleMarksAsync(Func<bool, bool> update, string operation)
    {
        if (_isUpdatingMarks || _isDeleting || !ScanButton.IsEnabled)
        {
            return;
        }

        var files = VisibleGroups.SelectMany(group => group.VisibleFiles).ToArray();
        if (files.Length == 0)
        {
            SetStatus("当前列表中没有文件可操作。", true);
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
        if (IsAnyFilterActive())
        {
            RefreshVisibleGroups();
        }

        SetStatus($"{operation}完成，共更新 {changed:N0} 个文件标记。");
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
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(VisibleGroupCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(DuplicateFileCount)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(MarkedFileCount)));
    }

    private void SetStatus(string message, bool isError = false)
    {
        StatusText.Text = message;
        StatusText.Foreground = isError ? Brushes.Firebrick : new SolidColorBrush(Color.FromRgb(0x59, 0x63, 0x6E));
    }
}

public sealed class SimilarFileGroup : ObservableObject
{
    public SimilarFileGroup(int number, List<SimilarFileItem> files)
    {
        Title = $"第 {number} 组";
        Files = files;
        VisibleFiles = new BulkObservableCollection<SimilarFileItem>(files);
        RefreshSummary();
        foreach (var file in files)
        {
            file.Group = this;
        }
    }

    public string Title { get; }
    public string Summary { get; private set; } = string.Empty;
    public List<SimilarFileItem> Files { get; }
    public BulkObservableCollection<SimilarFileItem> VisibleFiles { get; }

    public void RefreshSummary()
    {
        var summary = $"{Files.Count} 个文件";
        if (Summary != summary)
        {
            Summary = summary;
            OnPropertyChanged(nameof(Summary));
        }
    }
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

    public void Synchronize(IEnumerable<T> items)
    {
        var targetItems = items.ToArray();
        var comparer = EqualityComparer<T>.Default;
        for (var targetIndex = 0; targetIndex < targetItems.Length; targetIndex++)
        {
            var targetItem = targetItems[targetIndex];
            if (targetIndex < Count && comparer.Equals(this[targetIndex], targetItem))
            {
                continue;
            }

            var existingIndex = -1;
            for (var index = targetIndex + 1; index < Count; index++)
            {
                if (comparer.Equals(this[index], targetItem))
                {
                    existingIndex = index;
                    break;
                }
            }

            if (existingIndex >= 0)
            {
                Move(existingIndex, targetIndex);
            }
            else
            {
                Insert(targetIndex, targetItem);
            }
        }

        while (Count > targetItems.Length)
        {
            RemoveAt(Count - 1);
        }
    }
}

public partial class SimilarFileItem : ObservableObject
{
    private static readonly SemaphoreSlim ResolutionReadSlots = new(2);
    private int _resolutionLoadState;
    private int _resolutionGeneration;
    private string _resolutionText = "等待显示";

    public SimilarFileGroup Group { get; set; } = null!;
    public string FilePath { get; }
    public string Name { get; }
    public long Length { get; private set; }
    public DateTime Modified { get; private set; }
    public string SizeText => $"{Length / 1048576d:F2} MB";
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
    public string SimilarityText { get; private set; }
    public float Similarity { get; }
    public string MatchAlgorithmText { get; }
    public string ResolutionText
    {
        get => _resolutionText;
        private set
        {
            if (_resolutionText != value)
            {
                _resolutionText = value;
                OnPropertyChanged();
            }
        }
    }
    public int ImageVersion { get; private set; }

    [ObservableProperty]private bool isMarked;

    public SimilarFileItem(string filePath, long length, DateTime modified, float similarity, string matchAlgorithmText)
    {
        FilePath = filePath;
        Name = Path.GetFileName(filePath);
        Length = length;
        Modified = modified;
        Similarity = similarity;
        SimilarityText = $"{similarity * 100:F0}%";
        MatchAlgorithmText = matchAlgorithmText;
    }

    public void RequestResolutionLoad()
    {
        if (Interlocked.CompareExchange(ref _resolutionLoadState, 1, 0) != 0)
        {
            return;
        }

        var generation = Volatile.Read(ref _resolutionGeneration);
        ResolutionText = "读取中…";
        _ = LoadResolutionAsync(generation);
    }

    private async Task LoadResolutionAsync(int generation)
    {
        await ResolutionReadSlots.WaitAsync();
        string resolution;
        try
        {
            resolution = await Task.Run(() => GetResolutionText(FilePath));
        }
        finally
        {
            ResolutionReadSlots.Release();
        }

        if (generation == Volatile.Read(ref _resolutionGeneration))
        {
            ResolutionText = resolution;
        }
    }

    private static string GetResolutionText(string filePath)
    {
        try
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
            {
                return "不可用";
            }

            var frame = decoder.Frames[0];
            return $"{frame.PixelWidth} × {frame.PixelHeight} px";
        }
        catch (IOException)
        {
            return "不可用";
        }
        catch (UnauthorizedAccessException)
        {
            return "不可用";
        }
        catch (NotSupportedException)
        {
            return "不可用";
        }
        catch (ArgumentException)
        {
            return "不可用";
        }
        catch (System.IO.FileFormatException)
        {
            return "不可用";
        }
    }

    public void RefreshAfterReplacement(float similarity)
    {
        var fileInfo = new FileInfo(FilePath);
        Length = fileInfo.Length;
        Modified = fileInfo.LastWriteTime;
        SimilarityText = $"{similarity * 100:F0}%";
        Interlocked.Increment(ref _resolutionGeneration);
        Interlocked.Exchange(ref _resolutionLoadState, 0);
        ResolutionText = "等待显示";
        ImageVersion++;
        OnPropertyChanged(nameof(Length));
        OnPropertyChanged(nameof(Modified));
        OnPropertyChanged(nameof(SizeText));
        OnPropertyChanged(nameof(ModifiedText));
        OnPropertyChanged(nameof(SimilarityText));
        OnPropertyChanged(nameof(ResolutionText));
        OnPropertyChanged(nameof(ImageVersion));
        RequestResolutionLoad();
    }
}

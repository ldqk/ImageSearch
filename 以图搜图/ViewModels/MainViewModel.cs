using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Masuit.Tools.Files;
using Masuit.Tools.Files.FileDetector;
using Masuit.Tools.Media;
using Masuit.Tools.Systems;
using SkiaSharp;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Threading;
using Microsoft.Win32;
using 以图搜图.Helpers;
using 以图搜图.Models;
using 以图搜图.Services;
using 以图搜图.WebAPI;
using 以图搜图.WebAPI.Controllers;
using ModelsMatchAlgorithm = 以图搜图.Models.MatchAlgorithm;
using Timer = System.Timers.Timer;

namespace 以图搜图.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly ImageIndexService _indexService;
    private readonly ImageSearchService _searchService;

    [ObservableProperty]private string destImageInfo = string.Empty;

    [ObservableProperty]private string destImagePath = string.Empty;

    [ObservableProperty]private string directoryPath = string.Empty;

    [ObservableProperty]private string elapsedTime = string.Empty;

    [ObservableProperty]private bool findFlipped;

    [ObservableProperty]private bool findRotated = true;

    [ObservableProperty]private string imagePath = string.Empty;

    [ObservableProperty]private string indexCount = "正在加载索引...";

    [ObservableProperty]private string indexSpeed = string.Empty;

    [ObservableProperty]private string processStatus = string.Empty;

    [ObservableProperty]private bool removeInvalidIndex;

    [ObservableProperty]private ObservableCollection<SearchResult> searchResults = new();

    [ObservableProperty]private string searchKeyword = string.Empty;

    [ObservableProperty]private SearchResult? selectedResult;

    [ObservableProperty]private Visibility showRemoveInvalidIndex = Visibility.Collapsed;

    [ObservableProperty]private int similarity = 85;

    public ModelsMatchAlgorithm MatchAlgorithm
    {
        get;
        set
        {
            if (SetProperty(ref field, value))
            {
                if (Similarity < SimilarityMinimum)
                {
                    Similarity = SimilarityMinimum;
                }

                if (value == MatchAlgorithm.DctHash32)
                {
                    Similarity = 90;
                }

                OnPropertyChanged(nameof(SimilarityMinimum));
            }
        }
    } = ModelsMatchAlgorithm.All;

    public IReadOnlyList<ModelsMatchAlgorithm> MatchAlgorithms { get; } = Enum.GetValues<ModelsMatchAlgorithm>();

    public int SimilarityMinimum => MatchAlgorithm.HasFlag(ModelsMatchAlgorithm.DifferenceHash) ? 70 : 85;

    [ObservableProperty]private string sourceImageInfo = string.Empty;

    [ObservableProperty]private string sourceImagePath = string.Empty;

    [ObservableProperty]private string updateIndexButtonText = "🔄 更新索引";

    [ObservableProperty]private bool updateIndexButtonEnabled = true;

    [ObservableProperty]private bool isSearching;

    [ObservableProperty]private string searchStatusText = string.Empty;

    [ObservableProperty]private Visibility searchLoadingVisibility = Visibility.Collapsed;

    [ObservableProperty]private double indexProgress;

    [ObservableProperty]private string indexProgressText = string.Empty;

    [ObservableProperty]private Visibility indexProgressVisibility = Visibility.Collapsed;

    [ObservableProperty]private string indexSpeedText = string.Empty;

    [ObservableProperty]private string estimatedRemainingTimeText = string.Empty;

    [ObservableProperty]private string indexElapsedTimeText = string.Empty;

    [ObservableProperty]private string processingFilename = string.Empty;

    [ObservableProperty]private string processingQueueText = string.Empty;

    [ObservableProperty]private ObservableCollection<double> speedHistory = new();

    [ObservableProperty]private bool isSearchEnabled;

    [ObservableProperty]private double cpuUsage;

    [ObservableProperty]private double memoryUsage;

    [ObservableProperty]private string diskReadRate = "0 B/s";

    [ObservableProperty]private string diskWriteRate = "0 B/s";

    [ObservableProperty]private string webApiServer;

    [ObservableProperty]private bool webApiServerRunning;

    private Process? _currentProcess;
    private PerformanceCounter? _cpuCounter;
    private PerformanceCounter? _ioReadCounter;
    private PerformanceCounter? _ioWriteCounter;
    private Thread? _performanceThread;
    private PerformanceMetrics _latestPerformanceMetrics = new(0, 0, "0 B/s", "0 B/s");
    private int _performanceUpdatePending;
    private Timer? _updateIndexTimer;
    private readonly IniFile _config = new IniFile("config.ini");

    private sealed record PerformanceMetrics(double CpuUsage, double MemoryUsage, string DiskReadRate, string DiskWriteRate);

    public MainViewModel()
    {
        _indexService = ImageIndexService.Instance;
        _searchService = new ImageSearchService();
        _indexSpeedSamplingTimer.Tick += (_, _) => SampleIndexSpeed();
        SearchResultsView = CollectionViewSource.GetDefaultView(SearchResults);
        SearchResultsView.Filter = FilterSearchResult;

        _indexService.ProgressChanged += OnIndexProgressChanged;
        _indexService.IndexCompleted += OnIndexCompleted;
        _indexService.IndexUpdated += (sender, args) => Application.Current.Dispatcher.Invoke(UpdateIndexCount);

        _performanceThread = new Thread(InitializePerformanceMonitoring)
        {
            IsBackground = true,
            Name = "Performance monitor"
        };
        _performanceThread.Start();
        WebApiServerRunning = WebApiStartup.ServerRunning;
        LoadIndexAsync();
        HomeController.MainViewModel = this;
        WebApiServer = _config.GetValue("Global", "RunServer", false) ? $"http://127.0.0.1:{_config.GetValue("Global", "HttpPort", 5000)}/api" : "";
        if (_config.GetValue("Global", "IndexAutoUpdate", false))
        {
            _updateIndexTimer = new Timer(TimeSpan.FromHours(1));
            _updateIndexTimer.Elapsed += (sender, args) =>
            {
                if (UpdateIndexCommand.CanExecute(sender) && IndexProgressVisibility != Visibility.Visible)
                {
                    UpdateIndexCommand.Execute(sender);
                }
            };
            _updateIndexTimer.Start();
        }
    }

    public ICollectionView SearchResultsView { get; }

    partial void OnSearchKeywordChanged(string value)
    {
        SearchResultsView.Refresh();
    }

    private bool FilterSearchResult(object item)
    {
        if (item is not SearchResult result)
        {
            return false;
        }

        var keyword = SearchKeyword.Trim();
        return keyword.Length == 0 || result.路径.Contains(keyword, StringComparison.OrdinalIgnoreCase);
    }

    partial void OnImagePathChanged(string value)
    {
        if (File.Exists(value))
        {
            SourceImagePath = value;
            UpdateSourceImageInfo(value);
        }
    }

    partial void OnSelectedResultChanged(SearchResult? value)
    {
        if (value != null && File.Exists(value.路径))
        {
            DestImagePath = value.路径;
            UpdateDestImageInfo(value.路径);
        }
    }

    partial void OnSimilarityChanged(int value)
    {
        var minimum = SimilarityMinimum;
        if (value < minimum)
        {
            Similarity = minimum;
        }
    }

    private async void LoadIndexAsync()
    {
        await _indexService.LoadIndexAsync();
        UpdateIndexCount();
    }

    [RelayCommand]
    private void SelectDirectory()
    {
        var dialog = new OpenFolderDialog()
        {
            Title = "选择文件夹",
            ValidateNames = false,
            Multiselect = false
        };

        // Workaround for folder selection
        if (dialog.ShowDialog() == true)
        {
            DirectoryPath = dialog.FolderName;
        }
    }

    [RelayCommand]
    private void SelectImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图像和视频文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp;*.3gp;*.avi;*.flv;*.m2ts;*.m4v;*.mkv;*.mov;*.mp4;*.mpeg;*.mpg;*.mts;*.ts;*.webm;*.wmv|所有文件|*.*"
        };

        if (dialog.ShowDialog() == true)
        {
            ImagePath = dialog.FileName;
        }
    }

    [RelayCommand(AllowConcurrentExecutions = true)]
    private async Task UpdateIndex()
    {
        if (_indexService.IsIndexing)
        {
            _indexService.StopIndexing();
            UpdateIndexButtonText = "🔄 更新索引";
            UpdateIndexButtonEnabled = false;
            //MessageBox.Show(Application.Current.MainWindow!, "已发送停止请求，请等待完成...", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        // 立即更新 UI 显示
        OnIndexProgressChanged(this, new IndexProgressEventArgs
        {
            Message = "准备开始"
        });

        await Task.Run(async () =>
        {
            try
            {
                var paths = _indexService.Index.Keys;
                if (string.IsNullOrWhiteSpace(DirectoryPath) && paths.Count == 0)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        MessageBox.Show(Application.Current.MainWindow!, "请先选择文件夹", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
                        IndexProgressVisibility = Visibility.Collapsed;
                        UpdateIndexButtonText = "🔄 更新索引";
                    });
                    return;
                }

                var dirs = string.IsNullOrWhiteSpace(DirectoryPath) ? PathPrefixFinder.FindLongestCommonPathPrefixes(paths, 3).Where(Directory.Exists).ToArray() : [DirectoryPath];

                // 切回 UI 线程执行异步索引操作
                await Application.Current.Dispatcher.InvokeAsync(() => _indexService.UpdateIndexAsync(dirs, RemoveInvalidIndex).ContinueWith(t =>
                {
                    if (t.IsFaulted)
                    {
                        MessageBox.Show(t.Exception.Message);
                    }

                    OnIndexCompleted(this, new IndexCompletedEventArgs());
                }));
            }
            finally
            {
                Application.Current.Dispatcher.Invoke(() =>
                {
                    UpdateIndexButtonText = "🔄 更新索引";
                    RemoveInvalidIndex = false;
                });
            }
        });
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrEmpty(ImagePath))
        {
            MessageBox.Show(Application.Current.MainWindow!, "请先选择文件", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!IsSearchEnabled)
        {
            MessageBox.Show(Application.Current.MainWindow!, "当前没有任何索引，请先添加文件夹创建索引后再搜索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!new FileInfo(ImagePath).IsImage() && !VideoFrameHelper.IsVideoFile(ImagePath))
        {
            MessageBox.Show(Application.Current.MainWindow!, "不是支持的图像或视频文件，无法检索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await SearchCore(ImagePath);
    }

    [RelayCommand]
    private async Task SearchFromClipboard()
    {
        if (!IsSearchEnabled)
        {
            MessageBox.Show(Application.Current.MainWindow!, "当前没有任何索引，请先添加文件夹创建索引后再搜索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            if (files.Count > 0)
            {
                ImagePath = files[0]!;
                await Search();
            }
            else
            {
                IsSearching = false;
                SearchLoadingVisibility = Visibility.Collapsed;
            }

            return;
        }

        if (Clipboard.ContainsText())
        {
            var text = Clipboard.GetText().Trim();
            if (File.Exists(text))
            {
                ImagePath = text;
                await Search();
            }
            else
            {
                IsSearching = false;
                SearchLoadingVisibility = Visibility.Collapsed;
            }

            return;
        }

        if (Clipboard.ContainsImage())
        {
            // 在 UI 线程（STA 模式）获取剪贴板图片，然后在后台线程处理编码
            try
            {
                var image = Clipboard.GetImage();
                if (image != null)
                {
                    // 立即冻结图片对象，使其可以跨线程访问
                    image.Freeze();

                    // 后台处理图片编码和搜索，避免 UI 线程阻塞
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");

                            // 编码图片在后台线程执行
                            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

                            await using (var fileStream = new FileStream(filename, FileMode.Create))
                            {
                                encoder.Save(fileStream);
                            }

                            // 切回 UI 线程更新 UI
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OnImagePathChanged(filename);
                            });

                            // 执行搜索（在后台线程中等待）
                            await SearchCore(filename);

                            // 搜索完成后删除临时文件
                            await Task.Delay(1000);
                            if (File.Exists(filename))
                            {
                                File.Delete(filename);
                            }
                        }
                        catch (Exception ex)
                        {
                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                MessageBox.Show(Application.Current.MainWindow!, $"处理剪贴板图片失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                                IsSearching = false;
                                SearchLoadingVisibility = Visibility.Collapsed;
                                SearchStatusText = string.Empty;
                            });
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(Application.Current.MainWindow!, $"读取剪贴板失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                IsSearching = false;
                SearchLoadingVisibility = Visibility.Collapsed;
                SearchStatusText = string.Empty;
            }
        }
    }

    [RelayCommand]
    private void OpenFolder()
    {
        if (SelectedResult != null)
        {
            FileExplorerHelper.ExplorerFile(SelectedResult.路径);
        }
    }

    [RelayCommand]
    private void Delete()
    {
        if (SelectedResult == null) return;

        var result = MessageBox.Show(Application.Current.MainWindow!, "确认删除选中项吗？", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.OK)
        {
            if (File.Exists(SelectedResult.路径))
            {
                // 删除前释放 Image 控件占用的文件
                if (DestImagePath == SelectedResult.路径)
                {
                    DestImagePath = string.Empty;
                    DestImageInfo = string.Empty;
                }

                File.Delete(SelectedResult.路径);
            }

            _indexService.RemoveFromIndex(SelectedResult.路径);
            SearchResults.Remove(SelectedResult);
            UpdateIndexCount();
        }
    }

    [RelayCommand]
    private void DeleteToRecycleBin()
    {
        if (SelectedResult == null) return;

        var result = MessageBox.Show(Application.Current.MainWindow!, "确认删除到回收站吗？", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Question);
        if (result == MessageBoxResult.OK)
        {
            // 删除前释放 Image 控件占用的文件
            if (DestImagePath == SelectedResult.路径)
            {
                DestImagePath = string.Empty;
                DestImageInfo = string.Empty;
            }

            RecycleBinHelper.Delete(SelectedResult.路径);
            _indexService.RemoveFromIndex(SelectedResult.路径);
            SearchResults.Remove(SelectedResult);
            UpdateIndexCount();
        }
    }

    public async Task HandleDrop(IDataObject dataObject)
    {
        try
        {
            // 1. 检查文件拖放
            if (dataObject.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[]) dataObject.GetData(DataFormats.FileDrop)!;
                if (files.Length > 0)
                {
                    ImagePath = files[0];
                    await Search();
                    return;
                }
            }

            string virtualImage = null;
            try
            {
                virtualImage = VirtualFileDrop.SaveFirstImage(dataObject);
            }
            catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException or IOException or UnauthorizedAccessException or NotSupportedException or InvalidCastException or Win32Exception)
            {
                Debug.WriteLine($"读取拖放的虚拟文件失败，将尝试其他格式：{ex}");
            }

            if (virtualImage != null)
            {
                try
                {
                    OnImagePathChanged(virtualImage);
                    await SearchCore(virtualImage);
                    await Task.Delay(1000);
                }
                finally
                {
                    File.Delete(virtualImage);
                }

                return;
            }

            // 2. 直接获取位图数据（优先处理，避免格式转换问题）
            if (dataObject.GetDataPresent(DataFormats.Bitmap))
            {
                try
                {
                    var image = (System.Windows.Media.Imaging.BitmapSource) dataObject.GetData(DataFormats.Bitmap)!;
                    // 立即冻结图片对象，使其可以跨线程访问
                    image.Freeze();

                    // 在后台线程处理图片编码，避免 UI 线程阻塞
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");

                            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

                            await using (var fileStream = new FileStream(filename, FileMode.Create))
                            {
                                encoder.Save(fileStream);
                            }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OnImagePathChanged(filename);
                            });

                            await SearchCore(filename);

                            await Task.Delay(1000);
                            if (File.Exists(filename))
                                File.Delete(filename);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"位图处理异常: {ex.Message}");
                        }
                    });
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"位图数据处理失败: {ex.Message}");
                    // 继续尝试其他格式
                }
            }

            // 3. 处理 DIB (Device Independent Bitmap) 格式
            if (dataObject.GetDataPresent(DataFormats.Dib))
            {
                try
                {
                    var image = (System.Windows.Media.Imaging.BitmapSource) dataObject.GetData(DataFormats.Dib)!;
                    // 立即冻结图片对象，使其可以跨线程访问
                    image.Freeze();

                    // 在后台线程处理图片编码，避免 UI 线程阻塞
                    _ = Task.Run(async () =>
                    {
                        try
                        {
                            var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");

                            var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                            encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

                            await using (var fileStream = new FileStream(filename, FileMode.Create))
                            {
                                encoder.Save(fileStream);
                            }

                            Application.Current.Dispatcher.Invoke(() =>
                            {
                                OnImagePathChanged(filename);
                            });

                            await SearchCore(filename);

                            await Task.Delay(1000);
                            if (File.Exists(filename))
                                File.Delete(filename);
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"DIB 处理异常: {ex.Message}");
                        }
                    });
                    return;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"DIB 格式处理失败: {ex.Message}");
                    // 继续尝试其他格式
                }
            }

            // 4. 处理浏览器拖放的图片（FileContents）
            if (dataObject.GetDataPresent("FileContents"))
            {
                try
                {
                    var data = dataObject.GetData("FileContents");
                    if (data is Stream stream)
                    {
                        // 在后台线程保存文件，避免 UI 线程阻塞
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");
                                await stream.SaveFileAsync(filename);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    OnImagePathChanged(filename);
                                });

                                await SearchCore(filename);

                                await Task.Delay(1000);
                                if (File.Exists(filename))
                                    File.Delete(filename);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"FileContents 处理异常: {ex.Message}");
                            }
                        });
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"FileContents 处理失败: {ex.Message}");
                    // 继续尝试其他格式
                }
            }

            // 5. 处理URL或Base64文本
            if (dataObject.GetDataPresent(DataFormats.Text))
            {
                try
                {
                    string text = dataObject.GetData(DataFormats.Text)!.ToString()!;

                    // 检查是否为URL
                    if (Uri.TryCreate(text, UriKind.Absolute, out Uri? uri) && (uri.Scheme == "http" || uri.Scheme == "https"))
                    {
                        // 在后台线程下载和处理文件，避免 UI 线程阻塞
                        _ = Task.Run(async () =>
                        {
                            try
                            {
                                using var httpClient = new HttpClient();
                                httpClient.Timeout = TimeSpan.FromSeconds(10);
                                var bytes = await httpClient.GetByteArrayAsync(uri);
                                var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + Path.GetExtension(uri.AbsolutePath));
                                if (string.IsNullOrEmpty(Path.GetExtension(filename)))
                                {
                                    filename += ".jpg";
                                }

                                await File.WriteAllBytesAsync(filename, bytes);

                                Application.Current.Dispatcher.Invoke(() =>
                                {
                                    OnImagePathChanged(filename);
                                });

                                await SearchCore(filename);

                                await Task.Delay(1000);
                                if (File.Exists(filename))
                                    File.Delete(filename);
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"URL 处理异常: {ex.Message}");
                                Application.Current.Dispatcher.Invoke(() => MessageBox.Show(Application.Current.MainWindow!, $"无法下载拖放的图片：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error));
                            }
                        });
                        return;
                    }

                    // 检查是否为Base64图像数据
                    if (text.StartsWith("data:image/"))
                    {
                        int commaIndex = text.IndexOf(',');
                        if (commaIndex != -1)
                        {
                            string base64Data = text.Substring(commaIndex + 1);
                            byte[] bytes = Convert.FromBase64String(base64Data);
                            // 在后台线程处理文件写入，避免 UI 线程阻塞
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");
                                    await File.WriteAllBytesAsync(filename, bytes);

                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        OnImagePathChanged(filename);
                                    });

                                    await SearchCore(filename);

                                    await Task.Delay(1000);
                                    if (File.Exists(filename))
                                        File.Delete(filename);
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"Base64 处理异常: {ex.Message}");
                                }
                            });
                            return;
                        }
                    }

                    // 检查是否为本地文件路径
                    if (File.Exists(text))
                    {
                        ImagePath = text;
                        await Search();
                        return;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"文本数据处理失败: {ex.Message}");
                    // 继续尝试其他格式
                }
            }

            // 如果所有格式都失败，显示提示
            MessageBox.Show(Application.Current.MainWindow!, "无法识别拖放的数据格式，请尝试从剪切板搜索或选择本地文件拖放", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(Application.Current.MainWindow!, $"处理拖放数据时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            Debug.WriteLine($"HandleDrop 异常: {ex}");
        }
        finally
        {
            IsSearching = false;
            SearchLoadingVisibility = Visibility.Collapsed;
            SearchStatusText = string.Empty;
        }
    }

    public void HandleDataGridKeyUp(Key key, ModifierKeys modifiers)
    {
        if (key == Key.Delete && SelectedResult != null)
        {
            if (File.Exists(SelectedResult.路径))
            {
                // 删除前释放 Image 控件占用的文件
                if (DestImagePath == SelectedResult.路径)
                {
                    DestImagePath = string.Empty;
                    DestImageInfo = string.Empty;
                }

                if (modifiers == ModifierKeys.Shift)
                {
                    File.Delete(SelectedResult.路径);
                }
                else
                {
                    RecycleBinHelper.Delete(SelectedResult.路径);
                }
            }

            _indexService.RemoveFromIndex(SelectedResult.路径);
            SearchResults.Remove(SelectedResult);
            UpdateIndexCount();
        }

        if (modifiers == ModifierKeys.Control && key == Key.O && SelectedResult != null)
        {
            FileExplorerHelper.ExplorerFile(SelectedResult.路径);
        }
    }

    public bool CanClose()
    {
        return _indexService is {IsIndexing: false, IsWriting: false};
    }

    private async Task SearchCore(string filename)
    {
        try
        {
            IsSearching = true;
            SearchLoadingVisibility = Visibility.Visible;
            SearchStatusText = "🔍 正在搜索相似文件...";
            ElapsedTime = string.Empty;

            // 在后台线程执行搜索,避免 UI 线程阻塞
            var (results, elapsed) = await Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                var sim = Similarity / 100f;

                var resultList = await _searchService.SearchAsync(filename, _indexService.Index, MatchAlgorithm, sim, FindRotated, FindFlipped);

                sw.Stop();
                return (resultList, sw.ElapsedMilliseconds);
            });

            // 切回 UI 线程更新 UI
            Application.Current.Dispatcher.Invoke(() =>
            {
                ElapsedTime = $"{elapsed}ms";

                SearchResults.Clear();
                foreach (var result in results)
                {
                    SearchResults.Add(result);
                }

                if (SearchResults.Count > 0)
                {
                    SelectedResult = SearchResults[0];
                    SearchStatusText = $"✅ 搜索完成，找到 {SearchResults.Count} 个相似媒体";
                }
                else
                {
                    SearchStatusText = "ℹ️ 未找到相似媒体";
                }
            });
        }
        catch (Exception ex)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                SearchStatusText = $"❌ 搜索失败: {ex.Message}";
                MessageBox.Show(Application.Current.MainWindow!, $"搜索时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            });
        }
        finally
        {
            IsSearching = false;
            // 延迟隐藏 loading，让用户看到完成状态
            await Task.Delay(800);
            SearchLoadingVisibility = Visibility.Collapsed;
            SearchStatusText = string.Empty;
        }
    }

    private const int IndexSpeedHistoryLimit = 2000;
    private const int IndexSpeedSmoothingWindowSeconds = 2;
    private readonly Queue<(long Ticks, long Done)> _indexSpeedSamples = [];

    private readonly DispatcherTimer _indexSpeedSamplingTimer = new(DispatcherPriority.Background)
    {
        Interval = TimeSpan.FromMilliseconds(2000)
    };

    private readonly Stopwatch _indexSpeedStopwatch = new();
    private long _indexSpeedProcessedFiles;
    private double _indexSpeed;

    private void StartIndexSpeedSampling(long processedFiles)
    {
        SpeedHistory.Clear();
        _indexSpeedSamples.Clear();
        _indexSpeed = 0;
        _indexSpeedProcessedFiles = processedFiles;
        _indexSpeedStopwatch.Restart();
        _indexSpeedSamples.Enqueue((_indexSpeedStopwatch.ElapsedTicks, processedFiles));
        IndexSpeedText = string.Empty;
        IndexElapsedTimeText = FormatTimespan(_indexSpeedStopwatch.Elapsed);
        _indexSpeedSamplingTimer.Start();
    }

    private void SampleIndexSpeed()
    {
        UpdateProcessingQueueText();
        IndexElapsedTimeText = FormatTimespan(_indexSpeedStopwatch.Elapsed);

        var done = _indexSpeedProcessedFiles;
        var ticks = _indexSpeedStopwatch.ElapsedTicks;
        _indexSpeedSamples.Enqueue((ticks, done));
        var cutoffTicks = ticks - IndexSpeedSmoothingWindowSeconds * Stopwatch.Frequency;
        while (_indexSpeedSamples.Count > 2 && _indexSpeedSamples.ElementAt(1).Ticks < cutoffTicks)
        {
            _indexSpeedSamples.Dequeue();
        }

        var oldestSample = _indexSpeedSamples.Peek();
        var seconds = (ticks - oldestSample.Ticks) / (double) Stopwatch.Frequency;
        if (seconds > 0)
        {
            _indexSpeed = Math.Max(0, (done - oldestSample.Done) / seconds);
        }

        if (_indexSpeed == 0)
        {
            return;
        }

        SpeedHistory.Add(_indexSpeed);
        if (SpeedHistory.Count > IndexSpeedHistoryLimit)
        {
            SpeedHistory.RemoveAt(0);
        }

        IndexSpeed = $"索引速度: {_indexSpeed:F0} items/s";
        IndexSpeedText = $"{_indexSpeed:F0} items/s";
    }

    private void UpdateProcessingQueueText()
    {
        var processingFiles = _indexService.ProcessingFiles;
        ProcessingQueueText = processingFiles.Count > 0 ? $"正在计算索引的文件（{processingFiles.Count}）{Environment.NewLine}{string.Join(Environment.NewLine, processingFiles)}" : "当前没有正在计算索引的文件";
    }

    private void OnIndexProgressChanged(object? sender, IndexProgressEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            if (e.TotalFiles > 0)
            {
                if (!_indexSpeedSamplingTimer.IsEnabled)
                {
                    StartIndexSpeedSampling(e.ProcessedFiles);
                }

                _indexSpeedProcessedFiles = e.ProcessedFiles;
                UpdateProcessingQueueText();
            }

            UpdateIndexButtonText = "⏸️ 停止索引";
            ProcessStatus = e.Message;
            IndexProgress = e.ProgressPercentage;
            IndexProgressText = $"{e.ProcessedFiles:#,0} / {e.TotalFiles:#,0}";
            IndexProgressVisibility = Visibility.Visible;

            if (!string.IsNullOrEmpty(e.Filename))
            {
                ProcessingFilename = "正在处理：" + e.Filename;
            }

            if (e.ProcessedFiles > 0)
            {
                // 计算预估剩余时间
                var remainingFiles = e.TotalFiles - e.ProcessedFiles;
                if (remainingFiles > 0 && e.AverageSpeed > 0)
                {
                    var estimatedSeconds = remainingFiles / e.AverageSpeed / 0.9;
                    EstimatedRemainingTimeText = FormatTimespan(TimeSpan.FromSeconds(estimatedSeconds));
                }
                else
                {
                    EstimatedRemainingTimeText = "--";
                }
            }
        });
    }

    private string FormatTimespan(TimeSpan timespan)
    {
        if (timespan.TotalHours >= 1)
        {
            return $"{(int) timespan.TotalHours}h {timespan.Minutes}m {timespan.Seconds}s";
        }
        else if (timespan.TotalMinutes >= 1)
        {
            return $"{(int) timespan.TotalMinutes}m {timespan.Seconds}s";
        }
        else
        {
            return $"{timespan.Seconds}s";
        }
    }

    private void OnIndexCompleted(object? sender, IndexCompletedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            _indexSpeedSamplingTimer.Stop();
            _indexSpeedStopwatch.Stop();
            _indexSpeedSamples.Clear();
            _indexSpeed = 0;

            //UpdateIndexCount();

            if (e.Errors.Count > 0)
            {
                var errorDialog = new ErrorsDialog($"索引创建完成，耗时：{e.ElapsedSeconds:F2}s，以下文件格式不正确无法创建索引，请检查：\r\n{string.Join("\r\n", e.Errors)}");
                errorDialog.ShowDialog();
            }
            else if (e.FilesProcessed > 0)
            {
                MessageBox.Show(Application.Current.MainWindow!, $"索引创建完成，耗时：{e.ElapsedSeconds:F2}s", "消息", MessageBoxButton.OK, MessageBoxImage.Information);
            }

            IndexProgressVisibility = Visibility.Collapsed;
            IndexProgress = 0;
            IndexProgressText = string.Empty;
            IndexSpeedText = string.Empty;
            EstimatedRemainingTimeText = string.Empty;
            IndexElapsedTimeText = string.Empty;
            UpdateIndexButtonText = "🔄 更新索引";
            UpdateIndexButtonEnabled = true;
            ProcessingFilename = string.Empty;
            ProcessingQueueText = string.Empty;
            SpeedHistory.Clear();
        });
    }

    private void UpdateIndexCount()
    {
        var count = _indexService.Index.Count;
        IndexCount = count > 0 ? $"{count}文件" : "请先创建索引";

        // 根据索引总数决定是否显示移除无效索引选项
        ShowRemoveInvalidIndex = count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 根据索引总数决定是否启用搜索配置区域
        IsSearchEnabled = count > 0;
    }

    static bool TryGetImageInfo(string path, out int width, out int height)
    {
        width = 0;
        height = 0;
        using var codec = SKCodec.Create(path);
        if (codec == null)
        {
            return false;
        }

        width = codec.Info.Width;
        height = codec.Info.Height;
        return true;
    }

    private void UpdateSourceImageInfo(string path)
    {
        if (File.Exists(path))
        {
            if (TryGetVideoInfo(path, out var videoInfo))
            {
                SourceImageInfo = videoInfo;
                return;
            }

            try
            {
                if (TryGetImageInfo(path, out var width, out var height))
                {
                    var fileInfo = new FileInfo(path);
                    SourceImageInfo = $"分辨率：{width}x{height}，大小：{fileInfo.Length / 1024}KB";
                }
                else
                {
                    SourceImageInfo = "无法加载媒体信息";
                }
            }
            catch
            {
                SourceImageInfo = "无法加载媒体信息";
            }
        }
    }

    private void UpdateDestImageInfo(string path)
    {
        if (File.Exists(path))
        {
            if (TryGetVideoInfo(path, out var videoInfo))
            {
                DestImageInfo = videoInfo;
                return;
            }

            try
            {
                if (TryGetImageInfo(path, out var width, out var height))
                {
                    var fileInfo = new FileInfo(path);
                    DestImageInfo = $"分辨率：{width}x{height}，大小：{fileInfo.Length / 1024}KB";
                }
                else
                {
                    DestImageInfo = "无法加载媒体信息";
                }
            }
            catch
            {
                DestImageInfo = "无法加载媒体信息";
            }
        }
    }

    private static bool TryGetVideoInfo(string path, out string info)
    {
        info = string.Empty;
        if (!VideoFrameHelper.IsVideoFile(path))
        {
            return false;
        }

        try
        {
            var mediaInfo = VideoFrameHelper.Analyse(path);
            var videoStream = mediaInfo.PrimaryVideoStream;
            if (videoStream == null)
            {
                info = "无法加载媒体信息";
            }
            else
            {
                var fileInfo = new FileInfo(path);
                var frameRate = double.IsFinite(videoStream.RealFrameRate) && videoStream.RealFrameRate > 0 ? videoStream.RealFrameRate.ToString("0.##", CultureInfo.InvariantCulture) : "不可用";
                info = $"分辨率：{videoStream.Width}x{videoStream.Height} @ {frameRate}fps， 时长：{FormatDuration(mediaInfo.Duration)}， 大小：{fileInfo.Length / 1024}KB";
            }
        }
        catch (Exception exception) when (exception is FFMpegCore.Exceptions.FFProbeException or Win32Exception or IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException)
        {
            Debug.WriteLine($"Unable to read video metadata for '{path}': {exception.Message}");
            info = "无法加载媒体信息";
        }

        return true;
    }

    private static string FormatDuration(TimeSpan duration) => $"{(int) duration.TotalHours:00}:{duration.Minutes:00}:{duration.Seconds:00}";

    private void InitializePerformanceMonitoring()
    {
        try
        {
            _currentProcess = Process.GetCurrentProcess();

            // 初始化当前进程的 CPU 性能计数器
            _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _currentProcess.ProcessName, true);
            _cpuCounter.NextValue(); // 初始化

            _ioReadCounter = new PerformanceCounter("Process", "IO Read Bytes/sec", _currentProcess.ProcessName, true);
            _ioReadCounter.NextValue();
            _ioWriteCounter = new PerformanceCounter("Process", "IO Write Bytes/sec", _currentProcess.ProcessName, true);
            _ioWriteCounter.NextValue();

            while (true)
            {
                Thread.Sleep(1000);
                UpdatePerformanceMetrics();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"性能监测初始化失败: {ex.Message}");
        }
    }

    private void UpdatePerformanceMetrics()
    {
        try
        {
            if (_currentProcess != null)
            {
                // 刷新进程信息
                _currentProcess.Refresh();

                // 获取 CPU 使用率（百分比）
                var cpuUsageValue = _cpuCounter?.NextValue() ?? 0;

                // 获取内存使用量（转换为 MB）
                var memoryUsage = _currentProcess.WorkingSet64 / (1024.0 * 1024.0);
                var diskReadRate = _ioReadCounter?.NextValue() ?? 0;
                var diskWriteRate = _ioWriteCounter?.NextValue() ?? 0;

                Volatile.Write(ref _latestPerformanceMetrics, new PerformanceMetrics(cpuUsageValue / Environment.ProcessorCount, memoryUsage, FormatIoRate(diskReadRate), FormatIoRate(diskWriteRate)));
                QueuePerformanceMetricsUpdate();
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新性能指标失败: {ex.Message}");
        }
    }

    private void QueuePerformanceMetricsUpdate()
    {
        if (Interlocked.Exchange(ref _performanceUpdatePending, 1) != 0)
        {
            return;
        }

        try
        {
            Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.DataBind, new Action(() =>
            {
                try
                {
                    var metrics = Volatile.Read(ref _latestPerformanceMetrics);
                    CpuUsage = metrics.CpuUsage;
                    MemoryUsage = metrics.MemoryUsage;
                    DiskReadRate = metrics.DiskReadRate;
                    DiskWriteRate = metrics.DiskWriteRate;
                }
                finally
                {
                    Interlocked.Exchange(ref _performanceUpdatePending, 0);
                }
            }));
        }
        catch
        {
            Interlocked.Exchange(ref _performanceUpdatePending, 0);
            throw;
        }
    }

    private static string FormatIoRate(float bytesPerSecond)
    {
        string[] units = ["B/s", "KB/s", "MB/s", "GB/s"];
        var value = Math.Max(0, bytesPerSecond);
        var unitIndex = 0;

        while (value >= 1024 && unitIndex < units.Length - 1)
        {
            value /= 1024;
            unitIndex++;
        }

        return $"{value:0.0} {units[unitIndex]}";
    }

    ~MainViewModel()
    {
        _cpuCounter?.Dispose();
        _ioReadCounter?.Dispose();
        _ioWriteCounter?.Dispose();
        _currentProcess?.Dispose();
        _updateIndexTimer?.Dispose();
    }
}
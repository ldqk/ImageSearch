using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Masuit.Tools.Files;
using Masuit.Tools.Files.FileDetector;
using Masuit.Tools.Systems;
using SixLabors.ImageSharp;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Windows;
using System.Windows.Input;
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

    [ObservableProperty]
    private string destImageInfo = string.Empty;

    [ObservableProperty]
    private string destImagePath = string.Empty;

    [ObservableProperty]
    private string directoryPath = string.Empty;

    [ObservableProperty]
    private string elapsedTime = string.Empty;

    [ObservableProperty]
    private bool findFlipped;

    [ObservableProperty]
    private bool findRotated = true;

    [ObservableProperty]
    private string imagePath = string.Empty;

    [ObservableProperty]
    private string indexCount = "正在加载索引...";

    [ObservableProperty]
    private string indexSpeed = string.Empty;

    [ObservableProperty]
    private string processStatus = string.Empty;

    [ObservableProperty]
    private bool removeInvalidIndex;

    [ObservableProperty]
    private ObservableCollection<SearchResult> searchResults = new();

    [ObservableProperty]
    private SearchResult? selectedResult;

    [ObservableProperty]
    private Visibility showRemoveInvalidIndex = Visibility.Collapsed;

    [ObservableProperty]
    private int similarity = 80;

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

    [ObservableProperty]
    private string sourceImageInfo = string.Empty;

    [ObservableProperty]
    private string sourceImagePath = string.Empty;

    [ObservableProperty]
    private string updateIndexButtonText = "🔄 更新索引";

    [ObservableProperty]
    private bool updateIndexButtonEnabled = true;

    [ObservableProperty]
    private bool isSearching;

    [ObservableProperty]
    private string searchStatusText = string.Empty;

    [ObservableProperty]
    private Visibility searchLoadingVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private double indexProgress;

    [ObservableProperty]
    private string indexProgressText = string.Empty;

    [ObservableProperty]
    private Visibility indexProgressVisibility = Visibility.Collapsed;

    [ObservableProperty]
    private string indexSpeedText = string.Empty;

    [ObservableProperty]
    private string indexThroughputText = string.Empty;

    [ObservableProperty]
    private string maxThroughputText = string.Empty;

    [ObservableProperty]
    private string estimatedRemainingTimeText = string.Empty;

    [ObservableProperty]
    private string processingFilename = string.Empty;

    [ObservableProperty]
    private ObservableCollection<double> speedHistory = new();

    [ObservableProperty]
    private bool isSearchEnabled;

    [ObservableProperty]
    private double cpuUsage;

    [ObservableProperty]
    private double memoryUsage;

    [ObservableProperty]
    private bool webApiServerRunning;

    private Process? _currentProcess;
    private PerformanceCounter? _cpuCounter;
    private System.Timers.Timer? _performanceTimer;
    private System.Timers.Timer? _updateIndexTimer;

    public MainViewModel()
    {
        _indexService = ImageIndexService.Instance;
        _searchService = new ImageSearchService();

        _indexService.ProgressChanged += OnIndexProgressChanged;
        _indexService.IndexCompleted += OnIndexCompleted;
        _indexService.IndexUpdated += (sender, args) => Application.Current.Dispatcher.Invoke(UpdateIndexCount);

        // 异步初始化性能监测，避免阻塞 UI 线程
        _ = Task.Run(InitializePerformanceMonitoring);
        WebApiServerRunning = WebApiStartup.ServerRunning;
        LoadIndexAsync();
        HomeController.MainViewModel = this;
        if (new IniFile("config.ini").GetValue("Global", "IndexAutoUpdate", false))
        {
            _updateIndexTimer = new Timer(TimeSpan.FromHours(1));
            _updateIndexTimer.Elapsed += (sender, args) =>
            {
                if (UpdateIndexCommand.CanExecute(sender) && !_indexService.IsIndexing)
                {
                    UpdateIndexCommand.Execute(sender);
                }
            };
            _updateIndexTimer.Start();
        }
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
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            CheckFileExists = false,
            CheckPathExists = true,
            FileName = "选择文件夹",
            ValidateNames = false
        };

        // Workaround for folder selection
        if (dialog.ShowDialog() == true)
        {
            DirectoryPath = Path.GetDirectoryName(dialog.FileName) ?? string.Empty;
        }
    }

    [RelayCommand]
    private void SelectImage()
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.gif;*.webp|所有文件|*.*"
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
                var paths = _indexService.GetIndexedPaths().ToList();
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
            MessageBox.Show(Application.Current.MainWindow!, "请先选择图片", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!IsSearchEnabled)
        {
            MessageBox.Show(Application.Current.MainWindow!, "当前没有任何索引，请先添加文件夹创建索引后再搜索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (new FileInfo(ImagePath).DetectFiletype().MimeType?.StartsWith("image") != true)
        {
            MessageBox.Show(Application.Current.MainWindow!, "不是图像文件，无法检索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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
                var files = (string[])dataObject.GetData(DataFormats.FileDrop)!;
                if (files.Length > 0)
                {
                    ImagePath = files[0];
                    await Search();
                    return;
                }
            }

            // 2. 直接获取位图数据（优先处理，避免格式转换问题）
            if (dataObject.GetDataPresent(DataFormats.Bitmap))
            {
                try
                {
                    var image = (System.Windows.Media.Imaging.BitmapSource)dataObject.GetData(DataFormats.Bitmap)!;
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
                    var image = (System.Windows.Media.Imaging.BitmapSource)dataObject.GetData(DataFormats.Dib)!;
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
                    RecycleBinHelper.Delete(SelectedResult.路径);
                }
                else
                {
                    File.Delete(SelectedResult.路径);
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
        return _indexService is { IsIndexing: false, IsWriting: false };
    }

    private async Task SearchCore(string filename)
    {
        try
        {
            IsSearching = true;
            SearchLoadingVisibility = Visibility.Visible;
            SearchStatusText = "🔍 正在搜索相似图片...";
            ElapsedTime = string.Empty;

            // 在后台线程执行搜索,避免 UI 线程阻塞
            var (results, elapsed) = await Task.Run(async () =>
            {
                var sw = Stopwatch.StartNew();
                var sim = Similarity / 100f;

                var resultList = await _searchService.SearchAsync(
                    filename,
                    _indexService.Index,
                    _indexService.FrameIndex,
                    MatchAlgorithm,
                    sim,
                    FindRotated,
                    FindFlipped);

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
                    SearchStatusText = $"✅ 搜索完成，找到 {SearchResults.Count} 个相似图片";
                }
                else
                {
                    SearchStatusText = "ℹ️ 未找到相似图片";
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

    private double _maxThroughput;

    private void OnIndexProgressChanged(object? sender, IndexProgressEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateIndexButtonText = "⏸️ 停止索引";
            ProcessStatus = e.Message;
            IndexProgress = e.ProgressPercentage;
            IndexProgressText = $"{e.ProcessedFiles:#,0} / {e.TotalFiles:#,0}";
            IndexProgressVisibility = Visibility.Visible;

            if (e.ProcessedFiles > 0)
            {
                ProcessingFilename = "正在处理：" + e.Filename;
                IndexSpeed = $"索引速度: {e.Speed:F0} items/s ({e.ThroughputMB:F2}MB/s)";
                IndexSpeedText = $"{e.Speed:F0} items/s";
                IndexThroughputText = $"{e.ThroughputMB:F2} MB/s";

                // 计算最大吞吐量
                _maxThroughput = Math.Max(e.ThroughputMB, _maxThroughput);
                MaxThroughputText = $"{_maxThroughput:F2} MB/s";

                // 计算预估剩余时间
                var remainingFiles = e.TotalFiles - e.ProcessedFiles;
                if (remainingFiles > 0 && e.Speed > 0)
                {
                    var estimatedSeconds = remainingFiles / e.Speed / 0.9;
                    EstimatedRemainingTimeText = FormatTimespan(TimeSpan.FromSeconds(estimatedSeconds));
                }
                else
                {
                    EstimatedRemainingTimeText = "--";
                }

                // 添加速度数据点到历史记录 - 显示整个索引过程
                switch (e.TotalFiles)
                {
                    case <= 1000:
                    case <= 10000 when e.ProcessedFiles % 10 == 0:
                    case <= 100000 when e.ProcessedFiles % 100 == 0:
                    case > 100000 when e.ProcessedFiles % 200 == 0:
                        SpeedHistory.Add(e.ThroughputMB);
                        break;
                }
            }
        });
    }

    private string FormatTimespan(TimeSpan timespan)
    {
        if (timespan.TotalHours >= 1)
        {
            return $"{(int)timespan.TotalHours}h {timespan.Minutes}m {timespan.Seconds}s";
        }
        else if (timespan.TotalMinutes >= 1)
        {
            return $"{(int)timespan.TotalMinutes}m {timespan.Seconds}s";
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
            IndexThroughputText = string.Empty;
            MaxThroughputText = string.Empty;
            EstimatedRemainingTimeText = string.Empty;
            UpdateIndexButtonText = "🔄 更新索引";
            UpdateIndexButtonEnabled = true;
            ProcessingFilename = string.Empty;
            _maxThroughput = 0;
            SpeedHistory.Clear();
        });
    }

    private void UpdateIndexCount()
    {
        var count = _indexService.Index.Count + _indexService.FrameIndex.Count;
        IndexCount = count > 0 ? $"{count}文件" : "请先创建索引";

        // 根据索引总数决定是否显示移除无效索引选项
        ShowRemoveInvalidIndex = count > 0 ? Visibility.Visible : Visibility.Collapsed;

        // 根据索引总数决定是否启用搜索配置区域
        IsSearchEnabled = count > 0;
    }

    private void UpdateSourceImageInfo(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var image = Image.Identify(path);
                var fileInfo = new FileInfo(path);
                SourceImageInfo = $"分辨率：{image.Width}x{image.Height}，大小：{fileInfo.Length / 1024}KB";
            }
            catch
            {
                SourceImageInfo = "无法加载图片信息";
            }
        }
    }

    private void UpdateDestImageInfo(string path)
    {
        if (File.Exists(path))
        {
            try
            {
                var image = Image.Identify(path);
                var fileInfo = new FileInfo(path);
                DestImageInfo = $"分辨率：{image.Width}x{image.Height}，大小：{fileInfo.Length / 1024}KB";
            }
            catch
            {
                DestImageInfo = "无法加载图片信息";
            }
        }
    }

    private void InitializePerformanceMonitoring()
    {
        try
        {
            _currentProcess = Process.GetCurrentProcess();

            // 初始化当前进程的 CPU 性能计数器
            _cpuCounter = new PerformanceCounter("Process", "% Processor Time", _currentProcess.ProcessName, true);
            _cpuCounter.NextValue(); // 初始化

            // 创建定时器，每秒更新一次
            _performanceTimer = new System.Timers.Timer(1000);
            _performanceTimer.Elapsed += UpdatePerformanceMetrics;
            _performanceTimer.AutoReset = true;
            _performanceTimer.Start();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"性能监测初始化失败: {ex.Message}");
        }
    }

    private void UpdatePerformanceMetrics(object? sender, System.Timers.ElapsedEventArgs e)
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

                // 切回 UI 线程更新 UI
                Application.Current.Dispatcher.Invoke(() =>
                {
                    CpuUsage = cpuUsageValue / Environment.ProcessorCount;
                    MemoryUsage = memoryUsage;
                });
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"更新性能指标失败: {ex.Message}");
        }
    }

    ~MainViewModel()
    {
        _performanceTimer?.Dispose();
        _cpuCounter?.Dispose();
        _currentProcess?.Dispose();
        _updateIndexTimer?.Dispose();
    }
}
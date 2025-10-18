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

    [ObservableProperty]
    private string sourceImageInfo = string.Empty;

    [ObservableProperty]
    private string sourceImagePath = string.Empty;

    [ObservableProperty]
    private string updateIndexButtonText = "🔄 更新索引";

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
    private ObservableCollection<double> speedHistory = new();

    public MainViewModel()
    {
        _indexService = new ImageIndexService();
        _searchService = new ImageSearchService();

        _indexService.ProgressChanged += OnIndexProgressChanged;
        _indexService.IndexCompleted += OnIndexCompleted;

        LoadIndexAsync();
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
            return;
        }

        OnIndexProgressChanged(this, new IndexProgressEventArgs
        {
            Message = "准备开始"
        });
        var paths = _indexService.GetIndexedPaths().ToList();
        if (string.IsNullOrWhiteSpace(DirectoryPath) && paths.Count == 0)
        {
            MessageBox.Show("请先选择文件夹", "警告", MessageBoxButton.OK, MessageBoxImage.Warning);
            IndexProgressVisibility = Visibility.Collapsed;
            return;
        }

        UpdateIndexButtonText = "⏸️ 停止索引";
        var dirs = string.IsNullOrWhiteSpace(DirectoryPath) ? PathPrefixFinder.FindLongestCommonPathPrefixes(paths, 3).Where(Directory.Exists).ToArray() : [DirectoryPath];
        await _indexService.UpdateIndexAsync(dirs, RemoveInvalidIndex);
        UpdateIndexButtonText = "🔄 更新索引";
        RemoveInvalidIndex = false;
    }

    [RelayCommand]
    private async Task Search()
    {
        if (string.IsNullOrEmpty(ImagePath))
        {
            MessageBox.Show("请先选择图片", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (!_indexService.GetIndexedPaths().Any())
        {
            MessageBox.Show("当前没有任何索引，请先添加文件夹创建索引后再搜索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        if (new FileInfo(ImagePath).DetectFiletype().MimeType?.StartsWith("image") != true)
        {
            MessageBox.Show("不是图像文件，无法检索", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        await SearchCore(ImagePath);
    }

    [RelayCommand]
    private async Task SearchFromClipboard()
    {
        IsSearching = true;
        SearchLoadingVisibility = Visibility.Visible;
        SearchStatusText = "🔍 正在搜索相似图片...";
        if (Clipboard.ContainsFileDropList())
        {
            var files = Clipboard.GetFileDropList();
            if (files.Count > 0)
            {
                ImagePath = files[0]!;
                await Search();
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

            return;
        }

        if (Clipboard.ContainsImage())
        {
            var image = Clipboard.GetImage();
            if (image != null)
            {
                var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");

                var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

                using (var fileStream = new FileStream(filename, FileMode.Create))
                {
                    encoder.Save(fileStream);
                }
                OnImagePathChanged(filename);
                await SearchCore(filename);

                _ = Task.Run(async () =>
                {
                    await Task.Delay(1000);
                    File.Delete(filename);
                });
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

        var result = MessageBox.Show("确认删除选中项吗？", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Question);
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

        var result = MessageBox.Show("确认删除到回收站吗？", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Question);
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
        IsSearching = true;
        SearchLoadingVisibility = Visibility.Visible;
        SearchStatusText = "🔍 正在搜索相似图片...";
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
                    var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");

                    var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

                    using (var fileStream = new FileStream(filename, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }
                    OnImagePathChanged(filename);

                    await SearchCore(filename);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        if (File.Exists(filename))
                            File.Delete(filename);
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
                    var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");

                    var encoder = new System.Windows.Media.Imaging.JpegBitmapEncoder();
                    encoder.Frames.Add(System.Windows.Media.Imaging.BitmapFrame.Create(image));

                    using (var fileStream = new FileStream(filename, FileMode.Create))
                    {
                        encoder.Save(fileStream);
                    }
                    OnImagePathChanged(filename);

                    await SearchCore(filename);
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(1000);
                        if (File.Exists(filename))
                            File.Delete(filename);
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
                        var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");
                        await stream.SaveFileAsync(filename);
                        OnImagePathChanged(filename);
                        await SearchCore(filename);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            if (File.Exists(filename))
                                File.Delete(filename);
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
                        using var httpClient = new HttpClient();
                        httpClient.Timeout = TimeSpan.FromSeconds(10);
                        var bytes = await httpClient.GetByteArrayAsync(uri);
                        var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + Path.GetExtension(uri.AbsolutePath));
                        if (string.IsNullOrEmpty(Path.GetExtension(filename)))
                        {
                            filename += ".jpg";
                        }
                        await File.WriteAllBytesAsync(filename, bytes);
                        OnImagePathChanged(filename);
                        await SearchCore(filename);
                        _ = Task.Run(async () =>
                        {
                            await Task.Delay(1000);
                            if (File.Exists(filename))
                                File.Delete(filename);
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
                            var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");
                            await File.WriteAllBytesAsync(filename, bytes);
                            await SearchCore(filename);
                            _ = Task.Run(async () =>
                            {
                                await Task.Delay(1000);
                                if (File.Exists(filename))
                                    File.Delete(filename);
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
            MessageBox.Show("无法识别拖放的数据格式，请尝试从剪切板搜索或选择本地文件拖放", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show($"处理拖放数据时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

            var sw = Stopwatch.StartNew();
            var sim = Similarity / 100f;

            var results = await _searchService.SearchAsync(filename, _indexService.Index, _indexService.FrameIndex, sim, FindRotated, FindFlipped);

            sw.Stop();
            ElapsedTime = $"{sw.ElapsedMilliseconds}ms";

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
        }
        catch (Exception ex)
        {
            SearchStatusText = $"❌ 搜索失败: {ex.Message}";
            MessageBox.Show($"搜索时发生错误：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
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

    private void OnIndexProgressChanged(object? sender, IndexProgressEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            ProcessStatus = e.Message;
            IndexProgress = e.ProgressPercentage;
            IndexProgressText = $"{e.ProcessedFiles:#,0} / {e.TotalFiles:#,0}";
            IndexProgressVisibility = Visibility.Visible;

            if (e.Speed > 0)
            {
                IndexSpeed = $"索引速度: {e.Speed:F0} items/s ({e.ThroughputMB:F2}MB/s)";
                IndexSpeedText = $"{e.Speed:F0} items/s";
                IndexThroughputText = $"{e.ThroughputMB:F2} MB/s";

                // 添加速度数据点到历史记录 - 显示整个索引过程
                SpeedHistory.Add(e.Speed);
            }
        });
    }

    private void OnIndexCompleted(object? sender, IndexCompletedEventArgs e)
    {
        Application.Current.Dispatcher.Invoke(() =>
        {
            UpdateIndexCount();

            if (e.Errors.Count > 0)
            {
                var errorDialog = new ErrorsDialog($"耗时：{e.ElapsedSeconds:F2}s，以下文件格式不正确，无法创建索引，请检查：\r\n{string.Join("\r\n", e.Errors)}");
                errorDialog.ShowDialog();
            }
            else if (e.FilesProcessed > 0)
            {
                MessageBox.Show($"索引创建完成，耗时：{e.ElapsedSeconds:F2}s", "消息", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            IndexProgressVisibility = Visibility.Collapsed;
            IndexProgress = 0;
            IndexProgressText = string.Empty;
            IndexSpeedText = string.Empty;
            IndexThroughputText = string.Empty;
            SpeedHistory.Clear();
        });
    }

    private void UpdateIndexCount()
    {
        var count = _indexService.Index.Count + _indexService.FrameIndex.Count;
        IndexCount = count > 0 ? $"{count}文件" : "请先创建索引";

        // 根据索引总数决定是否显示移除无效索引选项
        ShowRemoveInvalidIndex = count > 0 ? Visibility.Visible : Visibility.Collapsed;
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
}
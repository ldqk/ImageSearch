using Masuit.Tools;
using Masuit.Tools.Hardware;
using Masuit.Tools.Logging;
using Masuit.Tools.Media;
using Masuit.Tools.Systems;
using SkiaSharp;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Windows;
using 以图搜图.Helpers;

namespace 以图搜图.Services;

public sealed class ImageIndexService : Disposable
{
    private readonly ConcurrentHashQueue<int> _writeQueue = new();
    private readonly FileStream? _indexStream;
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private readonly Task? _writeTask;
    private static readonly Dictionary<char, (string type, string index)> DriveType = new() { ['\\'] = ("HDD", "Unknown"), ['/'] = ("HDD", "Unknown") };
    public static ImageIndexService Instance { get; }

    static ImageIndexService()
    {
        foreach (var drive in "ABCDEFGHIJKLMNOPQRSTUVWXYZ".Where(drive => Directory.Exists(drive + ":")))
        {
            DriveType[drive] = GetDriveMediaType(drive);
        }

        Instance = new ImageIndexService();
    }

    private ImageIndexService()
    {
        _indexStream = File.Open("index.json", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        _cancellationTokenSource = new CancellationTokenSource();
        _writeTask = StartWriteTaskAsync(_cancellationTokenSource.Token);
    }

    private async Task StartWriteTaskAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                if (_writeQueue.TryDequeue(out _))
                {
                    _writeQueue.Clear();
                    await WriteIndexAsync();
                    IndexUpdated?.Invoke(this, EventArgs.Empty);
                }

                await Task.Delay(1000, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
            // 正常的取消操作
        }
    }

    public ConcurrentDictionary<string, IndexItem> Index { get; private set; } = new();

    public bool IsIndexing { get; private set; }
    public bool IsWriting { get; private set; }
    public ICollection<string> ProcessingFiles => _processingFiles.Keys;

    public event EventHandler<IndexProgressEventArgs> ProgressChanged;

    public event EventHandler<IndexCompletedEventArgs> IndexCompleted;

    public event EventHandler IndexUpdated;

    public async Task LoadIndexAsync()
    {
        try
        {
            if (_indexStream!.Length > 0)
            {
                _indexStream.Seek(0, SeekOrigin.Begin);
                var set = await JsonSerializer.DeserializeAsync<HashSet<IndexItem>>(_indexStream);
                if (set != null)
                {
                    Index = set.ToConcurrentDictionary(x => x.FilePath);
                }
            }
        }
        catch (Exception ex)
        {
            LogManager.Error(ex);
            var errorDialog = new ErrorsDialog(ex.ToString());
            errorDialog.ShowDialog();
        }
    }

    private int _totalCount;
    private readonly ConcurrentDictionary<string, long> _processingFiles = new(StringComparer.OrdinalIgnoreCase);
    private long _processingSequence;
    private static readonly TimeSpan ProgressSamplingInterval = TimeSpan.FromSeconds(1);
    private readonly Lock _progressSampleLock = new();
    private double _lastProgressSampleSeconds;
    private int _lastProgressSampleCount;
    private double _sampledProgressSpeed;

    public async Task UpdateIndexAsync(string[] directories, bool removeInvalid)
    {
        var files = EverythingHelper.GetFiles(directories);
        _totalCount = 0;
        _processingFiles.Clear();
        _processingSequence = 0;
        lock (_progressSampleLock)
        {
            _lastProgressSampleSeconds = 0;
            _lastProgressSampleCount = 0;
            _sampledProgressSpeed = 0;
        }

        IsIndexing = true;
        if (removeInvalid)
        {
            _ = Task.Run(() => RemoveInvalidIndexes(directories, files)).ContinueWith(t =>
            {
                if (t.IsFaulted)
                {
                    LogManager.Error(t.Exception);
                }
            });
        }

        var regex = new Regex("\\.(" + EverythingHelper.Extensions.Replace(";", "|") + ")$", RegexOptions.IgnoreCase);
        var filesToIndex = files.Where(path => regex.IsMatch(path) && !Index.ContainsKey(path)).ToArray();
        if (filesToIndex.Length == 0)
        {
            IsIndexing = false;
            OnIndexCompleted(new IndexCompletedEventArgs());
            return;
        }

        var errors = new ConcurrentBag<string>();
        var sw = Stopwatch.StartNew();

        await Task.Run(() =>
        {
            Parallel.Invoke(() => UpdateIndexOnSSD(filesToIndex, sw, errors), () => UpdateIndexOnHDD(filesToIndex, sw, errors));
            if (_totalCount > 0)
            {
                _writeQueue.Enqueue(1);
            }
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                LogManager.Error(t.Exception);
            }
        });

        sw.Stop();
        IsIndexing = false;
        OnIndexCompleted(new IndexCompletedEventArgs
        {
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            FilesProcessed = _totalCount,
            Errors = errors.ToList()
        });
    }

    private void UpdateIndexOnSSD(string[] filesToIndex, Stopwatch sw, ConcurrentBag<string> errors)
    {
        var parallelism = Math.Min(63,Environment.ProcessorCount * 4);
        filesToIndex.Where(s => DriveType[s[0]].type != "HDD").Chunk(parallelism).AsParallel().WithDegreeOfParallelism(parallelism).ForAll(g =>
        {
            foreach (var file in g.Where(File.Exists).TakeWhile(_ => IsIndexing))
            {
                try
                {
                    ReportIndexProgress(file, filesToIndex.Length, sw);

                    if (file.EndsWith(".gif", StringComparison.CurrentCultureIgnoreCase))
                    {
                        using var stream = File.OpenRead(file);
                        Index[file] = CreateGifIndexItem(file, stream);
                    }
                    else if (VideoFrameHelper.IsVideoFile(file))
                    {
                        var item = CreateVideoIndexItem(file);
                        if (item.DctHash.Count > 0)
                        {
                            Index[file] = item;
                        }
                    }
                    else
                    {
                        using var image = SkiaImageHelper.DecodeGrayThumb(file, 160);
                        var indexItem = new IndexItem(file)
                        {
                            DctHash = [image.DctHash()],
                            DifferenceHash = [image.DifferenceHash256()],
                            DctHash64 = [image.DctHash64()],
                            DctHash256 = [image.DctHash256()],
                            MHHash256 = [image.MHHash256()],
                        };
                        Index[file] = indexItem;
                    }

                    Interlocked.Increment(ref _totalCount);
                }
                catch (Exception ex)
                {
                    errors.Add(file);
                    LogManager.Error(ex);
                }
                finally
                {
                    RemoveProcessingFile(file);
                }
            }
        });
    }

    private void UpdateIndexOnHDD(string[] filesToIndex, Stopwatch sw, ConcurrentBag<string> errors)
    {
        var queue = new ConcurrentQueue<(string Path, Stream Stream, long Length)>();
        using var videoQueue = new BlockingCollection<(string Path, long Length)>();
        long queuedBytes = 0;
        var producer = Task.Run(() =>
        {
            try
            {
                var memoryAvailable = Math.Min(RamInfo.Local.MemoryAvailable / 2, 8589934592d);
                var diskCount = filesToIndex.Select(s => s[0]).Distinct().Count(s => DriveType[s].type == "HDD");
                switch (diskCount)
                {
                    case 1:
                        ReadFiles(filesToIndex.Where(s => DriveType[s[0]].type == "HDD").Order());
                        break;

                    case > 1:
                        filesToIndex.Where(s => DriveType[s[0]].type == "HDD").GroupBy(s => DriveType[s[0]].index).AsParallel().WithDegreeOfParallelism(diskCount).ForAll(grouping => ReadFiles(grouping.Order()));
                        break;
                }

                void ReadFiles(IEnumerable<string> files)
                {
                    foreach (var file in files.TakeWhile(_ => IsIndexing).Where(File.Exists))
                    {
                        try
                        {
                            if (VideoFrameHelper.IsVideoFile(file))
                            {
                                videoQueue.Add((file, new FileInfo(file).Length));
                            }
                            else
                            {
                                var stream = new MemoryStream(File.ReadAllBytes(file));
                                var length = stream.Length;
                                Interlocked.Add(ref queuedBytes, length);
                                queue.Enqueue((file, stream, length));

                                while (IsIndexing && Volatile.Read(ref queuedBytes) > memoryAvailable)
                                {
                                    Thread.Yield();
                                }
                            }
                        }
                        catch
                        {
                            errors.Add(file);
                        }
                    }
                }
            }
            finally
            {
                videoQueue.CompleteAdding();
            }
        });
        var videoConsumers = Enumerable.Range(0, Math.Min(Environment.ProcessorCount /2, 7)).Select(_ => Task.Run(() =>
        {
            foreach (var item in videoQueue.GetConsumingEnumerable())
            {
                if (!IsIndexing)
                {
                    continue;
                }

                try
                {
                    ReportIndexProgress(item.Path, filesToIndex.Length, sw);
                    var hash = CreateVideoIndexItem(item.Path);
                    if (hash.DctHash.Count > 0)
                    {
                        Index[item.Path] = hash;
                    }

                    Interlocked.Increment(ref _totalCount);
                }
                catch (Exception ex)
                {
                    errors.Add(item.Path);
                    LogManager.Error(ex);
                }
                finally
                {
                    RemoveProcessingFile(item.Path);
                }
            }
        })).ToArray();
        while (!producer.IsCompleted || !queue.IsEmpty)
        {
            Parallel.For(0, Math.Min(Environment.ProcessorCount * 4, queue.Count), _ =>
            {
                if (!queue.TryDequeue(out var item))
                {
                    return;
                }

                try
                {
                    if (!IsIndexing)
                    {
                        return;
                    }

                    ReportIndexProgress(item.Path, filesToIndex.Length, sw);
                    if (item.Path.EndsWith(".gif", StringComparison.CurrentCultureIgnoreCase))
                    {
                        Index[item.Path] = CreateGifIndexItem(item.Path, item.Stream);
                    }
                    else
                    {
                        using var image = SkiaImageHelper.DecodeGrayThumb(item.Stream!, 160);
                        var indexItem = new IndexItem(item.Path)
                        {
                            DctHash = [image.DctHash()],
                            DifferenceHash = [image.DifferenceHash256()],
                            DctHash64 = [image.DctHash64()],
                            DctHash256 = [image.DctHash256()],
                            MHHash256 = [image.MHHash256()],
                        };
                        Index[item.Path] = indexItem;
                    }

                    Interlocked.Increment(ref _totalCount);
                }
                catch (Exception ex)
                {
                    errors.Add(item.Path);
                    LogManager.Error(ex);
                }
                finally
                {
                    RemoveProcessingFile(item.Path);
                    Interlocked.Add(ref queuedBytes, -item.Length);
                    item.Stream?.Dispose();
                }
            });
            if (queue.IsEmpty && !producer.IsCompleted)
            {
                Thread.Yield();
            }
        }

        Task.WaitAll(videoConsumers.Append(producer).ToArray());
    }

    private void ReportIndexProgress(string file, int totalFiles, Stopwatch sw)
    {
        _processingFiles.TryAdd(file, Interlocked.Increment(ref _processingSequence));
        var count = Volatile.Read(ref _totalCount);
        var elapsedSeconds = sw.Elapsed.TotalSeconds;
        double speed;
        lock (_progressSampleLock)
        {
            var sampleDuration = elapsedSeconds - _lastProgressSampleSeconds;
            if (sampleDuration >= ProgressSamplingInterval.TotalSeconds)
            {
                speed = Math.Max(0, count - _lastProgressSampleCount) / sampleDuration;
                _lastProgressSampleSeconds = elapsedSeconds;
                _lastProgressSampleCount = count;
                _sampledProgressSpeed = speed;
            }
            else
            {
                speed = _sampledProgressSpeed;
            }
        }

        OnProgressChanged(new IndexProgressEventArgs
        {
            Filename = file,
            Message = $"{count}/{totalFiles}",
            Speed = speed,
            AverageSpeed = elapsedSeconds > 0 ? count / elapsedSeconds : 0,
            ProcessedFiles = count,
            TotalFiles = totalFiles
        });
    }

    private void RemoveProcessingFile(string file)
    {
        _processingFiles.TryRemove(file, out var sequence);
    }

    public void StopIndexing()
    {
        IsIndexing = false;
    }

    public void RemoveFromIndex(string path)
    {
        Index.TryRemove(path, out _);
        _writeQueue.Enqueue(1);
    }

    private void RemoveInvalidIndexes(string[] newDirs, string[] allFiles)
    {
        var allPaths = allFiles;

        if (newDirs.Length > 0)
        {
            var allDirs = PathPrefixFinder.FindLongestCommonPathPrefixes(Index.Keys, 3).Where(Directory.Exists);
            var combinedFiles = EverythingHelper.GetFiles(allDirs.Union(newDirs).ToArray());
            allPaths = combinedFiles;
        }

        var removed = false;
        var removes = Index.Keys.Except(allPaths).ToList();
        if (removes.Count>10000)
        {
            var result = Application.Current.Dispatcher.Invoke(() =>
                MessageBox.Show(Application.Current.MainWindow, $"检测到需要移除的索引数量为{removes.Count}，请确认是否需要继续移除无效索引？", "提示", MessageBoxButton.OKCancel, MessageBoxImage.Warning));
            if (result != MessageBoxResult.OK)
            {
                return;
            }
        }

        foreach (var key in removes)
        {
            Index.TryRemove(key, out _);
            removed = true;
        }

        if (removed)
        {
            _writeQueue.Enqueue(1);
        }
    }

    private static IndexItem CreateGifIndexItem(string file, Stream stream)
    {
        var differenceHashes = new ConcurrentBag<ulong[]>();
        var dctHashes = new ConcurrentBag<ulong>();
        var dctHash64s = new ConcurrentBag<ulong>();
        var dctHash256s = new ConcurrentBag<ulong[]>();
        var mhHashes256 = new ConcurrentBag<ulong[]>();
        using var frames = new DisposeCollection<SKBitmap>(SkiaImageHelper.DecodeGrayFrames(stream, 160));
        frames.AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount).ForAll(frame =>
        {
            differenceHashes.Add(frame.DifferenceHash256());
            dctHashes.Add(frame.DctHash());
            dctHash64s.Add(frame.DctHash64());
            dctHash256s.Add(frame.DctHash256());
            mhHashes256.Add(frame.MHHash256());
        });

        return new IndexItem(file)
        {
            DifferenceHash = new HashSet<ulong[]>(differenceHashes, new ArrayComparer()),
            DctHash = [.. dctHashes],
            DctHash64 = [.. dctHash64s],
            DctHash256 = [.. dctHash256s],
            MHHash256 = [.. mhHashes256]
        };
    }

    private static IndexItem CreateVideoIndexItem(string file)
    {
        var differenceHashes = new ConcurrentBag<ulong[]>();
        var dctHashes = new ConcurrentBag<ulong>();
        var dctHash64s = new ConcurrentBag<ulong>();
        var dctHash256s = new ConcurrentBag<ulong[]>();
        var mhHashes256 = new ConcurrentBag<ulong[]>();
        VideoFrameHelper.ForEachGrayFrame(file, 160, frame =>
        {
            differenceHashes.Add(frame.DifferenceHash256());
            dctHashes.Add(frame.DctHash());
            dctHash64s.Add(frame.DctHash64());
            dctHash256s.Add(frame.DctHash256());
            mhHashes256.Add(frame.MHHash256());
        });

        return new IndexItem(file)
        {
            DifferenceHash = new HashSet<ulong[]>(differenceHashes, new ArrayComparer()),
            DctHash = [.. dctHashes],
            DctHash64 = [.. dctHash64s],
            DctHash256 = [.. dctHash256s],
            MHHash256 = [.. mhHashes256]
        };
    }

    private async Task WriteIndexAsync()
    {
        IsWriting = true;
        try
        {
            _indexStream!.Seek(0, SeekOrigin.Begin);
            await JsonSerializer.SerializeAsync(_indexStream, Index.Values);
            _indexStream.SetLength(_indexStream.Position);
            await _indexStream.FlushAsync();
        }
        catch (Exception ex)
        {
            LogManager.Error(ex);
        }
        finally
        {
            IsWriting = false;
        }
    }

    private static (string type, string index) GetDriveMediaType(char driveLetter)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT * FROM Win32_LogicalDisk WHERE DeviceID = '{driveLetter}:'");
            foreach (ManagementObject logicalDisk in searcher.Get())
            {
                // 获取关联的物理磁盘
                foreach (ManagementObject partition in logicalDisk.GetRelated("Win32_DiskPartition"))
                {
                    foreach (ManagementObject diskDrive in partition.GetRelated("Win32_DiskDrive"))
                    {
                        string model = diskDrive["Model"]?.ToString() ?? "Unknown";
                        string mediaType = diskDrive["MediaType"]?.ToString() ?? "Unknown";
                        string interfaceType = diskDrive["InterfaceType"]?.ToString() ?? "Unknown";
                        string diskIndex = diskDrive["Index"]?.ToString() ?? "Unknown";

                        // 判断逻辑
                        if (model.Contains("SSD", StringComparison.CurrentCultureIgnoreCase) || mediaType.Contains("SSD"))
                            return ("SSD", diskIndex);

                        if (mediaType.Contains("Fixed") && !model.Contains("SSD", StringComparison.CurrentCultureIgnoreCase))
                            return ("HDD", diskIndex);

                        if (interfaceType == "USB")
                            return ("USB", diskIndex);

                        return ("Unknown", diskIndex);
                    }
                }
            }
        }
        catch (Exception)
        {
            return ("Unknown", "Unknown");
        }

        return ("Unknown", "Unknown");
    }

    private void OnProgressChanged(IndexProgressEventArgs e)
    {
        if (e.ProcessedFiles % 1000 == 0)
        {
            _writeQueue.Enqueue(1);
        }

        ProgressChanged?.Invoke(this, e);
    }

    private void OnIndexCompleted(IndexCompletedEventArgs e)
    {
        IndexCompleted?.Invoke(this, e);
    }

    /// <summary>释放</summary>
    /// <param name="disposing"></param>
    public override void Dispose(bool disposing)
    {
        // 停止后台任务
        _cancellationTokenSource?.Cancel();
        try
        {
            _writeTask?.Wait(TimeSpan.FromSeconds(5));
        }
        catch (OperationCanceledException)
        {
            // 预期的异常
        }

        // 清理事件订阅
        ProgressChanged = null;
        IndexCompleted = null;
        IndexUpdated = null;

        // 清理数据
        Index?.Clear();

        // 关闭文件流
        _indexStream?.Dispose();

        // 清理令牌源
        _cancellationTokenSource?.Dispose();
    }
}

public record IndexItem(string FilePath)
{
    public HashSet<ulong[]> DifferenceHash { get; set; } = new HashSet<ulong[]>(new ArrayComparer());
    public HashSet<ulong> DctHash { get; set; } = [];
    public HashSet<ulong> DctHash64 { get; set; } = [];
    public HashSet<ulong[]> DctHash256 { get; set; } = [];
    public HashSet<ulong[]> MHHash256 { get; set; } = [];

    [JsonIgnore]
    public bool IsAnimated => DifferenceHash.Count > 1;
}

public class ArrayComparer : IEqualityComparer<ulong[]>
{
    /// <summary>Determines whether the specified objects are equal.</summary>
    /// <param name="x">The first object of type <paramref name="T" /> to compare.</param>
    /// <param name="y">The second object of type <paramref name="T" /> to compare.</param>
    /// <returns>
    /// <see langword="true" /> if the specified objects are equal; otherwise, <see langword="false" />.</returns>
    public bool Equals(ulong[] x, ulong[] y)
    {
        return x.SequenceEqual(y);
    }

    /// <summary>Returns a hash code for the specified object.</summary>
    /// <param name="obj">The <see cref="T:System.Object" /> for which a hash code is to be returned.</param>
    /// <exception cref="T:System.ArgumentNullException">The type of <paramref name="obj" /> is a reference type and <paramref name="obj" /> is <see langword="null" />.</exception>
    /// <returns>A hash code for the specified object.</returns>
    public int GetHashCode(ulong[] obj)
    {
        return obj.Aggregate(17, (current, value) => current * 31 + value.GetHashCode());
    }
}
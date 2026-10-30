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
using Masuit.Tools.Files;
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

    public event EventHandler<IndexProgressEventArgs>? ProgressChanged;

    public event EventHandler<IndexCompletedEventArgs>? IndexCompleted;

    public event EventHandler? IndexUpdated;

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
    private long _totalSize;

    public async Task UpdateIndexAsync(string[] directories, bool removeInvalid)
    {
        var files = EverythingHelper.GetFiles(directories);
        _totalCount = 0;
        _totalSize = 0;
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

        var errors = new List<string>();
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
            Errors = errors
        });
    }

    private void UpdateIndexOnSSD(string[] filesToIndex, Stopwatch sw, List<string> errors)
    {
        var parallelism = Environment.ProcessorCount * 4;
        filesToIndex.Where(s => DriveType[s[0]].type != "HDD").Chunk(parallelism).AsParallel().WithDegreeOfParallelism(parallelism).ForAll(g =>
        {
            foreach (var file in g.Where(File.Exists).TakeWhile(_ => IsIndexing))
            {
                try
                {
                    OnProgressChanged(new IndexProgressEventArgs
                    {
                        Filename = file,
                        Message = $"{_totalCount}/{filesToIndex.Length}",
                        Speed = _totalCount / sw.Elapsed.TotalSeconds,
                        ThroughputMB = _totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                        ProcessedFiles = _totalCount,
                        TotalFiles = filesToIndex.Length
                    });

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
                            DctHash64 = [image.DctHash64()]
                        };
                        Index[file] = indexItem;
                    }

                    var size = new FileInfo(file).Length;
                    _totalCount++;
                    _totalSize += size;
                    OnProgressChanged(new IndexProgressEventArgs
                    {
                        Filename = file,
                        Message = $"{_totalCount}/{filesToIndex.Length}",
                        Speed = _totalCount / sw.Elapsed.TotalSeconds,
                        ThroughputMB = _totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                        ProcessedFiles = _totalCount,
                        TotalFiles = filesToIndex.Length
                    });
                }
                catch
                {
                    errors.Add(file);
                }
            }
        });
    }

    private void UpdateIndexOnHDD(string[] filesToIndex, Stopwatch sw, List<string> errors)
    {
        var queue = new ConcurrentQueue<(string Path, Stream Stream, long Length)>();
        long queuedBytes = 0;
        bool loading = true;
        Task.Run(() =>
        {
            var memoryAvailable = Math.Min(RamInfo.Local.MemoryAvailable / 2, 8589934592d);
            var diskCount = DriveType.Values.Where(t => t.type == "HDD").Select(t => t.index).Distinct().Count();
            switch (diskCount)
            {
                case 1:
                    foreach (var file in filesToIndex.Where(s => DriveType[s[0]].type == "HDD").Order().TakeWhile(_ => IsIndexing).Where(File.Exists))
                    {
                        try
                        {
                            if (VideoFrameHelper.IsVideoFile(file))
                            {
                                var fileInfo = new FileInfo(file);
                                var stream = fileInfo.OpenRead();
                                queue.Enqueue((file, stream, fileInfo.Length));
                                Interlocked.Add(ref queuedBytes, fileInfo.Length / 2);
                            }
                            else
                            {
                                var stream = new PooledMemoryStream(File.ReadAllBytes(file));
                                var length = stream.Length;
                                queue.Enqueue((file, stream, length));
                                Interlocked.Add(ref queuedBytes, length);
                            }

                            while (Volatile.Read(ref queuedBytes) > memoryAvailable)
                            {
                                Thread.Sleep(200);
                            }
                        }
                        catch
                        {
                            errors.Add(file);
                        }
                    }

                    break;

                case > 1:
                    filesToIndex.Where(s => DriveType[s[0]].type == "HDD").GroupBy(s => DriveType[s[0]].index).AsParallel().WithDegreeOfParallelism(diskCount).ForAll(grouping =>
                    {
                        foreach (var file in grouping.Order().TakeWhile(_ => IsIndexing).Where(File.Exists))
                        {
                            try
                            {
                                if (VideoFrameHelper.IsVideoFile(file))
                                {
                                    var fileInfo = new FileInfo(file);
                                    var stream = fileInfo.OpenRead();
                                    queue.Enqueue((file, stream, fileInfo.Length));
                                    Interlocked.Add(ref queuedBytes, fileInfo.Length);
                                }
                                else
                                {
                                    var stream = new PooledMemoryStream(File.ReadAllBytes(file));
                                    var length = stream.Length;
                                    queue.Enqueue((file, stream, length));
                                    Interlocked.Add(ref queuedBytes, length);
                                }

                                while (Volatile.Read(ref queuedBytes) > memoryAvailable)
                                {
                                    Thread.Sleep(200);
                                }
                            }
                            catch
                            {
                                errors.Add(file);
                            }
                        }
                    });
                    break;
            }

            loading = false;
        }).ContinueWith(t =>
        {
            if (t.IsFaulted)
            {
                LogManager.Error(t.Exception);
            }
        });
        while (loading || queue.Count > 0)
        {
            Parallel.For(0, Math.Min(Environment.ProcessorCount * 4, queue.Count), _ =>
            {
                if (!queue.TryDequeue(out var item))
                {
                    return;
                }

                if (item.Stream != null)
                {
                    Interlocked.Add(ref queuedBytes, -item.Length);
                }

                if (!IsIndexing)
                {
                    return;
                }

                try
                {
                    OnProgressChanged(new IndexProgressEventArgs
                    {
                        Filename = item.Path,
                        Message = $"{_totalCount}/{filesToIndex.Length}",
                        Speed = _totalCount / sw.Elapsed.TotalSeconds,
                        ThroughputMB = _totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                        ProcessedFiles = _totalCount,
                        TotalFiles = filesToIndex.Length
                    });

                    if (item.Path.EndsWith(".gif", StringComparison.CurrentCultureIgnoreCase))
                    {
                        Index[item.Path] = CreateGifIndexItem(item.Path, item.Stream);
                    }
                    else if (VideoFrameHelper.IsVideoFile(item.Path))
                    {
                        var hash = CreateVideoIndexItem(item.Path);
                        if (hash.DctHash.Count > 0)
                        {
                            Index[item.Path] = hash;
                        }
                    }
                    else
                    {
                        using var image = SkiaImageHelper.DecodeGrayThumb(item.Stream!, 160);
                        var indexItem = new IndexItem(item.Path)
                        {
                            DctHash = [image.DctHash()],
                            DifferenceHash = [image.DifferenceHash256()],
                            DctHash64 = [image.DctHash64()]
                        };
                        Index[item.Path] = indexItem;
                    }

                    _totalCount++;
                    _totalSize += item.Length;
                    OnProgressChanged(new IndexProgressEventArgs
                    {
                        Filename = item.Path,
                        Message = $"{_totalCount}/{filesToIndex.Length}",
                        Speed = _totalCount / sw.Elapsed.TotalSeconds,
                        ThroughputMB = _totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                        ProcessedFiles = _totalCount,
                        TotalFiles = filesToIndex.Length
                    });
                }
                catch (Exception ex)
                {
                    errors.Add(item.Path);
                    LogManager.Error(ex);
                }
                finally
                {
                    item.Stream?.Dispose();
                }
            });
            Thread.Yield();
        }
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
        using var frames = new DisposeCollection<SKBitmap>(SkiaImageHelper.DecodeGrayFrames(stream, 160));
        frames.AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount).ForAll(frame =>
        {
            differenceHashes.Add(frame.DifferenceHash256());
            dctHashes.Add(frame.DctHash());
            dctHash64s.Add(frame.DctHash64());
        });

        return new IndexItem(file)
        {
            DifferenceHash = new HashSet<ulong[]>(differenceHashes, new ArrayComparer()),
            DctHash = [.. dctHashes],
            DctHash64 = [.. dctHash64s]
        };
    }

    private static IndexItem CreateVideoIndexItem(string file)
    {
        var differenceHashes = new ConcurrentBag<ulong[]>();
        var dctHashes = new ConcurrentBag<ulong>();
        var dctHash64s = new ConcurrentBag<ulong>();
        VideoFrameHelper.ForEachGrayFrame(file, 160, frame =>
        {
            differenceHashes.Add(frame.DifferenceHash256());
            dctHashes.Add(frame.DctHash());
            dctHash64s.Add(frame.DctHash64());
        });

        return new IndexItem(file)
        {
            DifferenceHash = new HashSet<ulong[]>(differenceHashes, new ArrayComparer()),
            DctHash = [.. dctHashes],
            DctHash64 = [.. dctHash64s]
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
        if (e.ProcessedFiles % 100 == 0)
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
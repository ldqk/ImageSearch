using Masuit.Tools;
using Masuit.Tools.Logging;
using Masuit.Tools.Media;
using Masuit.Tools.Systems;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Text.Json;
using System.Text.RegularExpressions;
using Size = SixLabors.ImageSharp.Size;

namespace 以图搜图.Services;

public sealed class ImageIndexService : Disposable
{
    private readonly Regex _picRegex = new("(jpg|jpeg|png|bmp|webp)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ConcurrentQueue<int> _writeQueue = new();
    private readonly FileStream? _frameIndexStream;
    private readonly FileStream? _indexStream;
    private readonly CancellationTokenSource? _cancellationTokenSource;
    private readonly Task? _writeTask;
    private static readonly Dictionary<char, string> DriveType = new();

    static ImageIndexService()
    {
        foreach (var drive in "CDEFGHIJKLMNOPQRSTUVWXYZ".Where(drive => Directory.Exists(drive + ":")))
        {
            DriveType[drive] = GetDriveMediaType(drive);
        }
    }

    public ImageIndexService()
    {
        _indexStream = File.Open("index.json", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        _frameIndexStream = File.Open("frame_index.json", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
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
                    // 清空队列中的所有项
                    while (_writeQueue.TryDequeue(out _))
                    {
                    }

                    await WriteIndexAsync();
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
    public ConcurrentDictionary<string, FrameIndexItem> FrameIndex { get; private set; } = new();

    public bool IsIndexing { get; private set; }
    public bool IsWriting { get; private set; }

    public event EventHandler<IndexProgressEventArgs>? ProgressChanged;

    public event EventHandler<IndexCompletedEventArgs>? IndexCompleted;

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

            if (_frameIndexStream!.Length > 0)
            {
                _frameIndexStream.Seek(0, SeekOrigin.Begin);
                var set = await JsonSerializer.DeserializeAsync<HashSet<FrameIndexItem>>(_frameIndexStream);
                if (set != null)
                {
                    FrameIndex = set.ToConcurrentDictionary(x => x.FilePath);
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

    public async Task UpdateIndexAsync(string[] directories, bool removeInvalid)
    {
        IsIndexing = true;
        var files = GetFiles(directories);
        if (removeInvalid)
        {
            _ = Task.Run(() => RemoveInvalidIndexes(directories, files));
        }

        var filesToIndex = files.Except(Index.Keys).Except(FrameIndex.Keys).Where(s => Regex.IsMatch(s, "(gif|jpg|jpeg|png|bmp|webp)$", RegexOptions.IgnoreCase)).ToArray();
        var filesCount = filesToIndex.Length;
        if (filesCount == 0)
        {
            IsIndexing = false;
            OnIndexCompleted(new IndexCompletedEventArgs());
            return;
        }

        var errors = new List<string>();
        var sw = Stopwatch.StartNew();
        long totalSize = 0;

        // 使用 using 确保 ThreadLocal 被正确释放
        using var local = new ThreadLocal<int>(true);
        await Task.Run(() =>
        {
            var parallelism = Environment.ProcessorCount * 4;
            // 索引静态图片
            // 机械硬盘按文件夹分组扫描
            foreach (var grouping in filesToIndex.Where(s => _picRegex.IsMatch(s) && DriveType[s[0]] == "HDD").GroupBy(Path.GetDirectoryName).TakeWhile(_ => IsIndexing))
            {
                var lism = Math.Min(grouping.Count(), 32);
                grouping.Chunk(lism).AsParallel().WithDegreeOfParallelism(lism).ForAll(g =>
                {
                    foreach (var file in g.TakeWhile(_ => IsIndexing))
                    {
                        try
                        {
                            var image = Image.Load<L8>(new DecoderOptions
                            {
                                TargetSize = new Size(160),
                                SkipMetadata = true
                            }, file);
                            var indexItem = new IndexItem(file)
                            {
                                DctHash = image.DctHash(),
                                DifferenceHash = image.DifferenceHash256()
                            };
                            Index[file] = indexItem;
                            var size = new FileInfo(file).Length;
                            Interlocked.Add(ref totalSize, size);
                            local.Value++;

                            OnProgressChanged(new IndexProgressEventArgs
                            {
                                Filename = file,
                                Message = $"{local.Values.Sum()}/{filesCount}",
                                Speed = local.Values.Sum() / sw.Elapsed.TotalSeconds,
                                ThroughputMB = totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                                ProcessedFiles = local.Values.Sum(),
                                TotalFiles = filesCount
                            });
                        }
                        catch
                        {
                            errors.Add(file);
                        }
                    }
                });
            }

            // 非机械硬盘普通扫描
            filesToIndex.Where(s => _picRegex.IsMatch(s) && DriveType[s[0]] != "HDD").Chunk(parallelism).AsParallel().WithDegreeOfParallelism(parallelism).ForAll(g =>
            {
                foreach (var file in g.TakeWhile(_ => IsIndexing))
                {
                    try
                    {
                        var image = Image.Load<L8>(new DecoderOptions
                        {
                            TargetSize = new Size(160),
                            SkipMetadata = true
                        }, file);
                        var indexItem = new IndexItem(file)
                        {
                            DctHash = image.DctHash(),
                            DifferenceHash = image.DifferenceHash256()
                        };
                        Index[file] = indexItem;
                        var size = new FileInfo(file).Length;
                        Interlocked.Add(ref totalSize, size);
                        local.Value++;

                        OnProgressChanged(new IndexProgressEventArgs
                        {
                            Filename = file,
                            Message = $"{local.Values.Sum()}/{filesCount}",
                            Speed = local.Values.Sum() / sw.Elapsed.TotalSeconds,
                            ThroughputMB = totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                            ProcessedFiles = local.Values.Sum(),
                            TotalFiles = filesCount
                        });
                    }
                    catch
                    {
                        errors.Add(file);
                    }
                }
            });

            // 索引GIF动画
            filesToIndex.Where(s => s.EndsWith(".gif", StringComparison.CurrentCultureIgnoreCase)).Chunk(parallelism).AsParallel().WithDegreeOfParallelism(parallelism).ForAll(g =>
            {
                foreach (var file in g.TakeWhile(_ => IsIndexing))
                {
                    try
                    {
                        var indexItem = new FrameIndexItem(file);
                        using var gif = Image.Load<L8>(new DecoderOptions
                        {
                            TargetSize = new Size(160),
                            SkipMetadata = true
                        }, file);

                        indexItem.DifferenceHash = new List<ulong[]>();
                        for (var i = 0; i < gif.Frames.Count; i++)
                        {
                            using var frame = gif.Frames.ExportFrame(i);
                            var hash = frame.DifferenceHash256();
                            indexItem.DifferenceHash.Add(hash);
                        }
                        indexItem.DctHash = new List<ulong>();
                        for (var i = 0; i < gif.Frames.Count; i++)
                        {
                            using var frame = gif.Frames.ExportFrame(i);
                            var hash = frame.DctHash();
                            indexItem.DctHash.Add(hash);
                        }
                        FrameIndex[file] = indexItem;

                        var size = new FileInfo(file).Length;
                        Interlocked.Add(ref totalSize, size);
                        local.Value++;

                        OnProgressChanged(new IndexProgressEventArgs
                        {
                            Filename = file,
                            Message = $"{local.Values.Sum()}/{filesCount}",
                            Speed = local.Values.Sum() / sw.Elapsed.TotalSeconds,
                            ThroughputMB = totalSize / 1048576.0 / sw.Elapsed.TotalSeconds,
                            ProcessedFiles = local.Values.Sum(),
                            TotalFiles = filesCount
                        });
                    }
                    catch
                    {
                        errors.Add(file);
                    }
                }
            });

            if (totalSize > 0)
            {
                _writeQueue.Enqueue(1);
            }
        });

        sw.Stop();
        IsIndexing = false;
        OnIndexCompleted(new IndexCompletedEventArgs
        {
            ElapsedSeconds = sw.Elapsed.TotalSeconds,
            FilesProcessed = local.Values.Sum(),
            Errors = errors
        });
    }

    public void StopIndexing()
    {
        IsIndexing = false;
    }

    public void RemoveFromIndex(string path)
    {
        Index.TryRemove(path, out _);
        FrameIndex.TryRemove(path, out _);
        _writeQueue.Enqueue(1);
    }

    public IEnumerable<string> GetIndexedPaths()
    {
        return Index.Keys.Union(FrameIndex.Keys);
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

        return directories.SelectMany(s => Directory.GetFiles(s, "*", SearchOption.AllDirectories)).ToArray();
    }

    private void RemoveInvalidIndexes(string[] newDirs, string[] allFiles)
    {
        var allPaths = allFiles;

        if (newDirs.Length > 0)
        {
            var allDirs = PathPrefixFinder.FindLongestCommonPathPrefixes(Index.Keys.Union(FrameIndex.Keys), 3).Where(Directory.Exists);
            var combinedFiles = GetFiles(allDirs.Union(newDirs).ToArray());
            allPaths = combinedFiles;
        }

        var removed = false;
        var removes = Index.Keys.Except(allPaths).ToList();
        foreach (var key in removes)
        {
            Index.TryRemove(key, out _);
            removed = true;
        }

        removes = FrameIndex.Keys.Except(allPaths).ToList();
        foreach (var key in removes)
        {
            FrameIndex.TryRemove(key, out _);
            removed = true;
        }

        if (removed)
        {
            _writeQueue.Enqueue(1);
        }
    }

    private async Task WriteIndexAsync()
    {
        IsWriting = true;
        try
        {
            _indexStream!.Seek(0, SeekOrigin.Begin);
            _frameIndexStream!.Seek(0, SeekOrigin.Begin);

            await JsonSerializer.SerializeAsync(_indexStream, Index.Values);
            await JsonSerializer.SerializeAsync(_frameIndexStream, FrameIndex.Values);

            _indexStream.SetLength(_indexStream.Position);
            _frameIndexStream.SetLength(_frameIndexStream.Position);

            await _indexStream.FlushAsync();
            await _frameIndexStream.FlushAsync();
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

    private static string GetDriveMediaType(char driveLetter)
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

                        // 判断逻辑
                        if (model.Contains("SSD", StringComparison.CurrentCultureIgnoreCase) || mediaType.Contains("SSD"))
                            return "SSD";

                        if (mediaType.Contains("Fixed") && !model.Contains("SSD", StringComparison.CurrentCultureIgnoreCase))
                            return "HDD";

                        if (interfaceType == "USB")
                            return "USB";

                        return "Unknown";
                    }
                }
            }
        }
        catch (Exception)
        {
            return "Unknown";
        }

        return "Unknown";
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

        // 清理数据
        FrameIndex?.Clear();
        Index?.Clear();

        // 关闭文件流
        _indexStream?.Dispose();
        _frameIndexStream?.Dispose();

        // 清理令牌源
        _cancellationTokenSource?.Dispose();
    }
}

public record IndexItem(string FilePath)
{
    public ulong[] DifferenceHash { get; set; }
    public ulong DctHash { get; set; }
}

public sealed record FrameIndexItem(string FilePath)
{
    public List<ulong[]> DifferenceHash { get; set; } = new List<ulong[]>();
    public List<ulong> DctHash { get; set; } = new List<ulong>();
}
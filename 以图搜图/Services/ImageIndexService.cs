using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using Masuit.Tools.Logging;
using Masuit.Tools.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using Size = SixLabors.ImageSharp.Size;

namespace 以图搜图.Services;

public class ImageIndexService
{
    private readonly Regex _picRegex = new("(jpg|jpeg|png|bmp|webp)$", RegexOptions.Compiled | RegexOptions.IgnoreCase);
    private readonly ConcurrentQueue<int> _writeQueue = new();
    private FileStream? _frameIndexStream;
    private FileStream? _indexStream;

    public ImageIndexService()
    {
        _indexStream = File.Open("index.json", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);
        _frameIndexStream = File.Open("frame_index.json", FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.ReadWrite);

        Task.Run(async () =>
        {
            while (true)
            {
                if (_writeQueue.TryDequeue(out _))
                {
                    while (_writeQueue.TryDequeue(out _))
                    {
                    }

                    await WriteIndexAsync();
                }

                await Task.Delay(1000);
            }
        });
    }

    public ConcurrentDictionary<string, ulong[]> Index { get; private set; } = new();
    public ConcurrentDictionary<string, List<ulong[]>> FrameIndex { get; private set; } = new();

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
                Index = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<string, ulong[]>>(_indexStream) ?? new ConcurrentDictionary<string, ulong[]>();
            }

            if (_frameIndexStream!.Length > 0)
            {
                _frameIndexStream.Seek(0, SeekOrigin.Begin);
                FrameIndex = await JsonSerializer.DeserializeAsync<ConcurrentDictionary<string, List<ulong[]>>>(_frameIndexStream) ?? new ConcurrentDictionary<string, List<ulong[]>>();
            }
        }
        catch (Exception ex)
        {
            LogManager.Error(ex);
        }
    }

    public async Task UpdateIndexAsync(string[] directories, bool removeInvalid)
    {
        IsIndexing = true;
        var imageHasher = new ImageHasher(new ImageSharpTransformer());

        var files = GetFiles(directories);

        if (removeInvalid)
        {
            _ = Task.Run(() => RemoveInvalidIndexes(directories, files));
        }

        var filesToIndex = files.Except(Index.Keys).Except(FrameIndex.Keys).Where(s => Regex.IsMatch(s, "(gif|jpg|jpeg|png|bmp|webp)$", RegexOptions.IgnoreCase)).ToArray();

        var filesCount = filesToIndex.Length;
        var local = new ThreadLocal<int>(true);
        var errors = new List<string>();
        var sw = Stopwatch.StartNew();
        long totalSize = 0;

        await Task.Run(() =>
        {
            // 索引静态图片
            filesToIndex.Where(s => _picRegex.IsMatch(s)).Chunk(Environment.ProcessorCount * 4).AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 4).ForAll(g =>
            {
                foreach (var file in g)
                {
                    if (!IsIndexing) break;

                    try
                    {
                        Index.GetOrAdd(file, _ => imageHasher.DifferenceHash256(file));
                        var size = new FileInfo(file).Length;
                        Interlocked.Add(ref totalSize, size);
                        local.Value++;

                        OnProgressChanged(new IndexProgressEventArgs
                        {
                            Message = $"{local.Values.Sum()}/{filesCount}",
                            Speed = local.Values.Sum() / sw.Elapsed.TotalSeconds,
                            ThroughputMB = totalSize / 1048576.0 / sw.Elapsed.TotalSeconds
                        });
                    }
                    catch
                    {
                        errors.Add(file);
                    }
                }
            });

            // 索引GIF动画
            filesToIndex.Where(s => s.EndsWith(".gif", StringComparison.CurrentCultureIgnoreCase)).Chunk(Environment.ProcessorCount * 2).AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 2).ForAll(g =>
            {
                foreach (var file in g)
                {
                    if (!IsIndexing) break;

                    try
                    {
                        using var gif = Image.Load<L8>(new DecoderOptions
                        {
                            TargetSize = new Size(144),
                            SkipMetadata = true
                        }, file);

                        var hashes = new List<ulong[]>();
                        for (var i = 0; i < gif.Frames.Count; i++)
                        {
                            using var frame = gif.Frames.ExportFrame(i);
                            var hash = imageHasher.DifferenceHash256(frame);
                            hashes.Add(hash);
                        }

                        FrameIndex.GetOrAdd(file, _ => new List<ulong[]>()).AddRange(hashes);

                        var size = new FileInfo(file).Length;
                        Interlocked.Add(ref totalSize, size);
                        local.Value++;

                        OnProgressChanged(new IndexProgressEventArgs
                        {
                            Message = $"{local.Values.Sum()}/{filesCount}",
                            Speed = local.Values.Sum() / sw.Elapsed.TotalSeconds,
                            ThroughputMB = totalSize / 1048576.0 / sw.Elapsed.TotalSeconds
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

    public List<string> GetIndexedPaths()
    {
        return Index.Keys.Union(FrameIndex.Keys).ToList();
    }

    private string[] GetFiles(string[] directories)
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
        var allPaths = allFiles.ToHashSet();

        if (newDirs.Length > 0)
        {
            var allDirs = PathPrefixFinder.FindLongestCommonPathPrefixes(Index.Keys.Union(FrameIndex.Keys), 3).Where(Directory.Exists);
            var combinedFiles = GetFiles(allDirs.Union(newDirs).ToArray());
            allPaths = combinedFiles.ToHashSet();
        }

        var removes = Index.Keys.Except(allPaths).ToList();
        foreach (var key in removes)
        {
            Index.TryRemove(key, out _);
        }

        removes = FrameIndex.Keys.Except(allPaths).ToList();
        foreach (var key in removes)
        {
            FrameIndex.TryRemove(key, out _);
        }

        if (removes.Count > 0)
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

            await JsonSerializer.SerializeAsync(_indexStream, Index);
            await JsonSerializer.SerializeAsync(_frameIndexStream, FrameIndex);

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

    protected virtual void OnProgressChanged(IndexProgressEventArgs e)
    {
        ProgressChanged?.Invoke(this, e);
    }

    protected virtual void OnIndexCompleted(IndexCompletedEventArgs e)
    {
        IndexCompleted?.Invoke(this, e);
    }

    ~ImageIndexService()
    {
        _indexStream?.Dispose();
        _frameIndexStream?.Dispose();
    }
}
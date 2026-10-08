using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;

namespace 以图搜图.Controls;

public sealed class AsyncImage : Image
{
    private const long MaximumCacheBytes = 64 * 1024 * 1024;
    private static readonly SemaphoreSlim DecodeSlots = new(2);
    private static readonly Lock CacheLock = new();
    private static readonly Dictionary<ImageKey, LinkedListNode<CacheEntry>> ImageCache = new(ImageKeyComparer.Instance);
    private static readonly LinkedList<CacheEntry> CacheLru = [];
    private static long _cacheBytes;
    private int _loadGeneration;

    public static readonly DependencyProperty ImagePathProperty = DependencyProperty.Register(nameof(ImagePath), typeof(string), typeof(AsyncImage), new PropertyMetadata(null, OnImagePathChanged));

    public static readonly DependencyProperty DecodePixelSizeProperty = DependencyProperty.Register(nameof(DecodePixelSize), typeof(int), typeof(AsyncImage), new PropertyMetadata(360, OnImagePathChanged));

    public string? ImagePath
    {
        get => (string?) GetValue(ImagePathProperty);
        set => SetValue(ImagePathProperty, value);
    }

    public int DecodePixelSize
    {
        get => (int) GetValue(DecodePixelSizeProperty);
        set => SetValue(DecodePixelSizeProperty, value);
    }

    private static void OnImagePathChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs e)
    {
        var image = (AsyncImage) dependencyObject;
        image._loadGeneration++;
        image.Source = null;

        if (image.ImagePath is not {Length: > 0} path || image.DecodePixelSize <= 0)
        {
            return;
        }

        var key = new ImageKey(path, image.DecodePixelSize);
        var cached = GetCachedImage(key);
        if (cached != null)
        {
            image.Source = cached;
            return;
        }

        _ = image.LoadImageAsync(key, image._loadGeneration);
    }

    private async Task LoadImageAsync(ImageKey key, int generation)
    {
        await DecodeSlots.WaitAsync();
        try
        {
            if (generation != _loadGeneration)
            {
                return;
            }

            var cached = GetCachedImage(key);
            var bitmap = cached ?? await Task.Run(() => DecodeImage(key));
            if (bitmap == null)
            {
                return;
            }

            if (cached == null)
            {
                AddToCache(key, bitmap);
            }

            if (generation == _loadGeneration && ImagePath == key.Path && DecodePixelSize == key.DecodePixelSize)
            {
                Source = bitmap;
            }
        }
        finally
        {
            DecodeSlots.Release();
        }
    }

    private static BitmapSource? DecodeImage(ImageKey key)
    {
        try
        {
            using var stream = new FileStream(key.Path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = key.DecodePixelSize;
            bitmap.StreamSource = stream;
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch (IOException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (System.IO.FileFormatException)
        {
            return null;
        }
    }

    private static BitmapSource? GetCachedImage(ImageKey key)
    {
        lock (CacheLock)
        {
            if (!ImageCache.TryGetValue(key, out var node))
            {
                return null;
            }

            CacheLru.Remove(node);
            CacheLru.AddFirst(node);
            return node.Value.Bitmap;
        }
    }

    private static void AddToCache(ImageKey key, BitmapSource bitmap)
    {
        var sizeInBytes = (long) bitmap.PixelWidth * bitmap.PixelHeight * 4;
        if (sizeInBytes > MaximumCacheBytes)
        {
            return;
        }

        lock (CacheLock)
        {
            if (ImageCache.TryGetValue(key, out var existing))
            {
                CacheLru.Remove(existing);
                _cacheBytes -= existing.Value.SizeInBytes;
            }

            while (_cacheBytes + sizeInBytes > MaximumCacheBytes && CacheLru.Last is { } leastRecentlyUsed)
            {
                ImageCache.Remove(leastRecentlyUsed.Value.Key);
                _cacheBytes -= leastRecentlyUsed.Value.SizeInBytes;
                CacheLru.RemoveLast();
            }

            var entry = new CacheEntry(key, bitmap, sizeInBytes);
            ImageCache[key] = CacheLru.AddFirst(entry);
            _cacheBytes += sizeInBytes;
        }
    }

    private readonly record struct ImageKey(string Path, int DecodePixelSize);

    private sealed record CacheEntry(ImageKey Key, BitmapSource Bitmap, long SizeInBytes);

    private sealed class ImageKeyComparer : IEqualityComparer<ImageKey>
    {
        public static readonly ImageKeyComparer Instance = new();

        public bool Equals(ImageKey x, ImageKey y) => x.DecodePixelSize == y.DecodePixelSize && StringComparer.OrdinalIgnoreCase.Equals(x.Path, y.Path);

        public int GetHashCode(ImageKey key) => HashCode.Combine(StringComparer.OrdinalIgnoreCase.GetHashCode(key.Path), key.DecodePixelSize);
    }
}
using FFMpegCore;
using Masuit.Tools.Media;
using SkiaSharp;
using System.IO;
using Masuit.Tools.Systems;

namespace 以图搜图.Helpers;

internal static class VideoFrameHelper
{

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".m2ts", ".ts", ".webm", ".wmv"
    };

    public static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static void ForEachGrayFrame(string videoPath, int size, Action<SKBitmap> processFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentNullException.ThrowIfNull(processFrame);
        var directory = Path.Combine(Path.GetTempPath(), "imagesearch", $"video-{Path.GetFileNameWithoutExtension(videoPath)}-{SnowFlake.NewId}");
        Directory.CreateDirectory(directory);
        try
        {
            for (int i = 50; i <= 110; i+=10)
            {
                var succeeded = i>100?ExtractFrames(videoPath, size, directory):ExtractFrames(videoPath, size, directory, i);
                var frames = Directory.GetFiles(directory, "frame-*.jpg");
                if (frames.Length > 0)
                {
                    Parallel.ForEach(frames, new ParallelOptions { MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount * 4, frames.Length) }, framePath =>
                    {
                        using var frame = SkiaImageHelper.DecodeGrayThumb(framePath, size);
                        processFrame(frame);
                    });
                    break;
                }

                if (!succeeded)
                {
                    throw new InvalidDataException($"FFmpeg failed to extract frames from '{videoPath}'.");
                }
            }
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static bool ExtractFrames(string videoPath, int size, string directory, int pblack)
    {
        var outputPattern = Path.Combine(directory, "frame-%08d.jpg");
        return FFMpegArguments.FromFileInput(videoPath).OutputToFile(outputPattern, true, options => options.WithCustomArgument($"-vf \"fps=1,blackframe=0,metadata=select:key=lavfi.blackframe.pblack:value={pblack}:function=less,scale={size}:{size}:force_original_aspect_ratio=decrease\"")).ProcessSynchronously(false);
    }

    private static bool ExtractFrames(string videoPath, int size, string directory)
    {
        var outputPattern = Path.Combine(directory, "frame-%08d.jpg");
        return FFMpegArguments.FromFileInput(videoPath).OutputToFile(outputPattern, true, options => options.WithCustomArgument($"-vf \"fps=1,scale={size}:{size}:force_original_aspect_ratio=decrease\"")).ProcessSynchronously(false);
    }

    public static bool ExtractPreviewFrame(string videoPath, string outputPath, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var succeeded = FFMpegArguments.FromFileInput(videoPath).OutputToFile(outputPath, true, options => options.WithCustomArgument($"-vf \"thumbnail=300,scale={size}:-2\" -frames:v 1 -q:v 2")).ProcessSynchronously();
        return succeeded && File.Exists(outputPath) && new FileInfo(outputPath).Length > 0;
    }
    public static IMediaAnalysis Analyse(string videoPath) => FFProbe.Analyse(videoPath);
}

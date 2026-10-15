using FFMpegCore;
using Masuit.Tools.Media;
using SkiaSharp;
using System.IO;

namespace 以图搜图.Helpers;

internal static class VideoFrameHelper
{
    private const int PreviewCandidateCount = 8;
    private const double TargetPreviewLuminance = 127.5;

    private static readonly FFOptions FfmpegOptions = new()
    {
        //BinaryFolder = @"D:\Program Files\ffmpeg-5.1.2-essentials_build\bin\"
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".3gp", ".avi", ".flv", ".m4v", ".mkv", ".mov", ".mp4", ".mpeg", ".mpg", ".mts", ".m2ts", ".ts", ".webm", ".wmv"
    };

    public static bool IsVideoFile(string path) => VideoExtensions.Contains(Path.GetExtension(path));

    public static void ForEachGrayFrame(string videoPath, int size, Action<SKBitmap> processFrame)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);
        ArgumentNullException.ThrowIfNull(processFrame);

        var directory = Path.Combine(Path.GetTempPath(), $"imagesearch-video-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var outputPattern = Path.Combine(directory, "frame-%08d.png");
            FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(outputPattern, true, options => options.WithCustomArgument(
                    $"-vf \"fps=1,blackframe=0,metadata=select:key=lavfi.blackframe.pblack:value=50:function=less,scale={size}:{size}:force_original_aspect_ratio=decrease\""))
                .ProcessSynchronously(true, FfmpegOptions);

            var frames = Directory.GetFiles(directory, "frame-*.png");
            if (frames.Length == 0)
            {
                throw new InvalidDataException($"No video frames were extracted from '{videoPath}'.");
            }

            Parallel.ForEach(frames, new ParallelOptions {MaxDegreeOfParallelism = Environment.ProcessorCount*4}, framePath =>
            {
                using var frame = SkiaImageHelper.DecodeGrayThumb(framePath, size);
                processFrame(frame);
            });
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    public static bool ExtractPreviewFrame(string videoPath, string outputPath, int size)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(size);

        var directory = Path.Combine(Path.GetTempPath(), $"imagesearch-preview-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        try
        {
            var candidatePattern = Path.Combine(directory, "candidate-%02d.jpg");
            var filter = $"fps=1/2,scale={size}:{size}:force_original_aspect_ratio=decrease";
            var succeeded = FFMpegArguments
                .FromFileInput(videoPath)
                .OutputToFile(candidatePattern, true, options => options.WithCustomArgument(
                    $"-vf \"{filter}\" -frames:v {PreviewCandidateCount} -q:v 5"))
                .ProcessSynchronously(true, FfmpegOptions);

            if (!succeeded)
            {
                return false;
            }

            var bestCandidate = Directory.GetFiles(directory, "candidate-*.jpg")
                .Select(path => new { Path = path, Brightness = GetAverageLuminance(path) })
                .Where(candidate => candidate.Brightness >= 0)
                .OrderBy(candidate => Math.Abs(candidate.Brightness - TargetPreviewLuminance))
                .FirstOrDefault();
            if (bestCandidate == null)
            {
                return false;
            }

            File.Copy(bestCandidate.Path, outputPath, true);
            return new FileInfo(outputPath).Length > 0;
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static double GetAverageLuminance(string imagePath)
    {
        using var bitmap = SKBitmap.Decode(imagePath);
        if (bitmap == null || bitmap.Width == 0 || bitmap.Height == 0)
        {
            return -1;
        }

        var sampleStep = Math.Max(1, Math.Max(bitmap.Width, bitmap.Height) / 32);
        double luminanceTotal = 0;
        var sampleCount = 0;
        for (var y = 0; y < bitmap.Height; y += sampleStep)
        {
            for (var x = 0; x < bitmap.Width; x += sampleStep)
            {
                var color = bitmap.GetPixel(x, y);
                luminanceTotal += 0.2126 * color.Red + 0.7152 * color.Green + 0.0722 * color.Blue;
                sampleCount++;
            }
        }

        return sampleCount == 0 ? -1 : luminanceTotal / sampleCount;
    }

    public static IMediaAnalysis Analyse(string videoPath) => FFProbe.Analyse(videoPath, FfmpegOptions);
}

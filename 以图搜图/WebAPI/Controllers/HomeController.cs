using Masuit.Tools.Systems;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IO;
using Masuit.Tools.Files;
using 以图搜图.Helpers;
using 以图搜图.Models;
using 以图搜图.Services;
using 以图搜图.WebAPI.Models;
using 以图搜图.ViewModels;

namespace 以图搜图.WebAPI.Controllers;

[ApiController]
public class HomeController : Controller
{
    private const string ImageExtensionFilter = "jpg;jpeg;bmp;png;gif;webp";
    private const string VideoExtensionFilter = "3gp;avi;flv;m2ts;m4v;mkv;mov;mp4;mpeg;mpg;mts;ts;webm;wmv";
    private static readonly SemaphoreSlim SimilarSearchLock = new(1, 1);

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase) {".jpg", ".jpeg", ".bmp", ".png", ".gif", ".webp"};

    private readonly ImageIndexService _indexService = ImageIndexService.Instance;
    private readonly ImageSearchService _searchService = new ImageSearchService();
    public static MainViewModel MainViewModel { get; set; }

    /// <summary>
    /// 创建或更新索引
    /// </summary>
    /// <param name="dir">索引目录</param>
    /// <param name="removeInvalid">是否移除无效索引</param>
    /// <returns></returns>
    [HttpPatch("index")]
    public async Task<ActionResult> UpdateIndex([Required]string dir, bool removeInvalid)
    {
        MainViewModel.DirectoryPath = dir;
        MainViewModel.RemoveInvalidIndex = removeInvalid;
        await MainViewModel.UpdateIndexCommand.ExecuteAsync(this);
        return Ok("已发送指令，请查看主程序窗口");
    }

    /// <summary>
    /// 搜索图像
    /// </summary>
    /// <param name="file">需要搜索的图片</param>
    /// <param name="similar">相似度</param>
    /// <param name="algorithm">匹配算法，1：DifferenceHash，2：DctHash，4：DctHash64，7：所有</param>
    /// <param name="checkRotated">查找旋转</param>
    /// <param name="checkFlip">查找翻转</param>
    /// <returns></returns>
    [HttpPost("search")]
    public async Task<ActionResult> Search(IFormFile file, [Range(75, 100)]float similar = 75, MatchAlgorithm algorithm = MatchAlgorithm.All, bool checkRotated = true, bool checkFlip = false)
    {
        var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");
        await file.OpenReadStream().SaveFileAsync(filename);
        return Ok(await _searchService.SearchAsync(filename, _indexService.Index, algorithm, similar / 100, checkRotated, checkFlip));
    }

    /// <summary>
    /// 查找指定目录中的相似图像或视频。
    /// </summary>
    /// <param name="request">查重目录、匹配参数和模式。</param>
    /// <param name="cancellationToken">取消请求时停止查重。</param>
    /// <returns>扫描数量和相似文件对。</returns>
    [HttpPost("similar-search"), ProducesResponseType<SimilarSearchResponse>(StatusCodes.Status200OK), ProducesResponseType(StatusCodes.Status400BadRequest), ProducesResponseType(StatusCodes.Status409Conflict), ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<SimilarSearchResponse>> FindSimilar([FromBody]SimilarSearchRequest request, CancellationToken cancellationToken)
    {
        if (request is null)
        {
            return BadRequest("请求体不能为空。");
        }

        if (request.Directories is not {Length: > 0})
        {
            return BadRequest("至少需要指定一个扫描目录。");
        }

        if (!TryNormalizeDirectories(request.Directories, true, out var scanDirectories, out var directoryError))
        {
            return BadRequest(directoryError);
        }

        var excludedDirectoryInputs = request.ExcludedDirectories ?? [];
        if (!TryNormalizeDirectories(excludedDirectoryInputs, false, out var excludedDirectories, out directoryError))
        {
            return BadRequest(directoryError);
        }

        if (!await SimilarSearchLock.WaitAsync(0, cancellationToken))
        {
            return Conflict("已有相似查重任务正在运行。");
        }

        try
        {
            if (_indexService.IsIndexing)
            {
                return Conflict("索引正在更新，请等待索引完成后再查重。");
            }

            var extensionFilter = request.VideoMode ? VideoExtensionFilter : ImageExtensionFilter;
            var mediaFiles = EverythingHelper.GetFiles(scanDirectories, extensionFilter).Where(path => request.VideoMode ? VideoFrameHelper.IsVideoFile(path) : ImageExtensions.Contains(Path.GetExtension(path))).Where(path => request.IncludeSubfolders || scanDirectories.Any(directory => string.Equals(Path.GetDirectoryName(path), directory, StringComparison.OrdinalIgnoreCase))).Where(path => !IsWithinAnyDirectory(path, excludedDirectories)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();

            if (mediaFiles.Length < 2)
            {
                return BadRequest($"扫描目录中至少需要两个{(request.VideoMode ? "视频" : "图像")}文件。");
            }

            var missingFiles = mediaFiles.Where(path => !_indexService.Index.ContainsKey(path)).ToArray();
            if (missingFiles.Length > 0)
            {
                await _indexService.UpdateIndexAsync(scanDirectories, false);
                cancellationToken.ThrowIfCancellationRequested();
            }

            var unindexedFiles = mediaFiles.Where(path => !_indexService.Index.ContainsKey(path)).ToArray();
            if (unindexedFiles.Length > 0)
            {
                return UnprocessableEntity(new
                {
                    Message = "部分媒体文件未能建立索引，无法完成查重。",
                    UnindexedFileCount = unindexedFiles.Length,
                    UnindexedFiles = unindexedFiles.Take(20)
                });
            }

            var entries = mediaFiles.Select(path => _indexService.Index[path]).ToList();

            if (request.VideoMode && request.FilterShortVideos)
            {
                entries.RemoveAll(item => item.DctHash.Count < request.MinimumVideoDurationSeconds);
            }

            if (entries.Count < 2)
            {
                return BadRequest($"符合查重条件的{(request.VideoMode ? "视频" : "图像")}不足两个。");
            }

            var pairs = await _searchService.FindSimilarPairsAsync(entries, request.Algorithm, request.Similarity / 100, request.IgnoreSameFolder, cancellationToken: cancellationToken, videoMode: request.VideoMode, sampleFrames: request.SampleFrames);

            return Ok(new SimilarSearchResponse(entries.Count, pairs.Count, pairs));
        }
        finally
        {
            SimilarSearchLock.Release();
        }
    }

    private static bool TryNormalizeDirectories(IEnumerable<string> directories, bool requireExisting, out string[] normalizedDirectories, out string error)
    {
        var result = new List<string>();
        foreach (var directory in directories)
        {
            if (string.IsNullOrWhiteSpace(directory))
            {
                normalizedDirectories = [];
                error = "目录路径不能为空。";
                return false;
            }

            string fullPath;
            try
            {
                fullPath = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                normalizedDirectories = [];
                error = $"目录路径无效：{directory}";
                return false;
            }

            if (requireExisting && !Directory.Exists(fullPath))
            {
                normalizedDirectories = [];
                error = $"扫描目录不存在：{fullPath}";
                return false;
            }

            result.Add(fullPath);
        }

        normalizedDirectories = result.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        error = string.Empty;
        return true;
    }

    private static bool IsWithinAnyDirectory(string path, IEnumerable<string> directories)
    {
        var fullPath = Path.GetFullPath(path);
        return directories.Any(directory =>
        {
            var relativePath = Path.GetRelativePath(directory, fullPath);
            return relativePath == "." || (!Path.IsPathRooted(relativePath) && relativePath != ".." && !relativePath.StartsWith(".." + Path.DirectorySeparatorChar, StringComparison.Ordinal));
        });
    }
}
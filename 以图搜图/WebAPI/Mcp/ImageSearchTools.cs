using System.ComponentModel;
using System.IO;
using ModelContextProtocol.Server;
using 以图搜图.Models;
using 以图搜图.Services;

namespace 以图搜图.WebAPI.Mcp;

[McpServerToolType]
public static class ImageSearchTools
{
    private static readonly ImageIndexService IndexService = ImageIndexService.Instance;
    private static readonly ImageSearchService SearchService = new();

    [McpServerTool(Name = "get_image_index_status"), Description("获取已索引的媒体文件数量，以及索引或索引写入任务是否正在进行。")]
    public static ImageIndexStatus GetImageIndexStatus()
    {
        return new ImageIndexStatus(IndexService.Index.Count, IndexService.IsIndexing, IndexService.IsWriting);
    }

    [McpServerTool(Name = "search_similar_images"), Description("根据本机可访问的图像或视频文件，在本地索引中搜索相似媒体。")]
    public static async Task<IReadOnlyList<ImageSearchMatch>> SearchSimilarImages([Description("运行本程序的计算机可访问的图像或视频文件绝对路径。")]string imagePath, [Description("最低相似度百分比，范围为 75 到 100。")]int minimumSimilarity = 75, [Description("匹配算法，可选 all、differenceHash、dctHash32 或 dctHash64。")]string algorithm = "all", [Description("最多返回的相似结果数量，范围为 1 到 100。")]int maxResults = 50, [Description("是否查找源图像旋转后的匹配项。")]bool checkRotated = true, [Description("是否查找源图像翻转后的匹配项。")]bool checkFlipped = false)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        ArgumentException.ThrowIfNullOrWhiteSpace(algorithm);
        if (!Path.IsPathFullyQualified(imagePath))
        {
            throw new ArgumentException("图像或视频路径必须为绝对路径。", nameof(imagePath));
        }

        if (!File.Exists(imagePath))
        {
            throw new FileNotFoundException("未找到指定的图像或视频文件。", imagePath);
        }

        if (minimumSimilarity is < 75 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(minimumSimilarity), "最低相似度必须在 75 到 100 之间。");
        }

        if (maxResults is < 1 or > 100)
        {
            throw new ArgumentOutOfRangeException(nameof(maxResults), "最多返回结果数必须在 1 到 100 之间。");
        }

        var matchAlgorithm = algorithm.ToLowerInvariant() switch
        {
            "all" => MatchAlgorithm.All,
            "differencehash" => MatchAlgorithm.DifferenceHash,
            "dcthash32" => MatchAlgorithm.DctHash32,
            "dcthash64" => MatchAlgorithm.DctHash64,
            _ => throw new ArgumentException("匹配算法必须为 all、differenceHash、dctHash32 或 dctHash64。", nameof(algorithm))
        };

        var results = await SearchService.SearchAsync(imagePath, IndexService.Index, matchAlgorithm, minimumSimilarity / 100f, checkRotated, checkFlipped);

        return results.Take(maxResults).Select(result => new ImageSearchMatch(result.路径, result.匹配度 * 100, result.匹配算法, result.大小, result.所属文件夹大小, result.所属文件夹文件数)).ToArray();
    }
}

public sealed record ImageIndexStatus(int IndexedFileCount, bool IsIndexing, bool IsWriting);

public sealed record ImageSearchMatch(string Path, float SimilarityPercent, string Algorithm, string Size, string ParentFolderSize, int ParentFolderFileCount);
using Masuit.Tools.Systems;

namespace 以图搜图;

public static class PathPrefixFinder
{
    private readonly struct PathInfo(string path, string[] segments, string groupKey)
    {
        public readonly string Path = path;
        public readonly string[] Segments = segments;
        public readonly string GroupKey = groupKey;
    }

    /// <summary>
    /// 从路径列表中获取每个根路径组的最长公共路径前缀
    /// </summary>
    /// <param name="paths">路径字符串数组</param>
    /// <param name="depth">最小路径深度</param>
    /// <returns>最长公共路径前缀列表</returns>
    public static ISet<string> FindLongestCommonPathPrefixes(IEnumerable<string> paths, int depth = 2)
    {
        var results = new ConcurrentHashSet<string>();

        // 预处理所有路径，缓存路径段和分组键
        var pathInfos = paths
            .Where(path => !string.IsNullOrEmpty(path))
            .Select(path =>
            {
                var segments = GetPathSegmentsArray(path);
                var groupKey = segments.Length >= depth
                    ? BuildPathFromSegmentsSpan(segments.AsSpan(0, depth))
                    : BuildPathFromSegmentsSpan(segments.AsSpan());
                return new PathInfo(path, segments, groupKey);
            })
            .ToArray();

        if (pathInfos.Length == 0)
            return results;

        // 按分组键分组，使用更高效的分组方式
        var groups = pathInfos
            .GroupBy(info => info.GroupKey, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        // 并行处理每个组
        var parallelOptions = new ParallelOptions
        {
            MaxDegreeOfParallelism = Math.Min(Environment.ProcessorCount, groups.Length)
        };

        Parallel.ForEach(groups, parallelOptions, group =>
        {
            var groupArray = group.ToArray();

            if (groupArray.Length == 1)
            {
                // 只有一个路径，取其父目录
                var singlePath = groupArray[0].Path;
                var parentDir = Path.GetDirectoryName(singlePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    results.Add(parentDir);
                }
            }
            else
            {
                // 多个路径，找最长公共前缀，使用已缓存的segments
                var commonPrefix = FindLongestCommonPrefixOptimized(groupArray);
                if (!string.IsNullOrEmpty(commonPrefix))
                {
                    results.Add(commonPrefix);
                }
            }
        });

        return results;
    }

    /// <summary>
    /// 优化的最长公共前缀查找方法
    /// </summary>
    private static string FindLongestCommonPrefixOptimized(PathInfo[] pathInfos)
    {
        if (pathInfos.Length == 0) return string.Empty;
        if (pathInfos.Length == 1)
        {
            var parentDir = Path.GetDirectoryName(pathInfos[0].Path);
            return parentDir ?? string.Empty;
        }

        // 找到最短路径的长度
        var minLength = pathInfos.Min(info => info.Segments.Length);
        if (minLength <= 1) return string.Empty;

        // 使用第一个路径的segments作为基准
        var baseSegments = pathInfos[0].Segments;
        var commonSegmentCount = 0;

        // 逐层比较，找到公共前缀
        for (int i = 0; i < minLength - 1; i++) // -1 是为了排除文件名
        {
            var currentSegment = baseSegments[i];
            var isCommon = true;

            // 检查所有路径的当前段是否相同
            for (int j = 1; j < pathInfos.Length; j++)
            {
                if (!pathInfos[j].Segments[i].Equals(currentSegment, StringComparison.OrdinalIgnoreCase))
                {
                    isCommon = false;
                    break;
                }
            }

            if (isCommon)
            {
                commonSegmentCount++;
            }
            else
            {
                break;
            }
        }

        // 如果没有公共前缀，返回空
        if (commonSegmentCount == 0)
            return string.Empty;

        return BuildPathFromSegmentsSpan(baseSegments.AsSpan(0, commonSegmentCount));
    }

    /// <summary>
    /// 获取路径段数组（优化版本）
    /// </summary>
    private static string[] GetPathSegmentsArray(string path)
    {
        if (string.IsNullOrEmpty(path))
            return [];

        var segments = new List<string>();

        // 处理Windows路径 (C:/)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            segments.Add(path[..2]); // "C:"
            if (path.Length > 3)
            {
                var remaining = path.AsSpan(3);
                if (remaining.Length > 0)
                {
                    AddSegmentsFromSpan(segments, remaining);
                }
            }
        }
        // 处理Unix路径 (/)
        else if (path.StartsWith('/'))
        {
            if (path.Length > 1)
            {
                var remaining = path.AsSpan(1);
                if (remaining.Length > 0)
                {
                    AddSegmentsFromSpan(segments, remaining);
                }
            }
        }
        // 相对路径
        else
        {
            AddSegmentsFromSpan(segments, path.AsSpan());
        }

        return segments.ToArray();
    }

    /// <summary>
    /// 从字符串跨度添加路径段
    /// </summary>
    private static void AddSegmentsFromSpan(List<string> segments, ReadOnlySpan<char> span)
    {
        var start = 0;
        for (int i = 0; i < span.Length; i++)
        {
            if (span[i] == '/' || span[i] == '\\')
            {
                if (i > start)
                {
                    segments.Add(span[start..i].ToString());
                }
                start = i + 1;
            }
        }

        // 添加最后一段
        if (start < span.Length)
        {
            segments.Add(span[start..].ToString());
        }
    }

    /// <summary>
    /// 从路径段Span构建完整路径（优化版本）
    /// </summary>
    private static string BuildPathFromSegmentsSpan(ReadOnlySpan<string> segments)
    {
        if (segments.Length == 0) return string.Empty;

        // Windows路径
        if (segments[0].Length == 2 && char.IsLetter(segments[0][0]) && segments[0][1] == ':')
        {
            if (segments.Length == 1)
            {
                return segments[0] + "\\";
            }

            return string.Join("\\", segments.ToArray());
        }
        // Unix路径
        return "/" + string.Join("/", segments.ToArray());
    }
}
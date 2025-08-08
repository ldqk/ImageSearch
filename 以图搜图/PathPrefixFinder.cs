using Masuit.Tools.Systems;

namespace 以图搜图;

public static class PathPrefixFinder
{
    /// <summary>
    /// 从路径列表中获取每个根路径组的最长公共路径前缀
    /// </summary>
    /// <param name="paths">路径字符串数组</param>
    /// <param name="depth">最小路径深度</param>
    /// <returns>最长公共路径前缀列表</returns>
    public static ISet<string> FindLongestCommonPathPrefixes(IEnumerable<string> paths, int depth = 2)
    {
        var results = new ConcurrentHashSet<string>();
        // 按照根路径分组（前depth层）
        paths.Select(path => new { Path = path, Segments = GetPathSegments(path) }).GroupBy(item => BuildPathFromSegments(item.Segments.Take(depth).ToList())).AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 4).ForAll(group =>
        {
            // 对每个组找到最长公共前缀
            var pathsInGroup = group.Select(g => g.Path).ToList();
            if (pathsInGroup.Count == 1)
            {
                // 只有一个路径，取其父目录
                var singlePath = pathsInGroup[0];
                var parentDir = Path.GetDirectoryName(singlePath);
                if (!string.IsNullOrEmpty(parentDir))
                {
                    results.Add(parentDir);
                }
            }
            else
            {
                // 多个路径，找最长公共前缀
                var commonPrefix = FindLongestCommonPrefix(pathsInGroup);
                if (!string.IsNullOrEmpty(commonPrefix))
                {
                    results.Add(commonPrefix);
                }
            }
        });
        return results;
    }

    /// <summary>
    /// 找到多个路径的最长公共前缀目录
    /// </summary>
    private static string FindLongestCommonPrefix(List<string> paths)
    {
        if (paths.Count == 0) return string.Empty;
        if (paths.Count == 1)
        {
            var parentDir = Path.GetDirectoryName(paths[0]);
            return parentDir ?? string.Empty;
        }

        // 将所有路径转换为路径段列表
        var allSegments = paths.Select(GetPathSegments).ToList();

        // 找到最短路径的长度
        var minLength = allSegments.Min(segments => segments.Count);

        // 逐层比较，找到公共前缀
        var commonSegments = new List<string>();
        for (int i = 0; i < minLength - 1; i++) // -1 是为了排除文件名
        {
            var currentSegment = allSegments[0][i];
            if (allSegments.All(segments => segments[i].Equals(currentSegment, StringComparison.OrdinalIgnoreCase)))
            {
                commonSegments.Add(currentSegment);
            }
            else
            {
                break;
            }
        }

        // 如果没有公共前缀或公共前缀太短，返回空
        if (commonSegments.Count == 0)
            return string.Empty;

        return BuildPathFromSegments(commonSegments);
    }

    /// <summary>
    /// 获取路径段列表
    /// </summary>
    private static List<string> GetPathSegments(string path)
    {
        var segments = new List<string>();

        // 处理Windows路径 (C:/)
        if (path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':')
        {
            segments.Add(path[..2]); // "C:"
            if (path.Length > 3)
            {
                var remaining = path[3..];
                if (!string.IsNullOrEmpty(remaining))
                {
                    segments.AddRange(remaining.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }
        // 处理Unix路径 (/)
        else if (path.StartsWith("/"))
        {
            if (path.Length > 1)
            {
                var remaining = path[1..];
                if (!string.IsNullOrEmpty(remaining))
                {
                    segments.AddRange(remaining.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
                }
            }
        }
        // 相对路径
        else
        {
            segments.AddRange(path.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries));
        }

        return segments;
    }

    /// <summary>
    /// 从路径段构建完整路径
    /// </summary>
    private static string BuildPathFromSegments(IList<string> segments)
    {
        if (segments.Count == 0) return string.Empty;

        // Windows路径
        if (segments[0].Length == 2 && char.IsLetter(segments[0][0]) && segments[0][1] == ':')
        {
            if (segments.Count == 1)
            {
                return segments[0] + "\\";
            }

            return string.Join("\\", segments);
        }
        // Unix路径
        return "/" + string.Join("/", segments);
    }
}
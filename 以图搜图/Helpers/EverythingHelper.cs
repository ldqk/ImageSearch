using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace 以图搜图;

public static class EverythingHelper
{
    private static readonly Lock QueryLock = new();

    // 导入Everything DLL的方法
    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern uint Everything_SetSearch(string lpSearchString);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_GetResultFullPathName(uint index, StringBuilder path, uint length);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern bool Everything_Query(bool wait);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern bool Everything_GetResultSize(uint index, out ulong size);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    private static extern void Everything_SetMax(uint dwMaxResults);

    private static bool IsEverythingAvailable =>
        File.Exists("Everything64.dll") && Process.GetProcessesByName("Everything").Length > 0;

    private static bool TryFindFile(string filePath, out ulong? fileSize)
    {
        fileSize = null;
        if (!IsEverythingAvailable || string.IsNullOrWhiteSpace(filePath))
        {
            return false;
        }

        string fullPath;
        try
        {
            fullPath = Path.GetFullPath(filePath);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
        {
            return false;
        }

        lock (QueryLock)
        {
            Everything_SetMax(uint.MaxValue);
            Everything_SetSearch($"file:\"{fullPath}\"");
            if (!Everything_Query(true))
            {
                return false;
            }

            var numResults = Everything_GetNumResults();
            var path = new StringBuilder(4096);
            for (uint i = 0; i < numResults; i++)
            {
                path.Clear();
                Everything_GetResultFullPathName(i, path, 4096);
                if (string.Equals(path.ToString(), fullPath, StringComparison.OrdinalIgnoreCase))
                {
                    if (Everything_GetResultSize(i, out var size))
                    {
                        fileSize = size;
                    }

                    return true;
                }
            }
        }

        return false;
    }

    public const string Extensions = "jpg;jpeg;bmp;png;gif;webp;3gp;avi;flv;m2ts;m4v;mkv;mov;mp4;mpeg;mpg;mts;ts;webm;wmv";
    private static string[] EnumerateFiles(string directoryPath, string extFilter = Extensions)
    {
        string search = $"file:\"{directoryPath}\" ext:{extFilter}"; // 仅文件，并限制路径

        lock (QueryLock)
        {
            Everything_SetMax(uint.MaxValue);
            Everything_SetSearch(search);
            Everything_Query(true); // 执行搜索
            uint numResults = Everything_GetNumResults();
            StringBuilder path = new StringBuilder(4096); // 根据需要调整长度
            var files = new List<string>();

            for (uint i = 0; i < numResults; i++)
            {
                path.Clear();
                Everything_GetResultFullPathName(i, path, 4096);
                files.Add(path.ToString());
            }

            return files.ToArray();
        }
    }

    /// <summary>
    /// Determines whether the specified file exists, using Everything when available.
    /// </summary>
    public static bool FileExists(string filePath)
    {
        return TryFindFile(filePath, out _) || File.Exists(filePath);
    }

    /// <summary>
    /// Gets the specified file's size in bytes, using Everything when available.
    /// </summary>
    public static long GetFileSize(string filePath)
    {
        if (TryFindFile(filePath, out var size) && size is { } fileSize)
        {
            return checked((long)fileSize);
        }

        return new FileInfo(filePath).Length;
    }

    public static string[] GetFiles(string[] directories, string extFilter = Extensions)
    {
        if (File.Exists("Everything64.dll") && Process.GetProcessesByName("Everything").Length > 0)
        {
            return directories.SelectMany(s =>
            {
                var array = EnumerateFiles(s, extFilter);
                return array.Length == 0 ? Directory.GetFiles(s, "*", SearchOption.AllDirectories) : array;
            }).ToArray();
        }

        return directories.SelectMany(static s =>
        {
            try
            {
                return Directory.GetFiles(s, "*", SearchOption.AllDirectories);
            }
            catch
            {
                return [];
            }
        }).ToArray();
    }

    public static string[] GetFiles(string directory)
    {
        if (File.Exists("Everything64.dll") && Process.GetProcessesByName("Everything").Length > 0)
        {
            var array = EnumerateFiles(directory);
            return array.Length == 0 ? Directory.GetFiles(directory, "*", SearchOption.AllDirectories) : array;
        }

        try
        {
            return Directory.GetFiles(directory, "*", SearchOption.AllDirectories);
        }
        catch
        {
            return [];
        }
    }
}
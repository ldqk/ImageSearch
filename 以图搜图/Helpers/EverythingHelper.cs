using Masuit.Tools;
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
    private static extern void Everything_SetMax(uint dwMaxResults);

    public const string Extensions = "jpg;jpeg;bmp;png;gif;webp;3gp;avi;flv;m2ts;m4v;mkv;mov;mp4;mpeg;mpg;mts;webm;wmv";
    private static List<string> EnumerateFiles(string directoryPath, string extFilter = Extensions)
    {
        directoryPath= directoryPath.EndsWith('\\') ? directoryPath : directoryPath + '\\';
        string search = $"file:\"{directoryPath}\" ext:{extFilter}"; // 仅文件，并限制路径

        try
        {
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

                return files;
            }
        }
        catch (Exception e)
        {
            var exts = extFilter.Split(';');
            return Directory.EnumerateFiles(directoryPath, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(exts)).ToList();
        }
    }

    public static string[] GetFiles(string[] directories, string extFilter = Extensions)
    {
        var exts = extFilter.Split(';');
        return directories.SelectMany(s =>
        {
            var array = EnumerateFiles(s, extFilter);
            try
            {
                return array.Count == 0 ? Directory.EnumerateFiles(s, "*", SearchOption.AllDirectories).Where(f => f.EndsWith(exts)) : array;
            }
            catch
            {
                return [];
            }
        }).ToArray();
    }
}
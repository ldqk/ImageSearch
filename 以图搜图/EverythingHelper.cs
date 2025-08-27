using System.Runtime.InteropServices;
using System.Text;

namespace 以图搜图;

public class EverythingHelper
{
    // 导入Everything DLL的方法
    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern uint Everything_SetSearch(string lpSearchString);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern void Everything_GetResultFullPathName(uint index, StringBuilder path, uint length);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern bool Everything_Query(bool wait);

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern uint Everything_GetNumResults();

    [DllImport("Everything64.dll", CharSet = CharSet.Unicode)]
    public static extern void Everything_SetMax(uint dwMaxResults);

    static EverythingHelper()
    {
        Everything_SetMax(uint.MaxValue);
    }

    public static IEnumerable<string> EnumerateFiles(string directoryPath, string extFilter = "jpg;jpeg;bmp;png;gif")
    {
        string search = $"file:\"{directoryPath}\" ext:{extFilter}"; // 仅文件，并限制路径

        Everything_SetSearch(search);
        Everything_Query(true); // 执行搜索
        uint numResults = Everything_GetNumResults();
        StringBuilder path = new StringBuilder(4096); // 根据需要调整长度

        for (uint i = 0; i < numResults; i++)
        {
            Everything_GetResultFullPathName(i, path, 4096);
            yield return path.ToString();
        }
    }
}
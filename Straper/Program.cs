using Microsoft.Win32;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;
using System.Text.RegularExpressions;

Console.WriteLine("欢迎使用清除图像exif信息小工具——ExifStraper by 懒得勤快\n\n");
var dirs = new List<string>();
if (args.Length > 0)
{
    if (args[0] == "reg-menu")
    {
        RegContextMenu();
        return;
    }
    dirs.AddRange(args);
}
else
{
    var dir = "";
    while (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
    {
        Console.WriteLine("请将待处理文件夹拖放到此处：");
        dir = Console.ReadLine().Trim('"');
        if (dir == "reg-menu")
        {
            RegContextMenu();
            return;
        }
    }
    dirs.Add(dir);
}

Console.WriteLine("正在读取文件目录树......");
dirs.SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)).Where(s => Regex.IsMatch(s, "(jpg|jpeg|bmp)$", RegexOptions.IgnoreCase)).Chunk(32).AsParallel().ForAll(files =>
{
    foreach (var file in files)
    {
        Console.WriteLine("正在处理：" + file);
        try
        {
            bool modified = false;
            using var image = Image.Load<Rgba64>(file);
            if (image.Metadata.ExifProfile != null)
            {
                foreach (var exifValue in image.Metadata.ExifProfile.Values)
                {
                    if (exifValue.Tag is ExifTag<string> && exifValue.TrySetValue(null))
                    {
                        modified = true;
                    }
                }
            }

            if (image.Metadata.IptcProfile != null)
            {
                image.Metadata.IptcProfile = null;
                modified = true;
            }

            if (image.Metadata.XmpProfile != null)
            {
                image.Metadata.XmpProfile = null;
                modified = true;
            }

            if (modified)
            {
                image.Save(file);
            }
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("图像：" + file + " 不受支持");
        }
    }
});

void RegContextMenu()
{
    var key = Registry.ClassesRoot.OpenSubKey("Directory", true).OpenSubKey("shell", true).CreateSubKey("ExifStraper", true);
    var command = key.CreateSubKey("command", true);
    key.SetValue("Icon", "%SystemRoot%\\System32\\shell32.dll,141", RegistryValueKind.ExpandString);
    key.SetValue("MUIVerb", "ExifStraper");
    command.SetValue("", $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName}\" \"%1\"", RegistryValueKind.ExpandString);
    var key2 = Registry.ClassesRoot.OpenSubKey("*", true).OpenSubKey("shell", true).CreateSubKey("ExifStraper", true);
    var command2 = key2.CreateSubKey("command", true);
    key2.SetValue("Icon", "%SystemRoot%\\System32\\shell32.dll,141", RegistryValueKind.ExpandString);
    key2.SetValue("MUIVerb", "ExifStraper");
    command2.SetValue("", $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName}\" \"%1\"", RegistryValueKind.ExpandString);
    Console.WriteLine("右键菜单添加成功");
}

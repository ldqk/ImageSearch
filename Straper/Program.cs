using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using ExifLibrary;
using System.Text.RegularExpressions;
using SearchOption = System.IO.SearchOption;

AppDomain.CurrentDomain.UnhandledException += (sender, eventArgs) =>
{
    Console.Error.WriteLine("发生未处理异常：" + eventArgs.ExceptionObject);
};

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
var temp = dirs.SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)).Where(s => Regex.IsMatch(s, "(jpg|jpeg)$", RegexOptions.IgnoreCase)).ToList();
var tagsToRemove = new[] {
    ExifTag.ImageDescription,
    ExifTag.Artist,
    ExifTag.Copyright,
    ExifTag.WindowsTitle,
    ExifTag.WindowsComment,
    ExifTag.WindowsAuthor,
    ExifTag.WindowsSubject,
    ExifTag.WindowsKeywords,
    ExifTag.GPSAltitude,
    ExifTag.GPSAltitudeRef,
    ExifTag.GPSAreaInformation,
    ExifTag.GPSDateStamp,
    ExifTag.GPSDestBearing,
    ExifTag.GPSDestBearingRef,
    ExifTag.GPSDestDistance,
    ExifTag.GPSDestDistanceRef,
    ExifTag.GPSDestLatitude,
    ExifTag.GPSDestLatitudeRef,
    ExifTag.GPSDestLongitude,
    ExifTag.GPSDestLongitudeRef,
    ExifTag.GPSDifferential,
    ExifTag.GPSIFDPointer,
    ExifTag.GPSImgDirection,
    ExifTag.GPSImgDirectionRef,
    ExifTag.GPSMapDatum,
    ExifTag.GPSLatitude,
    ExifTag.GPSLatitudeRef,
    ExifTag.GPSLongitude,
    ExifTag.GPSLongitudeRef,
    ExifTag.GPSMeasureMode,
    ExifTag.GPSProcessingMethod,
    ExifTag.GPSSatellites,
    ExifTag.GPSSpeed,
    ExifTag.GPSSpeedRef,
    ExifTag.GPSStatus,
    ExifTag.GPSTimeStamp,
    ExifTag.GPSTrack,
    ExifTag.GPSTrackRef,
    ExifTag.GPSVersionID
};
Parallel.ForEach(temp,new ParallelOptions(){MaxDegreeOfParallelism = Environment.ProcessorCount*4}, file =>
{
    try
    {
        var image = ImageFile.FromFile(file);
        var changed = false;
        if (args.Contains("-a"))
        {
            var propertiesToRemove = image.Properties.Where(property => property.Interoperability.TypeID is InterOpType.ASCII or InterOpType.BYTE or InterOpType.UNDEFINED).ToList();
            foreach (var property in propertiesToRemove)
            {
                changed |= image.Properties.Remove(property);
            }
        }
        else
        {
            foreach (var tag in tagsToRemove.Where(tag => image.Properties.Get(tag) != null))
            {
                image.Properties.Remove(tag);
                changed = true;
            }
        }

        if (changed)
        {
            image.Save(file);
        }
    }
    catch (Exception e)
    {
        Console.Error.WriteLine($"图像：{file} 不受支持：{e.Message}");
    }
});

var alldirs = dirs.Union(dirs.SelectMany(dir => Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))).ToList();
foreach (var dir in alldirs)
{
    var newName = ChineseConverter.Convert(dir, ChineseConversionDirection.TraditionalToSimplified);
    if (dir != newName)
    {
        FileSystem.MoveDirectory(dir, newName);
    }
}

void RegContextMenu()
{
    using var key = Registry.ClassesRoot.OpenSubKey("Directory", true).OpenSubKey("shell", true).CreateSubKey("ExifStraper", true);
    using var command = key.CreateSubKey("command", true);
    key.SetValue("Icon", "%SystemRoot%\\System32\\shell32.dll,141", RegistryValueKind.ExpandString);
    key.SetValue("MUIVerb", "ExifStraper");
    command.SetValue("", $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName}\" \"%1\"", RegistryValueKind.ExpandString);
    using var key2 = Registry.ClassesRoot.OpenSubKey("*", true).OpenSubKey("shell", true).CreateSubKey("ExifStraper", true);
    using var command2 = key2.CreateSubKey("command", true);
    key2.SetValue("Icon", "%SystemRoot%\\System32\\shell32.dll,141", RegistryValueKind.ExpandString);
    key2.SetValue("MUIVerb", "ExifStraper");
    command2.SetValue("", $"\"{System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName}\" \"%1\"", RegistryValueKind.ExpandString);
    Console.WriteLine("右键菜单添加成功");
}
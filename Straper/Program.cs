using Microsoft.International.Converters.TraditionalChineseToSimplifiedConverter;
using Microsoft.VisualBasic.FileIO;
using Microsoft.Win32;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
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
var temp = dirs.SelectMany(dir => Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories)).Where(s => Regex.IsMatch(s, "(jpg|jpeg|bmp)$", RegexOptions.IgnoreCase)).ToList();
var count = temp.Count;
int index = 1;
temp.Chunk(Environment.ProcessorCount * 2).AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 2).ForAll(files =>
{
    foreach (var file in files)
    {
        Console.WriteLine($"正在处理[{index++}/{count}]：{file}");
        try
        {
            var bools = new HashSet<bool>() { false };
            using var image = Image.Load<Rgba64>(file);
            if (image.Metadata.ExifProfile != null)
            {
                if (args.Contains("-a"))
                {
                    foreach (var exifValue in image.Metadata.ExifProfile.Values)
                    {
                        if (exifValue.DataType is ExifDataType.Ascii or ExifDataType.Byte or ExifDataType.Undefined or ExifDataType.Unknown && image.Metadata.ExifProfile.RemoveValue(exifValue.Tag))
                        {
                            bools.Add(true);
                        }
                    }
                }
                else
                {
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.ImageDescription));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.Artist));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.Copyright));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.XPTitle));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.XPComment));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.XPAuthor));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.XPSubject));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.XPKeywords));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSAltitude));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSAltitudeRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSAreaInformation));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDateStamp));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestBearing));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestBearingRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestDistance));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestDistanceRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLatitude));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLatitudeRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLongitude));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLongitudeRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDifferential));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSIFDOffset));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSImgDirection));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSImgDirectionRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSMapDatum));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLatitude));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLatitudeRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLongitude));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLongitudeRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSMeasureMode));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSProcessingMethod));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSSatellites));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSSpeed));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSSpeedRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSStatus));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSTimestamp));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSTrack));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSTrackRef));
                    bools.Add(image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSVersionID));
                }
            }

            if (image.Metadata.IptcProfile != null)
            {
                image.Metadata.IptcProfile = null;
                bools.Add(true);
            }

            if (image.Metadata.XmpProfile != null)
            {
                image.Metadata.XmpProfile = null;
                bools.Add(true);
            }

            if (bools.Count == 2)
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

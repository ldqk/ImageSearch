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
                if (args.Contains("-a"))
                {
                    foreach (var exifValue in image.Metadata.ExifProfile.Values)
                    {
                        if (exifValue.DataType is ExifDataType.Ascii or ExifDataType.Byte or ExifDataType.Undefined or ExifDataType.Unknown && image.Metadata.ExifProfile.RemoveValue(exifValue.Tag))
                        {
                            modified = true;
                        }
                    }
                }
                else
                {
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.ImageDescription);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.Artist);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.Copyright);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.XPTitle);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.XPComment);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.XPAuthor);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.XPSubject);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.XPKeywords);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSAltitude);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSAltitudeRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSAreaInformation);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDateStamp);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestBearing);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestBearingRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestDistance);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestDistanceRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLatitude);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLatitudeRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLongitude);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDestLongitudeRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSDifferential);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSIFDOffset);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSImgDirection);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSImgDirectionRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSMapDatum);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLatitude);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLatitudeRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLongitude);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSLongitudeRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSMeasureMode);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSProcessingMethod);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSSatellites);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSSpeed);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSSpeedRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSStatus);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSTimestamp);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSTrack);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSTrackRef);
                    image.Metadata.ExifProfile.RemoveValue(ExifTag.GPSVersionID);
                    modified = true;
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

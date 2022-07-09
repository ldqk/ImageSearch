using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Metadata.Profiles.Exif;
using SixLabors.ImageSharp.PixelFormats;

Console.WriteLine("欢迎使用清除图像exif信息小工具——ExifStraper by 懒得勤快\n\n");
var dirs = new List<string>();
if (args.Length > 0)
{
    dirs.AddRange(args);
}
else
{
    var dir = "";
    while (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
    {
        Console.WriteLine("请将待处理文件夹拖放到此处：");
        dir = Console.ReadLine().Trim('"');
    }
    dirs.Add(dir);
}

Console.WriteLine("正在读取文件目录树......");
dirs.SelectMany(dir => Directory.GetFiles(dir, "*", SearchOption.AllDirectories)).Chunk(32).AsParallel().ForAll(files =>
{
    foreach (var file in files)
    {
        Console.WriteLine("正在处理：" + file);
        try
        {
            using var image = Image.Load<Rgba64>(file);
            if (image.Metadata.ExifProfile != null)
            {
                foreach (var exifValue in image.Metadata.ExifProfile.Values)
                {
                    if (exifValue.Tag is ExifTag<string>)
                    {
                        exifValue.TrySetValue(null);
                    }
                }
            }

            image.Metadata.IptcProfile = null;
            image.Metadata.XmpProfile = null;
            image.Save(file);
        }
        catch (Exception e)
        {
            Console.Error.WriteLine("图像：" + file + " 不受支持");
        }
    }
});

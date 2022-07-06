using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;

Console.WriteLine("欢迎使用清除图像exif信息小工具——ExifStraper by 懒得勤快\n\n");
string dir = "";
if (args.Length > 0)
{
    dir = args[0];
}
else
{
    while (string.IsNullOrWhiteSpace(dir) || !Directory.Exists(dir))
    {
        Console.WriteLine("请将待处理文件夹拖放到此处：");
        dir = Console.ReadLine().Trim('"');
    }
}

Console.WriteLine("正在读取文件目录树......");
Directory.GetFiles(dir, "*", SearchOption.AllDirectories).Chunk(32).AsParallel().ForAll(files =>
{
    foreach (var file in files)
    {
        Console.WriteLine("正在处理：" + file);
        try
        {
            using var image = Image.Load<Rgba64>(file);
            image.Metadata.ExifProfile = null;
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

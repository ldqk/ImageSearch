using Masuit.Tools;
using Masuit.Tools.Media;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.PixelFormats;
using SixLabors.ImageSharp.Processing;
using System.Collections.Concurrent;
using System.IO;
using 以图搜图.Models;
using Size = SixLabors.ImageSharp.Size;

namespace 以图搜图.Services;

public class ImageSearchService
{
    public async Task<List<SearchResult>> SearchAsync(string filename, ConcurrentDictionary<string, ulong[]> index, ConcurrentDictionary<string, List<ulong[]>> frameIndex, float similarity, bool checkRotated, bool checkFlipped)
    {
        return await Task.Run(() =>
        {
            var hasher = new ImageHasher();
            var hashs = new ConcurrentBag<ulong[]>();
            var actions = new List<Action>();

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                using (var gif = Image.Load<L8>(new DecoderOptions
                {
                    SkipMetadata = true,
                    TargetSize = new Size(144)
                }, filename))
                {
                    for (var i = 0; i < gif.Frames.Count; i++)
                    {
                        var frame = gif.Frames.ExportFrame(i);
                        actions.Add(() =>
                        {
                            hashs.Add(frame.DifferenceHash256());
                            frame.Dispose();
                        });
                    }

                    Parallel.Invoke(actions.ToArray());
                }
            }
            else
            {
                hashs.Add(hasher.DifferenceHash256(filename));

                using (var image = Image.Load<L8>(new DecoderOptions
                {
                    SkipMetadata = true,
                    TargetSize = new Size(144)
                }, filename))
                {
                    if (checkRotated)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Rotate(90));
                            hashs.Add(clone.DifferenceHash256());
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Rotate(180));
                            hashs.Add(clone.DifferenceHash256());
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Rotate(270));
                            hashs.Add(clone.DifferenceHash256());
                        });
                    }

                    if (checkFlipped)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Flip(FlipMode.Horizontal));
                            hashs.Add(clone.DifferenceHash256());
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Flip(FlipMode.Vertical));
                            hashs.Add(clone.DifferenceHash256());
                        });
                    }

                    Parallel.Invoke(actions.ToArray());
                }
            }

            var list = new List<SearchResult>();

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 4).Select(x => new SearchResult
                {
                    路径 = x.Key,
                    匹配度 = x.Value.SelectMany(h => hashs.Select(hh => ImageHasher.Compare(h, hh))).Where(f => f >= similarity).OrderDescending().Take(10).DefaultIfEmpty().Average()
                }).Where(x => x.匹配度 >= similarity));
            }
            else
            {
                list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 4).Select(x => new SearchResult
                {
                    路径 = x.Key,
                    匹配度 = x.Value.SelectMany(h => hashs.Select(hh => ImageHasher.Compare(h, hh))).Max()
                }).Where(x => x.匹配度 >= similarity));

                list.AddRange(index.AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 4).Select(x => new SearchResult
                {
                    路径 = x.Key,
                    匹配度 = hashs.Select(h => ImageHasher.Compare(x.Value, h)).Max()
                }).Where(x => x.匹配度 >= similarity));
            }

            list = list.OrderByDescending(a => a.匹配度).ToList();

            var dic = list.Where(e => File.Exists(e.路径)).GroupBy(r => new FileInfo(r.路径).DirectoryName).Where(g => g.Key != null).AsParallel().WithDegreeOfParallelism(Environment.ProcessorCount * 4).Select(g =>
            {
                var files = new DirectoryInfo(g.Key!).GetFiles("*.*", SearchOption.AllDirectories);
                return new
                {
                    Key = g.Key!,
                    files.Length,
                    Size = files.Sum(s => s.Length) / 1048576f
                };
            }).ToDictionary(a => a.Key);

            list.Where(e => File.Exists(e.路径)).ForEach(result =>
            {
                var file = new FileInfo(result.路径);
                result.大小 = $"{file.Length / 1024}KB";
                var dirName = file.DirectoryName!;
                if (dic.ContainsKey(dirName))
                {
                    result.所属文件夹文件数 = dic[dirName].Length;
                    result.所属文件夹大小 = $"{dic[dirName].Size:F2}MB";
                }
            });

            return list;
        });
    }
}
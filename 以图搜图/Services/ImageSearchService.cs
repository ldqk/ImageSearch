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
    public async Task<List<SearchResult>> SearchAsync(string filename, ConcurrentDictionary<string, IndexItem> index, ConcurrentDictionary<string, FrameIndexItem> frameIndex, MatchAlgorithm algorithm, float similarity, bool checkRotated, bool checkFlipped)
    {
        var parallelism = Environment.ProcessorCount * 4;
        return await Task.Run(() =>
        {
            var defHashs = new ConcurrentBag<ulong[]>();
            var dctHashs = new ConcurrentBag<ulong>();
            var actions = new List<Action>();

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                using (var gif = Image.Load<L8>(new DecoderOptions
                {
                    SkipMetadata = true,
                    TargetSize = new Size(160)
                }, filename))
                {
                    for (var i = 0; i < gif.Frames.Count; i++)
                    {
                        var frame = gif.Frames.ExportFrame(i);
                        actions.Add(() =>
                        {
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(frame.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                            {
                                dctHashs.Add(frame.DctHash());
                            }
                            frame.Dispose();
                        });
                    }

                    Parallel.Invoke(actions.ToArray());
                }
            }
            else
            {
                using (var image = Image.Load<L8>(new DecoderOptions
                {
                    SkipMetadata = true,
                    TargetSize = new Size(160)
                }, filename))
                {
                    if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                    {
                        defHashs.Add(image.DifferenceHash256());
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                    {
                        dctHashs.Add(image.DctHash());
                    }
                    if (checkRotated)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Rotate(90));
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                            {
                                dctHashs.Add(clone.DctHash());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Rotate(180));
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                            {
                                dctHashs.Add(clone.DctHash());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Rotate(270));
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                            {
                                dctHashs.Add(clone.DctHash());
                            }
                        });
                    }

                    if (checkFlipped)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Flip(FlipMode.Horizontal));
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                            {
                                dctHashs.Add(clone.DctHash());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Clone(c => c.Flip(FlipMode.Vertical));
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                            {
                                dctHashs.Add(clone.DctHash());
                            }
                        });
                    }

                    Parallel.Invoke(actions.ToArray());
                }
            }

            var list = new List<SearchResult>();

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                {
                    list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(parallelism).Select(x => new SearchResult
                    {
                        路径 = x.Key,
                        匹配度 = x.Value.DifferenceHash.SelectMany(h => defHashs.Select(hh => ImageHasher.Compare(h, hh))).Where(f => f >= similarity).OrderDescending().Take(10).DefaultIfEmpty().Average(),
                        匹配算法 = "DifferenceHash"
                    }).Where(x => x.匹配度 >= similarity));
                }

                if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                {
                    var sim = Math.Max(0.85, similarity);
                    list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(parallelism).Select(x => new SearchResult
                    {
                        路径 = x.Key,
                        匹配度 = x.Value.DctHash.SelectMany(h => dctHashs.Select(hh => ImageHasher.Compare(h, hh))).Where(f => f >= sim).OrderDescending().Take(10).DefaultIfEmpty().Average(),
                        匹配算法 = "DctHash"
                    }).Where(x => x.匹配度 >= sim));
                }
            }
            else
            {
                if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                {
                    list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(parallelism).Select(x => new SearchResult
                    {
                        路径 = x.Key,
                        匹配度 = x.Value.DifferenceHash.SelectMany(h => defHashs.Select(hh => ImageHasher.Compare(h, hh))).Max(),
                        匹配算法 = "DifferenceHash"
                    }).Where(x => x.匹配度 >= similarity));

                    list.AddRange(index.AsParallel().WithDegreeOfParallelism(parallelism).Select(x => new SearchResult
                    {
                        路径 = x.Key,
                        匹配度 = defHashs.Select(h => ImageHasher.Compare(x.Value.DifferenceHash, h)).Max(),
                        匹配算法 = "DifferenceHash"
                    }).Where(x => x.匹配度 >= similarity));
                }

                if (algorithm.HasFlag(MatchAlgorithm.DctHash))
                {
                    var sim = Math.Max(0.85, similarity);
                    list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(parallelism).Select(x => new SearchResult
                    {
                        路径 = x.Key,
                        匹配度 = x.Value.DctHash.SelectMany(h => dctHashs.Select(hh => ImageHasher.Compare(h, hh))).Max(),
                        匹配算法 = "DctHash"
                    }).Where(x => x.匹配度 >= sim));

                    list.AddRange(index.AsParallel().WithDegreeOfParallelism(parallelism).Select(x => new SearchResult
                    {
                        路径 = x.Key,
                        匹配度 = dctHashs.Select(h => ImageHasher.Compare(x.Value.DctHash, h)).Max(),
                        匹配算法 = "DctHash"
                    }).Where(x => x.匹配度 >= sim));
                }
            }

            if (algorithm != MatchAlgorithm.All)
            {
                var algorithmName = algorithm.ToString();
                list = list.Where(item => string.Equals(item.匹配算法, algorithmName, StringComparison.Ordinal)).ToList();
            }

            list = list.OrderByDescending(a => a.匹配度).DistinctBy(e => e.路径).ToList();

            var dic = list.Where(e => File.Exists(e.路径)).GroupBy(r => new FileInfo(r.路径).DirectoryName).Where(g => g.Key != null).AsParallel().WithDegreeOfParallelism(parallelism).Select(g =>
            {
                var files = new DirectoryInfo(g.Key!).GetFiles("*.*", SearchOption.AllDirectories);
                return new
                {
                    Key = g.Key!,
                    files.Length,
                    Size = files.Sum(s => s.Length) / 1048576f
                };
            }).ToDictionary(a => a.Key);

            list.Where(e => File.Exists(e.路径)).OrderBy(e => e.路径).ForEach(result =>
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
using System.Collections;
using Masuit.Tools;
using Masuit.Tools.Media;
using SkiaSharp;
using System.Collections.Concurrent;
using System.IO;
using 以图搜图.Models;

namespace 以图搜图.Services;

public class ImageSearchService
{
    /// <summary>
    /// 查找并生成相似文件组
    /// </summary>
    /// <param name="entries">需要查找相似的文件hash项目</param>
    /// <param name="algorithm">匹配算法</param>
    /// <param name="similarity">最小相似度</param>
    /// <param name="ignoreSameFolder">忽略同一文件夹下的相似</param>
    /// <param name="progress">进度报告</param>
    /// <param name="cancellationToken"></param>
    /// <returns></returns>
    public Task<List<SimilarImagePair>> FindSimilarPairsAsync(List<IndexItem> entries, MatchAlgorithm algorithm, float similarity, bool ignoreSameFolder, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        return Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var useDifferenceHash = algorithm.HasFlag(MatchAlgorithm.DifferenceHash);
            var useDctHash32 = algorithm.HasFlag(MatchAlgorithm.DctHash32);
            var useDctHash64 = algorithm.HasFlag(MatchAlgorithm.DctHash64);
            var canUseCandidateIndex = (!useDifferenceHash || HashCandidateIndex.MinimumSharedBuckets(256, 32, similarity) > 0) && (!(useDctHash32 || useDctHash64) || HashCandidateIndex.MinimumSharedBuckets(64, 8, similarity) > 0);
            var candidateIndex = canUseCandidateIndex ? HashCandidateIndex.Build(entries, algorithm, cancellationToken) : null;
            var folderIds = ignoreSameFolder ? BuildFolderIds(entries, cancellationToken) : null;
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Environment.ProcessorCount,
                CancellationToken = cancellationToken
            };

            var resultBatches = new ConcurrentBag<List<SimilarImagePair>>();
            var completed = 0;
            Parallel.ForEach(Partitioner.Create(0, entries.Count), parallelOptions, () => new PairSearchWorker(entries.Count, canUseCandidateIndex), (range, _, worker) =>
                {
                    for (var position = range.Item1; position < range.Item2; position++)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        var source = entries[position];
                        var candidates = worker.CandidateCollector;
                        if (candidates != null)
                        {
                            candidates.BeginSource();
                            candidateIndex!.AddCandidates(source, similarity, candidates, cancellationToken);
                        }

                        var candidatesExamined = 0;
                        void CompareCandidate(int candidatePosition)
                        {
                            if ((candidatesExamined++ & 0xFFF) == 0)
                            {
                                cancellationToken.ThrowIfCancellationRequested();
                            }

                            if (candidatePosition <= position)
                            {
                                return;
                            }

                            var candidate = entries[candidatePosition];
                            if (folderIds != null && folderIds[position] == folderIds[candidatePosition])
                            {
                                return;
                            }

                            var bestMatch = 0f;
                            var bestAlgorithms = (MatchAlgorithm) 0;
                            if (useDifferenceHash)
                            {
                                UpdateBestMatch(MaxCompare(source.DifferenceHash, candidate.DifferenceHash), MatchAlgorithm.DifferenceHash);
                            }

                            if (useDctHash32)
                            {
                                UpdateBestMatch(MaxCompare(source.DctHash, candidate.DctHash), MatchAlgorithm.DctHash32);
                            }

                            if (useDctHash64)
                            {
                                UpdateBestMatch(MaxCompare(source.DctHash64, candidate.DctHash64), MatchAlgorithm.DctHash64);
                            }

                            if (bestMatch >= similarity)
                            {
                                worker.Pairs.Add(new SimilarImagePair(source.FilePath, candidate.FilePath, bestMatch, bestAlgorithms));
                            }

                            void UpdateBestMatch(float match, MatchAlgorithm matchAlgorithm)
                            {
                                if (match > bestMatch)
                                {
                                    bestMatch = match;
                                    bestAlgorithms = matchAlgorithm;
                                }
                                else if (Math.Abs(match - bestMatch) < 1e-2 && match > 0)
                                {
                                    bestAlgorithms |= matchAlgorithm;
                                }
                            }
                        }

                        if (candidates != null)
                        {
                            foreach (var candidatePosition in candidates.Candidates)
                            {
                                CompareCandidate(candidatePosition);
                            }
                        }
                        else
                        {
                            for (var candidatePosition = position + 1; candidatePosition < entries.Count; candidatePosition++)
                            {
                                CompareCandidate(candidatePosition);
                            }
                        }

                        var current = Interlocked.Increment(ref completed);
                        if (current % 100 == 0 || current == entries.Count)
                        {
                            progress?.Report(current);
                        }
                    }

                    return worker;
                }, worker => resultBatches.Add(worker.Pairs));
            return resultBatches.SelectMany(batch => batch).ToList();
        }, cancellationToken);
    }

    private static int[] BuildFolderIds(List<IndexItem> entries, CancellationToken cancellationToken)
    {
        var folderIds = new int[entries.Count];
        var idsByFolder = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var position = 0; position < entries.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var folder = Path.GetDirectoryName(entries[position].FilePath);
            if (folder == null)
            {
                folderIds[position] = -1;
                continue;
            }

            if (!idsByFolder.TryGetValue(folder, out var folderId))
            {
                folderId = idsByFolder.Count;
                idsByFolder.Add(folder, folderId);
            }

            folderIds[position] = folderId;
        }

        return folderIds;
    }

    private sealed class PairSearchWorker(int entryCount, bool useCandidateIndex)
    {
        public HashCandidateIndex.CandidateCollector? CandidateCollector { get; } =
            useCandidateIndex ? new HashCandidateIndex.CandidateCollector(entryCount) : null;

        public List<SimilarImagePair> Pairs { get; } = [];
    }

    public async Task<List<SearchResult>> SearchAsync(string filename, ConcurrentDictionary<string, IndexItem> index, MatchAlgorithm algorithm, float similarity, bool checkRotated, bool checkFlipped)
    {
        var parallelism = Environment.ProcessorCount * 4;
        return await Task.Run(() =>
        {
            var defHashs = new ConcurrentBag<ulong[]>();
            var dctHashs = new ConcurrentBag<ulong>();
            var pHashs = new ConcurrentBag<ulong>();
            var actions = new List<Action>();

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                using var frames = new DisposeCollection<SKBitmap>(SkiaImageHelper.DecodeGrayFrames(filename, 160));
                foreach (var frame in frames.Items)
                {
                    actions.Add(() =>
                    {
                        if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                        {
                            defHashs.Add(frame.DifferenceHash256());
                        }

                        if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                        {
                            dctHashs.Add(frame.DctHash());
                        }

                        if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                        {
                            pHashs.Add(frame.DctHash64());
                        }

                        frame.Dispose();
                    });
                }

                Parallel.Invoke(actions.ToArray());
            }
            else
            {
                using (var image = SkiaImageHelper.DecodeGrayThumb(filename, 160))
                {
                    if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                    {
                        actions.Add(() => defHashs.Add(image.DifferenceHash256()));
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                    {
                        actions.Add(() => dctHashs.Add(image.DctHash()));
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                    {
                        actions.Add(() => pHashs.Add(image.DctHash64()));
                    }

                    if (checkRotated)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.Rotate(90);
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Rotate(180);
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.Rotate(270);
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                    }

                    if (checkFlipped)
                    {
                        actions.Add(() =>
                        {
                            using var clone = image.FlipHorizontal();
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                        actions.Add(() =>
                        {
                            using var clone = image.FlipVertical();
                            if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                            {
                                defHashs.Add(clone.DifferenceHash256());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                            {
                                dctHashs.Add(clone.DctHash());
                            }

                            if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                            {
                                pHashs.Add(clone.DctHash64());
                            }
                        });
                    }

                    Parallel.Invoke(actions.ToArray());
                }
            }

            var list = new List<SearchResult>();
            var queryDifferenceHashes = defHashs.ToArray();
            var queryDctHashes = dctHashs.ToArray();
            var queryDctHash64s = pHashs.ToArray();
            var useDifferenceHash = algorithm.HasFlag(MatchAlgorithm.DifferenceHash);
            var useDctHash32 = algorithm.HasFlag(MatchAlgorithm.DctHash32);
            var useDctHash64 = algorithm.HasFlag(MatchAlgorithm.DctHash64);
            var resultBatches = new ConcurrentBag<List<SearchResult>>();
            Parallel.ForEach(index, new ParallelOptions { MaxDegreeOfParallelism = parallelism }, () => new List<SearchResult>(), (entry, _, items) =>
            {
                var key = entry.Key;
                var value = entry.Value;
                if (useDctHash64)
                {
                    var match = MaxCompare(value.DctHash64, queryDctHash64s);
                    if (match >= similarity)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = key,
                            匹配度 = match,
                            匹配算法 = "DCT Hash 64"
                        });
                    }
                }

                if (useDifferenceHash)
                {
                    var match = MaxCompare(value.DifferenceHash, queryDifferenceHashes);
                    if (match >= similarity)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = key,
                            匹配度 = match,
                            匹配算法 = "Difference Hash"
                        });
                    }
                }

                if (useDctHash32)
                {
                    var match = MaxCompare(value.DctHash, queryDctHashes);
                    if (match >= similarity)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = key,
                            匹配度 = match,
                            匹配算法 = "DCT Hash 32"
                        });
                    }
                }

                return items;
            }, resultBatches.Add);
            list.AddRange(resultBatches.SelectMany(items => items));

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

    private static float MaxCompare(IEnumerable<ulong> values, IEnumerable<ulong> queryHashes)
    {
        var maximum = 0f;
        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                maximum = Math.Max(maximum, ImageHasher.Compare(value, queryHash));
            }
        }

        return maximum;
    }

    private static float MaxCompare(IEnumerable<ulong[]> values, IEnumerable<ulong[]> queryHashes)
    {
        var maximum = 0f;
        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                maximum = Math.Max(maximum, ImageHasher.Compare(value, queryHash));
            }
        }

        return maximum;
    }
}

public sealed record SimilarImagePair(string FirstPath, string SecondPath, float Similarity, MatchAlgorithm Algorithms);

internal sealed class DisposeCollection<T>(IEnumerable<T> items) : IEnumerable<T>, IDisposable where T : IDisposable
{
    public List<T> Items { get; } = items.ToList();

    public void Dispose()
    {
        foreach (var item in Items)
        {
            item.Dispose();
        }
    }

    /// <summary>Returns an enumerator that iterates through the collection.</summary>
    /// <returns>An enumerator that can be used to iterate through the collection.</returns>
    public IEnumerator<T> GetEnumerator()
    {
        return Items.GetEnumerator();
    }

    /// <summary>Returns an enumerator that iterates through a collection.</summary>
    /// <returns>An <see cref="T:System.Collections.IEnumerator" /> object that can be used to iterate through the collection.</returns>
    IEnumerator IEnumerable.GetEnumerator()
    {
        return GetEnumerator();
    }
}
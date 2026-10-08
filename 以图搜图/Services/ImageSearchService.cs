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
    private readonly Lock _candidateIndexLock = new();
    private HashCandidateIndex? _candidateIndex;
    private ConcurrentDictionary<string, IndexItem>? _candidateIndexSource;
    private int _candidateIndexSourceCount;

    public async Task<List<SimilarImagePair>> FindSimilarPairsAsync(string[] paths, ConcurrentDictionary<string, IndexItem> index, ConcurrentDictionary<string, FrameIndexItem> frameIndex, MatchAlgorithm algorithm, float similarity, bool ignoreSameFolder, IProgress<int>? progress = null, CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var entries = new SimilarityHashes?[paths.Length];
            var preprocessingOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount * 2),
                CancellationToken = cancellationToken
            };
            Parallel.For(0, paths.Length, preprocessingOptions, position =>
            {
                try
                {
                    entries[position] = CreateSimilarityHashes(paths[position], index, frameIndex, algorithm);
                }
                catch
                {
                }
            });

            var indexedPositions = Enumerable.Range(0, paths.Length).Where(position => entries[position] != null).ToArray();
            var useDifferenceBuckets = algorithm.HasFlag(MatchAlgorithm.DifferenceHash) && similarity >= 0.88f;
            var hasDctAlgorithm = algorithm.HasFlag(MatchAlgorithm.DctHash32) || algorithm.HasFlag(MatchAlgorithm.DctHash64);
            var canRestrictCandidates = useDifferenceBuckets || (hasDctAlgorithm && !algorithm.HasFlag(MatchAlgorithm.DifferenceHash));
            var dctCandidateIndex = hasDctAlgorithm ? GetCandidateIndex(index) : null;
            var positionsByPath = paths.Select((path, position) => (path, position)).ToDictionary(item => item.path, item => item.position, StringComparer.OrdinalIgnoreCase);
            var animatedPositions = indexedPositions.Where(position => entries[position]!.IsAnimated).ToArray();
            var differenceBuckets = useDifferenceBuckets ? BuildDifferenceHashBuckets(entries, cancellationToken) : null;
            var pairs = new ConcurrentBag<SimilarImagePair>();
            var completed = 0;
            var dctThreshold = Math.Max(0.85f, similarity);
            var parallelOptions = new ParallelOptions
            {
                MaxDegreeOfParallelism = Math.Max(1, Environment.ProcessorCount * 2),
                CancellationToken = cancellationToken
            };
            Parallel.ForEach(indexedPositions, parallelOptions, position =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var source = entries[position]!;
                HashSet<int>? allowedPositions = null;
                if (canRestrictCandidates && !source.IsAnimated)
                {
                    allowedPositions = new HashSet<int>(animatedPositions);
                    if (differenceBuckets != null)
                    {
                        AddDifferenceHashCandidates(source.DifferenceHashes, differenceBuckets, similarity, allowedPositions, cancellationToken);
                    }

                    if (dctCandidateIndex != null)
                    {
                        foreach (var candidatePath in dctCandidateIndex.FindCandidates(source.DctHashes, source.DctHash64s))
                        {
                            if (positionsByPath.TryGetValue(candidatePath, out var candidatePosition))
                            {
                                allowedPositions.Add(candidatePosition);
                            }
                        }
                    }
                }

                for (var candidatePosition = position + 1; candidatePosition < paths.Length; candidatePosition++)
                {
                    if ((candidatePosition & 0xFFF) == 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var candidate = entries[candidatePosition];
                    if (candidate == null || (allowedPositions != null && !allowedPositions.Contains(candidatePosition)) || (ignoreSameFolder && string.Equals(source.Directory, candidate.Directory, StringComparison.OrdinalIgnoreCase)))
                    {
                        continue;
                    }

                    var bestMatch = 0f;
                    if (algorithm.HasFlag(MatchAlgorithm.DifferenceHash))
                    {
                        bestMatch = Math.Max(bestMatch, MaxDifferenceMatch(source.DifferenceHashes, candidate.DifferenceHashes));
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash32))
                    {
                        bestMatch = Math.Max(bestMatch, MaxDctMatch(source.DctHashes, candidate.DctHashes, dctThreshold));
                    }

                    if (algorithm.HasFlag(MatchAlgorithm.DctHash64))
                    {
                        bestMatch = Math.Max(bestMatch, MaxDctMatch(source.DctHash64s, candidate.DctHash64s, dctThreshold));
                    }

                    if (bestMatch >= similarity)
                    {
                        pairs.Add(new SimilarImagePair(source.Path, candidate.Path, bestMatch));
                    }
                }

                var current = Interlocked.Increment(ref completed);
                if (current % 256 == 0 || current == indexedPositions.Length)
                {
                    progress?.Report(current);
                }
            });

            return pairs.ToList();
        }, cancellationToken);
    }

    private static SimilarityHashes? CreateSimilarityHashes(string path, ConcurrentDictionary<string, IndexItem> index, ConcurrentDictionary<string, FrameIndexItem> frameIndex, MatchAlgorithm algorithm)
    {
        if (frameIndex.TryGetValue(path, out var frameItem))
        {
            return new SimilarityHashes(path, Path.GetDirectoryName(path) ?? string.Empty, algorithm.HasFlag(MatchAlgorithm.DifferenceHash) ? frameItem.DifferenceHash.ToArray() : [], algorithm.HasFlag(MatchAlgorithm.DctHash32) ? frameItem.DctHash.ToArray() : [], algorithm.HasFlag(MatchAlgorithm.DctHash64) ? frameItem.DctHash64.ToArray() : [], true);
        }

        if (!index.TryGetValue(path, out var item))
        {
            return null;
        }

        return new SimilarityHashes(path, Path.GetDirectoryName(path) ?? string.Empty, algorithm.HasFlag(MatchAlgorithm.DifferenceHash) && item.DifferenceHash is {Length: > 0} ? [item.DifferenceHash] : [], algorithm.HasFlag(MatchAlgorithm.DctHash32) ? [item.DctHash] : [], algorithm.HasFlag(MatchAlgorithm.DctHash64) ? [item.DctHash64] : [], false);
    }

    private static Dictionary<byte, List<int>>[] BuildDifferenceHashBuckets(SimilarityHashes?[] entries, CancellationToken cancellationToken)
    {
        var buckets = Enumerable.Range(0, 32).Select(_ => new Dictionary<byte, List<int>>()).ToArray();
        for (var position = 0; position < entries.Length; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (entries[position] is not { } entry || entry.IsAnimated)
            {
                continue;
            }

            for (var table = 0; table < buckets.Length; table++)
            {
                foreach (var bucket in entry.DifferenceHashes.Select(hash => GetDifferenceHashByte(hash, table)).Distinct())
                {
                    if (!buckets[table].TryGetValue(bucket, out var positions))
                    {
                        positions = [];
                        buckets[table][bucket] = positions;
                    }

                    positions.Add(position);
                }
            }
        }

        return buckets;
    }

    private static void AddDifferenceHashCandidates(ulong[][] sourceHashes, Dictionary<byte, List<int>>[] buckets, float similarity, HashSet<int> candidates, CancellationToken cancellationToken)
    {
        var maximumChangedBits = (int) Math.Ceiling((1 - similarity) * 256);
        var minimumSharedBuckets = Math.Max(1, buckets.Length - maximumChangedBits);
        foreach (var sourceHash in sourceHashes)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var sharedBuckets = new Dictionary<int, int>();
            for (var table = 0; table < buckets.Length; table++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!buckets[table].TryGetValue(GetDifferenceHashByte(sourceHash, table), out var positions))
                {
                    continue;
                }

                foreach (var position in positions)
                {
                    sharedBuckets.TryGetValue(position, out var count);
                    sharedBuckets[position] = count + 1;
                }
            }

            foreach (var (position, count) in sharedBuckets)
            {
                if (count >= minimumSharedBuckets)
                {
                    candidates.Add(position);
                }
            }
        }
    }

    private static byte GetDifferenceHashByte(ulong[] hash, int bucket)
    {
        var word = bucket / 8;
        var bitOffset = bucket % 8 * 8;
        return word < hash.Length ? (byte) (hash[word] >> bitOffset) : (byte) 0;
    }

    private static float MaxDifferenceMatch(ulong[][] first, ulong[][] second)
    {
        var max = 0f;
        foreach (var firstHash in first)
        {
            foreach (var secondHash in second)
            {
                max = Math.Max(max, ImageHasher.Compare(firstHash, secondHash));
            }
        }

        return max;
    }

    private static float MaxDctMatch(ulong[] first, ulong[] second, float threshold)
    {
        var max = 0f;
        foreach (var firstHash in first)
        {
            foreach (var secondHash in second)
            {
                var match = ImageHasher.Compare(firstHash, secondHash);
                if (match >= threshold)
                {
                    max = Math.Max(max, match);
                }
            }
        }

        return max;
    }

    private sealed record SimilarityHashes(string Path, string Directory, ulong[][] DifferenceHashes, ulong[] DctHashes, ulong[] DctHash64s, bool IsAnimated);

    public async Task<List<SearchResult>> SearchAsync(string filename, ConcurrentDictionary<string, IndexItem> index, ConcurrentDictionary<string, FrameIndexItem> frameIndex, MatchAlgorithm algorithm, float similarity, bool checkRotated, bool checkFlipped, bool includeDirectoryStatistics = true)
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
            var frameSearchParallelism = Math.Max(1, Environment.ProcessorCount);

            if (filename.EndsWith("gif", StringComparison.OrdinalIgnoreCase))
            {
                list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(frameSearchParallelism).SelectMany(x =>
                {
                    var items = new List<SearchResult>(4);
                    if (useDifferenceHash)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = Top10Average(x.Value.DifferenceHash, queryDifferenceHashes, similarity),
                            匹配算法 = "Difference Hash"
                        });
                    }

                    var sim = Math.Max(0.85, similarity);
                    if (useDctHash64)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = Top10Average(x.Value.DctHash64, queryDctHash64s, (float) sim),
                            匹配算法 = "DCT Hash 64"
                        });
                    }

                    if (useDctHash32)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = Top10Average(x.Value.DctHash, queryDctHashes, (float) sim),
                            匹配算法 = "DCT Hash 32"
                        });
                    }

                    return items;
                }).Where(x => x.匹配度 >= similarity));
            }
            else
            {
                var sim = Math.Max(0.85, similarity);
                list.AddRange(frameIndex.AsParallel().WithDegreeOfParallelism(frameSearchParallelism).SelectMany(x =>
                {
                    var items = new List<SearchResult>(4);
                    if (useDctHash64)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = MaxCompare(x.Value.DctHash64, queryDctHash64s, (float) sim),
                            匹配算法 = "DCT Hash 64"
                        });
                    }

                    if (useDifferenceHash)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = MaxCompare(x.Value.DifferenceHash, queryDifferenceHashes),
                            匹配算法 = "Difference Hash"
                        });
                    }

                    if (useDctHash32)
                    {
                        items.Add(new SearchResult
                        {
                            路径 = x.Key,
                            匹配度 = MaxCompare(x.Value.DctHash, queryDctHashes, (float) sim),
                            匹配算法 = "DCT Hash 32"
                        });
                    }

                    return items;
                }).Where(x => x.匹配度 >= similarity));

                var indexSearchParallelism = Math.Max(1, Environment.ProcessorCount * 2);
                var indexSearchOptions = new ParallelOptions
                {
                    MaxDegreeOfParallelism = indexSearchParallelism
                };
                var resultBatches = new ConcurrentBag<List<SearchResult>>();
                IEnumerable<KeyValuePair<string, IndexItem>> searchEntries = index;

                if (!useDifferenceHash && (useDctHash32 || useDctHash64))
                {
                    var candidateIndex = GetCandidateIndex(index);
                    var candidatePaths = candidateIndex.FindCandidates(queryDctHashes, queryDctHash64s);
                    searchEntries = candidatePaths.Select(path => index.TryGetValue(path, out var item) ? (KeyValuePair<string, IndexItem>?) new KeyValuePair<string, IndexItem>(path, item) : null).Where(entry => entry.HasValue).Select(entry => entry.GetValueOrDefault());
                }

                Parallel.ForEach(searchEntries, indexSearchOptions, () => new List<SearchResult>(), (entry, _, items) =>
                {
                    var key = entry.Key;
                    var value = entry.Value;

                    if (useDctHash64)
                    {
                        var match = MaxCompare(value.DctHash64, queryDctHash64s);
                        if (match > sim)
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
                        if (match > similarity)
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
                        if (match > sim)
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
                }, items => resultBatches.Add(items));
                list.AddRange(resultBatches.SelectMany(items => items));
            }

            list = list.OrderByDescending(a => a.匹配度).DistinctBy(e => e.路径).ToList();
            if (includeDirectoryStatistics)
            {
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
            }

            return list;
        });
    }

    private HashCandidateIndex GetCandidateIndex(ConcurrentDictionary<string, IndexItem> index)
    {
        lock (_candidateIndexLock)
        {
            if (_candidateIndex != null && ReferenceEquals(_candidateIndexSource, index) && _candidateIndexSourceCount == index.Count)
            {
                return _candidateIndex;
            }

            var snapshot = index.ToArray();
            _candidateIndex = HashCandidateIndex.Build(snapshot);
            _candidateIndexSource = index;
            _candidateIndexSourceCount = snapshot.Length;
            return _candidateIndex;
        }
    }

    private static float MaxCompare(ulong value, ulong[] queryHashes)
    {
        var max = 0f;
        foreach (var queryHash in queryHashes)
        {
            var match = ImageHasher.Compare(value, queryHash);
            if (match > max)
            {
                max = match;
            }
        }

        return max;
    }

    private static float MaxCompare(ulong[] value, ulong[][] queryHashes)
    {
        var max = 0f;
        foreach (var queryHash in queryHashes)
        {
            var match = ImageHasher.Compare(value, queryHash);
            if (match > max)
            {
                max = match;
            }
        }

        return max;
    }

    private static float MaxCompare(List<ulong> values, ulong[] queryHashes, float threshold)
    {
        var max = 0f;
        foreach (var value in values)
        {
            for (var index = 0; index < queryHashes.Length; index++)
            {
                var match = ImageHasher.Compare(value, queryHashes[index]);
                if (match >= threshold && match > max)
                {
                    max = match;
                }
            }
        }

        return max;
    }

    private static float MaxCompare(List<ulong[]> values, ulong[][] queryHashes)
    {
        var max = 0f;
        foreach (var value in values)
        {
            for (var index = 0; index < queryHashes.Length; index++)
            {
                var match = ImageHasher.Compare(value, queryHashes[index]);
                if (match > max)
                {
                    max = match;
                }
            }
        }

        return max;
    }

    private static float Top10Average(List<ulong> values, ulong[] queryHashes, float threshold)
    {
        Span<float> topMatches = stackalloc float[10];
        var count = 0;

        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                var match = ImageHasher.Compare(value, queryHash);
                if (match >= threshold)
                {
                    AddTopMatch(topMatches, ref count, match);
                }
            }
        }

        return Average(topMatches, count);
    }

    private static float Top10Average(List<ulong[]> values, ulong[][] queryHashes, float threshold)
    {
        Span<float> topMatches = stackalloc float[10];
        var count = 0;

        foreach (var value in values)
        {
            foreach (var queryHash in queryHashes)
            {
                var match = ImageHasher.Compare(value, queryHash);
                if (match >= threshold)
                {
                    AddTopMatch(topMatches, ref count, match);
                }
            }
        }

        return Average(topMatches, count);
    }

    private static void AddTopMatch(Span<float> topMatches, ref int count, float match)
    {
        if (count == topMatches.Length && match <= topMatches[^1])
        {
            return;
        }

        var position = Math.Min(count, topMatches.Length - 1);
        if (count < topMatches.Length)
        {
            count++;
        }

        while (position > 0 && topMatches[position - 1] < match)
        {
            topMatches[position] = topMatches[position - 1];
            position--;
        }

        topMatches[position] = match;
    }

    private static float Average(Span<float> values, int count)
    {
        if (count == 0)
        {
            return 0;
        }

        var total = 0f;
        for (var i = 0; i < count; i++)
        {
            total += values[i];
        }

        return total / count;
    }
}

public sealed record SimilarImagePair(string FirstPath, string SecondPath, float Similarity);

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
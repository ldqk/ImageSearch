using 以图搜图.Models;

namespace 以图搜图.Services;

internal sealed class HashCandidateIndex
{
    private const int DifferenceHashBucketCount = 32;
    private const int DctHashBucketCount = 8;
    private readonly Dictionary<byte, List<int>>[]? _differenceHashBuckets;
    private readonly Dictionary<byte, List<int>>[]? _dctHashBuckets;
    private readonly Dictionary<byte, List<int>>[]? _dctHash64Buckets;

    private HashCandidateIndex(Dictionary<byte, List<int>>[]? differenceHashBuckets, Dictionary<byte, List<int>>[]? dctHashBuckets, Dictionary<byte, List<int>>[]? dctHash64Buckets)
    {
        _differenceHashBuckets = differenceHashBuckets;
        _dctHashBuckets = dctHashBuckets;
        _dctHash64Buckets = dctHash64Buckets;
    }

    public static int MinimumSharedBuckets(int bitCount, int bucketCount, float similarity)
    {
        var maximumChangedBits = Math.Clamp((int) Math.Ceiling((1 - similarity) * bitCount), 0, bitCount);
        return bucketCount - maximumChangedBits;
    }

    public static HashCandidateIndex Build(IReadOnlyList<IndexItem> entries, MatchAlgorithm algorithm, CancellationToken cancellationToken)
    {
        var differenceHashBuckets = algorithm.HasFlag(MatchAlgorithm.DifferenceHash) ? BuildDifferenceHashBuckets(entries, cancellationToken) : null;
        var dctHashBuckets = algorithm.HasFlag(MatchAlgorithm.DctHash32) ? BuildDctHashBuckets(entries, static item => item.DctHash.ToList(), cancellationToken) : null;
        var dctHash64Buckets = algorithm.HasFlag(MatchAlgorithm.DctHash64) ? BuildDctHashBuckets(entries, static item => item.DctHash64.ToList(), cancellationToken) : null;

        return new HashCandidateIndex(differenceHashBuckets, dctHashBuckets, dctHash64Buckets);
    }

    public void AddCandidates(IndexItem source, float similarity, CandidateCollector collector, CancellationToken cancellationToken)
    {
        if (_differenceHashBuckets != null)
        {
            var minimumSharedBuckets = MinimumSharedBuckets(256, DifferenceHashBucketCount, similarity);
            foreach (var hash in source.DifferenceHash)
            {
                var generation = collector.BeginHash();
                for (var table = 0; table < DifferenceHashBucketCount; table++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var bucket = GetDifferenceHashBucket(hash, table);
                    if (_differenceHashBuckets[table].TryGetValue(bucket, out var positions))
                    {
                        collector.AddBucket(positions, generation, minimumSharedBuckets);
                    }
                }
            }
        }

        if (_dctHashBuckets != null)
        {
            var minimumSharedBuckets = MinimumSharedBuckets(64, DctHashBucketCount, similarity);
            foreach (var hash in source.DctHash)
            {
                var generation = collector.BeginHash();
                for (var table = 0; table < DctHashBucketCount; table++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_dctHashBuckets[table].TryGetValue(GetDctHashBucket(hash, table), out var positions))
                    {
                        collector.AddBucket(positions, generation, minimumSharedBuckets);
                    }
                }
            }
        }

        if (_dctHash64Buckets != null)
        {
            var minimumSharedBuckets = MinimumSharedBuckets(64, DctHashBucketCount, similarity);
            foreach (var hash in source.DctHash64)
            {
                var generation = collector.BeginHash();
                for (var table = 0; table < DctHashBucketCount; table++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_dctHash64Buckets[table].TryGetValue(GetDctHashBucket(hash, table), out var positions))
                    {
                        collector.AddBucket(positions, generation, minimumSharedBuckets);
                    }
                }
            }
        }
    }

    private static Dictionary<byte, List<int>>[] BuildDifferenceHashBuckets(IReadOnlyList<IndexItem> entries, CancellationToken cancellationToken)
    {
        var buckets = CreateBuckets(DifferenceHashBucketCount);
        var lastAddedPosition = new int[DifferenceHashBucketCount, byte.MaxValue + 1];
        for (var position = 0; position < entries.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var hash in entries[position].DifferenceHash)
            {
                for (var table = 0; table < DifferenceHashBucketCount; table++)
                {
                    var bucket = GetDifferenceHashBucket(hash, table);
                    if (lastAddedPosition[table, bucket] == position + 1)
                    {
                        continue;
                    }

                    lastAddedPosition[table, bucket] = position + 1;
                    Add(buckets[table], bucket, position);
                }
            }
        }

        return buckets;
    }

    private static Dictionary<byte, List<int>>[] BuildDctHashBuckets(IReadOnlyList<IndexItem> entries, Func<IndexItem, List<ulong>> getHashes, CancellationToken cancellationToken)
    {
        var buckets = CreateBuckets(DctHashBucketCount);
        var lastAddedPosition = new int[DctHashBucketCount, byte.MaxValue + 1];
        for (var position = 0; position < entries.Count; position++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            foreach (var hash in getHashes(entries[position]))
            {
                for (var table = 0; table < DctHashBucketCount; table++)
                {
                    var bucket = GetDctHashBucket(hash, table);
                    if (lastAddedPosition[table, bucket] == position + 1)
                    {
                        continue;
                    }

                    lastAddedPosition[table, bucket] = position + 1;
                    Add(buckets[table], bucket, position);
                }
            }
        }

        return buckets;
    }

    private static Dictionary<byte, List<int>>[] CreateBuckets(int count)
    {
        return Enumerable.Range(0, count).Select(_ => new Dictionary<byte, List<int>>()).ToArray();
    }

    private static void Add(Dictionary<byte, List<int>> buckets, byte bucket, int position)
    {
        if (!buckets.TryGetValue(bucket, out var positions))
        {
            positions = [];
            buckets[bucket] = positions;
        }

        positions.Add(position);
    }

    private static byte GetDifferenceHashBucket(ulong[] hash, int table)
    {
        var word = table / 8;
        var bitOffset = table % 8 * 8;
        return word < hash.Length ? (byte) (hash[word] >> bitOffset) : (byte) 0;
    }

    private static byte GetDctHashBucket(ulong hash, int table)
    {
        return (byte) (hash >> (table * 8));
    }

    internal sealed class CandidateCollector
    {
        private readonly int[] _hashGenerations;
        private readonly byte[] _hashCounts;
        private readonly int[] _candidateGenerations;
        private int _hashGeneration;
        private int _candidateGeneration;

        public CandidateCollector(int entryCount)
        {
            _hashGenerations = new int[entryCount];
            _hashCounts = new byte[entryCount];
            _candidateGenerations = new int[entryCount];
            Candidates = new List<int>();
        }

        public List<int> Candidates { get; }

        public void BeginSource()
        {
            if (_candidateGeneration == int.MaxValue)
            {
                Array.Clear(_candidateGenerations);
                _candidateGeneration = 0;
            }

            _candidateGeneration++;
            Candidates.Clear();
        }

        public int BeginHash()
        {
            if (_hashGeneration == int.MaxValue)
            {
                Array.Clear(_hashGenerations);
                _hashGeneration = 0;
            }

            return ++_hashGeneration;
        }

        public void AddBucket(List<int> positions, int hashGeneration, int minimumSharedBuckets)
        {
            foreach (var position in positions)
            {
                if (_hashGenerations[position] != hashGeneration)
                {
                    _hashGenerations[position] = hashGeneration;
                    _hashCounts[position] = 0;
                }

                if (_hashCounts[position] < minimumSharedBuckets)
                {
                    _hashCounts[position]++;
                }

                if (_hashCounts[position] >= minimumSharedBuckets && _candidateGenerations[position] != _candidateGeneration)
                {
                    _candidateGenerations[position] = _candidateGeneration;
                    Candidates.Add(position);
                }
            }
        }
    }
}
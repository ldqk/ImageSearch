using 以图搜图.Models;

namespace 以图搜图.Services;

internal sealed class HashCandidateIndex
{
    // Register each algorithm here; bit counts must match comparison and be multiples of eight.
    private static readonly HashDefinition[] Definitions =
    [
        new HashDefinition<ulong[], ArrayHash>(MatchAlgorithm.DifferenceHash, 256, static item => item.DifferenceHash),
        new HashDefinition<ulong, ScalarHash>(MatchAlgorithm.DctHash32, 64, static item => item.DctHash),
        new HashDefinition<ulong, ScalarHash>(MatchAlgorithm.DctHash64, 64, static item => item.DctHash64),
        new HashDefinition<ulong[], ArrayHash>(MatchAlgorithm.DctHash256, 256, static item => item.DctHash256),
        new HashDefinition<ulong[], ArrayHash>(MatchAlgorithm.MHHash256, 256, static item => item.MHHash256)
    ];

    private readonly BucketIndex[] _indexes;

    private HashCandidateIndex(BucketIndex[] indexes)
    {
        _indexes = indexes;
    }

    public static int MinimumSharedBuckets(int bitCount, int bucketCount, float similarity)
    {
        var maximumChangedBits = Math.Clamp((int) Math.Ceiling((1 - similarity) * bitCount), 0, bitCount);
        return bucketCount - maximumChangedBits;
    }

    public static bool CanUseIndex(MatchAlgorithm algorithm, float similarity)
    {
        var supported = (MatchAlgorithm) 0;
        foreach (var definition in Definitions)
        {
            supported |= definition.Algorithm;
            if (algorithm.HasFlag(definition.Algorithm) && MinimumSharedBuckets(definition.BitCount, definition.BucketCount, similarity) <= 0)
            {
                return false;
            }
        }

        return (algorithm & ~supported) == 0;
    }

    public static HashCandidateIndex Build(IReadOnlyList<IndexItem> entries, MatchAlgorithm algorithm, CancellationToken cancellationToken)
    {
        var indexes = new List<BucketIndex>(Definitions.Length);
        foreach (var definition in Definitions)
        {
            if (algorithm.HasFlag(definition.Algorithm))
            {
                indexes.Add(definition.Build(entries, cancellationToken));
            }
        }

        return new HashCandidateIndex(indexes.ToArray());
    }

    public void AddCandidates(IndexItem source, float similarity, CandidateCollector collector, CancellationToken cancellationToken)
    {
        foreach (var index in _indexes)
        {
            index.AddCandidates(source, similarity, collector, cancellationToken);
        }
    }

    private abstract class HashDefinition(MatchAlgorithm algorithm, int bitCount)
    {
        public MatchAlgorithm Algorithm { get; } = algorithm;
        public int BitCount { get; } = bitCount;
        public int BucketCount { get; } = bitCount / 8;
        public abstract BucketIndex Build(IReadOnlyList<IndexItem> entries, CancellationToken cancellationToken);
    }

    private sealed class HashDefinition<THash, TBucket>(MatchAlgorithm algorithm, int bitCount, Func<IndexItem, HashSet<THash>> getHashes) : HashDefinition(algorithm, bitCount)
        where TBucket : struct, IHashBucket<THash>
    {
        public override BucketIndex Build(IReadOnlyList<IndexItem> entries, CancellationToken cancellationToken)
        {
            return new BucketIndex<THash, TBucket>(entries, getHashes, BitCount, BucketCount, cancellationToken);
        }
    }

    private abstract class BucketIndex
    {
        public abstract void AddCandidates(IndexItem source, float similarity, CandidateCollector collector, CancellationToken cancellationToken);
    }

    private sealed class BucketIndex<THash, TBucket> : BucketIndex where TBucket : struct, IHashBucket<THash>
    {
        private readonly Func<IndexItem, HashSet<THash>> _getHashes;
        private readonly int _bitCount;
        private readonly Dictionary<byte, List<int>>[] _buckets;

        public BucketIndex(IReadOnlyList<IndexItem> entries, Func<IndexItem, HashSet<THash>> getHashes, int bitCount, int bucketCount, CancellationToken cancellationToken)
        {
            _getHashes = getHashes;
            _bitCount = bitCount;
            _buckets = Enumerable.Range(0, bucketCount).Select(_ => new Dictionary<byte, List<int>>()).ToArray();
            var lastAddedPosition = new int[bucketCount * (byte.MaxValue + 1)];
            for (var position = 0; position < entries.Count; position++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                foreach (var hash in getHashes(entries[position]))
                {
                    for (var table = 0; table < bucketCount; table++)
                    {
                        var bucket = TBucket.GetBucket(hash, table);
                        ref var lastPosition = ref lastAddedPosition[table * (byte.MaxValue + 1) + bucket];
                        if (lastPosition == position + 1)
                        {
                            continue;
                        }

                        lastPosition = position + 1;
                        if (!_buckets[table].TryGetValue(bucket, out var positions))
                        {
                            positions = [];
                            _buckets[table][bucket] = positions;
                        }

                        positions.Add(position);
                    }
                }
            }
        }

        public override void AddCandidates(IndexItem source, float similarity, CandidateCollector collector, CancellationToken cancellationToken)
        {
            var minimumSharedBuckets = MinimumSharedBuckets(_bitCount, _buckets.Length, similarity);
            foreach (var hash in _getHashes(source))
            {
                var generation = collector.BeginHash();
                for (var table = 0; table < _buckets.Length; table++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (_buckets[table].TryGetValue(TBucket.GetBucket(hash, table), out var positions))
                    {
                        collector.AddBucket(positions, generation, minimumSharedBuckets);
                    }
                }
            }
        }
    }

    private interface IHashBucket<THash>
    {
        static abstract byte GetBucket(THash hash, int table);
    }

    private readonly struct ArrayHash : IHashBucket<ulong[]>
    {
        public static byte GetBucket(ulong[] hash, int table)
        {
            var word = table / 8;
            var bitOffset = table % 8 * 8;
            return word < hash.Length ? (byte) (hash[word] >> bitOffset) : (byte) 0;
        }
    }

    private readonly struct ScalarHash : IHashBucket<ulong>
    {
        public static byte GetBucket(ulong hash, int table)
        {
            return (byte) (hash >> (table * 8));
        }
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
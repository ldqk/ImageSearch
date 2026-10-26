using System.ComponentModel.DataAnnotations;
using Masuit.Tools.Core.Validator;
using 以图搜图.Models;
using 以图搜图.Services;

namespace 以图搜图.WebAPI.Models;

public sealed class SimilarSearchRequest
{
    [Required, MinLength(1)]
    public string[] Directories { get; init; } = [];

    public string[] ExcludedDirectories { get; init; } = [];

    [Range(85, 100)]
    public float Similarity { get; init; } = 93;

    [EnumDataType(typeof(MatchAlgorithm)),EnumOf(typeof(MatchAlgorithm))]
    public MatchAlgorithm Algorithm { get; init; } = MatchAlgorithm.All;

    public bool IgnoreSameFolder { get; init; }

    public bool IncludeSubfolders { get; init; } = true;

    public bool VideoMode { get; init; }

    [Range(10, 200)]
    public int SampleFrames { get; init; } = 64;

    public bool FilterShortVideos { get; init; } = true;

    [Range(1, 86400)]
    public int MinimumVideoDurationSeconds { get; init; } = 10;
}

public sealed record SimilarSearchResponse(int ScannedFileCount, int MatchCount, IReadOnlyList<SimilarImagePair> Matches);
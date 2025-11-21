using System.ComponentModel;

namespace 以图搜图.Models;

[Flags]
public enum MatchAlgorithm
{
    [Description("Difference Hash")]
    DifferenceHash = 1,

    [Description("DCT Hash")]
    DctHash = 2,

    [Description("全部")]
    All = DifferenceHash | DctHash
}
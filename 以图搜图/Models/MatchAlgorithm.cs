using System.ComponentModel;

namespace 以图搜图.Models;

[Flags]
public enum MatchAlgorithm
{
    [Description("Difference Hash")]
    DifferenceHash = 1,

    [Description("DCT Hash 32")]
    DctHash32 = 2,

    [Description("DCT Hash 64")]
    DctHash64 = 4,

    [Description("全部")]
    All = DifferenceHash | DctHash32 | DctHash64
}
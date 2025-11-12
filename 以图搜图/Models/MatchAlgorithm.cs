using System.ComponentModel;

namespace 以图搜图.Models;

[Flags]
public enum MatchAlgorithm
{
    [Description("DifferenceHash")]
    DifferenceHash = 1,

    [Description("DctHash")]
    DctHash = 2,

    [Description("全部")]
    All = DifferenceHash | DctHash
}
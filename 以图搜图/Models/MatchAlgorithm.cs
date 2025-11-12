using System.ComponentModel;

namespace 以图搜图.Models;

public enum MatchAlgorithm
{
    [Description("全部")]
    None,

    [Description("DifferenceHash")]
    DifferenceHash,

    [Description("DctHash")]
    DctHash
}
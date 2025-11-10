namespace 以图搜图.Models;

public record SearchResult
{
    public string 路径 { get; set; } = string.Empty;
    public float 匹配度 { get; set; }
    public string 匹配算法 { get; set; }

    public string 大小 { get; set; } = string.Empty;
    public string 所属文件夹大小 { get; set; } = string.Empty;
    public int 所属文件夹文件数 { get; set; }

    /// <summary>Indicates whether the current object is equal to another object of the same type.</summary>
    /// <param name="other">An object to compare with this object.</param>
    /// <returns>
    /// <see langword="true" /> if the current object is equal to the <paramref name="other" /> parameter; otherwise, <see langword="false" />.</returns>
    public virtual bool Equals(SearchResult? other)
    {
        return 路径 == other?.路径;
    }
}
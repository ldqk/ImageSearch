namespace 以图搜图.Models;

public record SearchResult
{
  public string 路径 { get; set; } = string.Empty;
  public float 匹配度 { get; set; }
  public string 大小 { get; set; } = string.Empty;
  public string 所属文件夹大小 { get; set; } = string.Empty;
  public int 所属文件夹文件数 { get; set; }
}

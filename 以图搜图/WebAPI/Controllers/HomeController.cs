using Masuit.Tools.Systems;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using System.ComponentModel.DataAnnotations;
using System.IO;
using System.Net;
using Masuit.Tools.Files;
using 以图搜图.Models;
using 以图搜图.Services;
using 以图搜图.ViewModels;

namespace 以图搜图.WebAPI.Controllers;

[ApiController]
public class HomeController : Controller
{
    private readonly ImageIndexService _indexService = ImageIndexService.Instance;
    private readonly ImageSearchService _searchService = new ImageSearchService();
    public static MainViewModel MainViewModel { get; set; }

    /// <summary>
    /// 创建或更新索引
    /// </summary>
    /// <param name="dir">索引目录</param>
    /// <param name="removeInvalid">是否移除无效索引</param>
    /// <returns></returns>
    [HttpPatch("index")]
    public async Task<ActionResult> UpdateIndex([Required] string dir, bool removeInvalid)
    {
        MainViewModel.DirectoryPath = dir;
        MainViewModel.RemoveInvalidIndex = removeInvalid;
        await MainViewModel.UpdateIndexCommand.ExecuteAsync(this);
        return Ok("已发送指令，请查看主程序窗口");
    }

    /// <summary>
    /// 搜索图像
    /// </summary>
    /// <param name="file">需要搜索的图片</param>
    /// <param name="similar">相似度</param>
    /// <param name="algorithm">匹配算法，1：DifferenceHash，2：DctHash，4：DctHash64，7：所有</param>
    /// <param name="checkRotated">查找旋转</param>
    /// <param name="checkFlip">查找翻转</param>
    /// <returns></returns>
    [HttpPost("search")]
    public async Task<ActionResult> Search(IFormFile file, [Range(75, 100)] float similar = 75, MatchAlgorithm algorithm = MatchAlgorithm.All, bool checkRotated = true, bool checkFlip = false)
    {
        var filename = Path.Combine(Path.GetTempPath(), SnowFlake.NewId + ".jpg");
        await file.OpenReadStream().SaveFileAsync(filename);
        return Ok(await _searchService.SearchAsync(filename, _indexService.Index, _indexService.FrameIndex, algorithm, similar / 100, checkRotated, checkFlip));
    }
}
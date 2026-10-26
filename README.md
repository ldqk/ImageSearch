# ImageSearch(现已改名：媒鉴查重MediaTrace)

<a href="https://gitee.com/masuit/ImageSearch"><img src="https://gitee.com/static/images/logo-black.svg" height="24"></a>
<a href="https://github.com/ldqk/ImageSearch"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/9/95/Font_Awesome_5_brands_github.svg/54px-Font_Awesome_5_brands_github.svg.png" height="24"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/2/29/GitHub_logo_2013.svg/128px-GitHub_logo_2013.svg.png" height="24"></a>

基于本地硬盘的图像与视频搜索、相似文件查找和重复项整理工具。支持用图像或视频检索本地索引，也支持扫描图像或视频中的相似文件并批量整理；，百万媒体毫秒级检索，分钟级查重比对，支持jpg、png、bmp、gif、视频相互检索，设计灵感来源于 [DuplicateCleaner](https://masuit.org/1776)，并且比[DuplicateCleaner](https://masuit.org/1776)快。目前全网很难找到一个本地版的以图搜图和查重工具，所以就自己动手搓一个吧。也包含图片 EXIF 信息移除工具的定位。
## 能做什么

### 按图像和视频搜索

先在主窗口选一个文件夹并更新索引，再选择图像或视频作为搜索源。支持 JPG、JPEG、PNG、BMP、GIF、WEBP 图像，以及 3GP、AVI、FLV、M2TS、M4V、MKV、MOV、MP4、MPEG、MPG、MTS、TS、WEBM、WMV 视频。预览区显示由 FFmpeg 提取的视频画面，不保证是视频首帧。

图片路径可以手动选择，也可以直接把图片拖进窗口；如果图片已经在剪贴板里，也能直接用剪贴板搜索。搜索时可以调相似度、选择匹配算法，还能决定是否把旋转或翻转过的图片也算作匹配。
<img width="1296" height="843" alt="image" src="https://github.com/user-attachments/assets/136b99a8-d202-44c6-93e0-3041d8520928" />
<img width="1298" height="839" alt="image" src="https://github.com/user-attachments/assets/6391360d-a2fe-4c39-a29b-fc72d1fbd82e" />

搜索结果会列出匹配文件的路径、匹配度、文件大小、所在文件夹的文件数量和大小，以及使用的算法。点选结果就能在旁边预览搜索源和匹配文件；双击结果可打开文件。右键菜单还可以打开文件夹、直接删除文件或把它移到回收站，也支持一次选中多项。
<img width="1244" height="841" alt="image" src="https://github.com/user-attachments/assets/3522c5f2-8c64-4eee-8d05-1f6179448c31" />

更新索引时，窗口会显示正在处理的文件、进度、速度、吞吐量和预计剩余时间，也会记录速度变化。需要时可以停止更新；勾选“移除无效索引”则会在更新时清理已经失效的记录。
<img width="1083" height="839" alt="image" src="https://github.com/user-attachments/assets/0f63f5d2-cd90-4f2a-baad-8b5878b5981c" />

### 扫描相似文件、整理重复项

在主窗口点“相似文件查找”，可以一次扫描多个目录。扫描位置既可以浏览选择，也可以输入、粘贴或拖放目录；不想纳入扫描的目录可以放进排除列表。还可以选择是否扫描子文件夹，以及是否忽略同一文件夹里的重复项。扫描时可切换“图片模式”或“视频模式”；视频模式会只查找视频，并可设置每段视频参与比较的抽样帧数（10–200，默认 64）和最短时长（默认启用，10 秒）。时长筛选按索引帧数估算，视频抽帧约为每秒一帧。缺少索引的文件会在扫描时自动建立索引。
<img width="1264" height="894" alt="image" src="https://github.com/user-attachments/assets/d79b3680-e438-46b3-945a-be8e3068b37c" />


图片和视频模式都可以调整相似度，并选择 Difference Hash、DCT Hash 32、DCT Hash 64 或全部算法。视频比较会综合抽样画面的匹配情况；视频结果还会显示时长和画面预览。界面会显示扫描进度、当前速度、处理数量和耗时，扫描也可以随时停止。
<img width="1265" height="891" alt="image" src="https://github.com/user-attachments/assets/6f005701-6859-410a-aa1d-58a7eac3cb4c" />

结果按组显示，分为重复文件和重复目录（找到重复目录时才会出现这一项）。可以切换列表或缩略图视图、调整缩略图大小，再按文件所在文件夹、标记状态、路径、算法和最低相似度筛选。文件如果已经在资源管理器中删掉了，点“刷新结果”可以清理旧记录并更新分组。
<img width="1267" height="891" alt="image" src="https://github.com/user-attachments/assets/c6dfaea5-f2eb-4d6b-ad83-4a8f0cab4f96" />
<img width="1266" height="893" alt="image" src="https://github.com/user-attachments/assets/83fde7e1-7532-4299-9940-10d4ebb3f4fe" />
<img width="1266" height="893" alt="image" src="https://github.com/user-attachments/assets/3367b891-66a6-44a3-a39f-c89edf60a83b" />


整理时，可以手动标记要清理的文件，也可以按规则给每组挑选保留项：保留最大或最小的文件、最新或最早修改的文件，或文件名最短、最长的文件。除了批量取消或反选标记，还能一次标记组内其他文件，或标记某个目录、目录树、磁盘中找到的文件。
<img width="1266" height="892" alt="image" src="https://github.com/user-attachments/assets/cc6f8d27-3eba-436a-8652-a04f514310ae" />

右键文件还可以打开所在文件夹、复制路径、忽略该文件所在目录、删除文件，或用选中的文件替换组内其他文件。删除过程会显示进度。“抑制所有确认提示”会跳过删除、覆盖等操作的确认步骤，批量处理时请特别留意。
<img width="1264" height="892" alt="image" src="https://github.com/user-attachments/assets/5e4065d3-533b-425a-97a2-4df425f58aaf" />

## 使用前留意

- 按图像或视频搜索需要先建立索引。第一次使用，或媒体目录有变化时，先更新索引再搜索；相似文件扫描遇到未索引的文件时会自动补建索引。
- 使用视频搜索或扫描时，需要可用的 FFmpeg 和 FFprobe，并确保 `ffmpeg.exe`、`ffprobe.exe` 可从 `PATH` 或程序可访问的位置找到。可以直接双击install_ffmpeg.ps1安装ffmpeg依赖。
- 如果电脑上运行着 Everything，且程序目录中有 `Everything64.dll`，软件会调用 Everything 帮忙查找文件。请确认要扫描的目录已经加入 Everything 索引。不想使用这项集成的话，移除程序目录中的 `Everything64.dll` 即可，程序仍可自行扫描目录。
- 目前不支持只搜索图片中的某个局部区域，只能比较整张图片。
- 相似度可以在界面中调整。Difference Hash 最低为 70，其他算法最低为 85；阈值越高，筛选越严格。具体匹配效果还和图片内容有关。
- 删除、移入回收站或替换文件前，建议先核对路径和标记，避免误操作。

## WebAPI

程序内置 HTTP API。编辑程序目录下的 `config.ini`，将 `[Global]` 下的 `RunServer` 设置为 `true` 后启动程序。默认地址为 `http://127.0.0.1:5000`，端口可通过 `HttpPort` 修改。`RunServer` 默认是 `false`。

- Scalar API 文档：`http://127.0.0.1:5000/api`
- OpenAPI JSON：`http://127.0.0.1:5000/openapi/v1.json`

### 更新索引

`PATCH /index` 根据指定目录创建或更新索引。`dir` 为必填目录路径，`removeInvalid` 可选，默认 `false`；设为 `true` 时会移除目录中已失效的索引。

```powershell
curl.exe -X PATCH "http://127.0.0.1:5000/index?dir=D%3A%5CPictures&removeInvalid=false"
```

成功时返回 HTTP 200 和提示文本“已发送指令，请查看主程序窗口”。建议先建立索引，再调用单图搜索接口。

### 以图搜索

`POST /search` 使用 `multipart/form-data` 上传一张查询图像，表单字段名为 `file`。视频搜索可在桌面程序中使用；此接口用于上传图像。

| 参数 | 位置 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- | --- |
| `file` | 表单 | 文件 | 必填 | 查询图像 |
| `similar` | Query | 数值，75–100 | 75 | 最低相似度百分比 |
| `algorithm` | Query | 整数 | 7 | 匹配算法，见下表 |
| `checkRotated` | Query | 布尔值 | `true` | 是否检查旋转后的图像 |
| `checkFlip` | Query | 布尔值 | `false` | 是否检查翻转后的图像 |

`algorithm` 的取值为 `1`（Difference Hash）、`2`（DCT Hash 32）、`4`（DCT Hash 64）或 `7`（全部）。示例：

```powershell
curl.exe -X POST "http://127.0.0.1:5000/search?similar=85&algorithm=7&checkRotated=true&checkFlip=false" -F "file=@D:\Pictures\query.jpg"
```

成功时返回匹配结果数组，按匹配度排序；无匹配时返回空数组。每条结果包含路径、匹配度、匹配算法、文件大小和所属文件夹信息。匹配度为 `0` 到 `1` 的数值，例如 `0.97` 表示约 97%。

响应示例：

```json
[
  {
    "路径": "D:\\Pictures\\match.jpg",
    "匹配度": 0.97,
    "匹配算法": "DCT Hash 64",
    "大小": "123KB",
    "所属文件夹大小": "32.00MB",
    "所属文件夹文件数": 14
  }
]
```

### 相似查重

`POST /similar-search` 使用 JSON 请求体，对指定目录中的图像或视频执行两两相似查重。请求示例：

```json
{
  "directories": ["D:\\Pictures"],
  "excludedDirectories": [],
  "similarity": 93,
  "algorithm": 7,
  "ignoreSameFolder": false,
  "includeSubfolders": true,
  "videoMode": false,
  "sampleFrames": 64,
  "filterShortVideos": true,
  "minimumVideoDurationSeconds": 10
}
```

| 字段 | 类型 | 默认值 | 说明 |
| --- | --- | --- | --- |
| `directories` | 字符串数组 | 必填 | 一个或多个已存在的扫描目录 |
| `excludedDirectories` | 字符串数组 | `[]` | 要排除的目录 |
| `similarity` | 数值，85–100 | 93 | 最低相似度百分比 |
| `algorithm` | 整数 | 7 | `1` Difference Hash、`2` DCT Hash 32、`4` DCT Hash 64、`7` 全部 |
| `ignoreSameFolder` | 布尔值 | `false` | 是否忽略同一文件夹中的相似文件 |
| `includeSubfolders` | 布尔值 | `true` | 是否扫描子目录 |
| `videoMode` | 布尔值 | `false` | `false` 查图像，`true` 查视频 |
| `sampleFrames` | 整数，10–200 | 64 | 视频模式下每个视频参与比较的最大抽样帧数 |
| `filterShortVideos` | 布尔值 | `true` | 视频模式下是否按最短时长过滤 |
| `minimumVideoDurationSeconds` | 整数，1–86400 | 10 | 视频最短时长；按索引帧数估算 |

缺失索引的媒体会在查重前自动建立索引。成功响应包含 `scannedFileCount`（参与比较的文件数）、`matchCount` 和 `matches`；每个匹配项包含两个文件路径、相似度（0–1）及算法。

响应示例：

```json
{
  "scannedFileCount": 2,
  "matchCount": 1,
  "matches": [
    {
      "firstPath": "D:\\Pictures\\first.jpg",
      "secondPath": "D:\\Pictures\\second.jpg",
      "similarity": 0.96,
      "algorithms": 4
    }
  ]
}
```

参数无效、目录不存在或符合条件的媒体少于两个时返回 HTTP 400；索引任务正在运行或已有查重任务时返回 HTTP 409；索引后仍有媒体无法建立索引时返回 HTTP 422。

### 服务访问与安全

HTTP 服务监听所有网络接口（`0.0.0.0`），目前没有身份验证。启用服务前请确认网络环境可信；否则应使用防火墙限制访问或保持 `RunServer=false`。

## MCP 集成

程序可通过 MCP（Streamable HTTP）向支持 MCP 的客户端提供本地媒体检索工具。启用上文的 `RunServer` 后，MCP 地址为 `http://127.0.0.1:5000/mcp`，端口随 `HttpPort` 配置变化。

可用工具：

- `get_image_index_status`：查看已索引文件数量及索引/写入状态。
- `search_similar_images`：根据本机可访问的绝对文件路径搜索相似图像或视频；支持相似度阈值、匹配算法、旋转/翻转选项，最多返回 100 条结果。

使用 `search_similar_images` 前请先建立索引。MCP 与 HTTP API 共用同一 HTTP 服务和访问范围，请一并遵守上面的网络安全提示。

## 环境要求

- 开发环境：Visual Studio 2026
- 运行时：.NET 10 Desktop
- 平台：Windows x64
- 视频处理：需要可用的 FFmpeg 和 FFprobe，并确保 `ffmpeg.exe`、`ffprobe.exe` 可从 `PATH` 或程序可访问的位置找到。可以直接双击install_ffmpeg.ps1安装ffmpeg依赖。

## 硬件建议

- 处理器：4 核或更多
- 内存：8 GB 或更多

## 相关理论

https://segmentfault.com/a/1190000038308093

## 特别鸣谢

[Masuit.Tools](https://github.com/ldqk/Masuit.Tools)

## 关于盗版版本

本项目完全开源。以下链接及其相关链接提供的为盗版版本；如您曾为此付费，请申请退款或向相关平台投诉：

https://www.chinapyg.com/forum.php?mod=viewthread&tid=162510  
https://download.csdn.net/download/china365love/92183755  
https://blog.csdn.net/china365love/article/details/153752532  
https://shop.owmei.com/

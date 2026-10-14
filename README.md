# ImageSearch

<a href="https://gitee.com/masuit/ImageSearch"><img src="https://gitee.com/static/images/logo-black.svg" height="24"></a>
<a href="https://github.com/ldqk/ImageSearch"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/9/95/Font_Awesome_5_brands_github.svg/54px-Font_Awesome_5_brands_github.svg.png" height="24"><img src="https://upload.wikimedia.org/wikipedia/commons/thumb/2/29/GitHub_logo_2013.svg/128px-GitHub_logo_2013.svg.png" height="24"></a>

基于本地硬盘的以图搜图和整理重复图片小程序，百万图片毫秒级检索，分钟级查重比对，支持jpg、png、bmp、gif相互检索，并且支持海量图片快速查找重复相似图，设计灵感来源于[DuplicateCleaner](https://masuit.org/1776)，并且比[DuplicateCleaner](https://masuit.org/1776)快。目前全网很难找到一个本地版的以图搜图和图片查重工具，所以就自己动手搓一个吧。也包含图片 EXIF 信息移除工具的定位。
## 能做什么

### 按图片搜索

先在主窗口选一个图片文件夹并更新索引，再选一张图片开始搜索。索引建好后，就可以用它在本地图片中查找相似图。

图片路径可以手动选择，也可以直接把图片拖进窗口；如果图片已经在剪贴板里，也能直接用剪贴板搜索。搜索时可以调相似度、选择匹配算法，还能决定是否把旋转或翻转过的图片也算作匹配。
<img width="1296" height="843" alt="image" src="https://github.com/user-attachments/assets/136b99a8-d202-44c6-93e0-3041d8520928" />
<img width="1298" height="839" alt="image" src="https://github.com/user-attachments/assets/6391360d-a2fe-4c39-a29b-fc72d1fbd82e" />

搜索结果会列出图片路径、匹配度、文件大小、所在文件夹的文件数量和大小，以及使用的算法。点选结果就能在旁边预览原图和匹配图；双击结果可打开文件。右键菜单还可以打开文件夹、直接删除图片或把它移到回收站，也支持一次选中多项。
<img width="1244" height="841" alt="image" src="https://github.com/user-attachments/assets/3522c5f2-8c64-4eee-8d05-1f6179448c31" />

更新索引时，窗口会显示正在处理的文件、进度、速度、吞吐量和预计剩余时间，也会记录速度变化。需要时可以停止更新；勾选“移除无效索引”则会在更新时清理已经失效的记录。
<img width="1083" height="839" alt="image" src="https://github.com/user-attachments/assets/0f63f5d2-cd90-4f2a-baad-8b5878b5981c" />

### 扫描相似文件、整理重复项

在主窗口点“相似文件查找”，可以一次扫描多个目录。扫描位置既可以浏览选择，也可以输入、粘贴或拖放目录；不想纳入扫描的目录可以放进排除列表。还可以选择是否扫描子文件夹，以及是否忽略同一文件夹里的重复项。整体功能对标 DuplicateCleaner，并且比DuplicateCleaner快很多。
<img width="1264" height="894" alt="image" src="https://github.com/user-attachments/assets/d79b3680-e438-46b3-945a-be8e3068b37c" />

扫描时可以调整相似度，并选择 Difference Hash、DCT Hash 32、DCT Hash 64 或全部算法。界面会显示扫描进度、当前速度、处理数量和耗时，扫描也可以随时停止。
<img width="1265" height="891" alt="image" src="https://github.com/user-attachments/assets/6f005701-6859-410a-aa1d-58a7eac3cb4c" />

结果按组显示，分为重复文件和重复目录（找到重复目录时才会出现这一项）。可以切换列表或缩略图视图、调整缩略图大小，再按文件所在文件夹、标记状态、路径、算法和最低相似度筛选。文件如果已经在资源管理器中删掉了，点“刷新结果”可以清理旧记录并更新分组。
<img width="1267" height="891" alt="image" src="https://github.com/user-attachments/assets/c6dfaea5-f2eb-4d6b-ad83-4a8f0cab4f96" />

整理时，可以手动标记要清理的文件，也可以按规则给每组挑选保留项：保留最大或最小的文件、最新或最早修改的文件，或文件名最短、最长的文件。除了批量取消或反选标记，还能一次标记组内其他文件，或标记某个目录、目录树、磁盘中找到的文件。
<img width="1266" height="892" alt="image" src="https://github.com/user-attachments/assets/cc6f8d27-3eba-436a-8652-a04f514310ae" />

右键文件还可以打开所在文件夹、复制路径、忽略该文件所在目录、删除文件，或用选中的文件替换组内其他文件。删除过程会显示进度。“抑制所有确认提示”会跳过删除、覆盖等操作的确认步骤，批量处理时请特别留意。
<img width="1264" height="892" alt="image" src="https://github.com/user-attachments/assets/5e4065d3-533b-425a-97a2-4df425f58aaf" />

## 使用前留意

- 按图搜索需要先建立索引。第一次使用，或图片目录有变化时，先更新索引再搜索。
- 如果电脑上运行着 Everything，且程序目录中有 `Everything64.dll`，软件会调用 Everything 帮忙查找文件。请确认要扫描的目录已经加入 Everything 索引。不想使用这项集成的话，移除程序目录中的 `Everything64.dll` 即可，程序仍可自行扫描目录。
- 目前不支持只搜索图片中的某个局部区域，只能比较整张图片。
- 相似度可以在界面中调整。Difference Hash 最低为 70，其他算法最低为 85；阈值越高，筛选越严格。具体匹配效果还和图片内容有关。
- 删除、移入回收站或替换文件前，建议先核对路径和标记，避免误操作。

## 环境要求

- 开发环境：Visual Studio 2026
- 运行时：.NET 10 Desktop
- 平台：Windows x64

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

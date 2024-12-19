# ImageSearch
图片exif信息移除小工具和本地硬盘以图搜图案例Demo分享，灵感来源于[DuplicateCleaner](https://masuit.org/1776)，**千万级图片秒级检索**：   
![image](https://user-images.githubusercontent.com/20254980/177007293-d9431b89-7999-496b-a865-1ccfadb6243f.png)
![image](https://user-images.githubusercontent.com/20254980/177023108-b2847b7c-4618-4878-8988-94a5df59fd63.png)
![image](https://user-images.githubusercontent.com/20254980/177023173-c0bb4be5-a015-4c05-981b-98c26c47010c.png)
## 环境要求
开发环境：Visual Studio 2022  
运行时：.net8 desktop  
## 特别说明
1. 如果电脑中安装有everything，软件会自动调取everything进行目录扫描，请确保要扫描的目录已经被everything索引，如果你想让软件不自动调取everything，把目录下的everything64.dll文件删掉即可
2. 软件不支持部分区域的图片检索，只能做相似检索
3. 相似度限定70是因为低于70的相似度肉眼看上去已经是完全不一样的图了
## Star趋势

<img src="https://starchart.cc/ldqk/ImageSearch.svg">

## 理论篇
https://segmentfault.com/a/1190000038308093

## 特别鸣谢
[Masuit.Tools](https://github.com/ldqk/Masuit.Tools)

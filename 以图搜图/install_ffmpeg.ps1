# 一键安装FFmpeg
Write-Host "正在通过 winget 安装 FFmpeg..." -ForegroundColor Cyan
winget install ffmpeg
Write-Host "`n✅ 安装执行完毕！请重新打开终端，运行 ffmpeg -version 检查" -ForegroundColor Green
Read-Host "按回车退出"

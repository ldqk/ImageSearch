@echo off
chcp 65001 >nul
echo ====================================
echo 以图搜图 WPF 版本 - 编译脚本
echo ====================================
echo.

cd /d "%~dp0"

echo [1/3] 还原 NuGet 包...
dotnet restore
if errorlevel 1 (
    echo 错误：NuGet 包还原失败
    pause
    exit /b 1
)

echo.
echo [2/3] 编译项目...
dotnet build -c Release
if errorlevel 1 (
    echo 错误：编译失败
    pause
    exit /b 1
)

echo.
echo [3/3] 编译成功！
echo.
echo 可执行文件位置：
echo bin\Release\net9.0-windows\以图搜图.exe
echo.

set /p runapp="是否立即运行程序？(Y/N): "
if /i "%runapp%"=="Y" (
    echo.
    echo 正在启动程序...
    start "" "bin\Release\net9.0-windows\以图搜图.exe"
)

pause

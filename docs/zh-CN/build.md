# 构建指南

## 前置条件

- Windows 10/11 x64
- .NET 8 SDK
- Python 3.12
- Git
- Inno Setup 6.7.3
- 可选：NVIDIA CUDA 驱动

## 主程序

```powershell
dotnet restore .\src\WatermarkRemover.Windows\WatermarkRemoverWindows.csproj `
  -r win-x64 --configfile .\src\WatermarkRemover.Windows\NuGet.Config

dotnet publish .\src\WatermarkRemover.Windows\WatermarkRemoverWindows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\release\staging\app `
  --configfile .\src\WatermarkRemover.Windows\NuGet.Config
```

## OpenCV worker

```powershell
.\packaging\scripts\build-opencv-worker.ps1 `
  -PythonExecutable C:\Path\To\python.exe `
  -OutputDirectory .\release\staging\ai
```

## ProPainter 引擎

```powershell
.\engines\heavy\bootstrap.ps1
```

首次执行会下载上游 ProPainter、CUDA PyTorch 和模型依赖。生成结果位于
`.runtime\propainter`，不会提交到仓库。

## 打包

```powershell
.\packaging\scripts\build-installer.ps1
```

脚本会：

1. 发布 WPF 主程序。
2. 复制 FFmpeg/ffprobe。
3. 复制 OpenCV worker。
4. 复制 ProPainter 运行环境与模型权重。
5. 调用 Inno Setup 生成安装包。

输出目录：`release\installer`。

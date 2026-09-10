# 视频去水印 Windows

面向 Windows 的视频去水印桌面工具，提供 FFmpeg、OpenCV 与 ProPainter
三级修复能力，并支持多任务后台处理、动态水印关键帧和长时间视频分片修复。

## 功能

- FFmpeg Delogo：短期 Logo、字幕角标，速度优先。
- OpenCV AI 修复：ROI 局部修复，支持多线程和自动检测。
- ProPainter 强力修复：时空一致视频修复，适合复杂背景和残留水印。
- 多视频队列：支持多选取消、移除和优先处理。
- 处理时间预计：显示当前视频预计耗时与动态剩余时间。
- 单个主程序窗口：后台 worker 无窗口运行。
- 标准 Windows 安装包：开始菜单、桌面快捷方式和卸载入口。

## 目录结构

```text
src/                 WPF 主程序及历史版本源码
engines/             AI 与 ProPainter 引擎源码/引导脚本
packaging/           便携版和 Inno Setup 安装包脚本
docs/                架构、构建、发布和运维文档
site/                静态展示页，可部署到 GitHub Pages
assets/              品牌 Logo 与图标
scripts/             开发辅助脚本
third_party/         第三方许可证和声明
release/             本地发布产物目录（不提交二进制）
```

## 本地构建

环境要求：

- Windows 10/11 x64
- .NET 8 SDK
- Python 3.12
- Inno Setup 6.7.3（仅构建安装包时需要）
- NVIDIA GPU（强力修复模式建议 8GB 以上显存）

构建主程序：

```powershell
dotnet publish .\src\WatermarkRemover.Windows\WatermarkRemoverWindows.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\release\staging\app
```

构建 OpenCV worker：

```powershell
.\packaging\scripts\build-opencv-worker.ps1 `
  -PythonExecutable C:\Path\To\python.exe `
  -OutputDirectory .\release\staging\ai
```

准备 ProPainter 开发环境：

```powershell
.\engines\heavy\bootstrap.ps1
```

构建标准安装包：

```powershell
.\packaging\scripts\build-installer.ps1
```

安装包输出到 `release\installer`，不会提交到 Git 仓库。

## 下载

正式安装包和便携版通过 GitHub Releases 发布。仓库只保存源码、构建脚本、
文档和静态页面，不保存数 GB 的 PyTorch/模型/安装包二进制。

最新安装包：
https://github.com/tenaciousMa/watermark-remover-windows/releases/latest

安装器超过 GitHub 单文件限制，Release 中拆成两个分卷。下载后运行
`join-and-install.ps1`，脚本会自动合并、校验 SHA256 并启动安装程序。

GitHub Actions 工作流以 `.example` 形式保存在
`.github/workflows/`，启用方式见 `docs/zh-CN/github-actions.md`。

## 许可与第三方组件

本项目自身的许可证见 [LICENSE](LICENSE)。

ProPainter 使用 NTU S-Lab License，仅允许非商业用途。发布或商用前必须阅读
[third_party/ProPainter/LICENSE](third_party/ProPainter/LICENSE)。

FFmpeg、OpenCV、PyTorch 等第三方组件的许可证见
[THIRD_PARTY_NOTICES.md](THIRD_PARTY_NOTICES.md)。

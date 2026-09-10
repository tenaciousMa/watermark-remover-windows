# Watermark Remover Windows v0.2

一个完全本地运行的 Windows 视频区域修复工具。主程序采用 **C# / WPF / .NET 8**，快速模式使用 **FFmpeg delogo**；算法修复模式使用 **OpenCV Telea inpaint** 逐帧修复，不依赖 ProPainter、PyTorch 或模型权重。

> 请只处理你有权编辑、去除标记或再利用的素材，并遵守素材来源、平台与第三方依赖的许可条款。

## v0.2 新增

- 已有水印框可直接拖动。
- 选中框后，右下角手柄可缩放。
- 每个区域支持多个关键帧。
- 关键帧之间自动线性插值 X / Y / W / H。
- FFmpeg 动态模式直接使用 `t` 表达式逐帧改变 delogo 区域，无需切段。
- 可选算法修复模式：自动生成逐帧 Mask -> OpenCV inpaint -> 重新封装原视频音轨。
- 算法修复模式支持静态框与动态关键帧框。
- 新增自动检测文字 / Logo 候选区域，可在当前帧识别后加入列表并继续手动微调。
- 新增 `InstallDependencies.exe`，可双击安装 FFmpeg、Python 与 OpenCV 依赖。
- `USE_THIS_...AllInOne...exe` 已内置 FFmpeg、ffprobe 与独立 OpenCV 引擎，正常使用无需另外下载或安装 Python/OpenCV。
- 启动解压、依赖检查、自动检测、逐帧修复和最终封装均提供分段进度反馈。
- UI 新增白色、黑色、Catppuccin 复古蓝三套主题，可在左侧栏一键切换。

## 推荐环境

- Windows 10 / Windows 11 x64
- Visual Studio 2022（安装“.NET 桌面开发”）或 .NET 8 SDK（用于开发/发布主程序）
- 最终 All-in-One 成品无需额外运行环境
- 仅源码开发或精简版需要通过 `InstallDependencies.exe` 配置 FFmpeg、Python、OpenCV 与 NumPy

## 1. 首次安装依赖

最终用户推荐直接双击文件名以 `USE_THIS_` 开头的 All-in-One 成品，无需执行本节。仅源码开发或精简版需要双击：

```text
InstallDependencies.exe
```

它会按顺序执行：

```text
检查 / 安装 Python
        ↓
检查 FFmpeg；已存在则跳过下载
        ↓
运行 setup-ai.ps1
        ↓
安装 OpenCV / NumPy
```

如果需要只走脚本，也可以在项目目录打开 PowerShell：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\setup-dependencies.ps1
```

## 2. 快速模式使用

### 单独配置 FFmpeg

在项目目录打开 PowerShell：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\setup-ffmpeg.ps1
```

脚本会把 `ffmpeg.exe` 与 `ffprobe.exe` 放进 `tools`。

### 运行

用 Visual Studio 打开：

```text
WatermarkRemoverWindows.csproj
```

运行后：

1. 导入或拖入视频。
2. 可点击“自动检测文字 / Logo”生成候选框，也可以手动拖出矩形框。
3. 已有框可直接拖动；选中后拖右下角黄色手柄缩放。
4. 固定水印无需关键帧，直接开始处理。
5. 选择输出文件并点击“开始去水印”。

## 3. 动态水印

动态框的典型操作：

1. 把时间轴移动到水印运动开始位置。
2. 框好区域并点击“当前时间添加 / 更新关键帧”。
3. 把时间轴移动到后一个时间点。
4. 直接拖动或缩放该框到新的水印位置；程序会自动创建当前时间关键帧。
5. 继续增加必要的关键帧。
6. FFmpeg 模式和算法修复模式都会在相邻关键帧之间逐帧线性插值。

关键帧不需要每帧添加。对于匀速或近似平滑移动的 Logo，少量关键帧通常即可描述轨迹。

## 4. 算法修复模式（可选）

算法修复模式不是程序运行的必需项。快速模式不需要 Python 环境。

首次配置：

```powershell
Set-ExecutionPolicy -Scope Process Bypass
.\setup-ai.ps1
```

脚本会把本地算法运行环境安装到：

```text
%LOCALAPPDATA%\WatermarkRemoverWindows\ai
```

其中包括：

- Python virtual environment
- 本项目的 `ai_runner.py`
- OpenCV headless
- NumPy

### 算法处理流程

```text
WPF 框选 / 关键帧
        ↓
mask_spec.json
        ↓
逐帧插值生成 Mask
        ↓
OpenCV Telea inpaint 修复
        ↓
FFmpeg 重新封装原视频音轨
        ↓
MP4 输出
```

算法修复模式安装轻、下载稳定，适合固定 Logo、字幕、小面积文字。复杂纹理或大面积遮挡仍可能需要更强的视频时序模型。

### 自动检测候选区域

导入视频后，点击左侧“自动检测文字 / Logo”。程序会分析当前时间点画面，把疑似文字、字幕或 Logo 的候选框加入区域列表。候选框可以继续拖动、缩放，也可以配合关键帧描述运动水印。

当前检测采用传统 OpenCV 高对比边缘与形态学合并，参考了 VSR / WatermarkRemover-AI Omni “检测 + 修复 + GUI”的工作流思路，以及 IOPaint 的本地修复方向，但没有把这些项目作为运行时依赖。

## 5. 主题切换

左侧栏提供三种主题按钮：

- 白色
- 黑色
- 复古蓝（Catppuccin）

也可以通过键盘切换：`Ctrl+1` 白色，`Ctrl+2` 黑色，`Ctrl+3` 复古蓝。

主题会同步应用到侧栏、右侧设置区、输入框、列表、预览提示与水印框选颜色。

## 6. 发布 Windows x64

配置好 .NET 8 SDK 后运行：

```powershell
.\publish-win-x64.ps1
```

输出目录：

```text
publish\win-x64
```

发布脚本会先生成 `InstallDependencies.exe`，再发布主程序。

## 7. 关于 FFmpeg 动态表达式

v0.2 会为动态区域构建类似下面的滤镜表达式：

```text
delogo=x='if(lt(t,3),100+(200-100)*(t-1)/(3-1),200)':...
```

因此水印框位置是按视频时间 `t` 在帧级求值的，而不是把视频切成很多片段后跳变。

## 8. 已知限制

- 当前缩放手柄为右下角单手柄，后续可扩展到 8 向控制点。
- 关键帧目前使用线性插值，不包含贝塞尔轨迹或自动目标跟踪。
- OpenCV 算法修复不是深度视频时序模型，对大面积遮挡、快速运动背景的自然度有限。
- 自动检测是候选框生成，不保证一次命中所有水印；复杂视频建议手动微调。
- 主题选择当前不持久化，重新打开程序会回到默认 Catppuccin 复古蓝主题。

## 9. 第三方许可

请阅读 `THIRD_PARTY_NOTICES.md`。本项目默认不再下载或调用 ProPainter、PyTorch 或模型权重。

# Third-Party Notices

## FFmpeg

本项目可调用外部 FFmpeg / ffprobe 可执行文件进行视频读取、过滤和编码。FFmpeg 的实际许可取决于所使用的构建配置及其中启用的组件。发布软件前请核对你选择的 FFmpeg 二进制构建及相应 LGPL/GPL 要求。

源码开发流程可通过 `setup-ffmpeg.ps1` 或 `InstallDependencies.exe` 获取第三方 Windows build。最终 All-in-One 成品内置 FFmpeg 与 ffprobe，发布软件前仍需核对所用构建的 LGPL/GPL 条款并随发行物提供相应许可材料。

## OpenCV

- Project: https://opencv.org/
- Repository: https://github.com/opencv/opencv

算法修复模式使用 `opencv-python-headless` 与 NumPy 在本机逐帧执行传统图像修复，不下载 ProPainter、PyTorch 或模型权重。最终 All-in-One 通过 PyInstaller 封装独立 OpenCV worker；OpenCV、NumPy、Python、PyInstaller 及其传递依赖分别适用各自许可。

## 参考项目方向

本版本的“自动检测候选区域 + 本地修复 + Windows GUI”方向参考了 Video Subtitle Remover、WatermarkRemover-AI Omni、IOPaint 等开源工具的产品思路；它们不是本项目的运行时依赖。

## Python / OpenCV and transitive dependencies

精简版的本地算法模式会在用户运行安装器后创建 Python 虚拟环境；最终 All-in-One 则使用内置独立 worker。相关包分别适用各自许可；商业或再分发前应单独审查。

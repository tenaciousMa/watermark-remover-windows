# 架构说明

## 进程模型

```text
WatermarkRemoverWindows.exe
  |
  |-- 主 UI 进程（唯一可见窗口）
  |-- worker 进程（--worker，无窗口）
        |
        |-- ai_runner.exe（OpenCV 模式）
        |-- heavy_runner.py（ProPainter 模式）
        |-- ffmpeg.exe（FFmpeg 模式/音频封装）
```

主界面不会等待视频处理，worker 通过 `status.json` 向主进程报告状态。

## 处理模式

### FFmpeg Delogo

适合固定 Logo、字幕角标和规则区域，速度快，输出稳定。

### OpenCV AI

生成逐帧 Mask，仅对水印周边地区执行 Telea/NS 修复，并使用多个 CPU
工作线程。适合中等复杂度水印。

### ProPainter

使用光流、递归流补全、图像传播和 Transformer 做时空视频重建。工程上
按短片段处理，避免把完整长视频读入内存；完成后再按原始分辨率回贴并保留
原音频。

## 队列

每个任务包含独立工作目录和状态文件。队列支持：

- 并发数限制
- 多选取消/移除
- 等待任务优先处理
- 失败原因展示
- 预计剩余时间

ProPainter 任务在单个应用实例内串行执行，避免 6GB 显卡被多个任务同时占满。

## 发布策略

Git 仓库只保存源码、脚本、文档和站点。FFmpeg、PyTorch、模型权重、便携版
和安装包作为 GitHub Release assets 发布。

# Watermark Remover v0.4 任务说明

更新日期：2026-09-10

## 与旧版本的关系

- v0.2 源码与 `outputs` 保持不变。
- v0.3 源码与 `outputs-v0.3` 保持不变。
- v0.4 源码独立在 `work\WatermarkRemoverWindows_v0.4`。
- ProPainter/PyTorch 独立开发环境在 `work\v0.4-propainter`，未写入旧项目。

## 本版本新增

1. 第三种“强力修复”模式：ProPainter AI。
   - 内置 RAFT 光流、Recurrent Flow Completion、ProPainter 视频修复。
   - 默认保持原始画质；高分辨率视频使用“水印周边智能裁剪”降低显存压力，显存不足时自动降内部分辨率重试。
   - FFmpeg 快速模式与 OpenCV Telea 模式都保留。
2. 后台队列操作简化：
   - 一个“取消 / 移除选中”按钮支持多选，合并处理中取消、等待移除、失败/完成移除。
   - “优先处理选中”可让等待任务越过当前等待顺序开始。
   - 失败项显示“失败原因：…”，悬停可查看完整错误。
3. 新 logo：
   - 生成文件：`Assets\AppLogo.png`、`Assets\AppLogo.ico`。
   - 已设置为窗口图标、程序图标和左侧页眉图标。
4. 强力任务在同一时间只启动一个，避免 6GB 显卡被多个任务同时挤爆。

## 已验证

- PyTorch `2.5.1+cu121`，CUDA 可用，RTX 4050 Laptop GPU。
- ProPainter 官方 running_car 样例：292 帧完成推理并输出视频。
- v0.4 worker `--mode heavy` 在 1920x1080 测试视频上完整跑通：裁剪、遮罩、ProPainter、回封音频流程。
- 便携版重处理测试通过，状态文件最终为 `done/100/处理完成`。
- 主界面启动后持续运行，无额外 worker 窗口。

## AI 模式修复

旧版 OpenCV AI runner 在用户指定时间范围时仍会从视频第 0 帧循环到最后一帧，
只是 Mask 只覆盖指定时间段。长视频因此看起来像卡死，Windows 也会把
`ai_runner.exe` 标记为长时间高内存运行。

2026-09-10 已修复：

- AI runner 现在只读取并处理 `start` 到 `end` 之间的帧。
- Telea 修复改为只计算水印框周围的小区域，而不是整帧。
- 输出时长与所选处理时间段一致，不再把完整原视频重新慢跑一遍。
- 用户视频 `C:\Users\user\Desktop\临时\1.mp4`（HEVC 928x544，210 秒）
  的 2 秒范围测试从约 75 秒降到约 4.5 秒，输出时长准确为 2 秒。

## 长视频强修复修复

用户队列中实际留下了失败状态：

```text
torchvision.io.read_video(...)
av.error.MemoryError: [Errno 12] Cannot allocate memory
```

原因是 ProPainter 官方推理脚本会把当前输入视频一次性读入内存。210 秒的
928x544 HEVC 视频会占用数 GB 内存，超过机器限制。

已修复：

- 强力模式按动态计算的短片段分段处理，再拼接输出；每段约 10 秒，
  并根据处理面积和内存预算自动缩短。
- 6GB 显存默认使用更小的 18 万像素处理面积和更保守的 ProPainter
  参数，避免 GPU 共享内存抖动。
- 15 秒、928x544 HEVC 测试已完整完成两段处理并拼接，输出时长精确
  为 15 秒。
- FFmpeg Delogo 的边界框增加右下角 1 像素安全约束，修复自动检测区域
  顶到边缘时出现的 `Logo area is outside of the frame`。

## 10 分钟视频支持

强力模式不把完整视频读入内存，而是按 10 秒短片段循环处理，因此时长不再
受单次内存分配限制。10 分钟视频会分成约 60 个片段，每个片段完成后立即
清理原始片段和 Mask，只保留小型修复结果用于最终拼接。

在 RTX 4050 Laptop 6GB 上，10 分钟 928x544 级别视频的预计耗时约
1 小时上下，具体取决于水印区域大小和背景运动复杂度。快速模式仍可在
几十秒到数分钟内完成同类视频。

### 2026-09-10 性能优化

- AI 模式增加最多 4 个并行修复线程，2 秒 HEVC 测试从约 4.5 秒降到
  约 3.5 秒。
- AI 模式只处理用户选定时间范围和 Mask 周边区域，不再整帧扫描。
- ProPainter 的分片、临时文件清理和 6GB 显存参数保留，保证长视频不会
  因为一次性读取而失败。
- 强力 ProPainter 模式的耗时主要来自神经网络逐帧时空重建，6GB 笔记本
  显卡无法同时做到“最高质量”和“接近实时”。10 分钟视频建议：
  - 画质优先：ProPainter，预计约 1 小时。
  - 速度优先：OpenCV 算法修复，预计约 15 至 25 分钟。
  - 极速处理：FFmpeg 快速模式，通常数分钟。

### 预计时间与进度显示

- 编辑页选择视频、模式、处理时间段或水印区域后，会立即显示预计处理时间。
- 队列任务创建时会保存预计总时间。
- 处理中任务根据已经运行的耗时和当前进度动态计算预计剩余时间。
- 队列顶部显示所有活动任务的合计预计剩余时间。
- 队列每一项在进度条下方显示“预计处理时间”或“预计剩余”。
- 全局 ProgressBar 模板已强制为 `LeftToRight`，指示条从左侧向右增长，
  不再从中间展开。

## Windows 标准安装包

- Inno Setup 脚本：`installer\WatermarkRemoverWindows.iss`
- 编译器：Inno Setup 6.7.3
- 安装类型：当前用户安装，无需管理员权限
- 默认安装目录：
  `%LOCALAPPDATA%\Programs\WatermarkRemoverWindows`
- 包含完整程序、FFmpeg、OpenCV worker、PyTorch 和 ProPainter 模型
- 自动创建开始菜单项、可选桌面快捷方式和标准卸载入口

输出文件：

```text
outputs-v0.4\Installer\WatermarkRemoverWindows_v0.4_Setup.exe
```

编译时间：约 10 分钟，大小约 2.7GB。

已通过静默安装和静默卸载验证：

- 安装退出码 `0`
- 安装后文件约 5.48GB、22629 个文件
- 主程序、AI worker、FFmpeg、PyTorch、ProPainter 模型和卸载器均存在
- 卸载退出码 `0`，测试安装目录已完整清除

## 运行方式

便携版路径：

```text
outputs-v0.4\WatermarkRemoverWindows_v0.4_Portable
```

直接双击 `WatermarkRemoverWindows.exe`。目录不可只复制 exe，需要保留：

```text
ai\       OpenCV 自动检测/算法修复
tools\    ffmpeg / ffprobe
heavy\    ProPainter + PyTorch 运行环境
```

## 构建便携版

```powershell
cd work\WatermarkRemoverWindows_v0.4
.\publish-portable-v0.4.ps1
```

脚本会重新 `dotnet publish`，然后把 `..\v0.4-propainter` 完整复制为
`heavy` 运行环境。

## 后续建议

- 如果需要最终单 EXE，应先把 PyTorch runtime 从 5.4GB 瘦身，并测试超大 payload
  的 `csc` 嵌入与解压稳定性；当前优先交付可稳定运行的便携完整目录。
- 如果面向商业发布，ProPainter 的 NTU S-Lab License 只允许非商业用途，
  需要先向作者申请商业授权或替换为商业许可模型。

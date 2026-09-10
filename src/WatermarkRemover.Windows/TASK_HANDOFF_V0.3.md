# Watermark Remover v0.3 任务说明

更新日期：2026-09-09

## 与 v0.2 的关系

- v0.2 原版保留在 `work\WatermarkRemoverWindows_v0.2`，交付文件仍在 `outputs`。
- v0.3 独立放在 `work\WatermarkRemoverWindows_v0.3`，交付文件在 `outputs-v0.3`。
- v0.3 内部缓存版本为 `v0.3-process-worker-r8`，与 v0.2 的 r2-r5 缓存互不覆盖。

## v0.3 新增

1. 每个视频由独立的 `WatermarkRemoverWindows.exe --worker` 子进程处理。
2. 主界面支持一次导入多个视频，任务进入处理队列。
3. 主窗口内提供“编辑页 / 后台处理进度页”切换，只有一个程序窗口，队列列表实时显示每个视频的状态、进度、模式和错误信息。
4. 处理在后台 worker 中执行，主 UI 不等待视频处理，可继续导入和编辑。
5. 队列并发数默认 `min(3, ProcessorCount / 2)`，每个任务独立写入状态文件供 UI 轮询。
6. 没有手动框选的任务会在 worker 中先自动检测水印再处理。
7. 同一个视频再次点击处理时会创建新的独立任务，并自动使用不重复的输出文件名，不会覆盖正在运行的前一个任务。
8. 开始处理不会自动切换页面或弹出第二个窗口；需要查看进度时手动点击“处理队列”按钮。

## 关键代码

- `Models\VideoJob.cs`：队列任务、状态、区域快照。
- `Services\WorkerJobRunner.cs`：worker 参数解析、状态文件、自动检测和实际处理。
- `MainWindow.xaml(.cs)`：多选导入、队列按钮、任务调度和进度轮询。
- `App.xaml.cs`：启动时识别 `--worker`，无界面直接执行后台任务。

## 构建

```powershell
dotnet restore .\WatermarkRemoverWindows.csproj -r win-x64 --configfile .\NuGet.Config
dotnet publish .\WatermarkRemoverWindows.csproj -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
  -o .\publish\win-x64 --configfile .\NuGet.Config --no-restore
.\package-all-in-one.ps1
```

最终交付文件：

```text
outputs-v0.3\WatermarkRemoverWindows_v0.3_AllInOne.exe
SHA256：76F62FA026049BDE265F0B505AA77A0E8F0CFD8BEF73CDC72584FD3FDC881C2F
```

using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using WatermarkRemoverWindows.Models;

namespace WatermarkRemoverWindows.Services;

public sealed class AiInpaintService
{
    public bool IsConfigured(out string reason)
    {
        var bundledWorker = GetBundledWorkerPath();
        if (bundledWorker is not null)
        {
            reason = "内置 OpenCV 算法引擎已就绪";
            return true;
        }

        var aiRoot = GetAiRoot();
        var python = Path.Combine(aiRoot, "venv", "Scripts", "python.exe");
        var runner = Path.Combine(aiRoot, "ai_runner.py");
        if (!File.Exists(python)) { reason = "未找到本地算法 Python 环境，请先运行 InstallDependencies.exe 或 setup-ai.ps1。"; return false; }
        if (!File.Exists(runner)) { reason = "缺少 ai/ai_runner.py。"; return false; }
        reason = "本地算法环境已就绪";
        return true;
    }

    public static string GetAiRoot() =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WatermarkRemoverWindows", "ai");

    private static string? GetBundledWorkerPath()
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "ai", "ai_runner.exe"),
            Path.Combine(GetAiRoot(), "ai_runner.exe"),
        };
        return candidates.FirstOrDefault(File.Exists);
    }

    private static RunnerCommand GetRunnerCommand()
    {
        var worker = GetBundledWorkerPath();
        if (worker is not null)
            return new RunnerCommand(worker, Path.GetDirectoryName(worker) ?? AppContext.BaseDirectory, null);

        var aiRoot = GetAiRoot();
        return new RunnerCommand(
            Path.Combine(aiRoot, "venv", "Scripts", "python.exe"),
            aiRoot,
            Path.Combine(aiRoot, "ai_runner.py"));
    }

    public async Task<IReadOnlyList<DetectedWatermarkRegion>> DetectAsync(
        string inputPath,
        VideoInfo info,
        double seconds,
        CancellationToken cancellationToken,
        IProgress<double>? progress = null)
    {
        if (!IsConfigured(out var reason))
            throw new InvalidOperationException(reason);

        var command = GetRunnerCommand();

        var psi = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (command.ScriptPath is not null)
            psi.ArgumentList.Add(command.ScriptPath);
        foreach (var arg in new[]
        {
            "--detect", "--video", inputPath, "--time", seconds.ToString(CultureInfo.InvariantCulture),
            "--display-width", info.DisplayWidth.ToString(CultureInfo.InvariantCulture),
            "--display-height", info.DisplayHeight.ToString(CultureInfo.InvariantCulture),
            "--max-regions", "8", "--padding", "8"
        })
            psi.ArgumentList.Add(arg);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动本地算法检测进程。");
        progress?.Report(3);

        var errors = new Queue<string>();
        var errorTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                lock (errors)
                {
                    errors.Enqueue(line);
                    while (errors.Count > 30) errors.Dequeue();
                }
            }
        }, cancellationToken);

        string? detectionLine = null;
        try
        {
            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;

                if (line.StartsWith("PROGRESS=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(line["PROGRESS=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                {
                    progress?.Report(Math.Clamp(p, 0, 100));
                }
                else if (line.StartsWith("DETECTIONS=", StringComparison.OrdinalIgnoreCase))
                {
                    detectionLine = line;
                }
            }

            await process.WaitForExitAsync(cancellationToken);
            await errorTask;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (errors) detail = string.Join(Environment.NewLine, errors);
            throw new InvalidOperationException($"自动检测失败（ExitCode={process.ExitCode}）。\n{detail}");
        }

        if (detectionLine is null)
            throw new InvalidOperationException("自动检测没有返回候选区域。");

        return JsonSerializer.Deserialize<List<DetectedWatermarkRegion>>(
                detectionLine["DETECTIONS=".Length..],
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? new List<DetectedWatermarkRegion>();
    }

    public async Task RunAsync(
        string inputPath,
        string outputPath,
        IReadOnlyCollection<WatermarkRegion> regions,
        VideoInfo info,
        TimeSpan start,
        TimeSpan end,
        bool expandMask,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        if (regions.Count == 0)
            throw new InvalidOperationException("请至少框选一个水印区域。");
        if (!IsConfigured(out var reason))
            throw new InvalidOperationException(reason);
        progress?.Report(1);

        var command = GetRunnerCommand();
        var ffmpeg = ToolLocator.Find("ffmpeg.exe")
            ?? throw new FileNotFoundException("本地算法修复也需要 ffmpeg.exe，请先运行 InstallDependencies.exe 或 setup-ffmpeg.ps1。", "ffmpeg.exe");

        var tempRoot = Path.Combine(Path.GetTempPath(), "WatermarkRemoverWindows", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempRoot);
        var specPath = Path.Combine(tempRoot, "mask_spec.json");

        var spec = new
        {
            width = info.DisplayWidth,
            height = info.DisplayHeight,
            duration = info.Duration.TotalSeconds,
            start = start.TotalSeconds,
            end = end.TotalSeconds,
            regions = regions.Select(r => new
            {
                name = r.Name,
                x = r.X,
                y = r.Y,
                width = r.Width,
                height = r.Height,
                keyframes = r.Keyframes.OrderBy(k => k.TimeSeconds).Select(k => new
                {
                    time = k.TimeSeconds,
                    x = k.X,
                    y = k.Y,
                    width = k.Width,
                    height = k.Height,
                }).ToArray()
            }).ToArray()
        };
        await File.WriteAllTextAsync(specPath, JsonSerializer.Serialize(spec, new JsonSerializerOptions { WriteIndented = true }), cancellationToken);
        progress?.Report(3);

        var psi = new ProcessStartInfo
        {
            FileName = command.FileName,
            WorkingDirectory = command.WorkingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        if (command.ScriptPath is not null)
            psi.ArgumentList.Add(command.ScriptPath);
        foreach (var arg in new[]
        {
            "--video", inputPath, "--spec", specPath, "--output", outputPath,
            "--ffmpeg", ffmpeg, "--method", "telea", "--radius", "3.0"
        })
            psi.ArgumentList.Add(arg);
        if (expandMask) psi.ArgumentList.Add("--expand-mask");

        using var process = new Process { StartInfo = psi };
        if (!process.Start()) throw new InvalidOperationException("无法启动本地算法修复进程。");

        var errors = new Queue<string>();
        var errorTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                lock (errors)
                {
                    errors.Enqueue(line);
                    while (errors.Count > 40) errors.Dequeue();
                }
            }
        }, cancellationToken);

        try
        {
            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;
                if (line.StartsWith("PROGRESS=", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(line["PROGRESS=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var p))
                    progress?.Report(Math.Clamp(p, 0, 100));
            }
            await process.WaitForExitAsync(cancellationToken);
            await errorTask;
        }
        catch (OperationCanceledException)
        {
            try { if (!process.HasExited) process.Kill(entireProcessTree: true); } catch { }
            throw;
        }
        finally
        {
            try { Directory.Delete(tempRoot, recursive: true); } catch { }
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (errors) detail = string.Join(Environment.NewLine, errors);
            throw new InvalidOperationException($"本地算法修复失败（ExitCode={process.ExitCode}）。\n{detail}");
        }
    }

    private sealed record RunnerCommand(string FileName, string WorkingDirectory, string? ScriptPath);
}

public sealed record DetectedWatermarkRegion(int X, int Y, int Width, int Height);

using System.Diagnostics;
using System.Globalization;
using System.IO;

namespace WatermarkRemoverWindows.Services;

/// <summary>
/// Runs the ProPainter-powered strong repair engine in a separate bundled Python runtime.
/// </summary>
public sealed class HeavyInpaintService
{
    public static string? FindEngineRoot()
    {
        var candidates = new[]
        {
            Environment.GetEnvironmentVariable("WMR_HEAVY_ROOT"),
            Path.Combine(AppContext.BaseDirectory, "heavy"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WatermarkRemoverWindows", "heavy"),
        };

        foreach (var candidate in candidates)
        {
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (File.Exists(Path.Combine(candidate, "heavy_runner.py")) &&
                File.Exists(Path.Combine(candidate, ".venv", "Scripts", "python.exe")))
                return Path.GetFullPath(candidate);
        }

        return null;
    }

    public bool IsReady(out string reason)
    {
        var root = FindEngineRoot();
        if (root is null)
        {
            reason = "未找到强力修复引擎。v0.4 完整安装包内置 ProPainter + PyTorch 运行环境；开发模式请设置 WMR_HEAVY_ROOT。";
            return false;
        }

        var repo = Path.Combine(root, "ProPainter", "inference_propainter.py");
        if (!File.Exists(repo))
        {
            reason = "强力修复引擎缺少 ProPainter 推理脚本。";
            return false;
        }
        var weights = Path.Combine(root, "ProPainter", "weights");
        foreach (var weight in new[] { "ProPainter.pth", "raft-things.pth", "recurrent_flow_completion.pth" })
        {
            if (!File.Exists(Path.Combine(weights, weight)))
            {
                reason = $"强力修复引擎缺少模型权重 {weight}。";
                return false;
            }
        }

        reason = "ProPainter 强力修复引擎已就绪";
        return true;
    }

    public async Task RunAsync(
        string inputPath,
        string outputPath,
        string regionsFile,
        TimeSpan start,
        TimeSpan end,
        bool expandMask,
        string statusFile,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        var root = FindEngineRoot()
            ?? throw new InvalidOperationException("未找到强力修复引擎，无法启动 ProPainter。");
        var python = Path.Combine(root, ".venv", "Scripts", "python.exe");
        var script = Path.Combine(root, "heavy_runner.py");
        var ffmpeg = ToolLocator.Find("ffmpeg.exe")
            ?? throw new FileNotFoundException("强力修复需要 ffmpeg.exe。", "ffmpeg.exe");
        var ffprobe = ToolLocator.Find("ffprobe.exe")
            ?? throw new FileNotFoundException("强力修复需要 ffprobe.exe。", "ffprobe.exe");

        var psi = new ProcessStartInfo
        {
            FileName = python,
            WorkingDirectory = root,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        psi.Environment["WMR_HEAVY_ROOT"] = root;
        psi.Environment["PYTHONIOENCODING"] = "utf-8";
        foreach (var arg in new[]
        {
            script,
            "--input", inputPath,
            "--output", outputPath,
            "--regions-file", regionsFile,
            "--start", start.TotalSeconds.ToString(CultureInfo.InvariantCulture),
            "--end", end.TotalSeconds.ToString(CultureInfo.InvariantCulture),
            "--ffmpeg", ffmpeg,
            "--ffprobe", ffprobe,
        })
            psi.ArgumentList.Add(arg);
        if (expandMask)
            psi.ArgumentList.Add("--expand-mask");

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException("无法启动强力修复后台进程。");

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
                    while (errors.Count > 60) errors.Dequeue();
                }
            }
        }, cancellationToken);

        var status = "正在启动强力修复引擎…";
        progress?.Report(0);
        try
        {
            while (!process.StandardOutput.EndOfStream)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null) break;

                if (line.StartsWith("STATUS=", StringComparison.OrdinalIgnoreCase))
                {
                    status = line["STATUS=".Length..];
                }
                else if (line.StartsWith("PROGRESS=", StringComparison.OrdinalIgnoreCase) &&
                         double.TryParse(line["PROGRESS=".Length..], NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
                {
                    var clamped = Math.Clamp(value, 0, 100);
                    progress?.Report(clamped);
                    WorkerJobRunner.WriteStatus(statusFile, "running", clamped, status, null);
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
            throw new InvalidOperationException($"强力修复失败（ExitCode={process.ExitCode}）。\n{detail}");
        }
    }
}

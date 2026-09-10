using System.Diagnostics;
using System.Globalization;
using System.IO;
using WatermarkRemoverWindows.Models;

namespace WatermarkRemoverWindows.Services;

public sealed record EncodeOptions(string Preset, int Crf);

public sealed class FfmpegService
{
    public async Task RunDelogoAsync(
        string inputPath,
        string outputPath,
        IReadOnlyCollection<WatermarkRegion> regions,
        VideoInfo videoInfo,
        TimeSpan? start,
        TimeSpan? end,
        EncodeOptions options,
        IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        if (regions.Count == 0)
            throw new InvalidOperationException("请至少框选一个水印区域。");

        progress?.Report(1);
        var ffmpeg = ToolLocator.Find("ffmpeg.exe")
            ?? throw new FileNotFoundException("找不到 ffmpeg.exe。请先运行 setup-ffmpeg.ps1，或把 ffmpeg.exe 放入 tools 文件夹。", "ffmpeg.exe");

        var filter = BuildFilterGraph(regions, videoInfo, start, end);
        progress?.Report(3);
        var psi = new ProcessStartInfo
        {
            FileName = ffmpeg,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };

        foreach (var arg in new[]
        {
            "-y", "-hide_banner", "-nostdin", "-i", inputPath,
            "-map", "0:v:0", "-map", "0:a?",
            "-vf", filter,
            "-c:v", "libx264", "-preset", options.Preset, "-crf", options.Crf.ToString(CultureInfo.InvariantCulture),
            "-c:a", "aac", "-b:a", "192k", "-map_metadata", "0", "-movflags", "+faststart",
            "-progress", "pipe:1", "-nostats", outputPath
        })
        {
            psi.ArgumentList.Add(arg);
        }

        await RunProcessWithProgressAsync(psi, videoInfo.Duration, progress, cancellationToken);
    }

    public static string BuildFilterGraph(
        IEnumerable<WatermarkRegion> regions,
        VideoInfo videoInfo,
        TimeSpan? start,
        TimeSpan? end)
    {
        var parts = new List<string>();

        foreach (var region in regions)
        {
            var keyframes = region.Keyframes
                .OrderBy(k => k.TimeSeconds)
                .Select(k => new RegionKeyframe
                {
                    TimeSeconds = Math.Clamp(k.TimeSeconds, 0, videoInfo.Duration.TotalSeconds),
                    X = k.X,
                    Y = k.Y,
                    Width = k.Width,
                    Height = k.Height,
                })
                .ToList();

            string xExpr;
            string yExpr;
            string wExpr;
            string hExpr;

            if (keyframes.Count == 0)
            {
                var safe = Clamp(region.BaseGeometry, videoInfo.DisplayWidth, videoInfo.DisplayHeight);
                xExpr = safe.X.ToString(CultureInfo.InvariantCulture);
                yExpr = safe.Y.ToString(CultureInfo.InvariantCulture);
                wExpr = safe.Width.ToString(CultureInfo.InvariantCulture);
                hExpr = safe.Height.ToString(CultureInfo.InvariantCulture);
            }
            else
            {
                // 逐关键帧先做边界约束，然后生成基于 FFmpeg 变量 t 的分段线性表达式。
                var safeFrames = keyframes
                    .Select(k => new TimedGeometry(k.TimeSeconds, Clamp(k.ToGeometry(), videoInfo.DisplayWidth, videoInfo.DisplayHeight)))
                    .ToList();

                xExpr = BuildInterpolatedExpression(safeFrames, g => g.X);
                yExpr = BuildInterpolatedExpression(safeFrames, g => g.Y);
                wExpr = BuildInterpolatedExpression(safeFrames, g => g.Width);
                hExpr = BuildInterpolatedExpression(safeFrames, g => g.Height);
            }

            var part = $"delogo=x='{xExpr}':y='{yExpr}':w='{wExpr}':h='{hExpr}':show=0";

            if (start.HasValue && end.HasValue && end.Value > start.Value)
            {
                var s = F(start.Value.TotalSeconds);
                var e = F(end.Value.TotalSeconds);
                part += $":enable='between(t,{s},{e})'";
            }

            parts.Add(part);
        }

        return string.Join(',', parts);
    }

    private static string BuildInterpolatedExpression(IReadOnlyList<TimedGeometry> frames, Func<RegionGeometry, int> selector)
    {
        if (frames.Count == 0) return "0";
        if (frames.Count == 1) return selector(frames[0].Geometry).ToString(CultureInfo.InvariantCulture);

        // if(lt(t,t1), lerp(k0,k1), if(lt(t,t2), lerp(k1,k2), ... lastValue))
        // 表达式在 filtergraph 的单引号内，因此逗号可以安全保留。
        string expr = selector(frames[^1].Geometry).ToString(CultureInfo.InvariantCulture);
        for (var i = frames.Count - 2; i >= 0; i--)
        {
            var a = frames[i];
            var b = frames[i + 1];
            var av = selector(a.Geometry);
            var bv = selector(b.Geometry);
            var dt = Math.Max(0.000001, b.TimeSeconds - a.TimeSeconds);
            var lerp = $"{av}+({bv - av})*(t-{F(a.TimeSeconds)})/{F(dt)}";
            expr = $"if(lt(t,{F(a.TimeSeconds)}),{av},if(lt(t,{F(b.TimeSeconds)}),{lerp},{expr}))";
        }
        return expr;
    }

    private static RegionGeometry Clamp(RegionGeometry r, int maxW, int maxH)
    {
        // delogo 的有效区域不能从 x=0 / y=0 开始；保留至少 1 px 边界。
        var x = Math.Clamp(r.X, 1, Math.Max(1, maxW - 2));
        var y = Math.Clamp(r.Y, 1, Math.Max(1, maxH - 2));
        var w = Math.Clamp(r.Width, 2, Math.Max(2, maxW - x));
        var h = Math.Clamp(r.Height, 2, Math.Max(2, maxH - y));
        return new RegionGeometry(x, y, w, h);
    }

    private static string F(double value) => value.ToString("0.######", CultureInfo.InvariantCulture);

    private static async Task RunProcessWithProgressAsync(
        ProcessStartInfo psi,
        TimeSpan duration,
        IProgress<double>? progress,
        CancellationToken cancellationToken)
    {
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        if (!process.Start())
            throw new InvalidOperationException("无法启动 FFmpeg。");
        progress?.Report(5);

        var stderrLines = new Queue<string>();
        var stderrTask = Task.Run(async () =>
        {
            while (!process.StandardError.EndOfStream)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (string.IsNullOrWhiteSpace(line)) continue;
                lock (stderrLines)
                {
                    stderrLines.Enqueue(line);
                    while (stderrLines.Count > 30) stderrLines.Dequeue();
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

                if (line.StartsWith("out_time=", StringComparison.OrdinalIgnoreCase))
                {
                    var value = line["out_time=".Length..];
                    if (TimeSpan.TryParse(value, CultureInfo.InvariantCulture, out var current) && duration.TotalSeconds > 0)
                        progress?.Report(Math.Clamp(Math.Max(5.0, current.TotalSeconds / duration.TotalSeconds * 100.0), 0, 100));
                }
                else if (line.Equals("progress=end", StringComparison.OrdinalIgnoreCase))
                {
                    progress?.Report(100);
                }
            }

            await process.WaitForExitAsync(cancellationToken);
            await stderrTask;
        }
        catch (OperationCanceledException)
        {
            try
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
            }
            catch { }
            throw;
        }

        if (process.ExitCode != 0)
        {
            string detail;
            lock (stderrLines) detail = string.Join(Environment.NewLine, stderrLines);
            throw new InvalidOperationException($"FFmpeg 处理失败（ExitCode={process.ExitCode}）。\n{detail}");
        }
    }

    private sealed record TimedGeometry(double TimeSeconds, RegionGeometry Geometry);
}

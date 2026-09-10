using System.Globalization;
using System.IO;
using System.Text.Json;
using WatermarkRemoverWindows.Models;

namespace WatermarkRemoverWindows.Services;

public static class WorkerJobRunner
{
    private static readonly object StatusLock = new();

    public static async Task<int> RunFromCommandLineAsync(string[] args)
    {
        var input = GetArg(args, "--input");
        var output = GetArg(args, "--output");
        var mode = GetArg(args, "--mode") ?? "ffmpeg";
        var statusFile = GetArg(args, "--status-file");
        var regionsFile = GetArg(args, "--regions-file");
        var qualityTag = GetArg(args, "--quality") ?? "balanced";
        var startText = GetArg(args, "--start") ?? "0";
        var endText = GetArg(args, "--end") ?? "0";
        var expandMaskText = GetArg(args, "--expand-mask") ?? "true";

        if (string.IsNullOrWhiteSpace(input) || string.IsNullOrWhiteSpace(output) || string.IsNullOrWhiteSpace(statusFile))
            return 1;

        if (!double.TryParse(startText, NumberStyles.Float, CultureInfo.InvariantCulture, out var startSeconds))
            startSeconds = 0;
        if (!double.TryParse(endText, NumberStyles.Float, CultureInfo.InvariantCulture, out var endSeconds))
            endSeconds = 0;
        bool expandMask = string.Equals(expandMaskText, "true", StringComparison.OrdinalIgnoreCase);

        var probe = new VideoProbeService();
        var ai = new AiInpaintService();
        var progress = new StatusFileProgress(statusFile);

        try
        {
            WriteStatus(statusFile, "running", 0, "正在读取视频信息…", null);
            var videoInfo = await probe.ProbeAsync(input);
            if (endSeconds <= startSeconds)
                endSeconds = videoInfo.Duration.TotalSeconds;

            var snapshots = new List<RegionSnapshot>();
            if (!string.IsNullOrWhiteSpace(regionsFile) && File.Exists(regionsFile))
            {
                var json = await File.ReadAllTextAsync(regionsFile);
                snapshots = JsonSerializer.Deserialize<List<RegionSnapshot>>(json) ?? new List<RegionSnapshot>();
            }

            if (snapshots.Count == 0)
            {
                progress.Report(1);
                WriteStatus(statusFile, "running", 1, "未找到手动框选，正在自动检测水印…", null);
                var detectionTime = Math.Min(Math.Max(0, startSeconds), Math.Max(0, videoInfo.Duration.TotalSeconds - 0.05));
                var detections = await ai.DetectAsync(
                    input,
                    videoInfo,
                    detectionTime,
                    CancellationToken.None,
                    progress);
                if (detections.Count == 0)
                    throw new InvalidOperationException("自动检测没有找到水印区域，请先手动框选后再处理。");

                snapshots = detections.Select(d => new RegionSnapshot
                {
                    Name = "自动检测",
                    X = d.X,
                    Y = d.Y,
                    Width = d.Width,
                    Height = d.Height,
                }).ToList();
            }

            var regions = snapshots
                .Select(snapshot => ToWatermarkRegion(snapshot))
                .ToList();
            var fullRange = startSeconds <= TimeSpan.FromMilliseconds(10).TotalSeconds &&
                            Math.Abs((endSeconds - videoInfo.Duration.TotalSeconds)) < 0.5;

            WriteStatus(statusFile, "running", 5, mode == "ai" ? "正在启动 OpenCV 算法修复…" : "正在启动 FFmpeg 去水印…", null);
            if (mode == "ai")
            {
                await ai.RunAsync(
                    input,
                    output,
                    regions,
                    videoInfo,
                    TimeSpan.FromSeconds(startSeconds),
                    TimeSpan.FromSeconds(endSeconds),
                    expandMask,
                    progress,
                    CancellationToken.None);
            }
            else
            {
                var options = qualityTag switch
                {
                    "fast" => new EncodeOptions("veryfast", 22),
                    "quality" => new EncodeOptions("slow", 16),
                    _ => new EncodeOptions("medium", 18),
                };
                var ffmpeg = new FfmpegService();
                await ffmpeg.RunDelogoAsync(
                    input,
                    output,
                    regions,
                    videoInfo,
                    fullRange ? null : TimeSpan.FromSeconds(startSeconds),
                    fullRange ? null : TimeSpan.FromSeconds(endSeconds),
                    options,
                    progress,
                    CancellationToken.None);
            }

            WriteStatus(statusFile, "done", 100, "处理完成", null);
            return 0;
        }
        catch (Exception ex)
        {
            WriteStatus(statusFile, "error", -1, "处理失败", ex.Message);
            return 1;
        }
    }

    private static WatermarkRegion ToWatermarkRegion(RegionSnapshot snapshot)
    {
        var region = new WatermarkRegion
        {
            Name = string.IsNullOrWhiteSpace(snapshot.Name) ? "水印区域" : snapshot.Name,
            X = snapshot.X,
            Y = snapshot.Y,
            Width = snapshot.Width,
            Height = snapshot.Height,
        };

        foreach (var keyframe in snapshot.Keyframes.OrderBy(k => k.TimeSeconds))
        {
            region.Keyframes.Add(new RegionKeyframe
            {
                TimeSeconds = keyframe.TimeSeconds,
                X = keyframe.X,
                Y = keyframe.Y,
                Width = keyframe.Width,
                Height = keyframe.Height,
            });
        }

        return region;
    }

    private static string? GetArg(string[] args, string name)
    {
        for (var i = 0; i < args.Length - 1; i++)
        {
            if (string.Equals(args[i], name, StringComparison.OrdinalIgnoreCase))
                return args[i + 1];
        }

        return null;
    }

    internal static void WriteStatus(string statusFile, string state, double progress, string status, string? error)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(statusFile)) return;
            Directory.CreateDirectory(Path.GetDirectoryName(statusFile)!);
            var payload = new
            {
                state,
                progress = progress < 0 ? 0 : Math.Round(Math.Max(0, Math.Min(100, progress)), 2),
                status,
                error,
            };
            var json = JsonSerializer.Serialize(payload);
            lock (StatusLock)
            {
                var temp = statusFile + ".tmp";
                File.WriteAllText(temp, json);
                if (File.Exists(statusFile))
                    File.Delete(statusFile);
                File.Move(temp, statusFile);
            }
        }
        catch
        {
        }
    }

    private sealed class StatusFileProgress : IProgress<double>
    {
        private readonly string _statusFile;
        private double _lastReported = -1;
        private DateTime _lastWrite = DateTime.MinValue;

        public StatusFileProgress(string statusFile)
        {
            _statusFile = statusFile;
        }

        public void Report(double value)
        {
            var now = DateTime.UtcNow;
            if (value - _lastReported < 0.5 && now - _lastWrite < TimeSpan.FromMilliseconds(500))
                return;

            _lastReported = value;
            _lastWrite = now;
            WriteStatus(_statusFile, "running", value, $"处理进度 {value:0.0}%", null);
        }
    }
}

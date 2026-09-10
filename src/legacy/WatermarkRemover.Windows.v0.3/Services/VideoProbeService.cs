using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text.Json;
using WatermarkRemoverWindows.Models;

namespace WatermarkRemoverWindows.Services;

public sealed class VideoProbeService
{
    public async Task<VideoInfo> ProbeAsync(string inputPath, CancellationToken cancellationToken = default)
    {
        var ffprobe = ToolLocator.Find("ffprobe.exe")
            ?? throw new FileNotFoundException("找不到 ffprobe.exe。请先运行 setup-ffmpeg.ps1，或把 ffprobe.exe 放入 tools 文件夹。", "ffprobe.exe");

        var psi = new ProcessStartInfo
        {
            FileName = ffprobe,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        psi.ArgumentList.Add("-v");
        psi.ArgumentList.Add("error");
        psi.ArgumentList.Add("-show_streams");
        psi.ArgumentList.Add("-show_format");
        psi.ArgumentList.Add("-of");
        psi.ArgumentList.Add("json");
        psi.ArgumentList.Add(inputPath);

        using var process = Process.Start(psi) ?? throw new InvalidOperationException("无法启动 ffprobe。") ;
        var stdoutTask = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var stderrTask = process.StandardError.ReadToEndAsync(cancellationToken);
        await process.WaitForExitAsync(cancellationToken);

        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
            throw new InvalidOperationException($"ffprobe 读取失败：{stderr}");

        using var doc = JsonDocument.Parse(stdout);
        var root = doc.RootElement;

        var streams = root.GetProperty("streams");
        JsonElement? videoStream = null;
        foreach (var stream in streams.EnumerateArray())
        {
            if (stream.TryGetProperty("codec_type", out var codecType) && codecType.GetString() == "video")
            {
                videoStream = stream;
                break;
            }
        }

        if (videoStream is null)
            throw new InvalidOperationException("文件中没有找到视频流。") ;

        var vs = videoStream.Value;
        var width = vs.GetProperty("width").GetInt32();
        var height = vs.GetProperty("height").GetInt32();
        var rotation = ReadRotation(vs);

        double durationSeconds = 0;
        if (root.TryGetProperty("format", out var format) &&
            format.TryGetProperty("duration", out var durationEl) &&
            double.TryParse(durationEl.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed))
        {
            durationSeconds = parsed;
        }
        else if (vs.TryGetProperty("duration", out var streamDuration) &&
                 double.TryParse(streamDuration.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out parsed))
        {
            durationSeconds = parsed;
        }

        return new VideoInfo
        {
            Width = width,
            Height = height,
            Rotation = rotation,
            Duration = TimeSpan.FromSeconds(Math.Max(0, durationSeconds)),
            Fps = ReadFrameRate(vs),
        };
    }

    private static double ReadFrameRate(JsonElement stream)
    {
        foreach (var propertyName in new[] { "avg_frame_rate", "r_frame_rate" })
        {
            if (!stream.TryGetProperty(propertyName, out var value) || value.ValueKind != JsonValueKind.String)
                continue;

            var text = value.GetString();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            var parts = text.Split('/');
            if (parts.Length == 2 &&
                double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out var numerator) &&
                double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out var denominator) &&
                denominator > 0)
            {
                return numerator / denominator;
            }

            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var fps))
                return fps;
        }

        return 0;
    }

    private static int ReadRotation(JsonElement stream)
    {
        if (stream.TryGetProperty("tags", out var tags) &&
            tags.TryGetProperty("rotate", out var rotateTag) &&
            int.TryParse(rotateTag.GetString(), out var tagRotation))
            return NormalizeRotation(tagRotation);

        if (stream.TryGetProperty("side_data_list", out var sideData))
        {
            foreach (var item in sideData.EnumerateArray())
            {
                if (item.TryGetProperty("rotation", out var rotationEl) && rotationEl.TryGetInt32(out var rotation))
                    return NormalizeRotation(rotation);
            }
        }

        return 0;
    }

    private static int NormalizeRotation(int rotation)
    {
        rotation %= 360;
        if (rotation < 0) rotation += 360;
        return rotation;
    }
}

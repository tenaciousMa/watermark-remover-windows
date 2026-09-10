using WatermarkRemoverWindows.Models;

namespace WatermarkRemoverWindows.Services;

public static class ProcessingTimeEstimator
{
    public static double EstimateSeconds(
        string mode,
        double durationSeconds,
        int width,
        int height,
        IReadOnlyCollection<WatermarkRegion>? regions,
        IReadOnlyCollection<RegionSnapshot>? snapshots = null)
    {
        if (durationSeconds <= 0 || width <= 0 || height <= 0)
            return 0;

        double regionArea = 0;
        if (regions is { Count: > 0 })
            regionArea = regions.Sum(r => Math.Max(1, r.Width) * Math.Max(1, r.Height));
        else if (snapshots is { Count: > 0 })
            regionArea = snapshots.Sum(r => Math.Max(1, r.Width) * Math.Max(1, r.Height));

        var frameArea = Math.Max(1.0, width * height);
        var areaRatio = Math.Clamp(regionArea / frameArea, 0.008, 0.65);
        var seconds = mode switch
        {
            "heavy" => 12.0 + durationSeconds * (8.0 + areaRatio * 12.0),
            "ai" => 1.0 + durationSeconds * (1.15 + areaRatio * 1.8),
            _ => 2.0 + durationSeconds * (0.035 + areaRatio * 0.25),
        };
        return Math.Max(2.0, seconds);
    }

    public static string FormatDuration(double seconds)
    {
        if (seconds <= 0)
            return "未知";
        if (seconds < 60)
            return $"约 {Math.Max(1, (int)Math.Ceiling(seconds))} 秒";
        if (seconds < 3600)
            return $"约 {Math.Max(1, (int)Math.Ceiling(seconds / 60.0))} 分钟";

        var hours = (int)Math.Floor(seconds / 3600.0);
        var minutes = (int)Math.Ceiling((seconds - hours * 3600.0) / 60.0);
        if (minutes >= 60)
        {
            hours += 1;
            minutes = 0;
        }
        return minutes == 0 ? $"约 {hours} 小时" : $"约 {hours} 小时 {minutes} 分钟";
    }
}

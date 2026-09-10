using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.CompilerServices;

namespace WatermarkRemoverWindows.Models;

public enum VideoJobState
{
    Queued,
    Running,
    Completed,
    Failed,
    Cancelled,
}

public sealed class VideoJob : INotifyPropertyChanged
{
    private string _outputPath = string.Empty;
    private string _mode = "ffmpeg";
    private string _qualityTag = "balanced";
    private bool _expandMask = true;
    private double _startSeconds;
    private double _endSeconds;
    private VideoJobState _state = VideoJobState.Queued;
    private double _progress;
    private string _status = "等待处理";
    private string _error = string.Empty;
    private double _estimatedTotalSeconds;
    private DateTime? _startedAtUtc;

    public string Id { get; } = Guid.NewGuid().ToString("N");
    public string InputPath { get; init; } = string.Empty;
    public ObservableCollection<RegionSnapshot> Regions { get; } = new();

    public string OutputPath
    {
        get => _outputPath;
        set { _outputPath = value; OnPropertyChanged(); }
    }

    public string Mode
    {
        get => _mode;
        set { _mode = value; OnPropertyChanged(); OnPropertyChanged(nameof(ModeText)); }
    }

    public string QualityTag
    {
        get => _qualityTag;
        set { _qualityTag = value; OnPropertyChanged(); }
    }

    public bool ExpandMask
    {
        get => _expandMask;
        set { _expandMask = value; OnPropertyChanged(); }
    }

    public double StartSeconds
    {
        get => _startSeconds;
        set { _startSeconds = value; OnPropertyChanged(); }
    }

    public double EndSeconds
    {
        get => _endSeconds;
        set { _endSeconds = value; OnPropertyChanged(); }
    }

    public VideoJobState State
    {
        get => _state;
        set
        {
            if (_state == value) return;
            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(StateText));
            OnPropertyChanged(nameof(IsFinished));
            OnPropertyChanged(nameof(TimingText));
        }
    }

    public double Progress
    {
        get => _progress;
        set
        {
            _progress = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ProgressText));
            OnPropertyChanged(nameof(TimingText));
        }
    }

    public string Status
    {
        get => _status;
        set { _status = value; OnPropertyChanged(); }
    }

    public string Error
    {
        get => _error;
        set { _error = value; OnPropertyChanged(); }
    }

    public double EstimatedTotalSeconds
    {
        get => _estimatedTotalSeconds;
        set
        {
            _estimatedTotalSeconds = Math.Max(0, value);
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimingText));
        }
    }

    public DateTime? StartedAtUtc
    {
        get => _startedAtUtc;
        set
        {
            _startedAtUtc = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(TimingText));
        }
    }

    public string FileName => Path.GetFileName(InputPath);
    public string ModeText => Mode switch
    {
        "ai" => "OpenCV 算法修复",
        "heavy" => "ProPainter 强力修复",
        _ => "FFmpeg 快速模式",
    };
    public string StateText => State switch
    {
        VideoJobState.Queued => "等待处理",
        VideoJobState.Running => "处理中",
        VideoJobState.Completed => "已完成",
        VideoJobState.Failed => "失败",
        VideoJobState.Cancelled => "已取消",
        _ => State.ToString(),
    };
    public string ProgressText => $"{Math.Max(0, Math.Min(100, Progress)):0.0}%";
    public bool IsFinished => State is VideoJobState.Completed or VideoJobState.Failed or VideoJobState.Cancelled;
    public string ErrorDisplay => string.IsNullOrWhiteSpace(Error) ? string.Empty : "失败原因：" + Error;
    public string TimingText
    {
        get
        {
            if (State == VideoJobState.Queued)
                return EstimatedTotalSeconds > 0
                    ? "预计处理时间：" + WatermarkRemoverWindows.Services.ProcessingTimeEstimator.FormatDuration(EstimatedTotalSeconds)
                    : "预计处理时间：等待估算";

            if (State != VideoJobState.Running)
                return string.Empty;

            var remaining = EstimatedRemainingSeconds;
            return remaining > 0
                ? "预计剩余：" + WatermarkRemoverWindows.Services.ProcessingTimeEstimator.FormatDuration(remaining)
                : "预计剩余：计算中";
        }
    }

    public double EstimatedRemainingSeconds
    {
        get
        {
            if (State == VideoJobState.Completed)
                return 0;
            if (State != VideoJobState.Running)
                return EstimatedTotalSeconds;
            if (Progress > 1 && StartedAtUtc.HasValue)
            {
                var elapsed = Math.Max(0.1, (DateTime.UtcNow - StartedAtUtc.Value).TotalSeconds);
                return Math.Max(1, elapsed * (100.0 - Math.Clamp(Progress, 0, 100)) / Math.Clamp(Progress, 1, 100));
            }
            return Math.Max(1, EstimatedTotalSeconds * (1.0 - Math.Clamp(Progress, 0, 100) / 100.0));
        }
    }

    public void RefreshTiming() => OnPropertyChanged(nameof(TimingText));
    public bool UsesAutoDetect => Regions.Count == 0;

    [System.Text.Json.Serialization.JsonIgnore]
    public Process? WorkerProcess { get; set; }

    [System.Text.Json.Serialization.JsonIgnore]
    public string StatusFile { get; set; } = string.Empty;

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public sealed class RegionSnapshot
{
    public string Name { get; set; } = "水印区域";
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public List<RegionKeyframeSnapshot> Keyframes { get; set; } = new();
}

public sealed class RegionKeyframeSnapshot
{
    public double TimeSeconds { get; set; }
    public int X { get; set; }
    public int Y { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
}

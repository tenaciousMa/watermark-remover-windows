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

    public string FileName => Path.GetFileName(InputPath);
    public string ModeText => Mode == "ai" ? "OpenCV 算法修复" : "FFmpeg 快速模式";
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

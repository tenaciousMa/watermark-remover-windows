using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WatermarkRemoverWindows.Models;

public sealed class RegionKeyframe : INotifyPropertyChanged
{
    private double _timeSeconds;
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public double TimeSeconds { get => _timeSeconds; set { _timeSeconds = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int X { get => _x; set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int Y { get => _y; set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int Width { get => _width; set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int Height { get => _height; set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }

    public string Display => $"{FormatTime(TimeSeconds)}  ·  X={X} Y={Y} W={Width} H={Height}";

    public RegionGeometry ToGeometry() => new(X, Y, Width, Height);

    private static string FormatTime(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}";
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

public readonly record struct RegionGeometry(int X, int Y, int Width, int Height);

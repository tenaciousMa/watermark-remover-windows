using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WatermarkRemoverWindows.Models;

public sealed class WatermarkRegion : INotifyPropertyChanged
{
    private string _name = "水印区域";
    private int _x;
    private int _y;
    private int _width;
    private int _height;

    public WatermarkRegion()
    {
        Keyframes.CollectionChanged += Keyframes_CollectionChanged;
    }

    public Guid Id { get; } = Guid.NewGuid();
    public ObservableCollection<RegionKeyframe> Keyframes { get; } = new();

    public string Name { get => _name; set { _name = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int X { get => _x; set { _x = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int Y { get => _y; set { _y = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int Width { get => _width; set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }
    public int Height { get => _height; set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(Display)); } }

    public bool IsDynamic => Keyframes.Count > 0;
    public string Display => IsDynamic
        ? $"{Name}  ·  动态 {Keyframes.Count} 帧"
        : $"{Name}  ·  X={X} Y={Y} W={Width} H={Height}";

    public RegionGeometry BaseGeometry => new(X, Y, Width, Height);

    public RegionGeometry GetGeometryAt(double seconds)
    {
        if (Keyframes.Count == 0)
            return BaseGeometry;

        var ordered = Keyframes.OrderBy(k => k.TimeSeconds).ToList();
        if (ordered.Count == 1 || seconds <= ordered[0].TimeSeconds)
            return ordered[0].ToGeometry();
        if (seconds >= ordered[^1].TimeSeconds)
            return ordered[^1].ToGeometry();

        for (var i = 0; i < ordered.Count - 1; i++)
        {
            var a = ordered[i];
            var b = ordered[i + 1];
            if (seconds < a.TimeSeconds || seconds > b.TimeSeconds) continue;
            var span = b.TimeSeconds - a.TimeSeconds;
            var p = span <= 0.000001 ? 0 : (seconds - a.TimeSeconds) / span;
            return new RegionGeometry(
                Lerp(a.X, b.X, p),
                Lerp(a.Y, b.Y, p),
                Math.Max(2, Lerp(a.Width, b.Width, p)),
                Math.Max(2, Lerp(a.Height, b.Height, p)));
        }
        return ordered[^1].ToGeometry();
    }

    public RegionKeyframe UpsertKeyframe(double seconds, RegionGeometry geometry, double toleranceSeconds = 0.035)
    {
        var existing = Keyframes.FirstOrDefault(k => Math.Abs(k.TimeSeconds - seconds) <= toleranceSeconds);
        if (existing is null)
        {
            existing = new RegionKeyframe { TimeSeconds = Math.Max(0, seconds) };
            Keyframes.Add(existing);
        }

        existing.X = geometry.X;
        existing.Y = geometry.Y;
        existing.Width = geometry.Width;
        existing.Height = geometry.Height;
        SortKeyframes();
        NotifyKeyframesChanged();
        return existing;
    }

    public void SetBaseGeometry(RegionGeometry geometry)
    {
        X = geometry.X;
        Y = geometry.Y;
        Width = geometry.Width;
        Height = geometry.Height;
    }

    public void ClearKeyframes()
    {
        if (Keyframes.Count > 0)
        {
            var first = Keyframes.OrderBy(k => k.TimeSeconds).First().ToGeometry();
            SetBaseGeometry(first);
        }
        Keyframes.Clear();
        NotifyKeyframesChanged();
    }

    private void Keyframes_CollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
    {
        if (e.OldItems is not null)
            foreach (RegionKeyframe item in e.OldItems)
                item.PropertyChanged -= Keyframe_PropertyChanged;
        if (e.NewItems is not null)
            foreach (RegionKeyframe item in e.NewItems)
                item.PropertyChanged += Keyframe_PropertyChanged;
        NotifyKeyframesChanged();
    }

    private void Keyframe_PropertyChanged(object? sender, PropertyChangedEventArgs e) => NotifyKeyframesChanged();

    private void SortKeyframes()
    {
        var sorted = Keyframes.OrderBy(k => k.TimeSeconds).ToList();
        for (var targetIndex = 0; targetIndex < sorted.Count; targetIndex++)
        {
            var currentIndex = Keyframes.IndexOf(sorted[targetIndex]);
            if (currentIndex != targetIndex)
                Keyframes.Move(currentIndex, targetIndex);
        }
    }

    private void NotifyKeyframesChanged()
    {
        OnPropertyChanged(nameof(IsDynamic));
        OnPropertyChanged(nameof(Display));
    }

    private static int Lerp(int a, int b, double p) => (int)Math.Round(a + (b - a) * p);

    public event PropertyChangedEventHandler? PropertyChanged;
    private void OnPropertyChanged([CallerMemberName] string? name = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

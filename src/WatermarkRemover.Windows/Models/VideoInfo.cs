namespace WatermarkRemoverWindows.Models;

public sealed class VideoInfo
{
    public int Width { get; init; }
    public int Height { get; init; }
    public int Rotation { get; init; }
    public TimeSpan Duration { get; init; }
    public double Fps { get; init; }

    // 用户看到的显示尺寸。90/270 度旋转视频需要交换宽高。
    public int DisplayWidth => Math.Abs(Rotation) % 180 == 90 ? Height : Width;
    public int DisplayHeight => Math.Abs(Rotation) % 180 == 90 ? Width : Height;
}

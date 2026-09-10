using Microsoft.Win32;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Text.Json;
using System.Threading;
using Directory = System.IO.Directory;
using File = System.IO.File;
using Path = System.IO.Path;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;
using WatermarkRemoverWindows.Models;
using WatermarkRemoverWindows.Services;

namespace WatermarkRemoverWindows;

public partial class MainWindow : Window
{
    public ObservableCollection<WatermarkRegion> Regions { get; } = new();
    public ObservableCollection<VideoJob> Jobs { get; } = new();

    private readonly VideoProbeService _probeService = new();
    private readonly AiInpaintService _aiService = new();
    private readonly HeavyInpaintService _heavyService = new();
    private readonly DispatcherTimer _positionTimer;
    private readonly DispatcherTimer _queueTimer;
    private readonly SemaphoreSlim _workerSlots = new(Math.Max(1, Math.Min(3, Environment.ProcessorCount / 2)));
    private readonly SemaphoreSlim _heavySlots = new(1);

    private string? _inputPath;
    private VideoInfo? _videoInfo;
    private string? _editingJobId;
    private bool _isStartingJob;
    private bool _isPlaying;
    private bool _isSeeking;
    private bool _isDrawing;
    private bool _isManipulatingExisting;
    private Point _drawStart;
    private Rectangle? _temporaryRect;

    private WatermarkRegion? _dragRegion;
    private Border? _dragBorder;
    private Point _dragStartScreen;
    private RegionGeometry _dragStartGeometry;
    private double _dragEditTime;
    private DateTime _lastFrameSeek = DateTime.MinValue;
    private string _currentTheme = "catppuccin";

    public MainWindow()
    {
        InitializeComponent();
        DataContext = this;
        ApplyTheme(_currentTheme);
        JobList.ItemsSource = Jobs;

        _positionTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
        _positionTimer.Tick += (_, _) =>
        {
            UpdateTimelineFromPlayer();
            if (!_isDrawing && !_isManipulatingExisting) RedrawRegions();
        };
        _positionTimer.Start();
        _queueTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _queueTimer.Tick += (_, _) => PollQueueStatuses();
        _queueTimer.Start();

        Regions.CollectionChanged += (_, _) => UpdateEstimatedTime();
        StartTimeTextBox.TextChanged += (_, _) => UpdateEstimatedTime();
        EndTimeTextBox.TextChanged += (_, _) => UpdateEstimatedTime();
        AllowDrop = true;
        PreviewKeyDown += Window_PreviewKeyDown;
        DragOver += Window_DragOver;
        Drop += Window_Drop;
        Closing += MainWindow_Closing;
    }

    private void Window_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        if ((Keyboard.Modifiers & ModifierKeys.Control) != ModifierKeys.Control) return;

        switch (e.Key)
        {
            case Key.D1:
            case Key.NumPad1:
                ApplyTheme("white");
                e.Handled = true;
                break;
            case Key.D2:
            case Key.NumPad2:
                ApplyTheme("black");
                e.Handled = true;
                break;
            case Key.D3:
            case Key.NumPad3:
                ApplyTheme("catppuccin");
                e.Handled = true;
                break;
        }
    }

    private void ThemeButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string themeKey)
            ApplyTheme(themeKey);
    }

    private void ApplyTheme(string themeKey)
    {
        var palette = themeKey switch
        {
            "white" => new ThemePalette(
                "#F8FAFC", "#FFFFFF", "#F1F5F9", "#CBD5E1", "#111827", "#475569",
                "#111827", "#334155", "#111827", "#FFFFFF", "#E2E8F0", "#FFFFFF",
                "#0F172A", "#EEF8FAFC", "#2563EB", "#262563EB", "#F59E0B", "#42F59E0B"),
            "black" => new ThemePalette(
                "#050505", "#0E0E0E", "#171717", "#2A2A2A", "#F5F5F5", "#A3A3A3",
                "#F5F5F5", "#D4D4D4", "#F5F5F5", "#050505", "#262626", "#111111",
                "#000000", "#DD111111", "#93C5FD", "#3093C5FD", "#FACC15", "#42FACC15"),
            _ => new ThemePalette(
                "#303446", "#414559", "#51576D", "#626880", "#C6D0F5", "#A5ADCE",
                "#8CAAEE", "#BABBFF", "#C6D0F5", "#232634", "#5B6078", "#292C3C",
                "#11111B", "#DD292C3C", "#8CAAEE", "#2A8CAAEE", "#E5C890", "#42E5C890"),
        };

        _currentTheme = themeKey == "white" || themeKey == "black" ? themeKey : "catppuccin";
        SetBrush("BgBrush", palette.Background);
        SetBrush("PanelBrush", palette.Panel);
        SetBrush("PanelAltBrush", palette.PanelAlt);
        SetBrush("BorderBrush", palette.Border);
        SetBrush("TextBrush", palette.Text);
        SetBrush("MutedBrush", palette.Muted);
        SetBrush("AccentBrush", palette.Accent);
        SetBrush("AccentHoverBrush", palette.AccentHover);
        SetBrush("ButtonForegroundBrush", palette.ButtonForeground);
        SetBrush("PrimaryButtonForegroundBrush", palette.PrimaryButtonForeground);
        SetBrush("ButtonHoverBrush", palette.ButtonHover);
        SetBrush("InputBrush", palette.Input);
        SetBrush("PreviewBrush", palette.Preview);
        SetBrush("HintBrush", palette.Hint);
        SetBrush("RegionBrush", palette.Region);
        SetBrush("RegionFillBrush", palette.RegionFill);
        SetBrush("SelectedRegionBrush", palette.SelectedRegion);
        SetBrush("SelectedRegionFillBrush", palette.SelectedRegionFill);

        RefreshThemeButtons();
        RedrawRegions();
    }

    private void RefreshThemeButtons()
    {
        if (WhiteThemeButton is null || BlackThemeButton is null || CatppuccinThemeButton is null) return;
        var primary = (Style)FindResource("PrimaryButton");
        WhiteThemeButton.ClearValue(FrameworkElement.StyleProperty);
        BlackThemeButton.ClearValue(FrameworkElement.StyleProperty);
        CatppuccinThemeButton.ClearValue(FrameworkElement.StyleProperty);
        if (_currentTheme == "white") WhiteThemeButton.Style = primary;
        if (_currentTheme == "black") BlackThemeButton.Style = primary;
        if (_currentTheme == "catppuccin") CatppuccinThemeButton.Style = primary;
    }

    private static void SetBrush(string key, string color)
    {
        var converted = (Color)ColorConverter.ConvertFromString(color);
        Application.Current.Resources[key] = new SolidColorBrush(converted);
    }

    private static Color GetThemeColor(string key, Color fallback)
    {
        if (Application.Current.Resources[key] is SolidColorBrush brush)
            return brush.Color;
        return fallback;
    }

    private async void OpenButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择多个视频",
            Multiselect = true,
            Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm;*.wmv|所有文件|*.*"
        };
        if (dialog.ShowDialog() == true)
            await OpenVideosForEditingAsync(dialog.FileNames);
    }

    private void Window_DragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private async void Window_Drop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files && files.Length > 0)
            await OpenVideosForEditingAsync(files);
    }

    private async Task OpenVideosForEditingAsync(IEnumerable<string> paths)
    {
        var file = paths.FirstOrDefault(File.Exists);
        if (file is not null)
            await LoadVideoAsync(file);
    }

    private async Task LoadVideoAsync(string path)
    {
        try
        {
            StatusText.Text = "正在读取视频信息…";
            _videoInfo = await _probeService.ProbeAsync(path);
            _inputPath = path;
            Regions.Clear();
            RegionList.SelectedItem = null;
            KeyframeList.ItemsSource = null;
            RedrawRegions();

            MediaPlayer.Source = new Uri(path);
            MediaPlayer.Position = TimeSpan.Zero;
            MediaPlayer.Play();
            MediaPlayer.Pause();
            _isPlaying = false;

            EmptyHint.Visibility = Visibility.Collapsed;
            PlayerBar.Visibility = Visibility.Visible;
            PlayPauseButton.IsEnabled = true;
            StopButton.IsEnabled = true;
            DetectWatermarkButton.IsEnabled = true;
            ProcessButton.IsEnabled = true;
            EncodeProgress.IsIndeterminate = false;
            EncodeProgress.Value = 0;
            TimelineSlider.IsEnabled = true;
            double fps = _videoInfo.Fps > 0 ? _videoInfo.Fps : 25.0;
            TimelineSlider.TickFrequency = 1.0 / fps;
            TimelineSlider.SmallChange = 1.0 / fps;
            TimelineSlider.IsSnapToTickEnabled = true;
            TimelineSlider.Maximum = Math.Max(0.001, _videoInfo.Duration.TotalSeconds);
            DurationText.Text = FormatTime(_videoInfo.Duration);
            StartTimeTextBox.Text = "00:00:00";
            EndTimeTextBox.Text = FormatTime(_videoInfo.Duration);
            OutputPathTextBox.Text = BuildDefaultOutputPath(path);
            FileInfoText.Text = $"{Path.GetFileName(path)}   ·   {_videoInfo.DisplayWidth}×{_videoInfo.DisplayHeight}   ·   {FormatTime(_videoInfo.Duration)}   ·   {fps:0.##} FPS";
            StatusText.Text = "空白处拖拽新建框；已有框可移动，右下角可缩放。";
            UpdatePlaybackButton();
        }
        catch (Exception ex)
        {
            MessageBox.Show(ex.Message, "导入失败", MessageBoxButton.OK, MessageBoxImage.Error);
            StatusText.Text = "导入失败";
        }
    }

    public async Task ImportVideosAsync(IReadOnlyCollection<string> paths, bool loadLast)
    {
        VideoJob? lastJob = null;
        var imported = new List<VideoJob>();
        foreach (var path in paths)
        {
            if (!File.Exists(path)) continue;
            var job = new VideoJob
            {
                InputPath = path,
                OutputPath = FindFreeOutputPath(path),
                Mode = SelectedMode,
                QualityTag = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "balanced",
                ExpandMask = SelectedMode == "ai"
                    ? AiFp16CheckBox.IsChecked == true
                    : HeavyDilationCheckBox.IsChecked == true,
                EndSeconds = 0,
            };
            Jobs.Add(job);
            imported.Add(job);
            lastJob = job;
        }

        foreach (var job in imported)
        {
            try
            {
                var info = await _probeService.ProbeAsync(job.InputPath);
                job.EstimatedTotalSeconds = ProcessingTimeEstimator.EstimateSeconds(
                    job.Mode,
                    info.Duration.TotalSeconds,
                    info.DisplayWidth,
                    info.DisplayHeight,
                    null);
            }
            catch
            {
                // The worker will still provide a clear error if the file cannot be probed.
            }
        }

        if (loadLast && lastJob is not null)
            await OpenVideoInEditorAsync(lastJob);

        UpdateQueueButton();
        RefreshQueueSummary();
    }

    public async Task OpenVideoInEditorAsync(VideoJob job)
    {
        _editingJobId = job.Id;
        await LoadVideoAsync(job.InputPath);
        if (!string.IsNullOrWhiteSpace(job.OutputPath))
            OutputPathTextBox.Text = job.OutputPath;
        if (job.EndSeconds > 0)
            EndTimeTextBox.Text = FormatTime(TimeSpan.FromSeconds(job.EndSeconds));
        if (job.StartSeconds > 0)
            StartTimeTextBox.Text = FormatTime(TimeSpan.FromSeconds(job.StartSeconds));
    }

    private void MediaPlayer_MediaOpened(object sender, RoutedEventArgs e) => RedrawRegions();

    private void MediaPlayer_MediaEnded(object sender, RoutedEventArgs e)
    {
        _isPlaying = false;
        MediaPlayer.Position = TimeSpan.Zero;
        MediaPlayer.Pause();
        UpdatePlaybackButton();
        RedrawRegions();
    }

    private void PlayPauseButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputPath is null) return;
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
        }
        else
        {
            MediaPlayer.Play();
            _isPlaying = true;
        }
        UpdatePlaybackButton();
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputPath is null) return;
        _isPlaying = false;
        MediaPlayer.Pause();
        MediaPlayer.Position = TimeSpan.Zero;
        TimelineSlider.Value = 0;
        CurrentTimeText.Text = FormatTime(TimeSpan.Zero, includeMilliseconds: true);
        UpdatePlaybackButton();
        RedrawRegions();
    }

    private void UpdatePlaybackButton()
    {
        if (PlayPauseButton is null) return;
        PlayPauseButton.Content = _isPlaying ? "⏸" : "▶";
        PlayPauseButton.ToolTip = _isPlaying ? "暂停" : "播放";
    }

    private void PauseForEditing()
    {
        if (_isPlaying)
        {
            MediaPlayer.Pause();
            _isPlaying = false;
            UpdatePlaybackButton();
        }
    }

    private double CurrentSeconds => Math.Clamp(
        _isSeeking ? TimelineSlider.Value : MediaPlayer.Position.TotalSeconds,
        0,
        _videoInfo?.Duration.TotalSeconds ?? double.MaxValue);

    private void UpdateTimelineFromPlayer()
    {
        if (_inputPath is null || _isSeeking) return;
        var p = MediaPlayer.Position;
        CurrentTimeText.Text = FormatTime(p, includeMilliseconds: true);
        TimelineSlider.Value = Math.Clamp(p.TotalSeconds, TimelineSlider.Minimum, TimelineSlider.Maximum);
    }

    private void TimelineSlider_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        _isSeeking = true;
        _lastFrameSeek = DateTime.MinValue;
        PauseForEditing();
    }

    private void TimelineSlider_PreviewMouseUp(object sender, MouseButtonEventArgs e)
    {
        var seconds = SnapToFrame(TimelineSlider.Value);
        SeekMediaToFrame(seconds);
        _isSeeking = false;
        CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(seconds), includeMilliseconds: true);
        RedrawRegions();
    }

    private void TimelineSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_isSeeking)
        {
            var seconds = SnapToFrame(TimelineSlider.Value);
            CurrentTimeText.Text = FormatTime(TimeSpan.FromSeconds(seconds), includeMilliseconds: true);
            RedrawRegions(seconds);
            if ((DateTime.UtcNow - _lastFrameSeek).TotalMilliseconds >= 120)
                SeekMediaToFrame(seconds);
        }
    }

    private void SeekMediaToFrame(double seconds)
    {
        if (_videoInfo is null) return;
        MediaPlayer.Position = TimeSpan.FromSeconds(seconds);
        _lastFrameSeek = DateTime.UtcNow;
    }

    private double SnapToFrame(double seconds)
    {
        if (_videoInfo is null)
            return Math.Max(0, seconds);

        double fps = _videoInfo.Fps > 0 ? _videoInfo.Fps : 25.0;
        double frame = Math.Round(Math.Max(0, seconds) * fps);
        return Math.Clamp(frame / fps, 0, _videoInfo.Duration.TotalSeconds);
    }

    // ---------- 新建区域 ----------
    private void OverlayCanvas_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (_videoInfo is null || e.OriginalSource != OverlayCanvas) return;
        var p = e.GetPosition(OverlayCanvas);
        var videoRect = GetDisplayedVideoRect();
        if (!videoRect.Contains(p)) return;

        PauseForEditing();
        _isDrawing = true;
        _drawStart = ClampPointToRect(p, videoRect);
        _temporaryRect = CreateTemporaryRectangle();
        OverlayCanvas.Children.Add(_temporaryRect);
        Canvas.SetLeft(_temporaryRect, _drawStart.X);
        Canvas.SetTop(_temporaryRect, _drawStart.Y);
        OverlayCanvas.CaptureMouse();
        e.Handled = true;
    }

    private void OverlayCanvas_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isDrawing || _temporaryRect is null || _videoInfo is null) return;
        var current = ClampPointToRect(e.GetPosition(OverlayCanvas), GetDisplayedVideoRect());
        UpdateRectangleVisual(_temporaryRect, _drawStart, current);
    }

    private void OverlayCanvas_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isDrawing || _temporaryRect is null || _videoInfo is null) return;

        var current = ClampPointToRect(e.GetPosition(OverlayCanvas), GetDisplayedVideoRect());
        var screenRect = NormalizeRect(_drawStart, current);
        _isDrawing = false;
        OverlayCanvas.ReleaseMouseCapture();
        OverlayCanvas.Children.Remove(_temporaryRect);
        _temporaryRect = null;

        if (screenRect.Width < 8 || screenRect.Height < 8) return;
        var geometry = ScreenRectToVideoGeometry(screenRect);
        if (geometry.Width < 2 || geometry.Height < 2) return;

        var region = new WatermarkRegion
        {
            Name = $"区域 {Regions.Count + 1:00}",
            X = geometry.X,
            Y = geometry.Y,
            Width = geometry.Width,
            Height = geometry.Height,
        };
        Regions.Add(region);
        RegionList.SelectedItem = region;
        RedrawRegions();
        StatusText.Text = $"已添加 {Regions.Count} 个区域。固定水印可直接处理；移动水印请添加关键帧。";
    }

    // ---------- 已有区域：拖动 ----------
    private void RegionBorder_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (sender is not Border border || border.Tag is not WatermarkRegion region || _videoInfo is null) return;
        PauseForEditing();
        _isManipulatingExisting = true;
        RegionList.SelectedItem = region;
        _dragRegion = region;
        _dragBorder = border;
        _dragStartScreen = e.GetPosition(OverlayCanvas);
        _dragEditTime = CurrentSeconds;
        _dragStartGeometry = region.GetGeometryAt(_dragEditTime);
        border.CaptureMouse();
        e.Handled = true;
    }

    private void RegionBorder_MouseMove(object sender, MouseEventArgs e)
    {
        if (!_isManipulatingExisting || _dragRegion is null || _dragBorder is null || _videoInfo is null || e.LeftButton != MouseButtonState.Pressed)
            return;

        var vr = GetDisplayedVideoRect();
        if (vr.IsEmpty) return;
        var p = e.GetPosition(OverlayCanvas);
        var dx = (p.X - _dragStartScreen.X) / vr.Width * _videoInfo.DisplayWidth;
        var dy = (p.Y - _dragStartScreen.Y) / vr.Height * _videoInfo.DisplayHeight;
        var g = ClampGeometry(new RegionGeometry(
            (int)Math.Round(_dragStartGeometry.X + dx),
            (int)Math.Round(_dragStartGeometry.Y + dy),
            _dragStartGeometry.Width,
            _dragStartGeometry.Height));

        UpdateRegionGeometry(_dragRegion, g, _dragEditTime);
        var sr = VideoGeometryToScreenRect(g);
        Canvas.SetLeft(_dragBorder, sr.Left);
        Canvas.SetTop(_dragBorder, sr.Top);
        e.Handled = true;
    }

    private void RegionBorder_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (!_isManipulatingExisting) return;
        if (_dragBorder is not null) _dragBorder.ReleaseMouseCapture();
        _isManipulatingExisting = false;
        _dragRegion = null;
        _dragBorder = null;
        RefreshKeyframeList();
        RedrawRegions();
        e.Handled = true;
    }

    // ---------- 已有区域：右下角缩放 ----------
    private void ResizeThumb_DragStarted(object sender, DragStartedEventArgs e)
    {
        PauseForEditing();
        _isManipulatingExisting = true;
        if (sender is Thumb thumb && thumb.Tag is ResizeVisualTag tag)
            RegionList.SelectedItem = tag.Region;
        e.Handled = true;
    }

    private void ResizeThumb_DragDelta(object sender, DragDeltaEventArgs e)
    {
        if (sender is not Thumb thumb || thumb.Tag is not ResizeVisualTag tag || _videoInfo is null) return;
        var region = tag.Region;
        var vr = GetDisplayedVideoRect();
        if (vr.IsEmpty) return;
        var seconds = CurrentSeconds;
        var g0 = region.GetGeometryAt(seconds);
        var dw = e.HorizontalChange / vr.Width * _videoInfo.DisplayWidth;
        var dh = e.VerticalChange / vr.Height * _videoInfo.DisplayHeight;
        var g = ClampGeometry(new RegionGeometry(
            g0.X,
            g0.Y,
            Math.Max(2, (int)Math.Round(g0.Width + dw)),
            Math.Max(2, (int)Math.Round(g0.Height + dh))));
        UpdateRegionGeometry(region, g, seconds);

        var sr = VideoGeometryToScreenRect(g);
        tag.Border.Width = Math.Max(2, sr.Width);
        tag.Border.Height = Math.Max(2, sr.Height);
        Canvas.SetLeft(tag.Border, sr.Left);
        Canvas.SetTop(tag.Border, sr.Top);
        Canvas.SetLeft(thumb, Math.Max(sr.Left, sr.Right - 7));
        Canvas.SetTop(thumb, Math.Max(sr.Top, sr.Bottom - 7));
        e.Handled = true;
    }

    private void ResizeThumb_DragCompleted(object sender, DragCompletedEventArgs e)
    {
        _isManipulatingExisting = false;
        RefreshKeyframeList();
        RedrawRegions();
        e.Handled = true;
    }

    private void UpdateRegionGeometry(WatermarkRegion region, RegionGeometry geometry, double seconds)
    {
        if (region.IsDynamic)
            region.UpsertKeyframe(seconds, geometry);
        else
            region.SetBaseGeometry(geometry);
    }

    // ---------- 关键帧 ----------
    private void AddKeyframeButton_Click(object sender, RoutedEventArgs e)
    {
        if (RegionList.SelectedItem is not WatermarkRegion region) return;
        PauseForEditing();
        var seconds = CurrentSeconds;
        var geometry = region.GetGeometryAt(seconds);
        var keyframe = region.UpsertKeyframe(seconds, geometry);
        RefreshKeyframeList();
        KeyframeList.SelectedItem = keyframe;
        RedrawRegions(seconds);
        StatusText.Text = $"已在 {FormatTime(TimeSpan.FromSeconds(seconds), true)} 添加/更新关键帧。移动时间轴后直接拖动框即可创建下一关键帧。";
    }

    private void DeleteKeyframeButton_Click(object sender, RoutedEventArgs e)
    {
        if (RegionList.SelectedItem is not WatermarkRegion region || KeyframeList.SelectedItem is not RegionKeyframe keyframe) return;
        if (region.Keyframes.Count == 1)
            region.SetBaseGeometry(keyframe.ToGeometry());
        region.Keyframes.Remove(keyframe);
        RefreshKeyframeList();
        RedrawRegions();
    }

    private void ClearKeyframesButton_Click(object sender, RoutedEventArgs e)
    {
        if (RegionList.SelectedItem is not WatermarkRegion region) return;
        region.ClearKeyframes();
        RefreshKeyframeList();
        RedrawRegions();
        StatusText.Text = "已转为静态水印框。";
    }

    private void RegionList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        RefreshKeyframeList();
        AddKeyframeButton.IsEnabled = RegionList.SelectedItem is WatermarkRegion;
        if (!_isManipulatingExisting) RedrawRegions();
    }

    private void RefreshKeyframeList()
    {
        if (RegionList.SelectedItem is WatermarkRegion region)
            KeyframeList.ItemsSource = region.Keyframes;
        else
            KeyframeList.ItemsSource = null;
    }

    // ---------- 绘制 ----------
    private void RedrawRegions(double? secondsOverride = null)
    {
        if (OverlayCanvas is null || _isDrawing) return;
        OverlayCanvas.Children.Clear();
        if (_videoInfo is null) return;

        var seconds = secondsOverride ?? CurrentSeconds;
        var selected = RegionList.SelectedItem as WatermarkRegion;

        foreach (var region in Regions)
        {
            var geometry = ClampGeometry(region.GetGeometryAt(seconds));
            var sr = VideoGeometryToScreenRect(geometry);
            var isSelected = ReferenceEquals(region, selected);

            var border = new Border
            {
                Width = Math.Max(2, sr.Width),
                Height = Math.Max(2, sr.Height),
                BorderThickness = new Thickness(isSelected ? 2.5 : 2),
                BorderBrush = new SolidColorBrush(isSelected
                    ? GetThemeColor("SelectedRegionBrush", Color.FromRgb(255, 204, 73))
                    : GetThemeColor("RegionBrush", Color.FromRgb(47, 128, 255))),
                Background = new SolidColorBrush(isSelected
                    ? GetThemeColor("SelectedRegionFillBrush", Color.FromArgb(42, 255, 204, 73))
                    : GetThemeColor("RegionFillBrush", Color.FromArgb(26, 47, 128, 255))),
                Cursor = Cursors.SizeAll,
                Tag = region,
                ToolTip = region.IsDynamic ? $"{region.Name} · 动态 {region.Keyframes.Count} 个关键帧" : $"{region.Name} · 静态区域",
            };
            border.MouseLeftButtonDown += RegionBorder_MouseLeftButtonDown;
            border.MouseMove += RegionBorder_MouseMove;
            border.MouseLeftButtonUp += RegionBorder_MouseLeftButtonUp;
            Canvas.SetLeft(border, sr.Left);
            Canvas.SetTop(border, sr.Top);
            OverlayCanvas.Children.Add(border);

            if (isSelected)
            {
                var thumb = new Thumb
                {
                    Width = 14,
                    Height = 14,
                    Background = new SolidColorBrush(GetThemeColor("SelectedRegionBrush", Color.FromRgb(255, 204, 73))),
                    BorderBrush = new SolidColorBrush(GetThemeColor("PanelBrush", Colors.White)),
                    BorderThickness = new Thickness(1),
                    Cursor = Cursors.SizeNWSE,
                    ToolTip = "拖动缩放",
                };
                thumb.Tag = new ResizeVisualTag(region, border);
                thumb.DragStarted += ResizeThumb_DragStarted;
                thumb.DragDelta += ResizeThumb_DragDelta;
                thumb.DragCompleted += ResizeThumb_DragCompleted;
                Canvas.SetLeft(thumb, Math.Max(sr.Left, sr.Right - 7));
                Canvas.SetTop(thumb, Math.Max(sr.Top, sr.Bottom - 7));
                OverlayCanvas.Children.Add(thumb);
            }
        }
    }

    private Rectangle CreateTemporaryRectangle() => new()
    {
        Stroke = new SolidColorBrush(GetThemeColor("SelectedRegionBrush", Color.FromRgb(255, 209, 102))),
        StrokeThickness = 2,
        Fill = new SolidColorBrush(GetThemeColor("SelectedRegionFillBrush", Color.FromArgb(42, 255, 209, 102))),
        IsHitTestVisible = false,
    };

    private static void UpdateRectangleVisual(Rectangle rect, Point a, Point b)
    {
        var r = NormalizeRect(a, b);
        rect.Width = r.Width;
        rect.Height = r.Height;
        Canvas.SetLeft(rect, r.Left);
        Canvas.SetTop(rect, r.Top);
    }

    private RegionGeometry ScreenRectToVideoGeometry(Rect screenRect)
    {
        var vr = GetDisplayedVideoRect();
        var info = _videoInfo!;
        var x = (screenRect.X - vr.X) / vr.Width * info.DisplayWidth;
        var y = (screenRect.Y - vr.Y) / vr.Height * info.DisplayHeight;
        var w = screenRect.Width / vr.Width * info.DisplayWidth;
        var h = screenRect.Height / vr.Height * info.DisplayHeight;
        return ClampGeometry(new RegionGeometry((int)Math.Round(x), (int)Math.Round(y), (int)Math.Round(w), (int)Math.Round(h)));
    }

    private Rect VideoGeometryToScreenRect(RegionGeometry g)
    {
        var vr = GetDisplayedVideoRect();
        var info = _videoInfo!;
        return new Rect(
            vr.X + g.X / (double)info.DisplayWidth * vr.Width,
            vr.Y + g.Y / (double)info.DisplayHeight * vr.Height,
            g.Width / (double)info.DisplayWidth * vr.Width,
            g.Height / (double)info.DisplayHeight * vr.Height);
    }

    private RegionGeometry ClampGeometry(RegionGeometry g)
    {
        var info = _videoInfo!;
        var x = Math.Clamp(g.X, 0, Math.Max(0, info.DisplayWidth - 2));
        var y = Math.Clamp(g.Y, 0, Math.Max(0, info.DisplayHeight - 2));
        var w = Math.Clamp(g.Width, 2, Math.Max(2, info.DisplayWidth - x));
        var h = Math.Clamp(g.Height, 2, Math.Max(2, info.DisplayHeight - y));
        return new RegionGeometry(x, y, w, h);
    }

    private Rect GetDisplayedVideoRect()
    {
        if (_videoInfo is null || OverlayCanvas.ActualWidth <= 0 || OverlayCanvas.ActualHeight <= 0)
            return Rect.Empty;
        var scale = Math.Min(OverlayCanvas.ActualWidth / _videoInfo.DisplayWidth, OverlayCanvas.ActualHeight / _videoInfo.DisplayHeight);
        var width = _videoInfo.DisplayWidth * scale;
        var height = _videoInfo.DisplayHeight * scale;
        return new Rect((OverlayCanvas.ActualWidth - width) / 2.0, (OverlayCanvas.ActualHeight - height) / 2.0, width, height);
    }

    private void PreviewHost_SizeChanged(object sender, SizeChangedEventArgs e) => RedrawRegions();

    private static Rect NormalizeRect(Point a, Point b) =>
        new(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));

    private static Point ClampPointToRect(Point p, Rect r) =>
        new(Math.Clamp(p.X, r.Left, r.Right), Math.Clamp(p.Y, r.Top, r.Bottom));

    // ---------- 列表 / 输出 ----------
    private void ClearRegionsButton_Click(object sender, RoutedEventArgs e)
    {
        Regions.Clear();
        RegionList.SelectedItem = null;
        KeyframeList.ItemsSource = null;
        RedrawRegions();
        StatusText.Text = "已清空全部区域。";
    }

    private async void DetectWatermarkButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputPath is null || _videoInfo is null) return;

        PauseForEditing();
        DetectWatermarkButton.IsEnabled = false;
        EncodeProgress.IsIndeterminate = false;
        EncodeProgress.Value = 0;
        StatusText.Text = "正在自动检测当前画面的文字 / Logo 候选区域…";

        try
        {
            var progress = new Progress<double>(p =>
            {
                EncodeProgress.Value = p;
                StatusText.Text = DescribeDetectionProgress(p);
            });

            var detections = await _aiService.DetectAsync(_inputPath, _videoInfo, CurrentSeconds, CancellationToken.None, progress);
            EncodeProgress.Value = 100;
            if (detections.Count == 0)
            {
                StatusText.Text = "未检测到明显候选区域，可以手动框选。";
                return;
            }

            foreach (var detection in detections)
            {
                var geometry = ClampGeometry(new RegionGeometry(detection.X, detection.Y, detection.Width, detection.Height));
                Regions.Add(new WatermarkRegion
                {
                    Name = $"检测 {Regions.Count + 1:00}",
                    X = geometry.X,
                    Y = geometry.Y,
                    Width = geometry.Width,
                    Height = geometry.Height,
                });
            }

            RegionList.SelectedItem = Regions.LastOrDefault();
            RedrawRegions();
            StatusText.Text = $"已添加 {detections.Count} 个候选区域，可继续拖动/缩放微调。";
        }
        catch (Exception ex)
        {
            StatusText.Text = "自动检测失败。";
            MessageBox.Show(ex.Message, "自动检测失败", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        finally
        {
            DetectWatermarkButton.IsEnabled = _inputPath is not null && !Jobs.Any(j => j.State == VideoJobState.Running);
        }
    }

    private void DeleteSelectedRegionButton_Click(object sender, RoutedEventArgs e)
    {
        if (RegionList.SelectedItem is WatermarkRegion region)
        {
            Regions.Remove(region);
            RegionList.SelectedItem = Regions.LastOrDefault();
            RefreshKeyframeList();
            RedrawRegions();
        }
    }

    private void ChooseOutputButton_Click(object sender, RoutedEventArgs e)
    {
        if (_inputPath is null) return;
        var dialog = new SaveFileDialog
        {
            Title = "选择输出视频",
            Filter = "MP4 视频|*.mp4",
            FileName = Path.GetFileNameWithoutExtension(_inputPath) + "_cleaned.mp4",
            InitialDirectory = Path.GetDirectoryName(_inputPath),
        };
        if (dialog.ShowDialog() == true)
            OutputPathTextBox.Text = dialog.FileName;
    }

    private bool ConfirmOutputPath(string currentPath)
    {
        if (_inputPath is null)
            return false;

        var defaultPath = string.IsNullOrWhiteSpace(currentPath)
            ? BuildDefaultOutputPath(_inputPath)
            : currentPath;
        var directory = Path.GetDirectoryName(defaultPath);
        var dialog = new SaveFileDialog
        {
            Title = "确认视频保存位置",
            Filter = "MP4 视频|*.mp4",
            DefaultExt = ".mp4",
            AddExtension = true,
            OverwritePrompt = true,
            FileName = Path.GetFileName(defaultPath),
            InitialDirectory = string.IsNullOrEmpty(directory) ? Environment.CurrentDirectory : directory,
        };

        if (dialog.ShowDialog() != true)
            return false;

        OutputPathTextBox.Text = dialog.FileName;
        return true;
    }

    private void ModeComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (FfmpegOptionsPanel is null || AiOptionsPanel is null || HeavyOptionsPanel is null || ModeHintText is null) return;
        var mode = SelectedMode;
        var ai = mode == "ai";
        var heavy = mode == "heavy";
        FfmpegOptionsPanel.Visibility = ai || heavy ? Visibility.Collapsed : Visibility.Visible;
        AiOptionsPanel.Visibility = ai ? Visibility.Visible : Visibility.Collapsed;
        HeavyOptionsPanel.Visibility = heavy ? Visibility.Visible : Visibility.Collapsed;
        ModeHintText.Text = heavy
            ? "使用 ProPainter 视频修复大模型做时空级重建，能更好处理复杂文字、半透明 Logo 和画面残留；速度较慢，适合需要最高画质的视频。"
            : ai
                ? "使用逐帧 Mask + OpenCV Telea 做本地算法修复；适合边界清晰的 Logo 与文字，速度比强力模式快。"
                : "适合固定 Logo、字幕角标；速度快，完全本地处理。";
        UpdateEstimatedTime();
    }

    private string SelectedMode => (ModeComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "ffmpeg";

    private void UpdateEstimatedTime()
    {
        if (EstimatedTimeText is null) return;
        if (_videoInfo is null)
        {
            EstimatedTimeText.Text = "预计处理时间：选择视频后显示。";
            return;
        }

        var endSeconds = _videoInfo.Duration.TotalSeconds;
        if (TryParseTime(EndTimeTextBox.Text, out var end) && end.TotalSeconds > 0)
            endSeconds = Math.Min(endSeconds, end.TotalSeconds);
        var startSeconds = TryParseTime(StartTimeTextBox.Text, out var start) ? start.TotalSeconds : 0;
        var duration = Math.Max(0, endSeconds - startSeconds);
        var estimate = ProcessingTimeEstimator.EstimateSeconds(
            SelectedMode,
            duration,
            _videoInfo.DisplayWidth,
            _videoInfo.DisplayHeight,
            Regions);
        var modeName = SelectedMode switch
        {
            "heavy" => "强力修复",
            "ai" => "算法修复",
            _ => "快速模式",
        };
        EstimatedTimeText.Text = estimate <= 0
            ? "预计处理时间：等待有效时间范围。"
            : $"预计处理时间（{modeName}）：{ProcessingTimeEstimator.FormatDuration(estimate)}";
    }

    private static string DescribeDetectionProgress(double progress)
    {
        if (progress < 10) return $"自动检测：阶段 1/4，正在打开视频… {progress:0.0}%";
        if (progress < 35) return $"自动检测：阶段 2/4，正在读取当前画面… {progress:0.0}%";
        if (progress < 70) return $"自动检测：阶段 3/4，正在分析文字和 Logo 边缘… {progress:0.0}%";
        if (progress < 100) return $"自动检测：阶段 4/4，正在合并候选区域… {progress:0.0}%";
        return "自动检测完成。";
    }

    private void CheckAiButton_Click(object sender, RoutedEventArgs e)
    {
        var ok = _aiService.IsConfigured(out var reason);
        AiStatusText.Text = reason;
        MessageBox.Show(reason, ok ? "本地算法环境可用" : "本地算法环境未配置", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void CheckHeavyButton_Click(object sender, RoutedEventArgs e)
    {
        var ok = _heavyService.IsReady(out var reason);
        HeavyStatusText.Text = reason;
        MessageBox.Show(reason, ok ? "强力修复引擎可用" : "强力修复引擎不可用", MessageBoxButton.OK,
            ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
    }

    private void ProcessButton_Click(object sender, RoutedEventArgs e)
    {
        if (_isStartingJob) return;
        _isStartingJob = true;
        try
        {
            StartCurrentVideoProcessing();
        }
        finally
        {
            _isStartingJob = false;
        }
    }

    private void StartCurrentVideoProcessing()
    {
        if (_inputPath is null || _videoInfo is null) return;
        if (Regions.Count == 0)
        {
            MessageBox.Show("请先在视频画面上框选至少一个水印区域。", "没有水印区域", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }

        if (!TryParseTime(StartTimeTextBox.Text, out var start) || !TryParseTime(EndTimeTextBox.Text, out var end))
        {
            MessageBox.Show("时间格式应为 HH:mm:ss 或 HH:mm:ss.fff。", "时间格式错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (end <= start || end > _videoInfo.Duration + TimeSpan.FromSeconds(1))
        {
            MessageBox.Show("请检查开始/结束时间范围。", "时间范围错误", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }
        if (SelectedMode == "heavy" && !_heavyService.IsReady(out var heavyReason))
        {
            StatusText.Text = heavyReason;
            MessageBox.Show(heavyReason, "强力修复引擎不可用", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var suggestedOutput = string.IsNullOrWhiteSpace(OutputPathTextBox.Text.Trim())
            ? BuildDefaultOutputPath(_inputPath)
            : OutputPathTextBox.Text.Trim();
        if (Jobs.Any(j => string.Equals(j.OutputPath, suggestedOutput, StringComparison.OrdinalIgnoreCase)))
            suggestedOutput = FindFreeOutputPath(_inputPath);

        if (!ConfirmOutputPath(suggestedOutput))
            return;
        var output = OutputPathTextBox.Text.Trim();

        var duplicateOutputJob = Jobs.FirstOrDefault(j =>
            string.Equals(j.OutputPath, output, StringComparison.OrdinalIgnoreCase));
        if (duplicateOutputJob is not null)
        {
            MessageBox.Show(
                $"输出文件已被另一个处理任务使用：\n{output}\n\n请重新选择不同的保存位置。",
                "输出文件冲突",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            OutputPathTextBox.Text = BuildDefaultOutputPath(_inputPath);
            return;
        }

        var estimatedSeconds = ProcessingTimeEstimator.EstimateSeconds(
            SelectedMode,
            (end - start).TotalSeconds,
            _videoInfo.DisplayWidth,
            _videoInfo.DisplayHeight,
            Regions);
        var job = new VideoJob
        {
            InputPath = _inputPath,
            OutputPath = output,
            Mode = SelectedMode,
            QualityTag = (QualityComboBox.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "balanced",
            ExpandMask = SelectedMode == "ai"
                ? AiFp16CheckBox.IsChecked == true
                : HeavyDilationCheckBox.IsChecked == true,
            StartSeconds = start.TotalSeconds,
            EndSeconds = end.TotalSeconds,
            EstimatedTotalSeconds = estimatedSeconds,
        };
        Jobs.Add(job);
        CopyActiveRegionsToJob(job);

        StatusText.Text = "已加入处理队列，正在后台处理…";
        _ = StartJobSafelyAsync(job);
        ResetEditorToInitialState();
        RefreshQueueSummary();
    }

    private async Task StartJobSafelyAsync(VideoJob job)
    {
        try
        {
            await StartJobWhenSlotAvailableAsync(job);
        }
        catch (Exception ex)
        {
            job.State = VideoJobState.Failed;
            job.Status = "处理失败";
            job.Error = ex.Message;
            UpdateQueueButton();
            RefreshQueueSummary();
        }
    }

    private void ResetEditorToInitialState()
    {
        _inputPath = null;
        _videoInfo = null;
        _editingJobId = null;
        _isPlaying = false;

        MediaPlayer.Close();
        Regions.Clear();
        RegionList.SelectedItem = null;
        KeyframeList.ItemsSource = null;

        EmptyHint.Visibility = Visibility.Visible;
        PlayerBar.Visibility = Visibility.Collapsed;
        PlayPauseButton.IsEnabled = false;
        StopButton.IsEnabled = false;
        DetectWatermarkButton.IsEnabled = false;
        ProcessButton.IsEnabled = false;
        TimelineSlider.IsEnabled = false;
        EncodeProgress.Value = 0;
        TimelineSlider.Value = 0;
        TimelineSlider.Maximum = 1;
        CurrentTimeText.Text = "00:00:00";
        DurationText.Text = "00:00:00";
        StartTimeTextBox.Text = "00:00:00";
        EndTimeTextBox.Text = "00:00:00";
        OutputPathTextBox.Text = string.Empty;
        FileInfoText.Text = "请选择视频；空白处拖拽可新建水印框，已有框可直接拖动，右下角可缩放。";
        StatusText.Text = "等待导入视频";
        UpdatePlaybackButton();
        UpdateQueueButton();
    }

    private void CopyActiveRegionsToJob(VideoJob job)
    {
        job.Regions.Clear();
        foreach (var region in Regions)
        {
            var snapshot = new RegionSnapshot
            {
                Name = region.Name,
                X = region.X,
                Y = region.Y,
                Width = region.Width,
                Height = region.Height,
            };
            foreach (var keyframe in region.Keyframes.OrderBy(k => k.TimeSeconds))
            {
                snapshot.Keyframes.Add(new RegionKeyframeSnapshot
                {
                    TimeSeconds = keyframe.TimeSeconds,
                    X = keyframe.X,
                    Y = keyframe.Y,
                    Width = keyframe.Width,
                    Height = keyframe.Height,
                });
            }
            job.Regions.Add(snapshot);
        }
    }

    private void QueueButton_Click(object sender, RoutedEventArgs e) => ShowQueueView();

    private void ShowQueueView()
    {
        EditorView.Visibility = Visibility.Collapsed;
        QueueView.Visibility = Visibility.Visible;
        RefreshQueueSummary();
    }

    private void ShowEditorView()
    {
        QueueView.Visibility = Visibility.Collapsed;
        EditorView.Visibility = Visibility.Visible;
    }

    private void BackToEditorButton_Click(object sender, RoutedEventArgs e) => ShowEditorView();

    public void RefreshQueueSummary()
    {
        if (JobList is null || QueueSummaryText is null) return;
        JobList.ItemsSource = Jobs;
        var running = Jobs.Count(j => j.State == VideoJobState.Running);
        var queued = Jobs.Count(j => j.State == VideoJobState.Queued);
        var done = Jobs.Count(j => j.State == VideoJobState.Completed);
        var failed = Jobs.Count(j => j.State == VideoJobState.Failed);
        var remaining = Jobs
            .Where(j => j.State is VideoJobState.Running or VideoJobState.Queued)
            .Sum(j => j.EstimatedRemainingSeconds);
        var etaText = remaining > 0
            ? $"  ·  预计剩余 {ProcessingTimeEstimator.FormatDuration(remaining)}"
            : string.Empty;
        QueueSummaryText.Text = $"共 {Jobs.Count} 个视频  ·  处理中 {running}  ·  等待 {queued}  ·  完成 {done}  ·  失败 {failed}{etaText}";
    }

    private async void QueueImportButton_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "选择多个视频",
            Multiselect = true,
            Filter = "视频文件|*.mp4;*.mov;*.mkv;*.avi;*.m4v;*.webm;*.wmv|所有文件|*.*",
        };
        if (dialog.ShowDialog() != true)
            return;

        await ImportVideosAsync(dialog.FileNames, loadLast: false);
    }

    private async void QueueStartAllButton_Click(object sender, RoutedEventArgs e)
    {
        await StartQueueAsync();
        RefreshQueueSummary();
    }

    private void QueueReRunSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        if (JobList.SelectedItem is not VideoJob source)
            return;

        var job = new VideoJob
        {
            InputPath = source.InputPath,
            OutputPath = FindFreeOutputPath(source.InputPath),
            Mode = source.Mode,
            QualityTag = source.QualityTag,
            ExpandMask = source.ExpandMask,
            StartSeconds = source.StartSeconds,
            EndSeconds = source.EndSeconds,
            EstimatedTotalSeconds = source.EstimatedTotalSeconds,
        };
        foreach (var region in source.Regions)
        {
            var snapshot = new RegionSnapshot
            {
                Name = region.Name,
                X = region.X,
                Y = region.Y,
                Width = region.Width,
                Height = region.Height,
            };
            foreach (var keyframe in region.Keyframes)
            {
                snapshot.Keyframes.Add(new RegionKeyframeSnapshot
                {
                    TimeSeconds = keyframe.TimeSeconds,
                    X = keyframe.X,
                    Y = keyframe.Y,
                    Width = keyframe.Width,
                    Height = keyframe.Height,
                });
            }
            job.Regions.Add(snapshot);
        }

        Jobs.Add(job);
        job.State = VideoJobState.Queued;
        job.Status = "等待处理";
        _ = StartJobSafelyAsync(job);
        UpdateQueueButton();
        RefreshQueueSummary();
    }

    private async void QueuePrioritySelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = JobList.SelectedItems.OfType<VideoJob>()
            .Where(j => j.State is VideoJobState.Queued or VideoJobState.Failed or VideoJobState.Cancelled)
            .ToList();
        if (selected.Count == 0)
            return;

        foreach (var job in selected)
        {
            job.State = VideoJobState.Queued;
            job.Progress = 0;
            job.Error = string.Empty;
            job.Status = "等待处理";
            _ = StartJobSafelyAsync(job);
        }
        UpdateQueueButton();
        RefreshQueueSummary();
        await Task.Yield();
    }

    private void QueueActionSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = JobList.SelectedItems.OfType<VideoJob>().ToList();
        if (selected.Count == 0)
            return;

        foreach (var job in selected)
        {
            if (job.State == VideoJobState.Running)
            {
                CancelJob(job);
            }
            else if (job.State == VideoJobState.Queued || job.IsFinished)
            {
                if (job.State == VideoJobState.Queued)
                    CancelJob(job);
                Jobs.Remove(job);
            }
        }
        UpdateQueueButton();
        RefreshQueueSummary();
    }

    private void QueueCancelSelectedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in JobList.SelectedItems.OfType<VideoJob>().Where(j => !j.IsFinished).ToList())
            CancelJob(job);
        RefreshQueueSummary();
    }

    private void QueueDeleteQueuedButton_Click(object sender, RoutedEventArgs e)
    {
        var selected = JobList.SelectedItems.OfType<VideoJob>().ToList();
        foreach (var job in selected.Where(j => j.State == VideoJobState.Queued).ToList())
        {
            CancelJob(job);
            Jobs.Remove(job);
        }
        UpdateQueueButton();
        RefreshQueueSummary();
    }

    private void QueueRemoveFinishedButton_Click(object sender, RoutedEventArgs e)
    {
        foreach (var job in Jobs.Where(j => j.IsFinished).ToList())
            Jobs.Remove(job);
        RefreshQueueSummary();
    }

    private async void JobList_MouseDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (JobList.SelectedItem is not VideoJob job)
            return;
        await OpenVideoInEditorAsync(job);
        ShowEditorView();
    }

    public async Task StartQueueAsync()
    {
        var jobs = Jobs.Where(j =>
            j.State != VideoJobState.Running &&
            j.State != VideoJobState.Completed).ToList();
        foreach (var job in jobs)
        {
            job.State = VideoJobState.Queued;
            job.Progress = 0;
            job.Error = string.Empty;
            job.Status = "等待处理";
            _ = StartJobWhenSlotAvailableAsync(job);
        }
        UpdateQueueButton();
        await Task.Yield();
    }

    private async Task StartJobWhenSlotAvailableAsync(VideoJob job)
    {
        if (job.State is VideoJobState.Running or VideoJobState.Completed)
            return;

        var slot = job.Mode == "heavy" ? _heavySlots : _workerSlots;
        await slot.WaitAsync();
        try
        {
            if (job.State == VideoJobState.Cancelled)
                return;
            await LaunchWorkerAsync(job);
        }
        finally
        {
            slot.Release();
        }
    }

    private async Task LaunchWorkerAsync(VideoJob job)
    {
        var outputDirectory = Path.GetDirectoryName(job.OutputPath);
        if (!string.IsNullOrWhiteSpace(outputDirectory))
            Directory.CreateDirectory(outputDirectory);

        var workRoot = Path.Combine(Path.GetTempPath(), "WatermarkRemoverWindows", "jobs", job.Id);
        Directory.CreateDirectory(workRoot);
        var statusFile = Path.Combine(workRoot, "status.json");
        var regionsFile = Path.Combine(workRoot, "regions.json");
        await File.WriteAllTextAsync(regionsFile, JsonSerializer.Serialize(job.Regions));

        job.StatusFile = statusFile;
        job.State = VideoJobState.Running;
        job.Progress = 0;
        job.Status = "正在启动后台处理进程…";
        job.StartedAtUtc = DateTime.UtcNow;
        UpdateQueueButton();

        var exePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(exePath))
            throw new InvalidOperationException("无法定位当前程序路径，不能启动后台处理进程。");

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = exePath,
            WorkingDirectory = AppContext.BaseDirectory,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var arg in new[]
        {
            "--worker",
            "--input", job.InputPath,
            "--output", job.OutputPath,
            "--mode", job.Mode,
            "--quality", job.QualityTag,
            "--expand-mask", job.ExpandMask ? "true" : "false",
            "--start", job.StartSeconds.ToString(CultureInfo.InvariantCulture),
            "--end", job.EndSeconds.ToString(CultureInfo.InvariantCulture),
            "--status-file", statusFile,
            "--regions-file", regionsFile,
        })
            psi.ArgumentList.Add(arg);

        var process = System.Diagnostics.Process.Start(psi);
        if (process is null)
            throw new InvalidOperationException("无法启动后台处理进程。");
        job.WorkerProcess = process;

        await process.WaitForExitAsync();
        PollSingleJob(job);
        if (job.State == VideoJobState.Running)
        {
            job.State = process.ExitCode == 0
                ? VideoJobState.Completed
                : VideoJobState.Failed;
            job.Status = process.ExitCode == 0 ? "处理完成" : "处理进程异常退出";
            if (process.ExitCode != 0)
                job.Error = string.IsNullOrWhiteSpace(job.Error) ? "后台处理进程异常退出。" : job.Error;
        }

        process.Dispose();
        job.WorkerProcess = null;
        UpdateQueueButton();
        RefreshQueueSummary();
    }

    private void PollQueueStatuses()
    {
        foreach (var job in Jobs.Where(j => j.State is VideoJobState.Running or VideoJobState.Queued))
            job.RefreshTiming();
        foreach (var job in Jobs.Where(j => j.State == VideoJobState.Running).ToList())
            PollSingleJob(job);
        if (QueueView.Visibility == Visibility.Visible)
            RefreshQueueSummary();
    }

    private void PollSingleJob(VideoJob job)
    {
        if (job.State != VideoJobState.Running || string.IsNullOrWhiteSpace(job.StatusFile) || !File.Exists(job.StatusFile))
            return;

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(job.StatusFile));
            var root = doc.RootElement;
            var state = root.TryGetProperty("state", out var stateValue) ? stateValue.GetString() : "running";
            if (root.TryGetProperty("progress", out var progressValue) && progressValue.TryGetDouble(out var progress))
                job.Progress = progress;
            if (root.TryGetProperty("status", out var statusValue))
                job.Status = statusValue.GetString() ?? job.Status;

            if (string.Equals(state, "done", StringComparison.OrdinalIgnoreCase))
            {
                job.State = VideoJobState.Completed;
                job.Progress = 100;
                job.Status = "处理完成";
            }
            else if (string.Equals(state, "error", StringComparison.OrdinalIgnoreCase))
            {
                job.State = VideoJobState.Failed;
                job.Status = "处理失败";
                job.Error = root.TryGetProperty("error", out var errorValue)
                    ? errorValue.GetString() ?? "未知错误"
                    : "未知错误";
            }
            else if (job.WorkerProcess is { HasExited: true })
            {
                job.State = VideoJobState.Failed;
                job.Status = "处理进程异常退出";
                job.Error = "后台处理进程已退出，但没有写入完成状态。";
            }
            UpdateQueueButton();
            RefreshQueueSummary();
        }
        catch
        {
        }
    }

    public void CancelJob(VideoJob job)
    {
        try
        {
            if (job.WorkerProcess is { HasExited: false })
                job.WorkerProcess.Kill(entireProcessTree: true);
        }
        catch
        {
        }
        job.WorkerProcess?.Dispose();
        job.WorkerProcess = null;
        job.State = VideoJobState.Cancelled;
        job.Status = "已取消";
        job.Progress = 0;
        UpdateQueueButton();
        RefreshQueueSummary();
    }

    private void UpdateQueueButton()
    {
        if (QueueButton is null) return;
        var active = Jobs.Count(j => j.State is VideoJobState.Running or VideoJobState.Queued);
        QueueButton.Content = active > 0 ? $"处理队列 · {active}" : "处理队列";
    }

    private void MainWindow_Closing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        foreach (var job in Jobs.Where(j => j.State is VideoJobState.Running or VideoJobState.Queued).ToList())
            CancelJob(job);
    }

    private static bool TryParseTime(string text, out TimeSpan value)
    {
        var formats = new[] { @"hh\:mm\:ss", @"hh\:mm\:ss\.fff", @"h\:mm\:ss", @"h\:mm\:ss\.fff" };
        return TimeSpan.TryParseExact(text.Trim(), formats, CultureInfo.InvariantCulture, out value)
               || TimeSpan.TryParse(text.Trim(), CultureInfo.InvariantCulture, out value);
    }

    private static string BuildDefaultOutputPath(string inputPath) =>
        Path.Combine(Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory,
                     Path.GetFileNameWithoutExtension(inputPath) + "_cleaned.mp4");

    private string FindFreeOutputPath(string inputPath)
    {
        var directory = Path.GetDirectoryName(inputPath) ?? Environment.CurrentDirectory;
        var stem = Path.GetFileNameWithoutExtension(inputPath);
        for (var index = 0; index < 10000; index++)
        {
            var name = index == 0 ? $"{stem}_cleaned.mp4" : $"{stem}_cleaned_{index}.mp4";
            var candidate = Path.Combine(directory, name);
            if (!File.Exists(candidate) &&
                !Jobs.Any(j => string.Equals(j.OutputPath, candidate, StringComparison.OrdinalIgnoreCase)))
                return candidate;
        }

        return BuildDefaultOutputPath(inputPath);
    }

    private static string FormatTime(TimeSpan t, bool includeMilliseconds = false) => includeMilliseconds
        ? $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}.{t.Milliseconds:000}"
        : $"{(int)t.TotalHours:00}:{t.Minutes:00}:{t.Seconds:00}";

    private sealed record ResizeVisualTag(WatermarkRegion Region, Border Border);

    private sealed record ThemePalette(
        string Background,
        string Panel,
        string PanelAlt,
        string Border,
        string Text,
        string Muted,
        string Accent,
        string AccentHover,
        string ButtonForeground,
        string PrimaryButtonForeground,
        string ButtonHover,
        string Input,
        string Preview,
        string Hint,
        string Region,
        string RegionFill,
        string SelectedRegion,
        string SelectedRegionFill);
}

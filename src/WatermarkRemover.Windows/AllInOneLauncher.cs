using System;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Threading;
using System.Windows.Forms;

internal static class AllInOneLauncher
{
    private const string AppName = "Watermark Remover Windows";
    private const string Version = "v0.4-propainter-heavy-r1";

    [STAThread]
    private static int Main()
    {
        ProgressWindow progressWindow = null;
        try
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            progressWindow = new ProgressWindow();
            progressWindow.Show();
            progressWindow.SetProgress(2, "正在检查程序文件...");

            string installRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WatermarkRemoverWindows",
                "app",
                Version);

            string marker = Path.Combine(installRoot, ".installed");
            string mainExe = Path.Combine(installRoot, "WatermarkRemoverWindows.exe");
            string installerExe = Path.Combine(installRoot, "InstallDependencies.exe");

            if (!File.Exists(marker) || !File.Exists(mainExe) || !File.Exists(installerExe))
                ExtractPayload(installRoot, marker, progressWindow);

            progressWindow.SetProgress(75, "正在检查 FFmpeg、OpenCV 与 ProPainter 引擎...");

            if (!DependenciesReady(installRoot))
            {
                progressWindow.SetProgress(80, "发现缺少的依赖，准备打开安装窗口...");
                progressWindow.Hide();
                DialogResult result = MessageBox.Show(
                    "首次运行需要准备依赖。已经安装好的 FFmpeg / Python / OpenCV 会自动跳过，只补缺少的部分。点击“确定”后会打开带分段进度条的安装窗口。安装完成后，请按任意键关闭安装窗口，程序会继续启动。",
                    AppName,
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information);
                if (result != DialogResult.OK)
                    return 1;

                using (Process setup = Process.Start(new ProcessStartInfo
                {
                    FileName = installerExe,
                    WorkingDirectory = installRoot,
                    UseShellExecute = true,
                }))
                {
                    if (setup == null)
                        throw new InvalidOperationException("无法启动依赖安装器。");
                    setup.WaitForExit();
                    if (setup.ExitCode != 0)
                        throw new InvalidOperationException("依赖安装失败，安装器退出码：" + setup.ExitCode);
                }

                progressWindow.Show();
                progressWindow.SetProgress(90, "正在验证安装结果...");
                if (!DependenciesReady(installRoot))
                    throw new InvalidOperationException("安装器已结束，但仍有依赖未准备完成。请重新运行本程序并查看安装窗口中的具体提示。");
            }

            progressWindow.SetProgress(96, "依赖已就绪，正在启动主程序...");
            Process mainProcess = Process.Start(new ProcessStartInfo
            {
                FileName = mainExe,
                WorkingDirectory = installRoot,
                UseShellExecute = true,
            });
            if (mainProcess == null)
                throw new InvalidOperationException("无法启动主程序。");

            if (!WaitForStableStartup(mainProcess, progressWindow))
            {
                string logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WatermarkRemoverWindows",
                    "logs",
                    "startup-error.log");
                throw new InvalidOperationException(
                    "主程序在启动后的 3 秒内退出，可能启动失败。详细日志：" +
                    logPath);
            }

            progressWindow.SetProgress(100, "启动完成");
            return 0;
        }
        catch (Exception ex)
        {
            if (progressWindow != null) progressWindow.Hide();
            MessageBox.Show(
                ex.Message + Environment.NewLine + Environment.NewLine +
                "日志位置：" + Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "WatermarkRemoverWindows",
                    "logs",
                    "startup-error.log"),
                AppName + " 启动失败",
                MessageBoxButtons.OK,
                MessageBoxIcon.Error);
            return 1;
        }
        finally
        {
            if (progressWindow != null) progressWindow.Close();
        }
    }

    private static bool WaitForStableStartup(Process process, ProgressWindow progressWindow)
    {
        DateTime deadline = DateTime.UtcNow.AddSeconds(3);
        while (DateTime.UtcNow < deadline)
        {
            progressWindow.SetProgress(98, "正在确认主程序稳定运行...");
            process.Refresh();
            if (process.HasExited)
                return false;

            Thread.Sleep(150);
            Application.DoEvents();
        }

        process.Refresh();
        return !process.HasExited;
    }

    private static void ExtractPayload(string installRoot, string marker, ProgressWindow progressWindow)
    {
        progressWindow.SetProgress(5, "正在准备全新程序目录...");
        if (Directory.Exists(installRoot))
            Directory.Delete(installRoot, true);
        Directory.CreateDirectory(installRoot);
        string payload = Path.Combine(Path.GetTempPath(), "WatermarkRemoverWindowsPayload-" + Guid.NewGuid().ToString("N") + ".zip");

        try
        {
            using (Stream input = Assembly.GetExecutingAssembly().GetManifestResourceStream("payload.zip"))
            {
                if (input == null)
                    throw new FileNotFoundException("启动器中缺少 payload.zip。");

                using (FileStream output = File.Create(payload))
                    CopyStreamWithProgress(input, output, progressWindow, 8, 28, "正在读取内置程序文件...");
            }

            ExtractZipWithProgress(payload, installRoot, progressWindow, 30, 70);
            File.WriteAllText(marker, DateTime.UtcNow.ToString("O"));
        }
        finally
        {
            try { File.Delete(payload); } catch { }
        }
    }

    private static void CopyStreamWithProgress(
        Stream input,
        Stream output,
        ProgressWindow progressWindow,
        int startPercent,
        int endPercent,
        string message)
    {
        byte[] buffer = new byte[1024 * 1024];
        long total = input.CanSeek ? input.Length : 0;
        long copied = 0;
        int read;
        int lastPercent = -1;

        while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
        {
            output.Write(buffer, 0, read);
            copied += read;
            int percent = total > 0
                ? startPercent + (int)((endPercent - startPercent) * copied / total)
                : startPercent;
            if (percent != lastPercent)
            {
                progressWindow.SetProgress(percent, message);
                lastPercent = percent;
            }
        }
    }

    private static void ExtractZipWithProgress(
        string payload,
        string installRoot,
        ProgressWindow progressWindow,
        int startPercent,
        int endPercent)
    {
        string rootPrefix = Path.GetFullPath(installRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

        using (ZipArchive archive = ZipFile.OpenRead(payload))
        {
            long total = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
                total += entry.Length;

            long extracted = 0;
            int lastPercent = -1;
            byte[] buffer = new byte[1024 * 1024];

            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string destination = Path.GetFullPath(Path.Combine(installRoot, entry.FullName.Replace('/', Path.DirectorySeparatorChar)));
                if (!destination.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("安装包中包含无效路径：" + entry.FullName);

                if (string.IsNullOrEmpty(entry.Name))
                {
                    Directory.CreateDirectory(destination);
                    continue;
                }

                string directory = Path.GetDirectoryName(destination);
                if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);

                using (Stream input = entry.Open())
                using (FileStream output = File.Create(destination))
                {
                    int read;
                    while ((read = input.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        output.Write(buffer, 0, read);
                        extracted += read;
                        int percent = total > 0
                            ? startPercent + (int)((endPercent - startPercent) * extracted / total)
                            : startPercent;
                        if (percent != lastPercent)
                        {
                            progressWindow.SetProgress(percent, "正在解压主程序和安装器...");
                            lastPercent = percent;
                        }
                    }
                }
            }
        }
    }

    private static bool DependenciesReady(string installRoot)
    {
        string bundledTools = Path.Combine(installRoot, "tools");
        string bundledAi = Path.Combine(installRoot, "ai");
        string bundledHeavy = Path.Combine(installRoot, "heavy");
        if (File.Exists(Path.Combine(bundledTools, "ffmpeg.exe"))
            && File.Exists(Path.Combine(bundledTools, "ffprobe.exe"))
            && File.Exists(Path.Combine(bundledAi, "ai_runner.exe"))
            && File.Exists(Path.Combine(bundledHeavy, ".venv", "Scripts", "python.exe"))
            && File.Exists(Path.Combine(bundledHeavy, "heavy_runner.py"))
            && File.Exists(Path.Combine(bundledHeavy, "ProPainter", "inference_propainter.py")))
            return true;

        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string tools = Path.Combine(local, "WatermarkRemoverWindows", "tools");
        string ai = Path.Combine(local, "WatermarkRemoverWindows", "ai");

        return File.Exists(Path.Combine(tools, "ffmpeg.exe"))
            && File.Exists(Path.Combine(tools, "ffprobe.exe"))
            && File.Exists(Path.Combine(ai, "venv", "Scripts", "python.exe"))
            && File.Exists(Path.Combine(ai, "ai_runner.py"));
    }

    private sealed class ProgressWindow : Form
    {
        private readonly Label statusLabel;
        private readonly ProgressBar progressBar;

        public ProgressWindow()
        {
            Text = AppName + " 正在启动";
            Width = 520;
            Height = 150;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            StartPosition = FormStartPosition.CenterScreen;
            MaximizeBox = false;
            MinimizeBox = false;
            ControlBox = false;
            ShowInTaskbar = true;

            statusLabel = new Label
            {
                Left = 22,
                Top = 20,
                Width = 460,
                Height = 28,
                Text = "正在启动..."
            };
            progressBar = new ProgressBar
            {
                Left = 22,
                Top = 58,
                Width = 460,
                Height = 22,
                Minimum = 0,
                Maximum = 100,
                Style = ProgressBarStyle.Continuous
            };

            Controls.Add(statusLabel);
            Controls.Add(progressBar);
        }

        public void SetProgress(int percent, string message)
        {
            progressBar.Value = Math.Max(0, Math.Min(100, percent));
            statusLabel.Text = message + "  " + progressBar.Value + "%";
            Refresh();
            Application.DoEvents();
        }
    }
}

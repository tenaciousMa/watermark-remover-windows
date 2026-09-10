using System.IO;
using System.Windows;
using System.Windows.Threading;
using WatermarkRemoverWindows.Services;

namespace WatermarkRemoverWindows;

public partial class App : Application
{
    protected override async void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnDispatcherUnhandledException;
        AppDomain.CurrentDomain.UnhandledException += OnAppDomainUnhandledException;
        TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;

        if (IsWorkerProcess())
        {
            try
            {
                int exitCode = await WorkerJobRunner.RunFromCommandLineAsync(Environment.GetCommandLineArgs());
                TryShutdown(exitCode);
            }
            catch (Exception ex)
            {
                WriteStartupError(ex);
                TryShutdown(1);
            }
            return;
        }

        base.OnStartup(e);
        var mainWindow = new MainWindow();
        MainWindow = mainWindow;
        mainWindow.Show();
    }

    private static bool IsWorkerProcess()
    {
        var args = Environment.GetCommandLineArgs();
        return args.Any(arg => string.Equals(arg, "--worker", StringComparison.OrdinalIgnoreCase));
    }

    private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        ShowFatalError(e.Exception);
        e.Handled = true;
        TryShutdown(1);
    }

    private void OnAppDomainUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception exception)
            WriteStartupError(exception);
    }

    private void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        WriteStartupError(e.Exception);
        e.SetObserved();
    }

    private void ShowFatalError(Exception exception)
    {
        WriteStartupError(exception);
        try
        {
            MessageBox.Show(
                exception.Message + Environment.NewLine + Environment.NewLine +
                "详细日志：" + GetStartupLogPath(),
                "Watermark Remover Windows 启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
        }
        catch
        {
        }
    }

    private void TryShutdown(int exitCode)
    {
        try
        {
            Shutdown(exitCode);
        }
        catch
        {
        }
    }

    private static string GetStartupLogPath()
    {
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "WatermarkRemoverWindows",
            "logs",
            "startup-error.log");
    }

    private static void WriteStartupError(Exception exception)
    {
        try
        {
            string logPath = GetStartupLogPath();
            Directory.CreateDirectory(Path.GetDirectoryName(logPath)!);
            File.AppendAllText(
                logPath,
                DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff") + Environment.NewLine +
                exception + Environment.NewLine +
                new string('-', 80) + Environment.NewLine);
        }
        catch
        {
        }
    }

}

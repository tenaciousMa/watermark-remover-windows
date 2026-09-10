using System.IO;

namespace WatermarkRemoverWindows.Services;

public static class ToolLocator
{
    public static string? Find(string fileName)
    {
        var candidates = new[]
        {
            Path.Combine(AppContext.BaseDirectory, "tools", fileName),
            Path.Combine(AppContext.BaseDirectory, fileName),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WatermarkRemoverWindows", "tools", fileName),
            Path.Combine(Environment.CurrentDirectory, "tools", fileName),
            Path.Combine(Environment.CurrentDirectory, fileName),
        };

        foreach (var candidate in candidates)
            if (File.Exists(candidate))
                return candidate;

        var path = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            try
            {
                var candidate = Path.Combine(dir.Trim(), fileName);
                if (File.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // 忽略异常 PATH 项。
            }
        }

        return null;
    }
}

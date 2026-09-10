using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

internal static class InstallDependencies
{
    private static int Main()
    {
        Console.Title = "Watermark Remover dependency installer";
        Console.WriteLine("Watermark Remover Windows v0.2 dependency installer");
        Console.WriteLine("Each dependency step now shows segmented progress.");
        Console.WriteLine();

        try
        {
            string root = AppDomain.CurrentDomain.BaseDirectory;
            ExtractResource("setup-dependencies.ps1", Path.Combine(root, "setup-dependencies.ps1"));
            ExtractResource("setup-ffmpeg.ps1", Path.Combine(root, "setup-ffmpeg.ps1"));
            ExtractResource("setup-ai.ps1", Path.Combine(root, "setup-ai.ps1"));
            string aiDir = Path.Combine(root, "ai");
            Directory.CreateDirectory(aiDir);
            ExtractResource("ai_runner.py", Path.Combine(aiDir, "ai_runner.py"));

            string script = Path.Combine(root, "setup-dependencies.ps1");
            ProcessStartInfo psi = new ProcessStartInfo();
            psi.FileName = "powershell.exe";
            psi.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + script + "\"";
            psi.WorkingDirectory = root;
            psi.UseShellExecute = false;
            psi.RedirectStandardOutput = true;
            psi.RedirectStandardError = true;

            using (Process process = new Process())
            {
                process.StartInfo = psi;
                process.OutputDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) Console.WriteLine(e.Data);
                };
                process.ErrorDataReceived += delegate(object sender, DataReceivedEventArgs e)
                {
                    if (e.Data != null) Console.Error.WriteLine(e.Data);
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                process.WaitForExit();

                if (process.ExitCode != 0)
                    throw new InvalidOperationException("Dependency setup failed with exit code " + process.ExitCode + ".");
            }

            Console.WriteLine();
            Console.WriteLine("All dependencies are ready.");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine("Install failed:");
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
        finally
        {
            Console.WriteLine();
            Console.WriteLine("Press any key to close this window.");
            try { Console.ReadKey(true); } catch { }
        }
    }

    private static void ExtractResource(string resourceName, string targetPath)
    {
        Assembly assembly = Assembly.GetExecutingAssembly();
        using (Stream input = assembly.GetManifestResourceStream(resourceName))
        {
            if (input == null)
                throw new FileNotFoundException("Embedded resource not found: " + resourceName);

            Directory.CreateDirectory(Path.GetDirectoryName(targetPath));
            using (FileStream output = File.Create(targetPath))
                input.CopyTo(output);
        }
    }
}

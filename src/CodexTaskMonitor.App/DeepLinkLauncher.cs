using System.Diagnostics;
using System.Runtime.InteropServices;
using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.App;

public static class DeepLinkLauncher
{
    public static bool Open(CodexTaskSnapshot task)
    {
        try
        {
            Process.Start(new ProcessStartInfo(task.DeepLink) { UseShellExecute = true });
            return true;
        }
        catch (Exception exception)
        {
            _ = AppLog.WriteAsync("WARN", $"Codex deep link failed for {task.ThreadId}", exception);
        }

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"shell:AppsFolder\\OpenAI.Codex_2p2nqsd0c76g0!App \"{task.DeepLink}\"",
                UseShellExecute = true,
            });
            return true;
        }
        catch (Exception exception)
        {
            _ = AppLog.WriteAsync("WARN", "Codex AppX fallback failed", exception);
        }

        return FocusCodex();
    }

    public static bool FocusCodex()
    {
        var process = Process.GetProcessesByName("ChatGPT")
            .FirstOrDefault(item => item.MainWindowHandle != IntPtr.Zero);
        return process is not null && SetForegroundWindow(process.MainWindowHandle);
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetForegroundWindow(IntPtr window);
}

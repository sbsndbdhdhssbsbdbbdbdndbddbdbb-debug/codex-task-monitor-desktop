using Microsoft.Win32;

namespace CodexTaskMonitor.App;

public static class StartupRegistration
{
    private const string ValueName = "CodexTaskMonitor";

    public static void SetEnabled(bool enabled)
    {
        using var key = Registry.CurrentUser.CreateSubKey(
            @"Software\Microsoft\Windows\CurrentVersion\Run",
            writable: true);
        if (enabled)
        {
            var executable = Environment.ProcessPath
                             ?? throw new InvalidOperationException("Executable path is unavailable.");
            key.SetValue(ValueName, $"\"{executable}\" --minimized");
        }
        else
        {
            key.DeleteValue(ValueName, throwOnMissingValue: false);
        }
    }
}

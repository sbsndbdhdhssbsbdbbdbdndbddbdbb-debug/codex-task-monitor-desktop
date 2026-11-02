namespace CodexTaskMonitor.Core;

public sealed record MonitorPaths(
    string CodexHome,
    string SessionsRoot,
    string AppDataRoot,
    string MonitorDatabase,
    string SettingsFile,
    string IntegrationFile,
    string StateFile,
    string LogsDirectory)
{
    public static MonitorPaths ForCurrentUser()
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var codexHome = Path.Combine(profile, ".codex");
        var appDataRoot = Path.Combine(localAppData, "CodexTaskMonitor");

        return new MonitorPaths(
            codexHome,
            Path.Combine(codexHome, "sessions"),
            appDataRoot,
            Path.Combine(appDataRoot, "monitor.db"),
            Path.Combine(appDataRoot, "settings.json"),
            Path.Combine(appDataRoot, "integration.json"),
            Path.Combine(appDataRoot, "state.json"),
            Path.Combine(appDataRoot, "logs"));
    }

    public string? FindLatestStateDatabase()
    {
        return FindLatestDatabase("state_*.sqlite");
    }

    public string? FindLatestLogsDatabase()
    {
        return FindLatestDatabase("logs_*.sqlite");
    }

    private string? FindLatestDatabase(string pattern)
    {
        if (!Directory.Exists(CodexHome))
        {
            return null;
        }

        return Directory.EnumerateFiles(CodexHome, pattern, SearchOption.TopDirectoryOnly)
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }
}

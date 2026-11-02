using CodexTaskMonitor.Core;

namespace CodexTaskMonitor.Tests;

public sealed class MonitorSettingsTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexTaskMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task PersistsDoNotDisturbAndDesktopPetState()
    {
        Directory.CreateDirectory(_directory);
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var settings = new MonitorSettings
        {
            DoNotDisturbEnabled = true,
            DesktopPetPackageId = "ocean",
            DesktopPetCollapsed = true,
            DesktopPetLeft = 120,
            DesktopPetTop = 240,
        };

        await store.SaveAsync(settings);
        var restored = await store.LoadAsync();

        Assert.True(restored.DoNotDisturbEnabled);
        Assert.Equal("ocean", restored.DesktopPetPackageId);
        Assert.True(restored.DesktopPetCollapsed);
        Assert.Equal(120, restored.DesktopPetLeft);
        Assert.Equal(240, restored.DesktopPetTop);
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }

    [Fact]
    public async Task SerializesRapidSettingChangesWithoutLosingTheLastChoice()
    {
        var store = new SettingsStore(Path.Combine(_directory, "settings.json"));
        var saves = Enumerable.Range(0, 12)
            .Select(index => store.SaveAsync(new MonitorSettings { ApiPort = 48000 + index, DoNotDisturbEnabled = index % 2 == 1 }))
            .ToArray();
        await Task.WhenAll(saves);
        var restored = await store.LoadAsync();
        Assert.Equal(48011, restored.ApiPort);
        Assert.True(restored.DoNotDisturbEnabled);
        Assert.Empty(Directory.EnumerateFiles(_directory, "*.tmp"));
    }

}

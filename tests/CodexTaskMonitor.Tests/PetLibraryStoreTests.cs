using CodexTaskMonitor.Core;
using SkiaSharp;

namespace CodexTaskMonitor.Tests;

public sealed class PetLibraryStoreTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(),
        "CodexTaskMonitor.Tests",
        Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task InstallsPetIntoItsOwnFolderAndKeepsSourceInformation()
    {
        Directory.CreateDirectory(_directory);
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku",
            "Goku",
            "A compact coding companion.",
            "https://petdex.dev/pets/goku",
            "https://petdex.crafter.run/pets/goku/pet.json",
            "https://petdex.crafter.run/pets/goku/spritesheet.png",
            new[] { "idle", "running", "waving" },
            SpriteSheetLayout.Standard);

        var installed = await store.InstallAsync(package, CreatePng(80, 90));

        Assert.Equal("goku", installed.Id);
        Assert.Equal("Goku", installed.Name);
        Assert.True(File.Exists(installed.MetadataPath));
        Assert.True(File.Exists(installed.SpriteSheetPath));
        Assert.EndsWith(".png", installed.SpriteSheetPath, StringComparison.OrdinalIgnoreCase);
        Assert.True(File.Exists(Path.Combine(_directory, "goku", "pet.json")));
        Assert.Equal("https://petdex.dev/pets/goku", installed.SourcePageUrl);
    }

    [Fact]
    public async Task RejectsAnInvalidSpriteSheetWithoutReplacingAnInstalledPet()
    {
        Directory.CreateDirectory(_directory);
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku", "Goku", "", "https://petdex.dev/pets/goku",
            "https://example.test/pet.json", "https://example.test/spritesheet.png",
            new[] { "idle" }, SpriteSheetLayout.Standard);
        var original = await store.InstallAsync(package, CreatePng(80, 90));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.InstallAsync(package with { Name = "Broken Goku" }, new byte[] { 1, 2, 3 }));

        var pets = await store.ListAsync();
        var restored = Assert.Single(pets);
        Assert.Equal(original.Name, restored.Name);
    }

    [Theory]
    [InlineData(8193, 9)]
    [InlineData(8, 8193)]
    [InlineData(4096, 4097)]
    [InlineData(80, 90)]
    public async Task RejectedDecodedContentLeavesTheInstalledPackageUntouched(int width, int height)
    {
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku", "Goku", "", "https://petdex.dev/pets/goku",
            "https://example.test/pet.json", "https://example.test/spritesheet.png",
            new[] { "idle" }, SpriteSheetLayout.Standard);
        var original = await store.InstallAsync(package, CreatePng(80, 90));
        var originalSprite = await File.ReadAllBytesAsync(original.SpriteSheetPath);
        var originalMetadata = await File.ReadAllTextAsync(original.MetadataPath);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.InstallAsync(
            package with { Name = "Broken Goku" }, PetSpriteTestImages.CreatePngHeader(width, height)));

        Assert.Equal(originalSprite, await File.ReadAllBytesAsync(original.SpriteSheetPath));
        Assert.Equal(originalMetadata, await File.ReadAllTextAsync(original.MetadataPath));
        Assert.Equal("Goku", Assert.Single(await store.ListAsync()).Name);
        Assert.Single(Directory.EnumerateDirectories(_directory));
    }

    [Fact]
    public async Task InstallsWebpAsRgbaPngWithoutChangingSourceInformation()
    {
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku", "Goku", "", "https://petdex.dev/pets/goku",
            "https://example.test/pet.json", "https://example.test/spritesheet.webp",
            new[] { "idle" }, SpriteSheetLayout.Standard);

        var installed = await store.InstallAsync(package, PetSpriteTestImages.Webp);

        Assert.EndsWith(".png", installed.SpriteSheetPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(package.SpriteSheetUrl, installed.SpriteSheetUrl);
        Assert.Equal(new SpriteSheetLayout(8, 9), installed.Layout);
        using var image = PetSpriteTestImages.DecodeRgbaPng(await File.ReadAllBytesAsync(installed.SpriteSheetPath));
        Assert.Equal(new SKColor(23, 45, 67, 128), image.GetPixel(0, 0));
        Assert.Equal(new SKColor(0, 0, 0, 0), image.GetPixel(0, 1));
    }

    [Fact]
    public async Task ListingMigratesLegacyWebpWithoutChangingPackageMetadata()
    {
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku", "Goku", "", "https://petdex.dev/pets/goku",
            "https://example.test/pet.json", "https://example.test/spritesheet.webp",
            new[] { "idle" }, SpriteSheetLayout.Standard);
        var installed = await store.InstallAsync(package, CreatePng(80, 90));
        var metadata = await File.ReadAllTextAsync(installed.MetadataPath);
        await File.WriteAllBytesAsync(Path.ChangeExtension(installed.SpriteSheetPath, ".webp"), PetSpriteTestImages.Webp);
        File.Delete(installed.SpriteSheetPath);

        var migrated = Assert.Single(await store.ListAsync());

        Assert.EndsWith(".png", migrated.SpriteSheetPath, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(metadata, await File.ReadAllTextAsync(migrated.MetadataPath));
        using var image = PetSpriteTestImages.DecodeRgbaPng(await File.ReadAllBytesAsync(migrated.SpriteSheetPath));
        Assert.Equal(new SKColor(20, 40, 60, 1), image.GetPixel(1, 0));
    }

    [Theory]
    [InlineData(8193, 9)]
    [InlineData(8, 8193)]
    [InlineData(4096, 4097)]
    public async Task ListingSkipsOversizedPngHeadersBeforeTheyReachTheUi(int width, int height)
    {
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku", "Goku", "", "https://petdex.dev/pets/goku",
            "https://example.test/pet.json", "https://example.test/spritesheet.png",
            new[] { "idle" }, SpriteSheetLayout.Standard);
        var installed = await store.InstallAsync(package, CreatePng(80, 90));
        var unsafeBytes = PetSpriteTestImages.CreatePngHeader(width, height);
        await File.WriteAllBytesAsync(installed.SpriteSheetPath, unsafeBytes);

        Assert.Empty(await store.ListAsync());
        Assert.Equal(unsafeBytes, await File.ReadAllBytesAsync(installed.SpriteSheetPath));
    }

    [Fact]
    public void FindsTheProjectPetsFolderInsteadOfUsingAppData()
    {
        var project = Path.Combine(_directory, "project");
        var output = Path.Combine(project, "artifacts", "publish", "win-x64");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(project, "CodexTaskMonitor.sln"), "");

        var petsRoot = PetLibraryPaths.ResolvePetsRoot(output);

        Assert.Equal(Path.Combine(project, "Pets"), petsRoot);
    }

    [Fact]
    public async Task DeletesTheWholeDownloadedPetFolder()
    {
        Directory.CreateDirectory(_directory);
        var store = new PetLibraryStore(_directory);
        var package = new DownloadedPetPackage(
            "goku", "Goku", "", "https://petdex.dev/pets/goku",
            "https://example.test/pet.json", "https://example.test/spritesheet.png",
            new[] { "idle" }, SpriteSheetLayout.Standard);
        await store.InstallAsync(package, CreatePng(80, 90));

        await store.DeleteAsync("goku");

        Assert.False(Directory.Exists(Path.Combine(_directory, "goku")));
        Assert.Empty(await store.ListAsync());
    }

    private static byte[] CreatePng(int width, int height)
    {
        using var image = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        image.Erase(SKColors.Transparent);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        return encoded.ToArray();
    }

    public void Dispose()
    {
        if (Directory.Exists(_directory))
        {
            Directory.Delete(_directory, recursive: true);
        }
    }
}

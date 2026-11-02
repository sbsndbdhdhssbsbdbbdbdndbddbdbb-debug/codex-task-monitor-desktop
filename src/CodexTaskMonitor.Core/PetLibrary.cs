using System.Buffers.Binary;
using System.Text.Json;
using SkiaSharp;

namespace CodexTaskMonitor.Core;

public sealed record SpriteSheetLayout(int Columns, int Rows)
{
    public static SpriteSheetLayout Standard { get; } = new(8, 9);

    public bool IsValid => Columns is > 0 and <= 32 && Rows is > 0 and <= 32;
}

public sealed record DownloadedPetPackage(
    string Id,
    string Name,
    string Description,
    string SourcePageUrl,
    string PackageUrl,
    string SpriteSheetUrl,
    IReadOnlyList<string> Actions,
    SpriteSheetLayout Layout,
    string? OriginalPackageJson = null);

public sealed record InstalledPetPackage(
    string Id,
    string Name,
    string Description,
    string SourcePageUrl,
    string PackageUrl,
    string SpriteSheetUrl,
    IReadOnlyList<string> Actions,
    SpriteSheetLayout Layout,
    string MetadataPath,
    string SpriteSheetPath);

public static class PetLibraryPaths
{
    public static string ResolvePetsRoot(string? startDirectory = null)
    {
        var directory = new DirectoryInfo(Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory));
        while (directory is not null)
        {
            if (directory.EnumerateFiles("*.sln").Any())
            {
                return Path.Combine(directory.FullName, "Pets");
            }

            directory = directory.Parent;
        }

        return Path.Combine(Path.GetFullPath(startDirectory ?? AppContext.BaseDirectory), "Pets");
    }
}

public sealed class PetLibraryStore
{
    private const string MetadataFileName = "petdex-package.json";
    private const string SpriteSheetFileName = "spritesheet";
    private readonly string _petsRoot;
    private readonly JsonSerializerOptions _jsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
    };

    public PetLibraryStore(string petsRoot)
    {
        _petsRoot = Path.GetFullPath(petsRoot);
    }

    public async Task<InstalledPetPackage> InstallAsync(
        DownloadedPetPackage package,
        ReadOnlyMemory<byte> spriteSheetBytes,
        CancellationToken cancellationToken = default)
    {
        var id = NormalizeId(package.Id);
        ValidatePackage(package, spriteSheetBytes.Span);
        var normalizedSpriteSheet = PetSpriteSheetNormalizer.Normalize(spriteSheetBytes.Span);
        Directory.CreateDirectory(_petsRoot);

        const string extension = ".png";
        var staging = Path.Combine(_petsRoot, $".staging-{Guid.NewGuid():N}");
        var destination = Path.Combine(_petsRoot, id);
        var backup = Path.Combine(_petsRoot, $".backup-{Guid.NewGuid():N}");
        var spriteSheetPath = Path.Combine(staging, SpriteSheetFileName + extension);

        try
        {
            Directory.CreateDirectory(staging);
            await File.WriteAllBytesAsync(spriteSheetPath, normalizedSpriteSheet, cancellationToken);
            var metadata = package with { Id = id, Actions = NormalizeActions(package.Actions) };
            await File.WriteAllTextAsync(
                Path.Combine(staging, "pet.json"),
                CreatePetJson(metadata, SpriteSheetFileName + extension),
                cancellationToken);
            await using (var stream = File.Create(Path.Combine(staging, MetadataFileName)))
            {
                await JsonSerializer.SerializeAsync(stream, metadata, _jsonOptions, cancellationToken);
            }

            if (Directory.Exists(destination))
            {
                Directory.Move(destination, backup);
            }

            Directory.Move(staging, destination);
            if (Directory.Exists(backup))
            {
                Directory.Delete(backup, recursive: true);
            }

            return ToInstalled(metadata, destination, extension);
        }
        catch
        {
            if (Directory.Exists(staging))
            {
                Directory.Delete(staging, recursive: true);
            }

            if (!Directory.Exists(destination) && Directory.Exists(backup))
            {
                Directory.Move(backup, destination);
            }

            throw;
        }
    }

    public async Task<IReadOnlyList<InstalledPetPackage>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_petsRoot))
        {
            return Array.Empty<InstalledPetPackage>();
        }

        var packages = new List<InstalledPetPackage>();
        foreach (var directory in Directory.EnumerateDirectories(_petsRoot))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var metadataPath = Path.Combine(directory, MetadataFileName);
            if (!File.Exists(metadataPath))
            {
                continue;
            }

            try
            {
                await using var stream = File.OpenRead(metadataPath);
                var package = await JsonSerializer.DeserializeAsync<DownloadedPetPackage>(stream, _jsonOptions, cancellationToken);
                if (package is null || !package.Layout.IsValid)
                {
                    continue;
                }

                var spriteSheetPath = FindSpriteSheet(directory);
                if (spriteSheetPath is null || !IsValidSpriteSheet(await File.ReadAllBytesAsync(spriteSheetPath, cancellationToken)))
                {
                    continue;
                }

                if (string.Equals(Path.GetExtension(spriteSheetPath), ".webp", StringComparison.OrdinalIgnoreCase))
                {
                    var normalizedPath = Path.Combine(directory, SpriteSheetFileName + ".png");
                    var normalizedBytes = PetSpriteSheetNormalizer.Normalize(await File.ReadAllBytesAsync(spriteSheetPath, cancellationToken));
                    await File.WriteAllBytesAsync(normalizedPath, normalizedBytes, cancellationToken);
                    spriteSheetPath = normalizedPath;
                }

                packages.Add(ToInstalled(package, directory, Path.GetExtension(spriteSheetPath)));
            }
            catch (IOException)
            {
                // A partially copied folder is ignored until its next successful install.
            }
            catch (JsonException)
            {
                // User-edited or corrupt metadata must not break the monitor.
            }
        }

        return packages.OrderBy(package => package.Name, StringComparer.CurrentCultureIgnoreCase).ToArray();
    }

    public Task DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var normalizedId = NormalizeId(id);
        if (string.IsNullOrWhiteSpace(normalizedId))
        {
            throw new InvalidDataException("The pet package id is invalid.");
        }

        var directory = Path.Combine(_petsRoot, normalizedId);
        if (Directory.Exists(directory))
        {
            Directory.Delete(directory, recursive: true);
        }

        return Task.CompletedTask;
    }

    private static InstalledPetPackage ToInstalled(DownloadedPetPackage package, string directory, string extension) => new(
        package.Id,
        package.Name,
        package.Description,
        package.SourcePageUrl,
        package.PackageUrl,
        package.SpriteSheetUrl,
        NormalizeActions(package.Actions),
        package.Layout,
        Path.Combine(directory, MetadataFileName),
        Path.Combine(directory, SpriteSheetFileName + extension));

    private static void ValidatePackage(DownloadedPetPackage package, ReadOnlySpan<byte> spriteSheetBytes)
    {
        if (string.IsNullOrWhiteSpace(NormalizeId(package.Id)) || string.IsNullOrWhiteSpace(package.Name))
        {
            throw new InvalidDataException("The pet package needs an id and a name.");
        }

        if (!package.Layout.IsValid || !IsValidSpriteSheet(spriteSheetBytes))
        {
            throw new InvalidDataException("The pet package does not contain a valid sprite sheet.");
        }
    }

    private static bool IsValidSpriteSheet(ReadOnlySpan<byte> bytes) =>
        PetSpriteSheetNormalizer.HasSafeHeader(bytes);

    private static string? FindSpriteSheet(string directory) =>
        Directory.EnumerateFiles(directory, SpriteSheetFileName + ".png").FirstOrDefault()
        ?? Directory.EnumerateFiles(directory, SpriteSheetFileName + ".webp").FirstOrDefault();

    private static string NormalizeId(string id)
    {
        var normalized = new string(id.Trim().ToLowerInvariant().Where(character =>
            char.IsAsciiLetterOrDigit(character) || character is '-' or '_').ToArray());
        return normalized.Trim('-', '_');
    }

    private static IReadOnlyList<string> NormalizeActions(IEnumerable<string>? actions) => (actions ?? [])
        .Where(action => !string.IsNullOrWhiteSpace(action))
        .Select(action => action.Trim().ToLowerInvariant())
        .Distinct(StringComparer.OrdinalIgnoreCase)
        .ToArray();

    private static string CreatePetJson(DownloadedPetPackage package, string spriteSheetFileName)
    {
        return JsonSerializer.Serialize(new
        {
            id = package.Id,
            displayName = package.Name,
            description = package.Description,
            spritesheetPath = spriteSheetFileName,
        }, new JsonSerializerOptions { WriteIndented = true });
    }

}

public static class PetSpriteSheetNormalizer
{
    private const int MaxEncodedBytes = 12 * 1024 * 1024;
    private const int MaxDimension = 8192;
    private const long MaxPixels = 16 * 1024 * 1024;

    public static byte[] CreatePreview(ReadOnlySpan<byte> source)
    {
        using var image = Decode(source);
        var width = image.Width / SpriteSheetLayout.Standard.Columns;
        var height = image.Height / SpriteSheetLayout.Standard.Rows;
        if (width < 1 || height < 1)
        {
            throw new InvalidDataException("The sprite sheet is too small to preview.");
        }

        using var frame = new SKBitmap();
        if (!image.ExtractSubset(frame, new SKRectI(0, 0, width, height)))
        {
            throw new InvalidDataException("The sprite sheet does not contain a readable first frame.");
        }

        var scale = 128.0 / Math.Max(width, height);
        var size = new SKSizeI(Math.Max(1, (int)Math.Round(width * scale)), Math.Max(1, (int)Math.Round(height * scale)));
        using var preview = frame.Resize(size, new SKSamplingOptions(SKCubicResampler.CatmullRom))
            ?? throw new InvalidDataException("The sprite sheet could not be resized for preview.");
        ClearTransparentPixels(preview);
        return EncodePng(preview);
    }

    public static byte[] Normalize(ReadOnlySpan<byte> source)
    {
        using var image = Decode(source);
        return EncodePng(image);
    }

    internal static bool HasSafeHeader(ReadOnlySpan<byte> source)
    {
        try
        {
            ValidateEncodedSource(source);
            using var data = SKData.CreateCopy(source);
            using var codec = SKCodec.Create(data)
                ?? throw new InvalidDataException("The pet package does not contain a readable sprite sheet.");
            ValidateDimensions(codec);
            return true;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    private static void ValidateEncodedSource(ReadOnlySpan<byte> source)
    {
        if (source.Length is <= 23 or > MaxEncodedBytes)
        {
            throw new InvalidDataException("The sprite sheet encoded size is outside the safety limit.");
        }

        ReadOnlySpan<byte> pngSignature = [137, 80, 78, 71, 13, 10, 26, 10];
        var isPng = source.StartsWith(pngSignature);
        var isWebp = source.StartsWith("RIFF"u8) && source.Slice(8, 4).SequenceEqual("WEBP"u8);
        if (!isPng && !isWebp)
        {
            throw new InvalidDataException("The sprite sheet must be PNG or WebP.");
        }

        // Skia can decode PNG pixels successfully even when the terminal IEND chunk is missing.
        ReadOnlySpan<byte> pngEnd = [0, 0, 0, 0, 73, 69, 78, 68, 174, 66, 96, 130];
        if ((isPng && !source.EndsWith(pngEnd)) ||
            (isWebp && BinaryPrimitives.ReadUInt32LittleEndian(source.Slice(4, 4)) != (uint)(source.Length - 8)))
        {
            throw new InvalidDataException("The sprite sheet container is incomplete or invalid.");
        }
    }

    private static void ValidateDimensions(SKCodec codec)
    {
        var header = codec.Info;
        if (codec.EncodedFormat is not (SKEncodedImageFormat.Png or SKEncodedImageFormat.Webp) ||
            header.Width <= 0 || header.Height <= 0)
        {
            throw new InvalidDataException("The pet package does not contain a valid sprite sheet.");
        }

        // Inspect metadata before allocating or decoding any full-resolution pixel buffer.
        if (header.Width > MaxDimension || header.Height > MaxDimension || (long)header.Width * header.Height > MaxPixels)
        {
            throw new InvalidDataException("The sprite sheet dimensions exceed the safety limit.");
        }
    }

    private static SKBitmap Decode(ReadOnlySpan<byte> source)
    {
        ValidateEncodedSource(source);
        using var data = SKData.CreateCopy(source);
        using var codec = SKCodec.Create(data)
            ?? throw new InvalidDataException("The pet package does not contain a readable sprite sheet.");
        ValidateDimensions(codec);
        var header = codec.Info;
        var info = new SKImageInfo(header.Width, header.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var image = new SKBitmap();
        try
        {
            if (!image.TryAllocPixels(info) ||
                codec.GetPixels(info, image.GetPixels(), image.RowBytes, new SKCodecOptions(0)) != SKCodecResult.Success)
            {
                throw new InvalidDataException("The pet package does not contain a readable sprite sheet.");
            }

            ClearTransparentPixels(image);
            return image;
        }
        catch
        {
            image.Dispose();
            throw;
        }
    }

    private static void ClearTransparentPixels(SKBitmap image)
    {
        var pixels = image.GetPixelSpan();
        for (var row = 0; row < image.Height; row++)
        {
            var offset = row * image.RowBytes;
            for (var column = 0; column < image.Width; column++, offset += 4)
            {
                if (pixels[offset + 3] == 0)
                {
                    pixels.Slice(offset, 4).Clear();
                }
            }
        }
    }

    private static byte[] EncodePng(SKBitmap image)
    {
        using var pixels = image.PeekPixels();
        using var encoded = pixels.Encode(SKPngEncoderOptions.Default)
            ?? throw new InvalidDataException("The sprite sheet could not be encoded as PNG.");
        return encoded.ToArray();
    }
}

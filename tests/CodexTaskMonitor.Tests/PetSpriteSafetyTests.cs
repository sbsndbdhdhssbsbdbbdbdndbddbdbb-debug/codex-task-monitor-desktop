using System.Buffers.Binary;
using CodexTaskMonitor.Core;
using SkiaSharp;

namespace CodexTaskMonitor.Tests;

public sealed class PetSpriteSafetyTests
{
    [Theory]
    [InlineData("gif")]
    [InlineData("bmp")]
    [InlineData("jpeg")]
    [InlineData("tiff")]
    public void OnlyPngAndWebpSpritesAreAccepted(string format)
    {
        using var image = new SKBitmap(8, 9, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        image.Erase(SKColors.Black);
        using var jpeg = image.Encode(SKEncodedImageFormat.Jpeg, 100);
        var bytes = format switch
        {
            "gif" => System.Convert.FromBase64String("R0lGODlhCAAJAIABAAAAAAAAACH5BAEAAAEALAAAAAAIAAkABwgQAAMIHEiwoMGDCBMqXGgwIAA7"),
            "bmp" => System.Convert.FromBase64String("Qk0OAQAAAAAAADYAAAAoAAAACAAAAAkAAAABABgAAAAAANgAAADEDgAAxA4AAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAA"),
            "tiff" => System.Convert.FromBase64String("SUkqAOAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAAOAAABBAABAAAACAAAAAEBBAABAAAACQAAAAIBAwADAAAAjgEAAAMBAwABAAAAAQAAAAYBAwABAAAAAgAAABEBBAABAAAACAAAABUBAwABAAAAAwAAABYBBAABAAAACQAAABcBBAABAAAA2AAAABoBBQABAAAAlAEAABsBBQABAAAAnAEAABwBAwABAAAAAQAAACgBAwABAAAAAgAAADEBAgALAAAApAEAAAAAAAAIAAgACABgAAAAAQAAAGAAAAABAAAASW1hZ2VTaGFycAAA"),
            _ => jpeg.ToArray(),
        };

        foreach (var preview in new[] { false, true })
        {
            Assert.Throws<InvalidDataException>(() => ProcessSprite(bytes, preview));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MalformedSpritesAlwaysReportInvalidData(bool preview)
    {
        foreach (var bytes in new[]
        {
            Array.Empty<byte>(),
            new byte[] { 1, 2, 3 },
            PetSpriteTestImages.CreatePngHeader(80, 90),
            "RIFF\u0010\0\0\0WEBPVP8L\u0004\0\0\0"u8.ToArray(),
        })
        {
            Assert.Throws<InvalidDataException>(() => ProcessSprite(bytes, preview));
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void EncodedSizeLimitAlsoAppliesToDirectNormalizerCalls(bool preview)
    {
        using var image = new SKBitmap(8, 9, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        image.Erase(SKColors.Transparent);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        var bytes = new byte[12 * 1024 * 1024 + 1];
        encoded.ToArray().CopyTo(bytes, 0);

        var error = Assert.Throws<InvalidDataException>(() => ProcessSprite(bytes, preview));
        Assert.Contains("encoded size", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(8193, 9, false)]
    [InlineData(8, 8193, false)]
    [InlineData(4096, 4097, false)]
    [InlineData(8193, 9, true)]
    [InlineData(8, 8193, true)]
    [InlineData(4096, 4097, true)]
    public void PngDimensionsAreRejectedBeforeReadingMissingPixelData(int width, int height, bool preview)
    {
        var error = Assert.Throws<InvalidDataException>(() => ProcessSprite(PetSpriteTestImages.CreatePngHeader(width, height), preview));

        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(8193, 9, false)]
    [InlineData(8, 8193, false)]
    [InlineData(4096, 4097, false)]
    [InlineData(8193, 9, true)]
    [InlineData(8, 8193, true)]
    [InlineData(4096, 4097, true)]
    public void WebpDimensionsAreRejectedBeforeReadingMismatchedPixelData(int width, int height, bool preview)
    {
        var bytes = PetSpriteTestImages.Webp;
        var chunk = bytes.AsSpan().IndexOf("VP8L"u8);
        Assert.True(chunk >= 12);
        BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(chunk + 9, 4),
            (uint)(width - 1) | ((uint)(height - 1) << 14));

        var error = Assert.Throws<InvalidDataException>(() => ProcessSprite(bytes, preview));

        Assert.Contains("dimensions", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NormalizationPreservesStraightAlphaColorsAndClearsHiddenMagenta(bool webp)
    {
        var normalized = PetSpriteSheetNormalizer.Normalize(webp ? PetSpriteTestImages.Webp : PetSpriteTestImages.Png);

        using var image = PetSpriteTestImages.DecodeRgbaPng(normalized);
        Assert.Equal(8, image.Width);
        Assert.Equal(9, image.Height);
        Assert.Equal(new SKColor(23, 45, 67, 128), image.GetPixel(0, 0));
        Assert.Equal(new SKColor(20, 40, 60, 1), image.GetPixel(1, 0));
        Assert.Equal(new SKColor(10, 120, 240, 255), image.GetPixel(2, 0));
        Assert.Equal(new SKColor(0, 0, 0, 0), image.GetPixel(0, 1));

        using var repeated = PetSpriteTestImages.DecodeRgbaPng(PetSpriteSheetNormalizer.Normalize(normalized));
        Assert.Equal(image.GetPixelSpan().ToArray(), repeated.GetPixelSpan().ToArray());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void TruncatedSpritesAreNotAcceptedAsPartiallyDecodedImages(bool webp, bool preview)
    {
        var source = webp ? PetSpriteTestImages.Webp[..^1] : PetSpriteTestImages.Png[..^12];

        Assert.Throws<InvalidDataException>(() => ProcessSprite(source, preview));
    }

    [Theory]
    [InlineData(-2)]
    [InlineData(2)]
    public void WebpRiffSizeMustMatchTheEncodedPayload(int delta)
    {
        var bytes = PetSpriteTestImages.Webp;
        BinaryPrimitives.WriteInt32LittleEndian(bytes.AsSpan(4, 4), bytes.Length - 8 + delta);

        Assert.Throws<InvalidDataException>(() => PetSpriteSheetNormalizer.Normalize(bytes));
        Assert.Throws<InvalidDataException>(() => PetSpriteSheetNormalizer.CreatePreview(bytes));
    }

    private static byte[] ProcessSprite(byte[] bytes, bool preview) => preview
        ? PetSpriteSheetNormalizer.CreatePreview(bytes)
        : PetSpriteSheetNormalizer.Normalize(bytes);
}

internal static class PetSpriteTestImages
{
    internal static byte[] Png => System.Convert.FromBase64String(
        "iVBORw0KGgoAAAANSUhEUgAAAAgAAAAJCAYAAAAPU20uAAAACXBIWXMAAA7EAAAOxAGVKw4bAAAANElEQVR4nGMR13Vu+Pv7Z+O3gC3/vnUIMDKgAZYXl/c0MDIAxbFIghWAJfEAFgYCYEgoAADgZAy5RJXCTQAAAABJRU5ErkJggg==");

    internal static byte[] Webp => System.Convert.FromBase64String(
        "UklGRkoAAABXRUJQVlA4TD4AAAAvBwACECegkG0EOHSvy7kcgQBF2OD/SAOQSdv0q6pKnsYJCIosdz0A8Fur8W9DIdsI0O4xOX/QM4jof64gXg==");

    internal static SKBitmap DecodeRgbaPng(byte[] bytes)
    {
        Assert.Equal(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 }, bytes[..8]);
        Assert.Equal(8, bytes[24]);
        Assert.Equal(6, bytes[25]);
        using var data = SKData.CreateCopy(bytes);
        using var codec = SKCodec.Create(data);
        var info = new SKImageInfo(codec.Info.Width, codec.Info.Height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var image = new SKBitmap(info);
        Assert.Equal(SKCodecResult.Success, codec.GetPixels(info, image.GetPixels()));
        return image;
    }

    internal static byte[] CreatePngHeader(int width, int height)
    {
        using var stream = new MemoryStream();
        stream.Write(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 });
        var header = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(0, 4), width);
        BinaryPrimitives.WriteInt32BigEndian(header.AsSpan(4, 4), height);
        header[8] = 8;
        header[9] = 6;
        WritePngChunk(stream, "IHDR"u8, header);
        WritePngChunk(stream, "IDAT"u8, []);
        WritePngChunk(stream, "IEND"u8, []);
        return stream.ToArray();
    }

    private static void WritePngChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> value = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(value, data.Length);
        stream.Write(value);
        stream.Write(type);
        stream.Write(data);
        uint crc = uint.MaxValue;
        foreach (var item in type.ToArray().Concat(data.ToArray()))
        {
            crc ^= item;
            for (var bit = 0; bit < 8; bit++)
            {
                crc = (crc >> 1) ^ ((crc & 1) == 0 ? 0 : 0xedb88320u);
            }
        }

        BinaryPrimitives.WriteUInt32BigEndian(value, ~crc);
        stream.Write(value);
    }
}

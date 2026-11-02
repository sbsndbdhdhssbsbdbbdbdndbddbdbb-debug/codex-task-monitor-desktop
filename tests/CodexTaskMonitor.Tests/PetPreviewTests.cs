using CodexTaskMonitor.Core;
using SkiaSharp;

namespace CodexTaskMonitor.Tests;

public sealed class PetPreviewTests
{
    [Fact]
    public void PreviewContainsOnlyFirstFrameAndBoundsDecodedSize()
    {
        using var source = new SKBitmap(1600, 1800, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        source.Erase(new SKColor(200, 0, 0));
        for (var y = 0; y < 80; y++)
            for (var x = 0; x < 80; x++) source.SetPixel(x, y, new SKColor(0, 200, 0));
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        using var preview = SKBitmap.Decode(PetSpriteSheetNormalizer.CreatePreview(encoded.ToArray()));
        Assert.Equal(128, preview.Width);
        Assert.Equal(128, preview.Height);
        Assert.True(preview.GetPixel(13, 13).Green > preview.GetPixel(13, 13).Red);
    }

    [Fact]
    public void TinySheetsFailClearlyInsteadOfCreatingAnEmptyPreview()
    {
        using var source = new SKBitmap(4, 4, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        source.Erase(SKColors.Transparent);
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        Assert.Throws<InvalidDataException>(() => PetSpriteSheetNormalizer.CreatePreview(encoded.ToArray()));
    }

    [Theory]
    [InlineData(1600, 900, 128, 64)]
    [InlineData(800, 1800, 64, 128)]
    [InlineData(80, 90, 128, 128)]
    public void PreviewKeepsAspectRatioAndDoesNotBlendAdjacentCells(int width, int height, int expectedWidth, int expectedHeight)
    {
        using var source = new SKBitmap(width, height, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        source.Erase(SKColors.Red);
        for (var y = 0; y < height / 9; y++)
            for (var x = 0; x < width / 8; x++) source.SetPixel(x, y, SKColors.Lime);
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);

        using var preview = PetSpriteTestImages.DecodeRgbaPng(PetSpriteSheetNormalizer.CreatePreview(encoded.ToArray()));

        Assert.Equal(expectedWidth, preview.Width);
        Assert.Equal(expectedHeight, preview.Height);
        Assert.All(preview.Pixels, pixel => Assert.Equal(SKColors.Lime, pixel));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void PreviewKeepsTheFirstCellColorAndAlpha(bool webp)
    {
        using var preview = PetSpriteTestImages.DecodeRgbaPng(
            PetSpriteSheetNormalizer.CreatePreview(webp ? PetSpriteTestImages.Webp : PetSpriteTestImages.Png));

        Assert.Equal(128, preview.Width);
        Assert.Equal(128, preview.Height);
        Assert.All(preview.Pixels, pixel => Assert.Equal(new SKColor(23, 45, 67, 128), pixel));
    }

    [Fact]
    public void PreviewZerosTransparentRgbBeforeAndAfterResampling()
    {
        using var source = new SKBitmap(1600, 1800, SKColorType.Rgba8888, SKAlphaType.Unpremul);
        var pixels = source.GetPixelSpan();
        for (var offset = 0; offset < pixels.Length; offset += 4)
        {
            pixels[offset] = 255;
            pixels[offset + 1] = 0;
            pixels[offset + 2] = 255;
            pixels[offset + 3] = 0;
        }

        for (var y = 0; y < 80; y++)
            for (var x = 0; x < 80; x++) source.SetPixel(x, y, SKColors.Lime);
        using var encoded = source.Encode(SKEncodedImageFormat.Png, 100);
        using var original = PetSpriteTestImages.DecodeRgbaPng(encoded.ToArray());
        Assert.Equal(new byte[] { 255, 0, 255, 0 }, original.GetPixelSpan().Slice(original.RowBytes * 100 + 400, 4).ToArray());

        using var preview = PetSpriteTestImages.DecodeRgbaPng(PetSpriteSheetNormalizer.CreatePreview(encoded.ToArray()));

        Assert.Equal(new SKColor(0, 0, 0, 0), preview.GetPixel(127, 127));
        Assert.All(preview.Pixels, pixel =>
        {
            Assert.Equal(0, pixel.Red);
            Assert.Equal(0, pixel.Blue);
            if (pixel.Alpha == 0) Assert.Equal(0, pixel.Green);
        });
    }
}

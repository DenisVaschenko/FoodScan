using FoodScan.Api.Services;
using SkiaSharp;
using Xunit;

namespace FoodScan.Tests;

public class ImageTests
{
    [Fact]
    public void FakeImageIsRejectedBeforeOcr()
    {
        using var stream = new MemoryStream("this is not a PNG"u8.ToArray());
        var error = Assert.Throws<ApiException>(() => TesseractOcr.PrepareImage(stream, "unused.png"));
        Assert.Equal("invalid_image", error.Code);
    }

    [Fact]
    public void ValidPngIsDecodedAndReencoded()
    {
        using var bitmap = new SKBitmap(100, 100);
        using (var canvas = new SKCanvas(bitmap)) canvas.Clear(SKColors.White);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        var path = Path.GetTempFileName();
        try
        {
            TesseractOcr.PrepareImage(stream, path);
            using var output = SKBitmap.Decode(path);
            Assert.Equal(100, output.Width);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void TinyImageIsRejected()
    {
        using var bitmap = new SKBitmap(2, 2);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        using var stream = new MemoryStream(data.ToArray());
        var error = Assert.Throws<ApiException>(() => TesseractOcr.PrepareImage(stream, "unused.png"));
        Assert.Equal("image_dimensions", error.Code);
    }
}

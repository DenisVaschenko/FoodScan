using System.Diagnostics;
using System.ComponentModel;
using SkiaSharp;

namespace FoodScan.Api.Services;

public sealed class TesseractOcr(IConfiguration config) : IOcrService
{
    public async Task<string> ReadAsync(Stream stream, CancellationToken ct)
    {
        // Only decoded JPEG/PNG; original names and metadata never reach the process arguments.
        var directory = Path.Combine(Path.GetTempPath(), "foodscan-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            var path = Path.Combine(directory, "input.png");
            PrepareImage(stream, path);
            ct.ThrowIfCancellationRequested();

            var start = new ProcessStartInfo(config["Ocr:Executable"] ?? "tesseract")
            { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
            foreach (var arg in new[] { path, "stdout", "-l", "ukr+eng", "--psm", "6" }) start.ArgumentList.Add(arg);
            start.Environment["OMP_THREAD_LIMIT"] = "1";
            using var process = new Process { StartInfo = start };
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            try
            {
                process.Start();
                var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
                var error = process.StandardError.ReadToEndAsync(timeout.Token);
                await process.WaitForExitAsync(timeout.Token);
                var result = await output;
                await error; // Drain stderr but never return local paths or label text to the client.
                if (process.ExitCode != 0)
                    throw new ApiException(503, "ocr_failed", "OCR не зміг обробити фото. Перевірте мовні дані ukr/eng та якість зображення.");
                return result.Trim();
            }
            catch (Win32Exception)
            { throw new ApiException(503, "ocr_not_installed", "Tesseract не встановлено або Ocr:Executable вказано неправильно."); }
            catch (OperationCanceledException)
            {
                if (!process.HasExited) process.Kill(entireProcessTree: true);
                if (ct.IsCancellationRequested) throw;
                throw new ApiException(504, "ocr_timeout", "OCR перевищив час обробки. Зменште або обріжте фото.");
            }
        }
        finally { Directory.Delete(directory, recursive: true); }
    }

    public static void PrepareImage(Stream stream, string outputPath)
    {
        using var codec = SKCodec.Create(stream);
        if (codec is null || codec.EncodedFormat is not (SKEncodedImageFormat.Jpeg or SKEncodedImageFormat.Png))
            throw new ApiException(400, "invalid_image", "Підтримуються лише коректні JPEG/PNG.");
        int w = codec.Info.Width, h = codec.Info.Height;
        if ((long)w * h > 20_000_000 || w < 30 || h < 30)
            throw new ApiException(400, "image_dimensions", "Зображення має містити від 30×30 до 20 мільйонів пікселів.");
        if (codec.FrameCount > 1)
            throw new ApiException(400, "image_frames", "Надішліть одне статичне зображення.");
        using var bitmap = new SKBitmap(new SKImageInfo(w, h, SKColorType.Rgba8888, SKAlphaType.Premul));
        if (codec.GetPixels(bitmap.Info, bitmap.GetPixels()) != SKCodecResult.Success)
            throw new ApiException(400, "invalid_image", "Зображення пошкоджено або обрізано.");
        bool swap = (int)codec.EncodedOrigin >= 5;
        using var oriented = new SKBitmap(swap ? h : w, swap ? w : h);
        using (var canvas = new SKCanvas(oriented))
        {
            canvas.Clear(SKColors.White);
            switch (codec.EncodedOrigin)
            {
                case SKEncodedOrigin.TopRight: canvas.Translate(w, 0); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.BottomRight: canvas.Translate(w, h); canvas.RotateDegrees(180); break;
                case SKEncodedOrigin.BottomLeft: canvas.Translate(0, h); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.LeftTop: canvas.RotateDegrees(90); canvas.Scale(1, -1); break;
                case SKEncodedOrigin.RightTop: canvas.Translate(h, 0); canvas.RotateDegrees(90); break;
                case SKEncodedOrigin.RightBottom: canvas.Translate(h, w); canvas.RotateDegrees(90); canvas.Scale(-1, 1); break;
                case SKEncodedOrigin.LeftBottom: canvas.Translate(0, w); canvas.RotateDegrees(270); break;
            }
            canvas.DrawBitmap(bitmap, 0, 0);
        }
        using var image = SKImage.FromBitmap(oriented);
        using var encoded = image.Encode(SKEncodedImageFormat.Png, 100);
        using var output = File.Create(outputPath);
        encoded.SaveTo(output);
    }
}

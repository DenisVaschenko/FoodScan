using System.Security.Claims;
using FoodScan.Api.Data;
using FoodScan.Api.Services;
using Microsoft.EntityFrameworkCore;

namespace FoodScan.Api.Endpoints;

public record TextAnalysisRequest(string Text, string? ProductName);

public static class AnalysisEndpoints
{
    public const long MaxImageBytes = 8 * 1024 * 1024;

    public static void MapAnalyses(this WebApplication app)
    {
        var group = app.MapGroup("/api/analyses").RequireAuthorization().WithTags("Analyses");
        group.MapPost("", async (HttpRequest request, ClaimsPrincipal user, IOcrService ocr,
            AnalysisService service, CancellationToken ct) =>
        {
            if (!request.HasFormContentType)
                throw new ApiException(415, "multipart_required", "Надішліть multipart/form-data із полем image.");
            var form = await request.ReadFormAsync(ct);
            var file = form.Files.GetFile("image");
            if (form.Files.Count != 1 || file is null || file.Length == 0)
                throw new ApiException(400, "one_image_required", "Потрібна рівно одна фотографія в полі image.");
            if (file.Length > MaxImageBytes)
                throw new ApiException(413, "image_too_large", "Максимальний розмір фото — 8 MiB.");
            var name = form["productName"].ToString();
            if (name.Length > 120) throw new ApiException(400, "name_too_long", "Назва продукту надто довга.");
            using var memory = new MemoryStream();
            await file.CopyToAsync(memory, ct);
            memory.Position = 0;
            var text = await ocr.ReadAsync(memory, ct);
            var result = await service.CreateAsync(user.UserId(), text, name, "photo", null, ct);
            return Results.Created($"/api/analyses/{result.Id}", AnalysisService.Detail(result));
        }).RequireRateLimiting("analysis").DisableAntiforgery();

        // Useful both for manually supplied labels and for testing the LLM without OCR.
        group.MapPost("/text", async (TextAnalysisRequest r, ClaimsPrincipal user, AnalysisService service, CancellationToken ct) =>
        {
            ValidateText(r.Text);
            var result = await service.CreateAsync(user.UserId(), r.Text, r.ProductName, "text", null, ct);
            return Results.Created($"/api/analyses/{result.Id}", AnalysisService.Detail(result));
        }).RequireRateLimiting("analysis");

        group.MapPost("/{id:guid}/reanalyze", async (Guid id, TextAnalysisRequest r, ClaimsPrincipal user,
            AppDbContext db, AnalysisService service, CancellationToken ct) =>
        {
            ValidateText(r.Text);
            var original = await db.Analyses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId(), ct);
            if (original is null) return Results.NotFound();
            var result = await service.CreateAsync(user.UserId(), r.Text, r.ProductName ?? original.ProductName, "correctedText", id, ct);
            return Results.Created($"/api/analyses/{result.Id}", AnalysisService.Detail(result));
        }).RequireRateLimiting("analysis");

        group.MapGet("", async (int? page, int? pageSize, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            int p = page ?? 1, size = pageSize ?? 20;
            if (p < 1 || p > 100_000 || size < 1 || size > 100)
                throw new ApiException(400, "pagination", "page: 1–100000; pageSize: 1–100.");
            var query = db.Analyses.AsNoTracking().Where(x => x.UserId == user.UserId());
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.CreatedAt).ThenByDescending(x => x.Id)
                .Skip((p - 1) * size).Take(size)
                .Select(x => new { x.Id, x.CreatedAt, x.ProductName, x.Status, x.Source, x.ParentAnalysisId }).ToListAsync(ct);
            return Results.Ok(new { page = p, pageSize = size, total, items });
        });
        group.MapGet("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var result = await db.Analyses.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id && x.UserId == user.UserId(), ct);
            return result is null ? Results.NotFound() : Results.Ok(AnalysisService.Detail(result));
        });
        group.MapDelete("/{id:guid}", async (Guid id, ClaimsPrincipal user, AppDbContext db, CancellationToken ct) =>
        {
            var count = await db.Analyses.Where(x => x.Id == id && x.UserId == user.UserId()).ExecuteDeleteAsync(ct);
            return count == 0 ? Results.NotFound() : Results.NoContent();
        });
    }

    private static void ValidateText(string? text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.Length > 20_000)
            throw new ApiException(400, "invalid_text", "Передайте текст складу довжиною 1–20000 символів.");
    }
}

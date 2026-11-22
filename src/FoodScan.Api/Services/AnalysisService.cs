using System.Text.Json;
using FoodScan.Api.Data;
using FoodScan.Api.Domain;
using Microsoft.EntityFrameworkCore;

namespace FoodScan.Api.Services;

public sealed class AnalysisService(AppDbContext db, IIngredientExtractor extractor)
{
    public async Task<Analysis> CreateAsync(Guid userId, string text, string? productName,
        string source, Guid? parentId, CancellationToken ct)
    {
        if (text.Length > 20_000)
            throw new ApiException(400, "text_too_long", "Склад не повинен перевищувати 20 000 символів.");
        if (productName?.Length > 120)
            throw new ApiException(400, "name_too_long", "Назва продукту не повинна перевищувати 120 символів.");
        var createdAt = DateTimeOffset.UtcNow;
        var tracked = await db.TrackedComponents.AsNoTracking().Where(x => x.UserId == userId).ToListAsync(ct);
        var catalog = await db.Components.AsNoTracking().ToListAsync(ct);
        bool empty = string.IsNullOrWhiteSpace(text);
        var extraction = empty
            ? new Extraction([], true, ["OCR не розпізнав текст. Завантажте чіткіше фото або введіть склад вручну."])
            : await extractor.ExtractAsync(text, ct);
        var result = ComponentMatcher.Match(text, extraction, catalog, tracked);
        var analysis = new Analysis
        {
            UserId = userId, ParentAnalysisId = parentId, CreatedAt = createdAt,
            RawText = text, ProductName = productName?.Trim() ?? "", Source = source,
            Model = empty ? "not-called" : extractor.ModelName,
            Status = result.NeedsReview ? "needsReview" : "completed",
            ResultJson = JsonSerializer.Serialize(result, JsonDefaults.Options)
        };
        db.Analyses.Add(analysis);
        await db.SaveChangesAsync(ct);
        return analysis;
    }

    public static object Detail(Analysis a) => new
    {
        a.Id, a.ParentAnalysisId, a.CreatedAt, a.ProductName, a.Source, a.RawText, a.Status, a.Model,
        result = JsonSerializer.Deserialize<AnalysisResult>(a.ResultJson, JsonDefaults.Options)
    };
}

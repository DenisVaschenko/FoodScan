using System.ComponentModel.DataAnnotations;

namespace FoodScan.Api.Domain;

public class User
{
    public Guid Id { get; set; } = Guid.NewGuid();
    [MaxLength(254)] public string Email { get; set; } = "";
    [MaxLength(254)] public string NormalizedEmail { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    [MaxLength(80)] public string DisplayName { get; set; } = "";
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
}

public class RefreshSession
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    [MaxLength(64)] public string TokenHash { get; set; } = "";
    public DateTimeOffset ExpiresAt { get; set; }
    // Incremented to make token rotation atomic via optimistic concurrency.
    public int Version { get; set; }
}

public class Component
{
    public int Id { get; set; }
    [MaxLength(100)] public string Name { get; set; } = "";
    public string[] Aliases { get; set; } = [];
    // Curated associations, not a claim that every derivative has identical allergenicity.
    public int[] RelatedGroupIds { get; set; } = [];
}

public enum TrackingReason { Allergy, Avoid, Track }
public enum Presence { Ingredient, MayContain }

public class TrackedComponent
{
    public Guid UserId { get; set; }
    public int ComponentId { get; set; }
    public TrackingReason Reason { get; set; }
    [MaxLength(300)] public string Note { get; set; } = "";
}

public class Analysis
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public Guid? ParentAnalysisId { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    [MaxLength(120)] public string ProductName { get; set; } = "";
    public string Source { get; set; } = "photo";
    public string RawText { get; set; } = "";
    public string Status { get; set; } = "needsReview";
    public string Model { get; set; } = "";
    public string ResultJson { get; set; } = "{}";
}

public record ExtractedItem(string Name, string Evidence, Presence Presence);
public record Extraction(List<ExtractedItem> Items, bool NeedsReview, List<string> Warnings);
public record TrackingSnapshot(int ComponentId, string Name, TrackingReason Reason, string Note);
public record IngredientResult(string Name, string Evidence, Presence Presence, int? ComponentId);
public record ComponentMatch(int ComponentId, string ComponentName, TrackingReason Reason,
    string IngredientName, string Evidence, Presence Presence, string MatchKind);
public record AnalysisResult(List<IngredientResult> Ingredients, List<ComponentMatch> Matches,
    List<TrackingSnapshot> TrackingSnapshot, List<string> Warnings, bool NeedsReview,
    string Notice = "Збігів може бути не знайдено через помилки OCR або неповний довідник. Результат не гарантує безпечність продукту.");

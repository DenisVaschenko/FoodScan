using System.Text;
using System.Text.RegularExpressions;
using FoodScan.Api.Domain;

namespace FoodScan.Api.Services;

public static partial class ComponentMatcher
{
    public static string Normalize(string text) => Spaces().Replace(text.Normalize(NormalizationForm.FormKC)
        .ToLowerInvariant().Replace('’', '\'').Replace('ʼ', '\'').Trim(), " ");

    public static AnalysisResult Match(string rawText, Extraction extraction,
        IReadOnlyList<Component> catalog, IReadOnlyList<TrackedComponent> tracked)
    {
        var lookup = catalog.SelectMany(c => c.Aliases.Append(c.Name).Select(a => (Key: Normalize(a), Value: c)))
            .GroupBy(p => p.Key).ToDictionary(g => g.Key, g => g.First().Value);
        var byId = catalog.ToDictionary(c => c.Id);
        var snapshots = tracked.Select(t => new TrackingSnapshot(t.ComponentId,
            byId[t.ComponentId].Name, t.Reason, t.Note)).ToList();
        var ingredients = new List<IngredientResult>();
        var matches = new List<ComponentMatch>();
        var warnings = new List<string>(extraction.Warnings);
        bool review = extraction.NeedsReview;
        foreach (var item in extraction.Items.Distinct())
        {
            if (string.IsNullOrWhiteSpace(item.Name) || string.IsNullOrWhiteSpace(item.Evidence)
                || !Normalize(rawText).Contains(Normalize(item.Evidence), StringComparison.Ordinal)
                || !ContainsPhrase(Normalize(item.Evidence), Normalize(item.Name)))
            {
                review = true;
                warnings.Add("Відповідь моделі містить компонент без підтвердження в тексті; його відкинуто.");
                continue;
            }
            lookup.TryGetValue(Normalize(item.Name), out var component);
            ingredients.Add(new(item.Name, item.Evidence, item.Presence, component?.Id));
            if (component is null)
            {
                review = true;
                warnings.Add($"Компонент «{item.Name}» відсутній у довіднику синонімів; перевірте вручну.");
                continue;
            }
            foreach (var t in tracked.Where(t => t.ComponentId == component.Id || component.RelatedGroupIds.Contains(t.ComponentId)))
                matches.Add(new(t.ComponentId, byId[t.ComponentId].Name, t.Reason, item.Name,
                    item.Evidence, item.Presence, t.ComponentId == component.Id ? "direct" : "relatedGroup"));
        }
        if (ingredients.Count == 0)
        {
            review = true;
            warnings.Add("Склад не вдалося виділити. Перевірте текст або завантажте чіткіше фото.");
        }
        return new(ingredients, matches.Distinct().ToList(), snapshots, warnings.Distinct().ToList(), review);
    }

    private static bool ContainsPhrase(string evidence, string name) => Regex.IsMatch(evidence,
        @"(?<![\p{L}\p{N}])" + Regex.Escape(name) + @"(?![\p{L}\p{N}])", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    [GeneratedRegex(@"\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}

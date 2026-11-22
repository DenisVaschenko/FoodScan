using FoodScan.Api.Data;
using FoodScan.Api.Domain;
using FoodScan.Api.Services;
using Xunit;

namespace FoodScan.Tests;

public class MatcherTests
{
    [Fact]
    public void MilkDerivativeAndTracesRemainDistinct()
    {
        const string text = "Склад: казеїн. Може містити арахіс.";
        var result = ComponentMatcher.Match(text, new([
            new("казеїн", "казеїн", Presence.Ingredient),
            new("арахіс", "Може містити арахіс", Presence.MayContain)
        ], false, []), Catalog.All, [Track(1), Track(6)]);
        Assert.Equal(2, result.Matches.Count);
        Assert.Contains(result.Matches, m => m.ComponentId == 1 && m.MatchKind == "relatedGroup" && m.Presence == Presence.Ingredient);
        Assert.Contains(result.Matches, m => m.ComponentId == 6 && m.Presence == Presence.MayContain);
    }

    [Theory]
    [InlineData("молочна кислота")]
    [InlineData("какао-масло")]
    [InlineData("лецитин")]
    public void SimilarWordsDoNotInventMilkOrSoy(string name)
    {
        var result = ComponentMatcher.Match(name, new([new(name, name, Presence.Ingredient)], false, []), Catalog.All, [Track(1), Track(5)]);
        Assert.Empty(result.Matches);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void ModelCannotInventEvidenceOrIngredientName()
    {
        var result = ComponentMatcher.Match("Склад: цукор.", new([
            new("молоко", "молоко", Presence.Ingredient),
            new("яйця", "цукор", Presence.Ingredient)
        ], false, []), Catalog.All, [Track(1), Track(2)]);
        Assert.Empty(result.Matches);
        Assert.Empty(result.Ingredients);
        Assert.True(result.NeedsReview);
    }

    [Fact]
    public void ApostrophesWhitespaceAndCaseAreNormalized()
    {
        var result = ComponentMatcher.Match("КЕШ’Ю", new([new("КЕШ’Ю", "КЕШ’Ю", Presence.Ingredient)], false, []), Catalog.All, [Track(7)]);
        Assert.Single(result.Matches);
        Assert.Equal("relatedGroup", result.Matches[0].MatchKind);
    }

    [Fact]
    public void EmptyExtractionIsNeverCompleted()
    {
        var result = ComponentMatcher.Match("", new([], false, []), Catalog.All, []);
        Assert.True(result.NeedsReview);
        Assert.NotEmpty(result.Warnings);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"items\":null,\"needsReview\":false,\"warnings\":[]}")]
    [InlineData("{\"items\":[{\"name\":\"milk\",\"evidence\":\"milk\"}],\"needsReview\":false,\"warnings\":[]}")]
    [InlineData("{\"items\":[{\"name\":\"milk\",\"evidence\":\"milk\",\"presence\":\"safe\"}],\"needsReview\":false,\"warnings\":[]}")]
    [InlineData("not json")]
    public void MalformedLlmOutputIsRejected(string json) => Assert.Throws<ApiException>(() => GeminiExtractor.Parse(json));

    [Fact]
    public void ValidLlmJsonIsParsed() => Assert.Single(GeminiExtractor.Parse(
        """{"items":[{"name":"цукор","evidence":"цукор","presence":"ingredient"}],"needsReview":false,"warnings":[]}""").Items);

    private static TrackedComponent Track(int id) => new() { ComponentId = id, Reason = TrackingReason.Allergy };
}

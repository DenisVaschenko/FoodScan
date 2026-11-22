using System.Text.Json;
using System.Text.Json.Serialization;
using FoodScan.Api.Domain;

namespace FoodScan.Api.Services;

public interface IOcrService
{
    Task<string> ReadAsync(Stream image, CancellationToken ct);
}
public interface IIngredientExtractor
{
    string ModelName { get; }
    Task<Extraction> ExtractAsync(string text, CancellationToken ct);
}

public interface IComponentSearchService
{
    Task<IReadOnlyList<Component>> SearchAsync(
        string query,
        int limit = 10,
        CancellationToken cancellationToken = default);
}
public sealed class ApiException(int status, string code, string message) : Exception(message)
{
    public int Status { get; } = status;
    public string Code { get; } = code;
}
public static class JsonDefaults
{
    public static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) }
    };
}

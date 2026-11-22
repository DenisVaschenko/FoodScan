using FoodScan.Api.Data;
using FoodScan.Api.Domain;
using FoodScan.Api.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace FoodScan.Tests;

public class ApiFactory : WebApplicationFactory<Program>
{
    private readonly SqliteConnection connection = new("Data Source=:memory:");
    public FakeExtractor Extractor { get; } = new();

    public ApiFactory() => connection.Open();
    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (root is not null && !File.Exists(Path.Combine(root.FullName, "FoodScan.sln"))) root = root.Parent;
        builder.UseContentRoot(Path.Combine(root!.FullName, "src", "FoodScan.Api"));
        builder.UseEnvironment("Development");
        builder.ConfigureAppConfiguration((_, config) => config.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Jwt:Key"] = "test-only-signing-secret-at-least-32-bytes-long",
            ["Database:Initialize"] = "true",
            ["AllowedHosts"] = "*"
        }));
        builder.ConfigureTestServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(connection));
            services.RemoveAll<IOcrService>();
            services.RemoveAll<IIngredientExtractor>();
            services.AddSingleton<IOcrService, FakeOcr>();
            services.AddSingleton<IIngredientExtractor>(Extractor);
        });
    }
    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) connection.Dispose();
    }
}

public sealed class FakeOcr : IOcrService
{
    public Task<string> ReadAsync(Stream image, CancellationToken ct) => Task.FromResult("Склад: молоко, цукор. Може містити арахіс.");
}

public sealed class FakeExtractor : IIngredientExtractor
{
    public string ModelName => "test-fixture-not-a-real-model";
    public bool Fail { get; set; }
    public Task<Extraction> ExtractAsync(string text, CancellationToken ct)
    {
        if (Fail) throw new ApiException(503, "llm_quota", "Test quota error");
        var items = new List<ExtractedItem>();
        if (text.Contains("молоко")) items.Add(new("молоко", "молоко", Presence.Ingredient));
        if (text.Contains("цукор")) items.Add(new("цукор", "цукор", Presence.Ingredient));
        if (text.Contains("Може містити арахіс")) items.Add(new("арахіс", "Може містити арахіс", Presence.MayContain));
        return Task.FromResult(new Extraction(items, false, []));
    }
}

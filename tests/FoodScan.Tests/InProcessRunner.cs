using System.Reflection;
using FoodScan.Api.Data;
using FoodScan.Api.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FoodScan.Tests;

// Optional runner for restricted environments where VSTest's communication sockets are unavailable.
// The same test methods remain runnable with the normal `dotnet test` command.
public static class InProcessRunner
{
    public static async Task<int> Main(string[] args)
    {
        if (args is ["--openapi", var specPath])
        {
            using var factory = new ApiFactory();
            using var client = factory.CreateClient();
            var response = await client.GetAsync("/openapi/v1.json");
            response.EnsureSuccessStatusCode();
            await File.WriteAllTextAsync(specPath, await response.Content.ReadAsStringAsync());
            return 0;
        }
        if (args is ["--ocr", var imagePath])
        {
            using var image = File.OpenRead(imagePath);
            var config = new ConfigurationBuilder().AddEnvironmentVariables().Build();
            Console.WriteLine(await new TesseractOcr(config).ReadAsync(image, CancellationToken.None));
            return 0;
        }
        if (args is ["--schema", var path])
        {
            using var db = new AppDbContext(new DbContextOptionsBuilder<AppDbContext>()
                .UseNpgsql("Host=localhost;Database=foodscan;Username=foodscan;Password=unused").Options);
            await File.WriteAllTextAsync(path, db.Database.GenerateCreateScript());
            return 0;
        }
        if (args is not ["--selftest"])
        {
            Console.WriteLine("Use dotnet test, or run with --selftest. --schema PATH exports PostgreSQL DDL.");
            return 0;
        }
        int passed = 0, failed = 0;
        foreach (var type in typeof(InProcessRunner).Assembly.GetTypes().Where(t => t.IsClass && !t.IsAbstract))
        foreach (var method in type.GetMethods().Where(m => m.GetCustomAttribute<FactAttribute>() is not null))
        {
            var rows = method.GetCustomAttributes<InlineDataAttribute>().SelectMany(a => a.GetData(method)).ToArray();
            if (rows.Length == 0) rows = [Array.Empty<object>()];
            foreach (var row in rows)
            {
                var instance = Activator.CreateInstance(type);
                try
                {
                    var result = method.Invoke(instance, row);
                    if (result is Task task) await task;
                    Console.WriteLine($"PASS {type.Name}.{method.Name}");
                    passed++;
                }
                catch (Exception e)
                {
                    Console.WriteLine($"FAIL {type.Name}.{method.Name}: {e.InnerException ?? e}");
                    failed++;
                }
                finally { (instance as IDisposable)?.Dispose(); }
            }
        }
        Console.WriteLine($"Passed: {passed}; failed: {failed}");
        return failed == 0 ? 0 : 1;
    }
}

using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using FoodScan.Api.Auth;
using FoodScan.Api.Data;
using FoodScan.Api.Domain;
using FoodScan.Api.Endpoints;
using FoodScan.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.ConfigureKestrel(o => o.Limits.MaxRequestBodySize = 9 * 1024 * 1024);
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = 9 * 1024 * 1024);
builder.Services.ConfigureHttpJsonOptions(o => o.SerializerOptions.Converters.Add(
    new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi(options => options.AddDocumentTransformer((document, _, _) =>
{
    document.Info.Title = "FoodScan API";
    document.Info.Version = "v1";
    document.Components ??= new OpenApiComponents();
    document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
    document.Components.SecuritySchemes["Bearer"] = new OpenApiSecurityScheme
    { Type = SecuritySchemeType.Http, Scheme = "bearer", BearerFormat = "JWT" };
    foreach (var (path, item) in document.Paths)
    {
        if (!path.StartsWith("/api/") || path.StartsWith("/api/auth/") || item.Operations is null) continue;
        foreach (var operation in item.Operations.Values)
            operation.Security = [new OpenApiSecurityRequirement
                { [new OpenApiSecuritySchemeReference("Bearer", document)] = [] }];
    }
    document.Paths["/api/analyses"].Operations![HttpMethod.Post].RequestBody = new OpenApiRequestBody
    {
        Required = true,
        Content = new Dictionary<string, IOpenApiMediaType>
        {
            ["multipart/form-data"] = new OpenApiMediaType()
            {
                Schema = new OpenApiSchema
                {
                    Type = JsonSchemaType.Object, Required = new HashSet<string> { "image" },
                    Properties = new Dictionary<string, IOpenApiSchema>
                    {
                        ["image"] = new OpenApiSchema { Type = JsonSchemaType.String, Format = "binary" },
                        ["productName"] = new OpenApiSchema { Type = JsonSchemaType.String, MaxLength = 120 }
                    }
                }
            }
        }
    };
    return Task.CompletedTask;
}));
builder.Services.AddDbContext<AppDbContext>(o => o.UseNpgsql(builder.Configuration.GetConnectionString("Default")));
builder.Services.AddScoped<IPasswordHasher<User>, PasswordHasher<User>>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<AnalysisService>();
builder.Services.AddScoped<IOcrService, TesseractOcr>();
builder.Services.AddHttpClient<IIngredientExtractor, GeminiExtractor>(h => h.Timeout = TimeSpan.FromSeconds(60));
builder.Services.AddSingleton<TrieSearcher>();
builder.Services.AddSingleton<IComponentSearchService>(services =>
    services.GetRequiredService<TrieSearcher>());
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme).Configure<IConfiguration>((o, config) =>
{
    var jwtKey = config["Jwt:Key"];
    if (string.IsNullOrWhiteSpace(jwtKey) || Encoding.UTF8.GetByteCount(jwtKey) < 32)
        throw new InvalidOperationException("Set Jwt:Key to a random secret of at least 32 bytes. See README.md.");
    o.TokenValidationParameters = new()
    {
        ValidateIssuer = true, ValidIssuer = "FoodScan", ValidateAudience = true, ValidAudience = "FoodScan.Client",
        ValidateIssuerSigningKey = true, IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ValidateLifetime = true, ClockSkew = TimeSpan.FromSeconds(15),
        ValidAlgorithms = [SecurityAlgorithms.HmacSha256]
    };
}).ValidateOnStart();
builder.Services.AddAuthorization();
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(context.Connection.RemoteIpAddress?.ToString() ?? "local",
            _ => new() { PermitLimit = 120, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    o.AddPolicy("auth", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "local",
        _ => new() { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
    // Bound OCR/model concurrency for the small local demo; no background queue required.
    o.AddConcurrencyLimiter("analysis", options => { options.PermitLimit = 2; options.QueueLimit = 0; });
});

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    var db = scope.ServiceProvider
        .GetRequiredService<AppDbContext>();

    if (app.Configuration.GetValue<bool>("Database:Initialize"))
        await db.Database.EnsureCreatedAsync();

    var components = await db.Components
        .AsNoTracking()
        .ToListAsync();

    var searcher = app.Services.GetRequiredService<TrieSearcher>();
    searcher.Initialize(components);
}

app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (ApiException e)
    {
        await Results.Problem(statusCode: e.Status, title: e.Code, detail: e.Message,
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
    catch (BadHttpRequestException e)
    { await Results.Problem(statusCode: e.StatusCode, title: "invalid_request", detail: "Перевірте формат і розмір запиту.").ExecuteAsync(context); }
    catch (InvalidDataException)
    { await Results.Problem(statusCode: 400, title: "invalid_form", detail: "Некоректна форма або завеликий файл.").ExecuteAsync(context); }
    catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested) { }
    catch (Exception e)
    {
        // No request bodies, credentials, or model responses are logged.
        app.Logger.LogError("Request {TraceId} failed ({Type})", context.TraceIdentifier, e.GetType().Name);
        await Results.Problem(statusCode: 500, title: "server_error", detail: "Внутрішня помилка сервера.",
            extensions: new Dictionary<string, object?> { ["traceId"] = context.TraceIdentifier }).ExecuteAsync(context);
    }
});
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();

// Demo bootstrap only. Do not combine EnsureCreated with EF migrations on the same database.
if (app.Configuration.GetValue<bool>("Database:Initialize"))
{
    await using var scope = app.Services.CreateAsyncScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.EnsureCreatedAsync();
}
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.MapGet("/health/ready", async (AppDbContext db, CancellationToken ct) =>
    await db.Database.CanConnectAsync(ct) ? Results.Ok(new { status = "ready" }) : Results.StatusCode(503));
if (app.Environment.IsDevelopment()) app.MapOpenApi();
app.MapAuth();
app.MapComponents();
app.MapAnalyses();
app.Run();

public partial class Program;

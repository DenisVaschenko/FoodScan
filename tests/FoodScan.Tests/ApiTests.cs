using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Xunit;

namespace FoodScan.Tests;

public class ApiTests
{
    [Fact]
    public async Task AuthRefreshRotationLogoutAndProfile()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/me")).StatusCode);
        var tokens = await RegisterAndLogin(client, "one@example.com");
        Assert.Equal("one@example.com", (await Read(await client.GetAsync("/api/me"))).GetProperty("email").GetString());
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/login", new { email = "one@example.com", password = "incorrect-password" })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync("/api/auth/register", new { email = "ONE@example.com", password = "Password-12345", displayName = "test" })).StatusCode);
        var refresh = tokens.GetProperty("refreshToken").GetString();
        var rotated = await Read(await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh }));
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = refresh })).StatusCode);
        var next = rotated.GetProperty("refreshToken").GetString();
        Assert.Equal(HttpStatusCode.NoContent, (await client.PostAsJsonAsync("/api/auth/logout", new { refreshToken = next })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.PostAsJsonAsync("/api/auth/refresh", new { refreshToken = next })).StatusCode);
        var profile = await Read(await client.PutAsJsonAsync("/api/me", new { displayName = "Нове ім'я" }));
        Assert.Equal("Нове ім'я", profile.GetProperty("displayName").GetString());
    }

    [Fact]
    public async Task AnalysisHistorySnapshotsCorrectionsAndOwnership()
    {
        using var factory = new ApiFactory();
        using var first = factory.CreateClient();
        using var second = factory.CreateClient();
        await RegisterAndLogin(first, "first@example.com");
        await RegisterAndLogin(second, "second@example.com");
        (await first.PutAsJsonAsync("/api/me/components/1", new { reason = "allergy", note = "" })).EnsureSuccessStatusCode();
        (await first.PutAsJsonAsync("/api/me/components/6", new { reason = "avoid" })).EnsureSuccessStatusCode();
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1, 2, 3]), "image", "fixture.png"); // OCR replaced by controlled fixture.
        var analysis = await Read(await first.PostAsync("/api/analyses", form));
        var id = analysis.GetProperty("id").GetString();
        var result = analysis.GetProperty("result");
        Assert.Equal(2, result.GetProperty("matches").GetArrayLength());
        Assert.Contains(result.GetProperty("matches").EnumerateArray(), m => m.GetProperty("presence").GetString() == "mayContain");
        Assert.Equal(HttpStatusCode.NotFound, (await second.GetAsync($"/api/analyses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.DeleteAsync($"/api/analyses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await second.PostAsJsonAsync($"/api/analyses/{id}/reanalyze", new { text = "цукор" })).StatusCode);
        Assert.Equal(0, (await Read(await second.GetAsync("/api/analyses"))).GetProperty("total").GetInt32());
        Assert.Equal(0, (await Read(await second.GetAsync("/api/me/components"))).GetArrayLength());

        (await first.DeleteAsync("/api/me/components/1")).EnsureSuccessStatusCode();
        var unchanged = await Read(await first.GetAsync($"/api/analyses/{id}"));
        Assert.Equal(2, unchanged.GetProperty("result").GetProperty("trackingSnapshot").GetArrayLength());
        var correction = await Read(await first.PostAsJsonAsync($"/api/analyses/{id}/reanalyze", new { text = "Склад: цукор." }));
        Assert.Equal(id, correction.GetProperty("parentAnalysisId").GetString());
        Assert.Equal(0, correction.GetProperty("result").GetProperty("matches").GetArrayLength());
        Assert.Equal(1, correction.GetProperty("result").GetProperty("trackingSnapshot").GetArrayLength());
        Assert.Equal(2, (await Read(await first.GetAsync("/api/analyses?page=1&pageSize=1"))).GetProperty("total").GetInt32());
        Assert.Equal(HttpStatusCode.NoContent, (await first.DeleteAsync($"/api/analyses/{id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await first.GetAsync($"/api/analyses/{id}")).StatusCode);
    }

    [Fact]
    public async Task InvalidRequestsAndProviderFailureAreNotSuccessfulAnalyses()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        await RegisterAndLogin(client, "validation@example.com");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync("/api/analyses/text", new { text = "" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.GetAsync("/api/analyses?page=0")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await client.PutAsJsonAsync("/api/me/components/999", new { reason = "allergy" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/me/components/1", new { reason = "wrong" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PutAsJsonAsync("/api/me/components/1", new { note = "missing reason" })).StatusCode);
        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent([1]), "image", "one.png");
        form.Add(new ByteArrayContent([2]), "extra", "two.png");
        Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsync("/api/analyses", form)).StatusCode);
        factory.Extractor.Fail = true;
        var failure = await client.PostAsJsonAsync("/api/analyses/text", new { text = "Склад: молоко." });
        Assert.Equal(HttpStatusCode.ServiceUnavailable, failure.StatusCode);
        Assert.Equal("llm_quota", (await Read(failure, false)).GetProperty("title").GetString());
        Assert.Equal(0, (await Read(await client.GetAsync("/api/analyses"))).GetProperty("total").GetInt32());
    }

    [Fact]
    public async Task OpenApiIsGenerated()
    {
        using var factory = new ApiFactory();
        using var client = factory.CreateClient();
        var spec = await Read(await client.GetAsync("/openapi/v1.json"));
        Assert.True(spec.GetProperty("paths").TryGetProperty("/api/analyses", out _));
        var upload = spec.GetProperty("paths").GetProperty("/api/analyses").GetProperty("post")
            .GetProperty("requestBody").GetProperty("content").GetProperty("multipart/form-data").GetProperty("schema");
        Assert.Equal("binary", upload.GetProperty("properties").GetProperty("image").GetProperty("format").GetString());
        Assert.Equal(1, upload.GetProperty("required").GetArrayLength());
    }

    private static async Task<JsonElement> RegisterAndLogin(HttpClient client, string email)
    {
        (await client.PostAsJsonAsync("/api/auth/register", new { email, password = "Password-12345", displayName = "Тест" })).EnsureSuccessStatusCode();
        var tokens = await Read(await client.PostAsJsonAsync("/api/auth/login", new { email, password = "Password-12345" }));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", tokens.GetProperty("accessToken").GetString());
        return tokens;
    }
    private static async Task<JsonElement> Read(HttpResponseMessage response, bool success = true)
    {
        if (success) response.EnsureSuccessStatusCode();
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync()).RootElement.Clone();
    }
}

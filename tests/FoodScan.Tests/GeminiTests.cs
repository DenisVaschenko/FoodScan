using System.Net;
using FoodScan.Api.Services;
using Microsoft.Extensions.Configuration;
using Xunit;

namespace FoodScan.Tests;

public class GeminiTests
{
    [Fact]
    public async Task StructuredRequestAndValidResponse()
    {
        var handler = new StubHandler(HttpStatusCode.OK,
            """{"candidates":[{"finishReason":"STOP","content":{"parts":[{"text":"{\"items\":[{\"name\":\"цукор\",\"evidence\":\"цукор\",\"presence\":\"ingredient\"}],\"needsReview\":false,\"warnings\":[]}"}]}}]}""");
        using var client = new HttpClient(handler);
        var result = await Extractor(client).ExtractAsync("цукор", CancellationToken.None);
        Assert.Single(result.Items);
        Assert.Contains("responseSchema", handler.Body);
        Assert.Contains("systemInstruction", handler.Body);
        Assert.Equal("test-key", handler.ApiKey);
        Assert.EndsWith("/models/test-model:generateContent", handler.Url);
    }

    [Theory]
    [InlineData(429, "{}", "llm_quota")]
    [InlineData(500, "{}", "llm_http_error")]
    [InlineData(200, "{\"candidates\":[]}", "llm_invalid_response")]
    [InlineData(200, "{\"candidates\":[{\"finishReason\":\"MAX_TOKENS\"}]}", "llm_invalid_response")]
    public async Task ProviderFailuresAreExplicit(int status, string response, string code)
    {
        using var client = new HttpClient(new StubHandler((HttpStatusCode)status, response));
        var error = await Assert.ThrowsAsync<ApiException>(() => Extractor(client).ExtractAsync("цукор", CancellationToken.None));
        Assert.Equal(code, error.Code);
    }

    private static GeminiExtractor Extractor(HttpClient client) => new(client,
        new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> {
            ["Gemini:ApiKey"] = "test-key", ["Gemini:Model"] = "test-model"
        }).Build());

    private sealed class StubHandler(HttpStatusCode status, string response) : HttpMessageHandler
    {
        public string Body { get; private set; } = "";
        public string Url { get; private set; } = "";
        public string ApiKey { get; private set; } = "";
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            Body = await request.Content!.ReadAsStringAsync(ct);
            Url = request.RequestUri!.ToString();
            ApiKey = request.Headers.GetValues("x-goog-api-key").Single();
            return new(status) { Content = new StringContent(response) };
        }
    }
}

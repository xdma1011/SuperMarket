using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Infrastructure.Services;

internal sealed class ClaudeInvoiceOcrProvider : IInvoiceOcrProvider
{
    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const string ApiVersion = "2023-06-01";
    private const string DefaultModelName = "claude-sonnet-5";
    private const int MaxTokens = 4096;

    private readonly HttpClient _httpClient;
    private readonly ISettingsProvider _settingsProvider;

    public string ProviderName => "Claude";

    public ClaudeInvoiceOcrProvider(HttpClient httpClient, ISettingsProvider settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public async Task<Result<InvoiceExtractionResult>> ExtractAsync(
        byte[] imageBytes, string mimeType, CancellationToken cancellationToken)
    {
        var apiKey = await _settingsProvider.GetStringAsync(InvoiceOcrSettingsKeys.ClaudeApiKey, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Failure<InvoiceExtractionResult>(
                Error.BusinessRule("InvoiceOcr.ProviderNotConfigured", $"{ProviderName} غير مفعّل (مفتاح API غير مُعدّ)."));
        }

        var modelName = await _settingsProvider.GetStringAsync(InvoiceOcrSettingsKeys.ClaudeModelName, DefaultModelName, cancellationToken);

        try
        {
            var requestBody = new ClaudeRequest(
                Model: modelName!,
                MaxTokens: MaxTokens,
                Messages: new[]
                {
                    new ClaudeMessage("user", new object[]
                    {
                        new ClaudeTextContent(InvoiceOcrPrompt.Text),
                        new ClaudeImageContent(new ClaudeImageSource("base64", mimeType, Convert.ToBase64String(imageBytes)))
                    })
                });

            using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl)
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("x-api-key", apiKey);
            request.Headers.Add("anthropic-version", ApiVersion);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<InvoiceExtractionResult>(Error.BusinessRule(
                    "InvoiceOcr.ApiError", $"{ProviderName} رجّعت {(int)response.StatusCode}: {errorBody}"));
            }

            var parsed = await response.Content.ReadFromJsonAsync<ClaudeResponse>(cancellationToken: cancellationToken);
            var text = parsed?.Content?.FirstOrDefault(c => c.Type == "text")?.Text;

            if (string.IsNullOrWhiteSpace(text))
            {
                return Result.Failure<InvoiceExtractionResult>(
                    Error.BusinessRule("InvoiceOcr.EmptyResponse", $"{ProviderName} رجّعت استجابة فارغة."));
            }

            return InvoiceOcrResponseParser.Parse(text, ProviderName);
        }
        catch (Exception ex)
        {
            return Result.Failure<InvoiceExtractionResult>(
                Error.BusinessRule("InvoiceOcr.RequestFailed", $"فشل الاتصال بـ{ProviderName}: {ex.Message}"));
        }
    }

    private sealed record ClaudeRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("messages")] ClaudeMessage[] Messages);

    private sealed record ClaudeMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] object[] Content);

    private sealed record ClaudeTextContent([property: JsonPropertyName("text")] string Text)
    {
        [JsonPropertyName("type")]
        public string Type => "text";
    }

    private sealed record ClaudeImageContent([property: JsonPropertyName("source")] ClaudeImageSource Source)
    {
        [JsonPropertyName("type")]
        public string Type => "image";
    }

    private sealed record ClaudeImageSource(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("media_type")] string MediaType,
        [property: JsonPropertyName("data")] string Data);

    private sealed record ClaudeResponse([property: JsonPropertyName("content")] ClaudeResponseContent[]? Content);
    private sealed record ClaudeResponseContent(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string? Text);
}

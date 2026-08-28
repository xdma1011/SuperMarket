using System.Net.Http.Json;
using System.Text.Json.Serialization;
using SupermarketSystem.Application.Common.Interfaces;
using SupermarketSystem.Application.Common.Results;

namespace SupermarketSystem.Infrastructure.Services;

internal abstract class GeminiInvoiceOcrProviderBase : IInvoiceOcrProvider
{
    private const string BaseUrl = "https://generativelanguage.googleapis.com/v1beta/models";

    private readonly HttpClient _httpClient;
    private readonly ISettingsProvider _settingsProvider;
    private readonly string _defaultModelName;
    private readonly string _modelNameSettingKey;

    public abstract string ProviderName { get; }

    protected GeminiInvoiceOcrProviderBase(
        HttpClient httpClient, ISettingsProvider settingsProvider, string modelNameSettingKey, string defaultModelName)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
        _modelNameSettingKey = modelNameSettingKey;
        _defaultModelName = defaultModelName;
    }

    public async Task<Result<InvoiceExtractionResult>> ExtractAsync(
        byte[] imageBytes, string mimeType, CancellationToken cancellationToken)
    {
        var apiKey = await _settingsProvider.GetStringAsync(InvoiceOcrSettingsKeys.GeminiApiKey, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return Result.Failure<InvoiceExtractionResult>(
                Error.BusinessRule("InvoiceOcr.ProviderNotConfigured", $"{ProviderName} غير مفعّل (مفتاح API غير مُعدّ)."));
        }

        var modelName = await _settingsProvider.GetStringAsync(_modelNameSettingKey, _defaultModelName, cancellationToken);

        try
        {
            var requestBody = new GeminiRequest(
                Contents: new[]
                {
                    new GeminiContent(Parts: new object[]
                    {
                        new GeminiTextPart(InvoiceOcrPrompt.Text),
                        new GeminiInlineDataPart(new GeminiInlineData(mimeType, Convert.ToBase64String(imageBytes)))
                    })
                },
                GenerationConfig: new GeminiGenerationConfig(ResponseMimeType: "application/json"));

            using var request = new HttpRequestMessage(HttpMethod.Post, $"{BaseUrl}/{modelName}:generateContent")
            {
                Content = JsonContent.Create(requestBody)
            };
            request.Headers.Add("x-goog-api-key", apiKey);

            using var response = await _httpClient.SendAsync(request, cancellationToken);

            if (!response.IsSuccessStatusCode)
            {
                var errorBody = await response.Content.ReadAsStringAsync(cancellationToken);
                return Result.Failure<InvoiceExtractionResult>(Error.BusinessRule(
                    "InvoiceOcr.ApiError", $"{ProviderName} رجّعت {(int)response.StatusCode}: {errorBody}"));
            }

            var parsed = await response.Content.ReadFromJsonAsync<GeminiResponse>(cancellationToken: cancellationToken);
            var text = parsed?.Candidates?.FirstOrDefault()?.Content?.Parts?.FirstOrDefault()?.Text;

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

    private sealed record GeminiRequest(
        [property: JsonPropertyName("contents")] GeminiContent[] Contents,
        [property: JsonPropertyName("generationConfig")] GeminiGenerationConfig GenerationConfig);

    private sealed record GeminiContent([property: JsonPropertyName("parts")] object[] Parts);

    private sealed record GeminiTextPart([property: JsonPropertyName("text")] string Text);

    private sealed record GeminiInlineDataPart([property: JsonPropertyName("inline_data")] GeminiInlineData InlineData);

    private sealed record GeminiInlineData(
        [property: JsonPropertyName("mime_type")] string MimeType,
        [property: JsonPropertyName("data")] string Data);

    private sealed record GeminiGenerationConfig([property: JsonPropertyName("responseMimeType")] string ResponseMimeType);

    private sealed record GeminiResponse([property: JsonPropertyName("candidates")] GeminiCandidate[]? Candidates);
    private sealed record GeminiCandidate([property: JsonPropertyName("content")] GeminiResponseContent? Content);
    private sealed record GeminiResponseContent([property: JsonPropertyName("parts")] GeminiResponsePart[]? Parts);
    private sealed record GeminiResponsePart([property: JsonPropertyName("text")] string? Text);
}

/// <summary>Gemini العادي (Pro) — أول محاولة بترتيب الـfallback المتفق عليه.</summary>
internal sealed class GeminiProInvoiceOcrProvider : GeminiInvoiceOcrProviderBase
{
    public override string ProviderName => "Gemini";

    public GeminiProInvoiceOcrProvider(HttpClient httpClient, ISettingsProvider settingsProvider)
        : base(httpClient, settingsProvider, InvoiceOcrSettingsKeys.GeminiProModelName, "gemini-pro-latest")
    {
    }
}

/// <summary>Gemini Flash — ثاني محاولة، لو Gemini العادي فشل أو غير مفعّل.</summary>
internal sealed class GeminiFlashInvoiceOcrProvider : GeminiInvoiceOcrProviderBase
{
    public override string ProviderName => "Gemini Flash";

    public GeminiFlashInvoiceOcrProvider(HttpClient httpClient, ISettingsProvider settingsProvider)
        : base(httpClient, settingsProvider, InvoiceOcrSettingsKeys.GeminiFlashModelName, "gemini-flash-latest")
    {
    }
}

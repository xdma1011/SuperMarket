using System.IdentityModel.Tokens.Jwt;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.IdentityModel.Tokens;
using SupermarketSystem.Application.Common.Interfaces;

namespace SupermarketSystem.Infrastructure.Services;

/// <summary>
/// Firebase Cloud Messaging - HTTP v1 API (المُوصى بها حاليًا من Google،
/// الـlegacy Server Key API متوقفة). تتطلب OAuth2 عبر ملف Service Account
/// JSON كامل (Firebase.ServiceAccountJson) - لا مفتاح بسيط زي بقية
/// التكاملات، لأن Google لا تدعم مصادقة أبسط لهاي الـAPI. الخطوات:
/// 1) توقيع JWT بالمفتاح الخاص بالملف (RS256).
/// 2) تبادله بـaccess token عبر oauth2.googleapis.com.
/// 3) استخدام الـaccess token لإرسال الإشعار فعليًا.
///
/// access token يُخزَّن مؤقتًا بالذاكرة (صالح ساعة من Google) ضمن عمر
/// نسخة الخدمة نفسها - يفيد لو أرسلنا أكثر من إشعار بنفس الطلب (زبون
/// عنده أكثر من جهاز)، بلا تعقيد Singleton إضافي عبر HttpClientFactory.
/// </summary>
public sealed class FirebasePushNotificationSender : IPushNotificationSender
{
    private const string TokenUri = "https://oauth2.googleapis.com/token";
    private const string FcmScope = "https://www.googleapis.com/auth/firebase.messaging";

    private readonly HttpClient _httpClient;
    private readonly ISettingsProvider _settingsProvider;

    private readonly SemaphoreSlim _tokenLock = new(1, 1);
    private string? _cachedAccessToken;
    private DateTime _cachedAccessTokenExpiresAtUtc;
    private string? _cachedProjectId;

    public FirebasePushNotificationSender(HttpClient httpClient, ISettingsProvider settingsProvider)
    {
        _httpClient = httpClient;
        _settingsProvider = settingsProvider;
    }

    public async Task<bool> SendAsync(string deviceToken, string title, string body, CancellationToken cancellationToken)
    {
        var serviceAccountJson = await _settingsProvider.GetStringAsync(FirebaseSettingsKeys.ServiceAccountJson, null, cancellationToken);
        if (string.IsNullOrWhiteSpace(serviceAccountJson))
        {
            // فشل هادئ ومقصود - نفس مبدأ كل تكامل خارجي بالمشروع.
            return false;
        }

        try
        {
            var (accessToken, projectId) = await GetAccessTokenAsync(serviceAccountJson, cancellationToken);
            if (accessToken is null || projectId is null)
            {
                return false;
            }

            var url = $"https://fcm.googleapis.com/v1/projects/{projectId}/messages:send";
            var request = new HttpRequestMessage(HttpMethod.Post, url);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
            request.Content = JsonContent.Create(new FcmSendRequest(new FcmMessage(deviceToken, new FcmNotification(title, body))));

            var response = await _httpClient.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<(string? AccessToken, string? ProjectId)> GetAccessTokenAsync(string serviceAccountJson, CancellationToken cancellationToken)
    {
        await _tokenLock.WaitAsync(cancellationToken);
        try
        {
            if (_cachedAccessToken is not null && DateTime.UtcNow < _cachedAccessTokenExpiresAtUtc)
            {
                return (_cachedAccessToken, _cachedProjectId);
            }

            using var document = JsonDocument.Parse(serviceAccountJson);
            var root = document.RootElement;
            var projectId = root.GetProperty("project_id").GetString();
            var clientEmail = root.GetProperty("client_email").GetString();
            var privateKeyPem = root.GetProperty("private_key").GetString();
            var tokenUri = root.TryGetProperty("token_uri", out var tokenUriProperty) ? tokenUriProperty.GetString() : TokenUri;

            if (projectId is null || clientEmail is null || privateKeyPem is null)
            {
                return (null, null);
            }

            var assertion = BuildSignedAssertion(clientEmail, tokenUri ?? TokenUri, privateKeyPem);

            var tokenResponse = await _httpClient.PostAsync(
                tokenUri ?? TokenUri,
                new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["grant_type"] = "urn:ietf:params:oauth:grant-type:jwt-bearer",
                    ["assertion"] = assertion
                }),
                cancellationToken);

            if (!tokenResponse.IsSuccessStatusCode)
            {
                return (null, null);
            }

            var tokenResult = await tokenResponse.Content.ReadFromJsonAsync<GoogleTokenResponse>(cancellationToken: cancellationToken);
            if (tokenResult?.AccessToken is null)
            {
                return (null, null);
            }

            _cachedAccessToken = tokenResult.AccessToken;
            _cachedProjectId = projectId;
            // هامش أمان 60 ثانية قبل الانتهاء الفعلي (Google بترجع expires_in=3600 عادة).
            _cachedAccessTokenExpiresAtUtc = DateTime.UtcNow.AddSeconds(Math.Max(tokenResult.ExpiresIn - 60, 60));

            return (_cachedAccessToken, _cachedProjectId);
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private static string BuildSignedAssertion(string clientEmail, string tokenUri, string privateKeyPem)
    {
        using var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);

        var credentials = new SigningCredentials(new RsaSecurityKey(rsa), SecurityAlgorithms.RsaSha256);
        var now = DateTime.UtcNow;

        var token = new JwtSecurityToken(
            issuer: clientEmail,
            audience: tokenUri,
            claims: new[] { new Claim("scope", FcmScope) },
            notBefore: now,
            expires: now.AddHours(1),
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    private sealed record FcmSendRequest([property: JsonPropertyName("message")] FcmMessage Message);

    private sealed record FcmMessage(
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("notification")] FcmNotification Notification);

    private sealed record FcmNotification(
        [property: JsonPropertyName("title")] string Title,
        [property: JsonPropertyName("body")] string Body);

    private sealed record GoogleTokenResponse(
        [property: JsonPropertyName("access_token")] string? AccessToken,
        [property: JsonPropertyName("expires_in")] int ExpiresIn);
}

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Caching.Memory;

namespace TicketShield.AntiBot.Verification;

public sealed class ChallengeVerificationService(
    IHttpClientFactory clients, IConfiguration config, IHostEnvironment environment, IMemoryCache cache)
{
    private readonly object _lock = new();

    public async Task<string?> VerifyAsync(string token, string? provider, CancellationToken ct)
    {
        var turnstile = provider == "turnstile";
        if (!turnstile && provider is not (null or "recaptcha" or "sequential"))
            return "ChallengeTokenInvalid";

        var secret = config[turnstile ? "AntiBotScoring:TurnstileSecretKey" : "AntiBotScoring:RecaptchaSecretKey"];
        if (secret is null && environment.IsDevelopment())
            secret = turnstile ? "1x0000000000000000000000000000000AA" : "6LeIxAcTAAAAAGG-vFI1TnRWxMZNFuojJ4WifJWe";
        if (string.IsNullOrWhiteSpace(secret)) return "ChallengeVerificationUnavailable";

        var key = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));
        if (cache.TryGetValue(key, out _)) return "ChallengeTokenReplayed";
        var endpoint = turnstile ? "https://challenges.cloudflare.com/turnstile/v0/siteverify"
                                 : "https://www.google.com/recaptcha/api/siteverify";
        try
        {
            using var client = clients.CreateClient("captcha");
            using var form = new FormUrlEncodedContent(new Dictionary<string, string> {
                ["secret"] = secret, ["response"] = token
            });
            using var response = await client.PostAsync(endpoint, form, ct);
            if (!response.IsSuccessStatusCode) return "ChallengeVerificationUnavailable";
            var result = await response.Content.ReadFromJsonAsync<JsonElement>(cancellationToken: ct);
            if (!result.TryGetProperty("success", out var success) || success.ValueKind != JsonValueKind.True)
                return "ChallengeTokenInvalid";
            lock (_lock)
            {
                if (cache.TryGetValue(key, out _)) return "ChallengeTokenReplayed";
                cache.Set(key, true, TimeSpan.FromMinutes(10));
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException ||
                                   ex is OperationCanceledException && !ct.IsCancellationRequested)
        {
            return "ChallengeVerificationUnavailable";
        }
    }
}

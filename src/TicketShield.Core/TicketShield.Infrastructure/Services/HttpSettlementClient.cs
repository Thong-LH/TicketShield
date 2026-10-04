using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.Extensions.Configuration;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Infrastructure.Services;

public class HttpSettlementClient : ISettlementClient
{
    private readonly HttpClient _http;
    private readonly string _sharedSecret;

    public HttpSettlementClient(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _sharedSecret = configuration["Settlement:SharedSecret"] ?? "";
    }

    public async Task<bool> SendAsync(PayoutRequestedEvent command, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/payouts")
        {
            Content = JsonContent.Create(command)
        };
        request.Headers.TryAddWithoutValidation("X-Settlement-Key", _sharedSecret);
        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (HttpRequestException)
        {
            return false;
        }
    }

    public async Task<SettlementTransferStatus?> GetStatusAsync(string idempotencyKey, CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, $"api/v1/payouts/{Uri.EscapeDataString(idempotencyKey)}");
        request.Headers.TryAddWithoutValidation("X-Settlement-Key", _sharedSecret);
        try
        {
            var response = await _http.SendAsync(request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var body = await response.Content.ReadFromJsonAsync<SettlementTransferStatus>(cancellationToken);
            return body;
        }
        catch (HttpRequestException)
        {
            return null;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}

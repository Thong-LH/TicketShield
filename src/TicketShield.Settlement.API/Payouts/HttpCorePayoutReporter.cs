using System.Net.Http.Json;

namespace TicketShield.Settlement.API.Payouts;

public class HttpCorePayoutReporter : ICorePayoutReporter
{
    private readonly HttpClient _http;
    private readonly string _sharedSecret;

    public HttpCorePayoutReporter(HttpClient http, IConfiguration configuration)
    {
        _http = http;
        _sharedSecret = configuration["Settlement:SharedSecret"] ?? "";
    }

    public async Task<bool> ReportAsync(Guid escrowId, string idempotencyKey, bool succeeded, string? bankReference, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "api/v1/internal/payouts/reports")
        {
            Content = JsonContent.Create(new
            {
                escrowId,
                idempotencyKey,
                succeeded,
                bankReference
            })
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
}

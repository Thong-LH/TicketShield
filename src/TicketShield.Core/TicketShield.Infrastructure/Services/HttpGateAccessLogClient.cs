using System.Net.Http.Json;
using System.Text.Json;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes;

namespace TicketShield.Infrastructure.Services;

public class HttpGateAccessLogClient : IGateAccessLogClient
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly HttpClient _http;

    public HttpGateAccessLogClient(HttpClient http)
    {
        _http = http;
    }

    public async Task<IReadOnlyList<GateScan>> GetScansAsync(string ticketCode, CancellationToken cancellationToken)
    {
        var path = "api/v1/gate/access-logs?ticketCode=" + Uri.EscapeDataString(ticketCode);
        using var response = await _http.GetAsync(path, cancellationToken);
        if (!response.IsSuccessStatusCode)
        {
            throw new HttpRequestException($"Gate access logs returned {(int)response.StatusCode}.");
        }

        var scans = await response.Content.ReadFromJsonAsync<List<GateScan>>(Json, cancellationToken);
        return scans ?? new List<GateScan>();
    }
}

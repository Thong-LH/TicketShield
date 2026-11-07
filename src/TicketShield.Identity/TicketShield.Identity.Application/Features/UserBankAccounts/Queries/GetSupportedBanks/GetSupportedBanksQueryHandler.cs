using System.Net.Http.Json;
using System.Text.Json.Serialization;
using MediatR;
using Microsoft.Extensions.Configuration;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetSupportedBanks;

/// <summary>Proxies BankLookup's bank directory so clients never own bank/BIN data.</summary>
public sealed class GetSupportedBanksQueryHandler
    : IRequestHandler<GetSupportedBanksQuery, ApiResponse<List<BankDirectoryItem>>>
{
    private static readonly HttpClient HttpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IConfiguration _configuration;

    public GetSupportedBanksQueryHandler(IConfiguration configuration) => _configuration = configuration;

    public async Task<ApiResponse<List<BankDirectoryItem>>> Handle(
        GetSupportedBanksQuery request,
        CancellationToken cancellationToken)
    {
        var baseUrl = (_configuration["BankLookup:BaseUrl"] ?? "https://api.banklookup.net").TrimEnd('/');

        try
        {
            var response = await HttpClient.GetFromJsonAsync<BankLookupDirectoryResponse>(
                $"{baseUrl}/bank/list", cancellationToken);

            if (response?.Success == true && response.Data is not null)
            {
                var banks = response.Data
                    .Where(bank => !string.IsNullOrWhiteSpace(bank.Code) && bank.Bin > 0)
                    .Select(bank => new BankDirectoryItem(
                        bank.Code,
                        bank.Name,
                        bank.Bin.ToString(),
                        string.IsNullOrWhiteSpace(bank.ShortName) ? bank.Code : bank.ShortName))
                    .ToList();

                return ApiResponse<List<BankDirectoryItem>>.SuccessResponse(banks, "Lấy danh mục ngân hàng thành công.");
            }
        }
        catch (HttpRequestException)
        {
            // The client receives an explicit unavailable response rather than stale hard-coded data.
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // BankLookup timeout.
        }

        return ApiResponse<List<BankDirectoryItem>>.FailureResponse(
            "Không thể tải danh mục ngân hàng từ BankLookup. Vui lòng thử lại.");
    }

    private sealed class BankLookupDirectoryResponse
    {
        public bool Success { get; set; }
        public List<BankLookupBank>? Data { get; set; }
    }

    private sealed class BankLookupBank
    {
        public string Code { get; set; } = string.Empty;
        public string Name { get; set; } = string.Empty;
        public long Bin { get; set; }
        [JsonPropertyName("short_name")]
        public string ShortName { get; set; } = string.Empty;
    }
}

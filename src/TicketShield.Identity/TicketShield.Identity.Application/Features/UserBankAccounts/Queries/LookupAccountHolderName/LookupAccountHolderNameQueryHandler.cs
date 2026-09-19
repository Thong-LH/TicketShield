using System.Net.Http.Json;
using MediatR;
using Microsoft.Extensions.Configuration;
using TicketShield.Identity.Application.Common.Models;

namespace TicketShield.Identity.Application.Features.UserBankAccounts.Queries.LookupAccountHolderName;

/// <summary>
/// BE-CORE-3.1.5: Lookup bank account holder name via BankLookup.net API (proxy to avoid CORS & hide secrets).
/// </summary>
public class LookupAccountHolderNameQueryHandler
    : IRequestHandler<LookupAccountHolderNameQuery, ApiResponse<LookupAccountHolderNameResult>>
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly IConfiguration _configuration;

    public LookupAccountHolderNameQueryHandler(IConfiguration configuration)
    {
        _configuration = configuration;
    }

    public async Task<ApiResponse<LookupAccountHolderNameResult>> Handle(
        LookupAccountHolderNameQuery request, CancellationToken cancellationToken)
    {
        var cleanAcc = System.Text.RegularExpressions.Regex.Replace(request.AccountNumber, @"\D", "");
        if (string.IsNullOrWhiteSpace(cleanAcc) || cleanAcc.Length < 6 || string.IsNullOrWhiteSpace(request.BankBin))
        {
            return ApiResponse<LookupAccountHolderNameResult>.SuccessResponse(
                new LookupAccountHolderNameResult { Success = false, AccountName = string.Empty, IsVerified = false },
                "Thông tin không hợp lệ.");
        }

        try
        {
            var apiKey = _configuration["BankLookup:ApiKey"] ?? "a19523a7-d635-4874-afba-33f0c3a7bb20key";
            var apiSecret = _configuration["BankLookup:ApiSecret"] ?? "aafb8ddc-db07-480f-903a-697108984f05secret";
            var baseUrl = _configuration["BankLookup:BaseUrl"] ?? "https://api.banklookup.net";

            using var httpRequest = new HttpRequestMessage(HttpMethod.Post, baseUrl);
            httpRequest.Headers.Add("x-api-key", apiKey);
            httpRequest.Headers.Add("x-api-secret", apiSecret);

            var payload = new
            {
                bank = request.BankBin,
                account = cleanAcc
            };

            httpRequest.Content = JsonContent.Create(payload);

            var response = await _httpClient.SendAsync(httpRequest, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var data = await response.Content.ReadFromJsonAsync<BankLookupResponse>(cancellationToken: cancellationToken);
                if (data != null && data.Success && data.Data != null && !string.IsNullOrWhiteSpace(data.Data.OwnerName))
                {
                    return ApiResponse<LookupAccountHolderNameResult>.SuccessResponse(
                        new LookupAccountHolderNameResult
                        {
                            Success = true,
                            AccountName = data.Data.OwnerName.ToUpper(),
                            IsVerified = true
                        },
                        "Tra cứu tên tài khoản thành công qua BankLookup.");
                }
            }
        }
        catch
        {
            // Fallback gracefully if external API is unreachable or times out
        }

        return ApiResponse<LookupAccountHolderNameResult>.SuccessResponse(
            new LookupAccountHolderNameResult { Success = false, AccountName = string.Empty, IsVerified = false },
            "Không thể tra cứu tên tài khoản tự động. Vui lòng nhập thủ công.");
    }

    private class BankLookupResponse
    {
        public int Code { get; set; }
        public bool Success { get; set; }
        public string Msg { get; set; } = string.Empty;
        public BankLookupData? Data { get; set; }
    }

    private class BankLookupData
    {
        public string OwnerName { get; set; } = string.Empty;
        public string Bank { get; set; } = string.Empty;
        public string Account { get; set; } = string.Empty;
    }
}

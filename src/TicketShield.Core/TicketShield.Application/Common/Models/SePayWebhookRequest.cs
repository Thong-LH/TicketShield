using System.Text.Json.Serialization;

namespace TicketShield.Application.Common.Models;

public class SePayWebhookRequest
{
    [JsonPropertyName("id")]
    public long Id { get; set; }

    [JsonPropertyName("gateway")]
    public string? Gateway { get; set; }

    [JsonPropertyName("transactionDate")]
    public string? TransactionDate { get; set; }

    [JsonPropertyName("accountNumber")]
    public string? AccountNumber { get; set; }

    [JsonPropertyName("subAccount")]
    public string? SubAccount { get; set; }

    [JsonPropertyName("code")]
    public string? Code { get; set; }

    [JsonPropertyName("content")]
    public string Content { get; set; } = string.Empty;

    [JsonPropertyName("transferType")]
    public string TransferType { get; set; } = string.Empty; // "in" or "out"

    [JsonPropertyName("transferAmount")]
    public decimal TransferAmount { get; set; }

    [JsonPropertyName("accumulated")]
    public decimal Accumulated { get; set; }

    [JsonPropertyName("referenceCode")]
    public string? ReferenceCode { get; set; }

    [JsonPropertyName("referenceNum")]
    public string? ReferenceNum { get; set; }

    [JsonPropertyName("description")]
    public string? Description { get; set; }
}

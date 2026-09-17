using System.Web;
using Microsoft.Extensions.Options;
using TicketShield.Application.Common.Configurations;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;

namespace TicketShield.Infrastructure.Services;

public class VietQrService : IVietQrService
{
    private readonly VietQrSettings _settings;

    public VietQrService(IOptions<VietQrSettings> settings)
    {
        _settings = settings.Value;
    }

    public VietQrQuickLinkResult GenerateQuickLink(VietQrQuickLinkRequest request)
    {
        var bankBin = string.IsNullOrWhiteSpace(request.BankBin) ? _settings.BankBin : request.BankBin.Trim();
        var accountNumber = string.IsNullOrWhiteSpace(request.AccountNumber) ? _settings.AccountNumber : request.AccountNumber.Trim();
        var accountName = string.IsNullOrWhiteSpace(request.AccountName) ? _settings.AccountName : request.AccountName.Trim();
        var template = string.IsNullOrWhiteSpace(request.Template) ? _settings.QrTemplate : request.Template.Trim();
        if (string.IsNullOrWhiteSpace(template)) template = "compact2";

        var transferContent = request.TransferContent?.Trim() ?? string.Empty;
        var roundedAmount = Math.Max(0, Math.Round(request.Amount, 0));

        // Format: https://img.vietqr.io/image/{bankId}-{accountNo}-{template}.png?amount={amount}&addInfo={addInfo}&accountName={accountName}
        var encodedAddInfo = HttpUtility.UrlEncode(transferContent);
        var encodedAccountName = HttpUtility.UrlEncode(accountName);

        var qrImageUrl = $"https://img.vietqr.io/image/{bankBin}-{accountNumber}-{template}.png?amount={roundedAmount:0}&addInfo={encodedAddInfo}&accountName={encodedAccountName}";
        var quickLinkUrl = $"https://vietqr.me/image/{bankBin}-{accountNumber}-{template}.png?amount={roundedAmount:0}&addInfo={encodedAddInfo}&accountName={encodedAccountName}";

        return new VietQrQuickLinkResult
        {
            QrImageUrl = qrImageUrl,
            QuickLinkUrl = quickLinkUrl,
            BankBin = bankBin,
            AccountNumber = accountNumber,
            AccountName = accountName,
            Amount = roundedAmount,
            TransferContent = transferContent
        };
    }

    public VietQrQuickLinkResult GenerateSystemQuickLink(decimal amount, string transferContent, string? template = null)
    {
        return GenerateQuickLink(new VietQrQuickLinkRequest
        {
            BankBin = _settings.BankBin,
            AccountNumber = _settings.AccountNumber,
            AccountName = _settings.AccountName,
            Amount = amount,
            TransferContent = transferContent,
            Template = template ?? _settings.QrTemplate
        });
    }
}

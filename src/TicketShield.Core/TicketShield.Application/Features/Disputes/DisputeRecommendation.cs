namespace TicketShield.Application.Features.Disputes;

public static class DisputeRecommendation
{
    public const string RecommendRefund = "RECOMMEND_REFUND";
    public const string RecommendRejectFraud = "RECOMMEND_REJECT_FRAUD";

    public static string? Decide(DateTimeOffset? transferredAt, IReadOnlyList<TicketGateLog> tickets)
    {
        if (transferredAt is null || tickets.Count == 0)
        {
            return null;
        }

        var signals = tickets.Select(ticket => Classify(transferredAt.Value, ticket.Scans)).ToArray();
        if (signals.Any(signal => signal == TicketSignal.Refund))
        {
            return RecommendRefund;
        }

        if (signals.All(signal => signal == TicketSignal.Entered))
        {
            return RecommendRejectFraud;
        }

        return null;
    }

    private static TicketSignal Classify(DateTimeOffset transferredAt, IReadOnlyList<GateScan> scans)
    {
        if (scans.Any(scan => scan.ScanResult == "SUCCESS" && scan.ScannedAt >= transferredAt))
        {
            return TicketSignal.Entered;
        }

        if (scans.Any(scan => scan.ScannedAt < transferredAt)
            || scans.Any(scan => scan.ScanResult is "INVALID_TICKET" or "REVOKED"))
        {
            return TicketSignal.Refund;
        }

        return TicketSignal.Unknown;
    }

    private enum TicketSignal
    {
        Entered,
        Refund,
        Unknown
    }
}

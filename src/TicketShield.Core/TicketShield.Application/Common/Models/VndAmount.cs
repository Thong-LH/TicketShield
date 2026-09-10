namespace TicketShield.Application.Common.Models;

/// <summary>Exact whole-dong conversion at the numeric(15,2) database boundary.</summary>
public static class VndAmount
{
    public const long MaxDatabaseValue = 9_999_999_999_999;

    public static long FromDatabase(decimal value)
    {
        if (value < 0 || value > MaxDatabaseValue || decimal.Truncate(value) != value)
            throw new ArgumentOutOfRangeException(nameof(value), "VND must be whole dong within numeric(15,2) range.");
        return decimal.ToInt64(value);
    }

    public static decimal ToDatabase(long value)
    {
        if (value < 0 || value > MaxDatabaseValue)
            throw new ArgumentOutOfRangeException(nameof(value), "VND is outside numeric(15,2) range.");
        return value;
    }
    // Zero is representable on the wire. Whether it may be published is a separate business policy.
}

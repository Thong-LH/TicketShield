namespace TicketShield.Domain.Enums;

public enum ListingStatus
{
    Draft,
    Verified,
    Transacting,
    Sold,
    Cancelled
}

public enum VerificationStatus
{
    PendingOtp,
    Verified,
    Rejected
}

public enum EscrowStatus
{
    Pending,
    Locked,
    Released,
    Refunded,
    Disputed
}

public enum PayoutStatus
{
    Pending,
    Processing,
    Success,
    Failed
}

public enum DisputeStatus
{
    Open,
    UnderReview,
    Resolved,
    Rejected
}

public enum DisputeResolution
{
    RefundBuyer,
    ReleaseSeller
}

public enum DisputeReasonCode
{
    TicketInvalid,
    DuplicateEntry,
    FakeTicket
}

public enum UserRole
{
    User,
    Admin,
    Cskh,
    Organizer
}

public enum TicketStatus
{
    Valid,
    LockedForResale,
    Transferred,
    Used,
    Cancelled
}

public enum GateScanResult
{
    Success,
    DuplicateEntry,
    InvalidTicket,
    Revoked
}

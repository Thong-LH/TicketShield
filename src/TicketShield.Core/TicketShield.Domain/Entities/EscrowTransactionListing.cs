using TicketShield.Domain.Common;

namespace TicketShield.Domain.Entities;

public class EscrowTransactionListing : BaseEntity
{
    public Guid EscrowId { get; set; }
    public Guid ListingId { get; set; }

    public EscrowTransaction Escrow { get; set; } = null!;
    public ResaleListing Listing { get; set; } = null!;
}


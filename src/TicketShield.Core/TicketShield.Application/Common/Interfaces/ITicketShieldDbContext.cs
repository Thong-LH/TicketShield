using Microsoft.EntityFrameworkCore;
using TicketShield.Domain.Entities;

namespace TicketShield.Application.Common.Interfaces;

public interface ITicketShieldDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserBankAccount> UserBankAccounts { get; }
    DbSet<Organizer> Organizers { get; }
    DbSet<Event> Events { get; }
    DbSet<TicketTier> TicketTiers { get; }
    DbSet<ResaleListing> ResaleListings { get; }
    DbSet<EscrowTransaction> EscrowTransactions { get; }
    DbSet<PayoutTransaction> PayoutTransactions { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<DisputeEvidence> DisputeEvidences { get; }
    DbSet<DisputeMessage> DisputeMessages { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

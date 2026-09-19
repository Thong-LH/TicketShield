using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using TicketShield.Domain.Entities;

namespace TicketShield.Application.Common.Interfaces;

public interface ITicketShieldDbContext
{
    DbSet<Organizer> Organizers { get; }
    DbSet<Event> Events { get; }
    DbSet<TicketTier> TicketTiers { get; }
    DbSet<ResaleListing> ResaleListings { get; }
    DbSet<EscrowTransaction> EscrowTransactions { get; }
    DbSet<PayoutTransaction> PayoutTransactions { get; }
    DbSet<Dispute> Disputes { get; }
    DbSet<DisputeEvidence> DisputeEvidences { get; }
    DbSet<DisputeMessage> DisputeMessages { get; }
    DbSet<SystemSetting> SystemSettings { get; }
    DbSet<CoreResaleRow> CoreResaleRecords { get; }
    DbSet<ShadowUser> ShadowUsers { get; }

    DatabaseFacade Database { get; }

    Task<IDbContextTransactionProxy?> BeginAdvisoryLockTransactionAsync(long lockKey, CancellationToken cancellationToken = default);

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}

public interface IDbContextTransactionProxy : IAsyncDisposable
{
    Task CommitAsync(CancellationToken cancellationToken = default);
    Task RollbackAsync(CancellationToken cancellationToken = default);
}


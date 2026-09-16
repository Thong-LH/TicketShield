using Microsoft.EntityFrameworkCore;
using TicketShield.Identity.Domain.Entities;

namespace TicketShield.Identity.Application.Common.Interfaces;

public interface IIdentityDbContext
{
    DbSet<User> Users { get; }
    DbSet<UserBankAccount> UserBankAccounts { get; }
    DbSet<RefreshToken> RefreshTokens { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}

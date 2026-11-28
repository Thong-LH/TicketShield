using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;

namespace TicketShield.Application.Common.Helpers;

/// <summary>
/// Development-only compatibility adapter for running Identity and Core as
/// separate local processes without a shared message broker. Production keeps
/// ShadowUser synchronization on the UserCreated/UserProfileUpdated events.
/// </summary>
public static class IdentityDevelopmentProfileSyncHelper
{
    public static async Task<ShadowUser?> TryCreateShadowUserAsync(
        ITicketShieldDbContext db,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!db.Database.IsRelational())
            {
                return null;
            }

            var coreConnection = db.Database.GetDbConnection();
            var coreConnectionString = db.Database.GetConnectionString() ?? coreConnection.ConnectionString;
            if (string.IsNullOrWhiteSpace(coreConnectionString) ||
                Activator.CreateInstance(coreConnection.GetType()) is not System.Data.Common.DbConnection identityConnection)
            {
                return null;
            }

            await using (identityConnection)
            {
                identityConnection.ConnectionString = System.Text.RegularExpressions.Regex.Replace(
                    coreConnectionString,
                    @"Database=[^;]+",
                    "Database=identity_db",
                    System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                await identityConnection.OpenAsync(cancellationToken);

                await using var command = identityConnection.CreateCommand();
                command.CommandText = "SELECT \"Email\", \"FullName\", \"PhoneNumber\", \"Role\", \"IsActive\" " +
                                      "FROM users WHERE \"Id\" = @userId LIMIT 1;";
                var userIdParameter = command.CreateParameter();
                userIdParameter.ParameterName = "@userId";
                userIdParameter.DbType = System.Data.DbType.Guid;
                userIdParameter.Value = userId;
                command.Parameters.Add(userIdParameter);

                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return null;
                }

                var role = Enum.TryParse<UserRole>(reader.GetString(3), true, out var parsedRole)
                    ? parsedRole
                    : UserRole.User;
                var now = DateTimeOffset.UtcNow;
                var shadowUser = new ShadowUser
                {
                    Id = userId,
                    Email = reader.GetString(0),
                    FullName = reader.GetString(1),
                    PhoneNumber = reader.IsDBNull(2) ? null : reader.GetString(2),
                    Role = role,
                    IsActive = reader.GetBoolean(4),
                    CreatedAt = now,
                    UpdatedAt = now
                };

                db.ShadowUsers.Add(shadowUser);
                return shadowUser;
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IdentityDevelopmentProfileSync] Unable to sync {userId}: {ex.Message}");
            return null;
        }
    }
}

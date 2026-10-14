using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Domain.Entities;

namespace TicketShield.Application.Common.Helpers;

public static class IdentityBankSyncHelper
{
    public static async Task<bool> TrySyncUserBankFromIdentityAsync(
        ITicketShieldDbContext db,
        ShadowUser user,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (!db.Database.IsRelational())
            {
                return false;
            }

            var dbConn = db.Database.GetDbConnection();
            // Npgsql loại bỏ Password khỏi ConnectionString của kết nối đã mở (Persist Security Info=false),
            // nên phải lấy chuỗi đầy đủ từ DbContext options, nếu không sẽ lỗi "No password has been provided".
            var connStr = db.Database.GetConnectionString() ?? dbConn.ConnectionString;
            if (string.IsNullOrWhiteSpace(connStr))
            {
                return false;
            }

            var identityConnStr = System.Text.RegularExpressions.Regex.Replace(
                connStr,
                @"Database=[^;]+",
                "Database=identity_db",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase);

            if (Activator.CreateInstance(dbConn.GetType()) is not System.Data.Common.DbConnection conn)
            {
                return false;
            }

            await using (conn)
            {
                conn.ConnectionString = identityConnStr;
                await conn.OpenAsync(cancellationToken);

                await using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT \"BankCode\", \"BankAccountNumber\", \"AccountHolderName\" FROM user_bank_accounts WHERE \"UserId\" = @userId ORDER BY \"IsDefault\" DESC, \"CreatedAt\" DESC LIMIT 1;";
                var p = cmd.CreateParameter();
                p.ParameterName = "@userId";
                p.DbType = System.Data.DbType.Guid;
                p.Value = user.Id;
                cmd.Parameters.Add(p);

                await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    user.PayoutBankCode = reader.GetString(0);
                    user.PayoutAccountNumber = reader.GetString(1);
                    user.PayoutAccountName = reader.GetString(2);
                    user.UpdatedAt = DateTimeOffset.UtcNow;
                    return true;
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[IdentityBankSyncHelper] Error syncing bank for {user.Id}: {ex.Message}");
        }

        return false;
    }
}

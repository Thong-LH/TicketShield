using System.Collections.Concurrent;
using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes.Commands.CreateDispute;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services;
using Xunit;

namespace TicketShield.UnitTests.QualityAssurance.MF03;

/// <summary>
/// TEST-5.5.2: High-Concurrency Race Condition Integration Test
/// Description: Giả lập 50 luồng đồng thời tranh chấp tại cùng 1 mili-giây giữa Dispute Ticket và Release Escrow,
/// xác minh tính bất biến CAS. [Phụ thuộc: sau BE-SETTLE-5.3.4 | Mở khóa cho: TEST-5.5.3]
///
/// Các mục tiêu kiểm thử cốt lõi:
/// 1. 50 luồng kích hoạt đồng thời tại cùng 1 mili-giây (đồng bộ hóa bằng TaskCompletionSource / Barrier).
/// 2. Tranh chấp giữa Người mua mở khiếu nại (CreateDisputeCommand) và Hệ thống giải ngân tự động (EscrowSettlementCas).
/// 3. Xác minh tính bất biến CAS (Compare-And-Swap Invariance):
///    - Đúng duy nhất 1 phía giành chiến thắng (CAS Winner: 1, CAS Losers: 49).
///    - Tuyệt đối không xảy ra tình trạng phân mảnh trạng thái (Split-Brain) hay giải ngân trùng (Double Payout).
/// 4. Stress Test bão khiếu nại (50 Concurrent Disputes) và bão giải ngân (50 Concurrent Releases).
/// 5. Kiểm thử song song đa Escrow (50 luồng trên 10 Escrow độc lập) đạt 100% ACID và 0 deadlock.
/// </summary>
public class HighConcurrencyRaceConditionIntegrationTests
{
    private const int HighConcurrencyDegree = 50;

    #region Fixture & Database Helpers

    private static string CreateSqliteConnectionString()
    {
        var dbPath = Path.Combine(Path.GetTempPath(), $"ts-cas-race-50-{Guid.NewGuid():N}.db");
        return new SqliteConnectionStringBuilder
        {
            DataSource = dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 60, // 60 seconds busy timeout
            Pooling = false
        }.ConnectionString;
    }

    private static TicketShieldDbContext OpenDb(string connectionString)
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(connectionString)
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task InitializeDatabaseAsync(string connectionString)
    {
        await using var db = OpenDb(connectionString);
        await db.Database.EnsureCreatedAsync();

        // Kích hoạt WAL (Write-Ahead Logging) và thiết lập busy_timeout 60 giây để chịu tải 50 luồng song song
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode = WAL; PRAGMA busy_timeout = 60000; PRAGMA synchronous = NORMAL;");
    }

    private static void CleanupDatabase(string connectionString)
    {
        try
        {
            var dbPath = new SqliteConnectionStringBuilder(connectionString).DataSource;
            if (File.Exists(dbPath)) File.Delete(dbPath);
            var shm = $"{dbPath}-shm";
            if (File.Exists(shm)) File.Delete(shm);
            var wal = $"{dbPath}-wal";
            if (File.Exists(wal)) File.Delete(wal);
        }
        catch
        {
            // Ignore file lock during temp cleanup
        }
    }

    private static async Task<(Guid EscrowId, Guid BuyerId, Guid SellerId)> SeedLockedEscrowAsync(
        string connectionString, decimal price = 1_000_000m, decimal sellerFee = 30_000m)
    {
        await using var db = OpenDb(connectionString);
        var suffix = Guid.NewGuid().ToString("N")[..8];

        var organizer = new Organizer
        {
            Id = Guid.NewGuid(),
            Name = "BTC Sự Kiện",
            OfficialEmail = $"btc-{suffix}@test.local"
        };

        var seller = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"seller-{suffix}@test.local",
            FullName = "Nguyen Van Ban",
            PayoutBankCode = "MB",
            PayoutAccountNumber = "0938434102",
            PayoutAccountName = "NGUYEN VAN BAN"
        };

        var buyer = new ShadowUser
        {
            Id = Guid.NewGuid(),
            Email = $"buyer-{suffix}@test.local",
            FullName = "Tran Thi Mua",
            PayoutBankCode = "VCB",
            PayoutAccountNumber = "0938434103",
            PayoutAccountName = "TRAN THI MUA"
        };

        var ev = new Event
        {
            Id = Guid.NewGuid(),
            OrganizerId = organizer.Id,
            Name = "Đêm Nhạc Đỉnh Cao 2026",
            Venue = "Sân Vận Động Mỹ Đình",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(3),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(6)
        };

        var tier = new TicketTier
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierName = "VIP 1",
            OriginalPrice = price
        };

        var listing = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = ev.Id,
            TierId = tier.Id,
            SellerId = seller.Id,
            OriginalTicketCode = $"TICK-{suffix}",
            OriginalPrice = price,
            ResalePrice = price,
            ListingStatus = ListingStatus.Sold
        };

        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = listing.Id,
            BuyerId = buyer.Id,
            SellerId = seller.Id,
            OriginalTicketPrice = price,
            BuyerFee = Math.Round(price * 0.05m, 0),
            SellerFee = sellerFee,
            TotalBuyerPaid = price + Math.Round(price * 0.05m, 0),
            NetSellerPayout = price - sellerFee,
            Status = EscrowStatus.Locked,
            InSettlementBuffer = true,
            UnlockAt = DateTimeOffset.UtcNow.AddMinutes(-5) // Đã quá hạn 24h, đủ điều kiện kích hoạt giải ngân
        };

        db.Organizers.Add(organizer);
        db.ShadowUsers.AddRange(seller, buyer);
        db.Events.Add(ev);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        return (escrow.Id, buyer.Id, seller.Id);
    }

    private sealed class StubCurrentUserService(Guid userId) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "buyer@test.local";
        public string? Role => "User";
        public bool IsAuthenticated => true;
    }

    private sealed record EscrowSnapshotState(
        EscrowStatus Status,
        bool InSettlementBuffer,
        int DisputeCount,
        int PayoutCount,
        int OutboxPayoutEventCount);

    private static async Task<EscrowSnapshotState> ReadEscrowStateAsync(string connectionString, Guid escrowId)
    {
        await using var db = OpenDb(connectionString);
        var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == escrowId);
        var disputeCount = await db.Disputes.AsNoTracking().CountAsync(d => d.EscrowId == escrowId);
        var payoutCount = await db.PayoutTransactions.AsNoTracking().CountAsync(p => p.EscrowId == escrowId);
        var outboxPayoutEvents = await db.OutboxMessages.AsNoTracking()
            .CountAsync(m => m.EventType == nameof(PayoutRequestedEvent) && m.Payload.Contains(escrowId.ToString()));

        return new EscrowSnapshotState(
            escrow.Status,
            escrow.InSettlementBuffer,
            disputeCount,
            payoutCount,
            outboxPayoutEvents);
    }

    #endregion

    #region Core Test Cases: High-Concurrency CAS Race Scenarios

    [Fact]
    public async Task Concurrency_FiftyThreadsContendSameMillisecond_BetweenDisputeAndRelease_ExactlyOneSideWinsCAS()
    {
        // Arrange
        var connStr = CreateSqliteConnectionString();
        try
        {
            await InitializeDatabaseAsync(connStr);
            var (escrowId, buyerId, _) = await SeedLockedEscrowAsync(connStr);

            var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);

            var disputeSuccessCount = 0;
            var disputeFailureCount = 0;
            var releaseSuccessCount = 0;
            var releaseFailureCount = 0;

            var tasks = new List<Task>();

            // Phân bổ 50 luồng: 25 luồng gửi Dispute, 25 luồng kích hoạt Release
            for (var i = 0; i < HighConcurrencyDegree; i++)
            {
                var isDisputeThread = (i % 2 == 0);

                if (isDisputeThread)
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await startBarrier.Task; // Chờ tín hiệu đồng loạt tại cùng 1 mili-giây
                        try
                        {
                            await using var db = OpenDb(connStr);
                            var handler = new CreateDisputeCommandHandler(db, new StubCurrentUserService(buyerId));
                            var res = await handler.Handle(
                                new CreateDisputeCommand
                                {
                                    EscrowId = escrowId,
                                    Reason = "Vé giả mạo không vào được cổng",
                                    ReasonCode = DisputeReasonCode.FakeTicket
                                }, CancellationToken.None);

                            if (res.Success)
                            {
                                Interlocked.Increment(ref disputeSuccessCount);
                            }
                        }
                        catch (DisputeDeadlineExpiredException)
                        {
                            Interlocked.Increment(ref disputeFailureCount);
                        }
                        catch (Exception)
                        {
                            Interlocked.Increment(ref disputeFailureCount);
                        }
                    }));
                }
                else
                {
                    tasks.Add(Task.Run(async () =>
                    {
                        await startBarrier.Task; // Chờ tín hiệu đồng loạt tại cùng 1 mili-giây
                        try
                        {
                            await using var db = OpenDb(connStr);
                            var cas = new EscrowSettlementCas(db);
                            var won = await cas.TryBeginReleaseAsync(escrowId, CancellationToken.None);

                            if (won)
                            {
                                Interlocked.Increment(ref releaseSuccessCount);
                            }
                            else
                            {
                                Interlocked.Increment(ref releaseFailureCount);
                            }
                        }
                        catch (Exception)
                        {
                            Interlocked.Increment(ref releaseFailureCount);
                        }
                    }));
                }
            }

            // Act - Bắn đồng loạt 50 luồng tại cùng 1 mili-giây
            startBarrier.SetResult(true);
            await Task.WhenAll(tasks);

            // Assert - Kiểm tra tính bất biến CAS: Tổng số bên thắng TUYỆT ĐỐI BẰNG 1
            var totalWins = disputeSuccessCount + releaseSuccessCount;
            var totalLosses = disputeFailureCount + releaseFailureCount;

            Assert.Equal(1, totalWins);
            Assert.Equal(HighConcurrencyDegree - 1, totalLosses);

            // Kiểm tra trạng thái toàn vẹn của CSDL
            var finalState = await ReadEscrowStateAsync(connStr, escrowId);

            if (disputeSuccessCount == 1)
            {
                // Kịch bản A: Dispute giành chiến thắng
                Assert.Equal(0, releaseSuccessCount);
                Assert.Equal(EscrowStatus.Disputed, finalState.Status);
                Assert.False(finalState.InSettlementBuffer);
                Assert.Equal(1, finalState.DisputeCount);
                Assert.Equal(0, finalState.PayoutCount);
                Assert.Equal(0, finalState.OutboxPayoutEventCount);
            }
            else
            {
                // Kịch bản B: Release giành chiến thắng
                Assert.Equal(1, releaseSuccessCount);
                Assert.Equal(EscrowStatus.Releasing, finalState.Status);
                Assert.Equal(0, finalState.DisputeCount);
                Assert.Equal(1, finalState.PayoutCount);
                Assert.Equal(1, finalState.OutboxPayoutEventCount);
            }
        }
        finally
        {
            CleanupDatabase(connStr);
        }
    }

    [Fact]
    public async Task Concurrency_FiftyDisputeThreadsStorm_OnlySingleDisputeRecordCreated()
    {
        // Arrange - Mô phỏng 50 yêu cầu Dispute đồng thời gửi tới cùng 1 Escrow
        var connStr = CreateSqliteConnectionString();
        try
        {
            await InitializeDatabaseAsync(connStr);
            var (escrowId, buyerId, _) = await SeedLockedEscrowAsync(connStr);

            var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var successCount = 0;
            var failureCount = 0;

            var tasks = Enumerable.Range(0, HighConcurrencyDegree).Select(_ => Task.Run(async () =>
            {
                await startBarrier.Task;
                try
                {
                    await using var db = OpenDb(connStr);
                    var handler = new CreateDisputeCommandHandler(db, new StubCurrentUserService(buyerId));
                    var res = await handler.Handle(
                        new CreateDisputeCommand
                        {
                            EscrowId = escrowId,
                            Reason = "Trùng lặp yêu cầu báo cáo sự cố",
                            ReasonCode = DisputeReasonCode.DuplicateEntry
                        }, CancellationToken.None);

                    if (res.Success)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                }
                catch (DisputeDeadlineExpiredException)
                {
                    Interlocked.Increment(ref failureCount);
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref failureCount);
                }
            })).ToList();

            // Act
            startBarrier.SetResult(true);
            await Task.WhenAll(tasks);

            // Assert - Đúng duy nhất 1 Dispute thành công, 49 yêu cầu còn lại bị chặn
            Assert.Equal(1, successCount);
            Assert.Equal(HighConcurrencyDegree - 1, failureCount);

            var state = await ReadEscrowStateAsync(connStr, escrowId);
            Assert.Equal(EscrowStatus.Disputed, state.Status);
            Assert.Equal(1, state.DisputeCount);
            Assert.Equal(0, state.PayoutCount);
        }
        finally
        {
            CleanupDatabase(connStr);
        }
    }

    [Fact]
    public async Task Concurrency_FiftyReleaseThreadsStorm_OnlySinglePayoutTriggered()
    {
        // Arrange - Mô phỏng 50 Worker / Luồng đồng thời quét và gọi TryBeginReleaseAsync trên cùng 1 Escrow
        var connStr = CreateSqliteConnectionString();
        try
        {
            await InitializeDatabaseAsync(connStr);
            var (escrowId, _, _) = await SeedLockedEscrowAsync(connStr);

            var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var wonCount = 0;
            var lostCount = 0;

            var tasks = Enumerable.Range(0, HighConcurrencyDegree).Select(_ => Task.Run(async () =>
            {
                await startBarrier.Task;
                try
                {
                    await using var db = OpenDb(connStr);
                    var cas = new EscrowSettlementCas(db);
                    var won = await cas.TryBeginReleaseAsync(escrowId, CancellationToken.None);

                    if (won)
                    {
                        Interlocked.Increment(ref wonCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref lostCount);
                    }
                }
                catch (Exception)
                {
                    Interlocked.Increment(ref lostCount);
                }
            })).ToList();

            // Act
            startBarrier.SetResult(true);
            await Task.WhenAll(tasks);

            // Assert - Chỉ có đúng 1 lệnh giải ngân thắng CAS
            Assert.Equal(1, wonCount);
            Assert.Equal(HighConcurrencyDegree - 1, lostCount);

            var state = await ReadEscrowStateAsync(connStr, escrowId);
            Assert.Equal(EscrowStatus.Releasing, state.Status);
            Assert.Equal(1, state.PayoutCount);
            Assert.Equal(1, state.OutboxPayoutEventCount);
            Assert.Equal(0, state.DisputeCount);
        }
        finally
        {
            CleanupDatabase(connStr);
        }
    }

    [Fact]
    public async Task Concurrency_FiftyThreadsAcrossTenDistinctEscrows_ResolvesAllWithZeroDeadlocks()
    {
        // Arrange - 10 Escrow độc lập, mỗi Escrow có 5 luồng tranh chấp đồng thời (Tổng cộng 50 luồng)
        var connStr = CreateSqliteConnectionString();
        try
        {
            await InitializeDatabaseAsync(connStr);
            const int escrowBatchCount = 10;
            const int threadsPerEscrow = 5;

            var escrowList = new List<(Guid EscrowId, Guid BuyerId)>();
            for (var i = 0; i < escrowBatchCount; i++)
            {
                var seeded = await SeedLockedEscrowAsync(connStr, 500_000m + (i * 100_000m));
                escrowList.Add((seeded.EscrowId, seeded.BuyerId));
            }

            var startBarrier = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var allTasks = new List<Task>();
            var resultsPerEscrow = new ConcurrentDictionary<Guid, ConcurrentBag<string>>();

            foreach (var (escrowId, buyerId) in escrowList)
            {
                resultsPerEscrow[escrowId] = new ConcurrentBag<string>();

                for (var t = 0; t < threadsPerEscrow; t++)
                {
                    var isDispute = (t % 2 == 0);
                    allTasks.Add(Task.Run(async () =>
                    {
                        await startBarrier.Task;
                        await using var db = OpenDb(connStr);

                        if (isDispute)
                        {
                            try
                            {
                                var handler = new CreateDisputeCommandHandler(db, new StubCurrentUserService(buyerId));
                                var res = await handler.Handle(
                                    new CreateDisputeCommand
                                    {
                                        EscrowId = escrowId,
                                        Reason = "Vé không sử dụng được tại cổng sự kiện",
                                        ReasonCode = DisputeReasonCode.TicketInvalid
                                    }, CancellationToken.None);

                                if (res.Success)
                                    resultsPerEscrow[escrowId].Add("DisputeWon");
                            }
                            catch
                            {
                                resultsPerEscrow[escrowId].Add("DisputeLost");
                            }
                        }
                        else
                        {
                            try
                            {
                                var cas = new EscrowSettlementCas(db);
                                var won = await cas.TryBeginReleaseAsync(escrowId, CancellationToken.None);
                                resultsPerEscrow[escrowId].Add(won ? "ReleaseWon" : "ReleaseLost");
                            }
                            catch
                            {
                                resultsPerEscrow[escrowId].Add("ReleaseLost");
                            }
                        }
                    }));
                }
            }

            // Act - Bắn đồng loạt toàn bộ 50 luồng
            startBarrier.SetResult(true);
            await Task.WhenAll(allTasks);

            // Assert - Từng Escrow trong 10 Escrow đều phải có DUY NHẤT 1 bên thắng
            Assert.Equal(HighConcurrencyDegree, allTasks.Count);

            foreach (var (escrowId, _) in escrowList)
            {
                var records = resultsPerEscrow[escrowId].ToList();
                var wins = records.Count(r => r is "DisputeWon" or "ReleaseWon");
                Assert.Equal(1, wins);

                var state = await ReadEscrowStateAsync(connStr, escrowId);
                Assert.True(state.Status is EscrowStatus.Disputed or EscrowStatus.Releasing);
                Assert.Equal(1, state.DisputeCount + state.PayoutCount);
            }
        }
        finally
        {
            CleanupDatabase(connStr);
        }
    }

    [Fact]
    public async Task Concurrency_LateComerRejection_StrictInvarianceAfterTransition()
    {
        // Arrange - Kiểm tra tính bất biến sau khi một bên đã thắng CAS
        var connStr = CreateSqliteConnectionString();
        try
        {
            await InitializeDatabaseAsync(connStr);
            var (escrowId, buyerId, _) = await SeedLockedEscrowAsync(connStr);

            // Bước 1: Cho Dispute thắng trước
            await using (var db = OpenDb(connStr))
            {
                var handler = new CreateDisputeCommandHandler(db, new StubCurrentUserService(buyerId));
                await handler.Handle(new CreateDisputeCommand
                {
                    EscrowId = escrowId,
                    Reason = "Vé bị quét trùng",
                    ReasonCode = DisputeReasonCode.DuplicateEntry
                }, CancellationToken.None);
            }

            // Bước 2: 10 luồng kích hoạt Release đến muộn (Late Comers)
            var releaseTasks = Enumerable.Range(0, 10).Select(_ => Task.Run(async () =>
            {
                await using var db = OpenDb(connStr);
                var cas = new EscrowSettlementCas(db);
                return await cas.TryBeginReleaseAsync(escrowId, CancellationToken.None);
            })).ToList();

            var releaseResults = await Task.WhenAll(releaseTasks);

            // Assert - Cả 10 luồng giải ngân đến muộn ĐỀU BỊ TỪ CHỐI 100%
            Assert.All(releaseResults, won => Assert.False(won));

            var state = await ReadEscrowStateAsync(connStr, escrowId);
            Assert.Equal(EscrowStatus.Disputed, state.Status);
            Assert.Equal(1, state.DisputeCount);
            Assert.Equal(0, state.PayoutCount);
            Assert.Equal(0, state.OutboxPayoutEventCount);
        }
        finally
        {
            CleanupDatabase(connStr);
        }
    }

    #endregion
}

using System.Text.Json;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Disputes;
using TicketShield.Application.Features.Disputes.Commands.CreateDispute;
using TicketShield.Application.Features.Disputes.Commands.ResolveDispute;
using TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Workers;

namespace TicketShield.UnitTests.Features.Disputes;

/// <summary>
/// TEST-6.3.5: Integration Tests for Automated Gate Log Harvesting and Dispute Resolution Scenarios.
/// Kiểm thử tích hợp toàn trình:
/// - Kịch bản 1: Gian lận vào cổng rồi khiếu nại (bác bỏ & giải ngân seller).
/// - Kịch bản 2: Máy quét từ chối vé tại cổng (hoàn tiền thành công cho buyer).
/// - Kịch bản 3: Vé bị thu hồi (REVOKED) tại cổng (hoàn tiền thành công).
/// - Kịch bản 4: Vé bị quét trước thời điểm chuyển giao (Pre-transfer barcode leak/reuse -> hoàn tiền).
/// - Kịch bản 5: Mua combo nhiều vé (Multi-ticket bundle) với cả 2 vé hợp lệ hoặc 1 vé lỗi.
/// - Kịch bản 6: Tính bất biến và xử lý lỗi khi người bán chưa liên kết ngân hàng.
/// </summary>
public class AutomatedGateLogHarvestingAndDisputeResolutionIntegrationTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<TicketShieldDbContext> _dbOptions;

    public AutomatedGateLogHarvestingAndDisputeResolutionIntegrationTests()
    {
        _connection = new SqliteConnection("Data Source=:memory:");
        _connection.Open();
        _dbOptions = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var db = new TicketShieldDbContext(_dbOptions);
        db.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
        GC.SuppressFinalize(this);
    }

    private TicketShieldDbContext CreateDbContext() => new(_dbOptions);

    #region Scenario 1: Fraudulent Entry Followed by Dispute -> Rejection & Payout

    [Fact]
    public async Task Scenario1_FraudulentEntry_ThenDisputeFiled_AutomatedHarvestRecommendsRejectFraud_AndStaffRejectionReleasesSellerPayout()
    {
        // 1. SETUP: Giao dịch mua vé chuyển nhượng đã hoàn tất, Escrow ở trạng thái Locked
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-2);
        const string reissuedTicketCode = "TCK-REISSUE-FRAUD-001";
        const decimal totalPaid = 750_000m;
        const decimal sellerPayout = 650_000m;
        const string sellerBankAcc = "0987654321";

        var seed = await SeedCompleteTransactionAsync(
            reissuedTicketCode,
            transferredAt,
            totalPaid,
            sellerPayout,
            sellerBankAcc);

        // 2. GATE ENTRY: Người mua (hoặc người cầm vé) đã quét cổng THÀNH CÔNG sau thời điểm nhận vé (transferredAt + 35m)
        var gateClient = new ConfigurableGateAccessLogClient();
        gateClient.RegisterScans(reissuedTicketCode, new GateScan
        {
            TicketCode = reissuedTicketCode,
            ScannedAt = transferredAt.AddMinutes(35),
            ScanResult = "SUCCESS",
            GateName = "Cổng Tây VIP",
            ScannerDeviceId = "GATE-TURNSTILE-01"
        });

        // 3. FRAUDULENT DISPUTE: Người mua dù đã vào cổng xem sự kiện vẫn cố tình gửi khiếu nại gian lận
        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            var createDisputeHandler = new CreateDisputeCommandHandler(db, buyerUser);

            var createResult = await createDisputeHandler.Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Tôi không vào được cổng vì vé báo không hợp lệ",
                ReasonCode = DisputeReasonCode.TicketInvalid
            }, CancellationToken.None);

            Assert.True(createResult.Success);
            Assert.NotNull(createResult.Data);
            Assert.Equal(seed.DisputeIdPlaceholder, seed.DisputeIdPlaceholder); // verified later via DB

            // Kiểm tra Escrow đã bị đóng băng sang Disputed và OutboxMessage đã sinh ra
            var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == seed.EscrowId);
            Assert.Equal(EscrowStatus.Disputed, escrow.Status);

            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            Assert.Equal(DisputeStatus.Open, dispute.Status);
            Assert.StartsWith("DP-", dispute.DisputeCode);

            var harvestMessage = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.EventType == nameof(DisputeHarvestRequested));
            Assert.Null(harvestMessage.ProcessedAt);
            var payload = JsonSerializer.Deserialize<DisputeHarvestRequested>(harvestMessage.Payload);
            Assert.Equal(dispute.Id, payload!.DisputeId);
        }

        // 4. AUTOMATED HARVESTING: Background worker thức dậy và tự động thu thập nhật ký cổng từ đối tác
        var services = BuildServiceProvider(gateClient);
        var harvestWorker = services.GetRequiredService<DisputeHarvestWorker>();

        var processedCount = await harvestWorker.HarvestPendingAsync(CancellationToken.None);
        Assert.Equal(1, processedCount);

        Guid createdDisputeId;
        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            createdDisputeId = dispute.Id;

            // Worker phải cập nhật Recommendation = RECOMMEND_REJECT_FRAUD
            Assert.Equal(DisputeRecommendation.RecommendRejectFraud, dispute.Recommendation);
            Assert.NotNull(dispute.HarvestedAt);
            Assert.NotNull(dispute.GateLogSnapshot);
            Assert.Contains("SUCCESS", dispute.GateLogSnapshot);
            Assert.Contains("GATE-TURNSTILE-01", dispute.GateLogSnapshot);

            // Outbox message phải được đánh dấu đã xử lý
            var message = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.EventType == nameof(DisputeHarvestRequested));
            Assert.NotNull(message.ProcessedAt);
        }

        // 5. STAFF REVIEW: Nhân viên CSKH/Admin tra cứu hồ sơ chứng cứ và khuyến nghị
        await using (var db = CreateDbContext())
        {
            var staffUser = new StubCurrentUser(seed.StaffId, "Admin");
            var queryHandler = new GetDisputeRecommendationQueryHandler(db, staffUser);

            var dossier = await queryHandler.Handle(new GetDisputeRecommendationQuery
            {
                DisputeId = createdDisputeId
            }, CancellationToken.None);

            Assert.True(dossier.Success);
            Assert.Equal(DisputeRecommendation.RecommendRejectFraud, dossier.Data!.Recommendation);
            Assert.Equal(transferredAt, dossier.Data.TransferredAt);
            var scan = Assert.Single(dossier.Data.Scans);
            Assert.Equal(reissuedTicketCode, scan.TicketCode);
            Assert.Equal("SUCCESS", scan.ScanResult);
            Assert.Equal("Cổng Tây VIP", scan.GateName);
        }

        // 6. DISPUTE RESOLUTION: Nhân viên CSKH/Admin ra phán quyết Bác bỏ khiếu nại gian lận
        await using (var db = CreateDbContext())
        {
            var staffUser = new StubCurrentUser(seed.StaffId, "Admin");
            var resolveHandler = new ResolveDisputeCommandHandler(db, staffUser);

            var resolutionResult = await resolveHandler.Handle(new ResolveDisputeCommand
            {
                DisputeId = createdDisputeId,
                Decision = DisputeDecision.Reject,
                Reason = "Bác bỏ khiếu nại: Nhật ký kiểm soát cổng xác nhận vé đã quét SUCCESS vào lúc 19:35 tại Cổng Tây."
            }, CancellationToken.None);

            Assert.True(resolutionResult.Success);
            Assert.Equal(nameof(DisputeStatus.Rejected), resolutionResult.Data!.DisputeStatus);
            Assert.Equal(nameof(EscrowStatus.Releasing), resolutionResult.Data.EscrowStatus);

            // Kiểm tra trạng thái toàn trình sau phán quyết
            var finalDispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.Id == createdDisputeId);
            Assert.Equal(DisputeStatus.Rejected, finalDispute.Status);
            Assert.Equal(DisputeResolution.ReleaseSeller, finalDispute.Resolution);
            Assert.Equal(0, finalDispute.RefundAmount);
            Assert.Equal(seed.StaffId, finalDispute.ResolvedBy);
            Assert.NotNull(finalDispute.ResolvedAt);
            Assert.Contains("Nhật ký kiểm soát cổng", finalDispute.AdminNotes);

            var finalEscrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == seed.EscrowId);
            Assert.Equal(EscrowStatus.Releasing, finalEscrow.Status);

            // PayoutTransaction phải được tạo ở trạng thái Processing với đúng số tiền giải ngân cho người bán
            var payout = await db.PayoutTransactions.AsNoTracking().SingleAsync(p => p.EscrowId == seed.EscrowId);
            Assert.Equal(PayoutStatus.Processing, payout.Status);
            Assert.Equal(seed.SellerId, payout.SellerId);
            Assert.Equal(sellerPayout, payout.Amount);

            // OutboxMessage PayoutRequestedEvent phải được sinh ra với thông tin tài khoản người bán
            var payoutEventMessage = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.EventType == nameof(PayoutRequestedEvent));
            var payoutPayload = JsonSerializer.Deserialize<PayoutRequestedEvent>(payoutEventMessage.Payload);
            Assert.NotNull(payoutPayload);
            Assert.Equal(sellerPayout, payoutPayload.Amount);
            Assert.Equal(sellerBankAcc, payoutPayload.AccountNumber);
            Assert.Equal("VCB", payoutPayload.BankCode);
            Assert.Equal(PayoutIdempotency.Key(seed.EscrowId, 0), payoutPayload.IdempotencyKey);
        }
    }

    #endregion

    #region Scenario 2: Scanner Denied -> Dispute Approved & Buyer Fully Refunded

    [Fact]
    public async Task Scenario2_ScannerDenied_ThenDisputeFiled_AutomatedHarvestRecommendsRefund_AndStaffApprovalRefundsBuyerFully()
    {
        // 1. SETUP: Giao dịch mua vé chuyển nhượng đã hoàn tất, Escrow ở trạng thái Locked
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-3);
        const string reissuedTicketCode = "TCK-REISSUE-DENIED-002";
        const decimal totalPaid = 1_200_000m;
        const decimal sellerPayout = 1_050_000m;
        const string sellerBankAcc = "0112233445";

        var seed = await SeedCompleteTransactionAsync(
            reissuedTicketCode,
            transferredAt,
            totalPaid,
            sellerPayout,
            sellerBankAcc);

        // 2. GATE SCAN DENIAL: Người mua đến sự kiện nhưng máy quét tại cổng TỪ CHỐI vé (INVALID_TICKET)
        var gateClient = new ConfigurableGateAccessLogClient();
        gateClient.RegisterScans(reissuedTicketCode, new GateScan
        {
            TicketCode = reissuedTicketCode,
            ScannedAt = transferredAt.AddMinutes(40),
            ScanResult = "INVALID_TICKET",
            GateName = "Cổng Đông 02",
            ScannerDeviceId = "TURNSTILE-EAST-02"
        });

        // 3. DISPUTE FILED: Người mua gửi khiếu nại chính đáng kèm lý do máy quét từ chối
        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            var createDisputeHandler = new CreateDisputeCommandHandler(db, buyerUser);

            var createResult = await createDisputeHandler.Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Máy quét tại Cổng Đông báo mã vé không hợp lệ, nhân viên không cho vào cửa.",
                ReasonCode = DisputeReasonCode.TicketInvalid
            }, CancellationToken.None);

            Assert.True(createResult.Success);
            Assert.NotNull(createResult.Data);

            var escrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == seed.EscrowId);
            Assert.Equal(EscrowStatus.Disputed, escrow.Status);

            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            Assert.Equal(DisputeStatus.Open, dispute.Status);
        }

        // 4. AUTOMATED HARVESTING: Worker tự động thu thập log và phân tích khuyến nghị
        var services = BuildServiceProvider(gateClient);
        var harvestWorker = services.GetRequiredService<DisputeHarvestWorker>();

        var processedCount = await harvestWorker.HarvestPendingAsync(CancellationToken.None);
        Assert.Equal(1, processedCount);

        Guid createdDisputeId;
        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            createdDisputeId = dispute.Id;

            // Worker phải cập nhật Recommendation = RECOMMEND_REFUND
            Assert.Equal(DisputeRecommendation.RecommendRefund, dispute.Recommendation);
            Assert.NotNull(dispute.HarvestedAt);
            Assert.NotNull(dispute.GateLogSnapshot);
            Assert.Contains("INVALID_TICKET", dispute.GateLogSnapshot);
            Assert.Contains("TURNSTILE-EAST-02", dispute.GateLogSnapshot);

            var message = await db.OutboxMessages.AsNoTracking()
                .SingleAsync(m => m.EventType == nameof(DisputeHarvestRequested));
            Assert.NotNull(message.ProcessedAt);
        }

        // 5. STAFF REVIEW: Nhân viên CSKH/Moderator xem xét dossier
        await using (var db = CreateDbContext())
        {
            var staffUser = new StubCurrentUser(seed.StaffId, "Moderator");
            var queryHandler = new GetDisputeRecommendationQueryHandler(db, staffUser);

            var dossier = await queryHandler.Handle(new GetDisputeRecommendationQuery
            {
                DisputeId = createdDisputeId
            }, CancellationToken.None);

            Assert.True(dossier.Success);
            Assert.Equal(DisputeRecommendation.RecommendRefund, dossier.Data!.Recommendation);
            var scan = Assert.Single(dossier.Data.Scans);
            Assert.Equal(reissuedTicketCode, scan.TicketCode);
            Assert.Equal("INVALID_TICKET", scan.ScanResult);
        }

        // 6. DISPUTE RESOLUTION: Nhân viên chấp thuận khiếu nại và hoàn tiền 100% cho người mua
        await using (var db = CreateDbContext())
        {
            var staffUser = new StubCurrentUser(seed.StaffId, "Moderator");
            var resolveHandler = new ResolveDisputeCommandHandler(db, staffUser);

            var resolutionResult = await resolveHandler.Handle(new ResolveDisputeCommand
            {
                DisputeId = createdDisputeId,
                Decision = DisputeDecision.Approve,
                Reason = "Chấp thuận khiếu nại: Bằng chứng máy quét xác nhận lỗi INVALID_TICKET. Hoàn trả toàn bộ tiền mua cho khách."
            }, CancellationToken.None);

            Assert.True(resolutionResult.Success);
            Assert.Equal(nameof(DisputeStatus.Resolved), resolutionResult.Data!.DisputeStatus);
            Assert.Equal(nameof(EscrowStatus.Refunded), resolutionResult.Data.EscrowStatus);

            // Kiểm tra trạng thái toàn trình sau phán quyết hoàn tiền
            var finalDispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.Id == createdDisputeId);
            Assert.Equal(DisputeStatus.Resolved, finalDispute.Status);
            Assert.Equal(DisputeResolution.RefundBuyer, finalDispute.Resolution);
            Assert.Equal(totalPaid, finalDispute.RefundAmount); // 100% hoàn tiền
            Assert.Equal(seed.StaffId, finalDispute.ResolvedBy);
            Assert.NotNull(finalDispute.ResolvedAt);

            var finalEscrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == seed.EscrowId);
            Assert.Equal(EscrowStatus.Refunded, finalEscrow.Status);

            // TUYỆT ĐỐI KHÔNG sinh PayoutTransaction hay PayoutRequestedEvent cho người bán
            Assert.Empty(await db.PayoutTransactions.AsNoTracking().ToListAsync());
            var outboxEvents = await db.OutboxMessages.AsNoTracking().ToListAsync();
            Assert.DoesNotContain(outboxEvents, m => m.EventType == nameof(PayoutRequestedEvent));
        }
    }

    #endregion

    #region Scenario 3: Revoked Ticket at Gate -> Dispute Approved & Refunded

    [Fact]
    public async Task Scenario3_RevokedTicket_AtGate_RecommendsRefund_AndFullRefundGranted()
    {
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-1);
        const string reissuedTicketCode = "TCK-REISSUE-REVOKED-003";
        const decimal totalPaid = 500_000m;

        var seed = await SeedCompleteTransactionAsync(
            reissuedTicketCode,
            transferredAt,
            totalPaid,
            425_000m,
            "0999888777");

        var gateClient = new ConfigurableGateAccessLogClient();
        gateClient.RegisterScans(reissuedTicketCode, new GateScan
        {
            TicketCode = reissuedTicketCode,
            ScannedAt = transferredAt.AddMinutes(15),
            ScanResult = "REVOKED",
            GateName = "Cổng Khách Mời",
            ScannerDeviceId = "DEV-VIP-01"
        });

        // Buyer khiếu nại
        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            var handler = new CreateDisputeCommandHandler(db, buyerUser);
            await handler.Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Vé bị ban tổ chức thông báo đã thu hồi",
                ReasonCode = DisputeReasonCode.TicketInvalid
            }, CancellationToken.None);
        }

        // Worker harvest
        var services = BuildServiceProvider(gateClient);
        await services.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync(CancellationToken.None);

        // Verify recommendation is RECOMMEND_REFUND
        Guid disputeId;
        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            disputeId = dispute.Id;
            Assert.Equal(DisputeRecommendation.RecommendRefund, dispute.Recommendation);
            Assert.Contains("REVOKED", dispute.GateLogSnapshot);
        }

        // Staff phán quyết hoàn tiền
        await using (var db = CreateDbContext())
        {
            var staffUser = new StubCurrentUser(seed.StaffId, "Cskh");
            var handler = new ResolveDisputeCommandHandler(db, staffUser);
            var res = await handler.Handle(new ResolveDisputeCommand
            {
                DisputeId = disputeId,
                Decision = DisputeDecision.Approve,
                Reason = "Vé bị thu hồi từ ban tổ chức, hoàn tiền cho người mua"
            }, CancellationToken.None);

            Assert.Equal(nameof(DisputeStatus.Resolved), res.Data!.DisputeStatus);
            Assert.Equal(nameof(EscrowStatus.Refunded), res.Data.EscrowStatus);
        }
    }

    #endregion

    #region Scenario 4: Ticket Scanned BEFORE Transfer Time (Barcode leak/reuse by seller)

    [Fact]
    public async Task Scenario4_TicketScannedBeforeTransfer_RecommendsRefund_AndFullRefundGranted()
    {
        // Khi vé đã bị quét SUCCESS nhưng TRƯỚC thời điểm người mua nhận vé
        // (nghĩa là người bán hoặc ai đó đã dùng vé trước khi bán lại cho buyer)
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-1);
        const string reissuedTicketCode = "TCK-REISSUE-USED-BEFORE-TRANSFER";
        const decimal totalPaid = 800_000m;

        var seed = await SeedCompleteTransactionAsync(
            reissuedTicketCode,
            transferredAt,
            totalPaid,
            700_000m,
            "0333222111");

        var gateClient = new ConfigurableGateAccessLogClient();
        gateClient.RegisterScans(reissuedTicketCode, new GateScan
        {
            TicketCode = reissuedTicketCode,
            // Quét 30 phút TRƯỚC khi vé được chuyển giao
            ScannedAt = transferredAt.AddMinutes(-30),
            ScanResult = "SUCCESS",
            GateName = "Cổng Trưởng Ban",
            ScannerDeviceId = "DEV-MAIN-01"
        });

        // Buyer khiếu nại
        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            await new CreateDisputeCommandHandler(db, buyerUser).Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Vé đã bị sử dụng từ trước khi tôi mua",
                ReasonCode = DisputeReasonCode.TicketInvalid
            }, CancellationToken.None);
        }

        // Worker harvest
        var services = BuildServiceProvider(gateClient);
        await services.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync(CancellationToken.None);

        // Phải phát hiện scan trước thời điểm chuyển giao và khuyến nghị HOÀN TIỀN
        Guid disputeId;
        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            disputeId = dispute.Id;
            Assert.Equal(DisputeRecommendation.RecommendRefund, dispute.Recommendation);
        }

        // Staff duyệt hoàn tiền
        await using (var db = CreateDbContext())
        {
            var staffUser = new StubCurrentUser(seed.StaffId, "Admin");
            var result = await new ResolveDisputeCommandHandler(db, staffUser).Handle(new ResolveDisputeCommand
            {
                DisputeId = disputeId,
                Decision = DisputeDecision.Approve,
                Reason = "Vé đã quét trước thời điểm sang tên. Hoàn tiền 100% cho người mua."
            }, CancellationToken.None);

            Assert.Equal(nameof(DisputeStatus.Resolved), result.Data!.DisputeStatus);
            Assert.Equal(nameof(EscrowStatus.Refunded), result.Data.EscrowStatus);

            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.Id == disputeId);
            Assert.Equal(totalPaid, dispute.RefundAmount);
        }
    }

    #endregion

    #region Scenario 5: Multi-Ticket Bundle Scenarios

    [Fact]
    public async Task Scenario5A_MultiTicketBundle_AllTicketsEnteredSuccessfully_RecommendsRejectFraud()
    {
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-2);
        const string bundleCodes = "TCK-BUNDLE-A1,TCK-BUNDLE-A2";

        var seed = await SeedCompleteTransactionAsync(
            bundleCodes,
            transferredAt,
            2_000_000m,
            1_750_000m,
            "0777666555");

        var gateClient = new ConfigurableGateAccessLogClient();
        gateClient.RegisterScans("TCK-BUNDLE-A1", new GateScan
        {
            TicketCode = "TCK-BUNDLE-A1",
            ScannedAt = transferredAt.AddMinutes(20),
            ScanResult = "SUCCESS",
            GateName = "Cổng A"
        });
        gateClient.RegisterScans("TCK-BUNDLE-A2", new GateScan
        {
            TicketCode = "TCK-BUNDLE-A2",
            ScannedAt = transferredAt.AddMinutes(25),
            ScanResult = "SUCCESS",
            GateName = "Cổng B"
        });

        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            await new CreateDisputeCommandHandler(db, buyerUser).Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Gian lận đòi hoàn tiền combo",
                ReasonCode = DisputeReasonCode.DuplicateEntry
            }, CancellationToken.None);
        }

        var services = BuildServiceProvider(gateClient);
        await services.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync(CancellationToken.None);

        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            // Cả 2 vé đều đã vào cổng thành công -> Khuyến nghị BÁC BỎ GIAN LẬN
            Assert.Equal(DisputeRecommendation.RecommendRejectFraud, dispute.Recommendation);
        }
    }

    [Fact]
    public async Task Scenario5B_MultiTicketBundle_OneTicketDenied_RecommendsRefund()
    {
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-2);
        const string bundleCodes = "TCK-BUNDLE-B1,TCK-BUNDLE-B2";

        var seed = await SeedCompleteTransactionAsync(
            bundleCodes,
            transferredAt,
            2_000_000m,
            1_750_000m,
            "0777666555");

        var gateClient = new ConfigurableGateAccessLogClient();
        // 1 vé vào được, nhưng 1 vé bị từ chối
        gateClient.RegisterScans("TCK-BUNDLE-B1", new GateScan
        {
            TicketCode = "TCK-BUNDLE-B1",
            ScannedAt = transferredAt.AddMinutes(20),
            ScanResult = "SUCCESS",
            GateName = "Cổng A"
        });
        gateClient.RegisterScans("TCK-BUNDLE-B2", new GateScan
        {
            TicketCode = "TCK-BUNDLE-B2",
            ScannedAt = transferredAt.AddMinutes(22),
            ScanResult = "INVALID_TICKET",
            GateName = "Cổng B"
        });

        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            await new CreateDisputeCommandHandler(db, buyerUser).Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Có 1 vé trong combo không quét được",
                ReasonCode = DisputeReasonCode.TicketInvalid
            }, CancellationToken.None);
        }

        var services = BuildServiceProvider(gateClient);
        await services.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync(CancellationToken.None);

        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            // Có ít nhất 1 vé bị lỗi quét cổng -> Khuyến nghị HOÀN TIỀN
            Assert.Equal(DisputeRecommendation.RecommendRefund, dispute.Recommendation);
        }
    }

    #endregion

    #region Scenario 6: Robustness & Seller Payout Account Invariant

    [Fact]
    public async Task Scenario6_RejectDispute_WhenSellerHasNoPayoutAccount_ThrowsAndLeavesDisputeOpen()
    {
        var now = DateTimeOffset.UtcNow;
        var transferredAt = now.AddHours(-2);
        const string reissuedTicketCode = "TCK-REISSUE-NO-BANK";

        // Người bán CHƯA cài đặt số tài khoản ngân hàng
        var seed = await SeedCompleteTransactionAsync(
            reissuedTicketCode,
            transferredAt,
            500_000m,
            450_000m,
            sellerPayoutAccountNumber: ""); // Không có STK

        var gateClient = new ConfigurableGateAccessLogClient();
        gateClient.RegisterScans(reissuedTicketCode, new GateScan
        {
            TicketCode = reissuedTicketCode,
            ScannedAt = transferredAt.AddMinutes(10),
            ScanResult = "SUCCESS",
            GateName = "Cổng VIP"
        });

        await using (var db = CreateDbContext())
        {
            var buyerUser = new StubCurrentUser(seed.BuyerId, "User");
            await new CreateDisputeCommandHandler(db, buyerUser).Handle(new CreateDisputeCommand
            {
                EscrowId = seed.EscrowId,
                Reason = "Khiếu nại kiểm tra tài khoản người bán",
                ReasonCode = DisputeReasonCode.FakeTicket
            }, CancellationToken.None);
        }

        var services = BuildServiceProvider(gateClient);
        await services.GetRequiredService<DisputeHarvestWorker>().HarvestPendingAsync(CancellationToken.None);

        // Staff cố tình reject để trả tiền người bán, nhưng người bán chưa có STK -> Hệ thống phải chặn an toàn
        await using (var db = CreateDbContext())
        {
            var dispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.EscrowId == seed.EscrowId);
            var staffUser = new StubCurrentUser(seed.StaffId, "Admin");
            var resolveHandler = new ResolveDisputeCommandHandler(db, staffUser);

            var ex = await Assert.ThrowsAsync<BusinessRuleViolationException>(() =>
                resolveHandler.Handle(new ResolveDisputeCommand
                {
                    DisputeId = dispute.Id,
                    Decision = DisputeDecision.Reject,
                    Reason = "Đã vào cửa"
                }, CancellationToken.None));

            Assert.Contains("Người bán chưa có tài khoản nhận tiền", ex.Message);

            // Kiểm tra trạng thái tranh chấp vẫn Open và tiền vẫn bị Disputed an toàn
            var freshDispute = await db.Disputes.AsNoTracking().SingleAsync(d => d.Id == dispute.Id);
            var freshEscrow = await db.EscrowTransactions.AsNoTracking().SingleAsync(e => e.Id == seed.EscrowId);
            Assert.Equal(DisputeStatus.Open, freshDispute.Status);
            Assert.Equal(EscrowStatus.Disputed, freshEscrow.Status);
            Assert.Empty(await db.PayoutTransactions.AsNoTracking().ToListAsync());
        }
    }

    #endregion

    #region Helper Infrastructure & Database Seeding

    private sealed record TransactionSeed(
        Guid EscrowId,
        Guid BuyerId,
        Guid SellerId,
        Guid StaffId,
        Guid DisputeIdPlaceholder);

    private async Task<TransactionSeed> SeedCompleteTransactionAsync(
        string reissuedTicketCode,
        DateTimeOffset transferredAt,
        decimal totalBuyerPaid,
        decimal netSellerPayout,
        string sellerPayoutAccountNumber = "0123456789",
        string buyerPayoutAccountNumber = "0987654321")
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var staffId = Guid.NewGuid();
        var buyerId = Guid.NewGuid();
        var sellerId = Guid.NewGuid();
        var organizerId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var tierId = Guid.NewGuid();
        var listingId = Guid.NewGuid();
        var escrowId = Guid.NewGuid();

        var staff = new ShadowUser
        {
            Id = staffId,
            Email = $"staff-{suffix}@ticketshield.local",
            FullName = "Staff Member",
            Role = UserRole.Admin
        };

        var buyer = new ShadowUser
        {
            Id = buyerId,
            Email = $"buyer-{suffix}@ticketshield.local",
            FullName = "Nguyễn Văn Buyer",
            PayoutBankCode = "MB",
            PayoutAccountNumber = buyerPayoutAccountNumber,
            PayoutAccountName = "NGUYEN VAN BUYER"
        };

        var seller = new ShadowUser
        {
            Id = sellerId,
            Email = $"seller-{suffix}@ticketshield.local",
            FullName = "Trần Thị Seller",
            PayoutBankCode = "VCB",
            PayoutAccountNumber = sellerPayoutAccountNumber,
            PayoutAccountName = "TRAN THI SELLER"
        };

        var organizer = new Organizer
        {
            Id = organizerId,
            Name = "V-Concert Organizer",
            OfficialEmail = $"org-{suffix}@vconcert.vn"
        };

        var concertEvent = new Event
        {
            Id = eventId,
            OrganizerId = organizerId,
            Name = "Live Concert Vietnam 2026",
            Venue = "SVĐ Mỹ Đình",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(5),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(5).AddHours(4),
            ResaleDeadline = DateTimeOffset.UtcNow.AddDays(4)
        };

        var tier = new TicketTier
        {
            Id = tierId,
            EventId = eventId,
            TierName = "VIP Standing",
            OriginalPrice = totalBuyerPaid
        };

        var listing = new ResaleListing
        {
            Id = listingId,
            EventId = eventId,
            TierId = tierId,
            SellerId = sellerId,
            OriginalTicketCode = $"ORIG-{suffix}",
            OriginalPrice = totalBuyerPaid,
            ResalePrice = totalBuyerPaid,
            ListingStatus = ListingStatus.Sold
        };

        var escrow = new EscrowTransaction
        {
            Id = escrowId,
            ListingId = listingId,
            BuyerId = buyerId,
            SellerId = sellerId,
            Status = EscrowStatus.Locked,
            OriginalTicketPrice = totalBuyerPaid,
            BuyerFee = 0,
            SellerFee = 0,
            TotalBuyerPaid = totalBuyerPaid,
            NetSellerPayout = netSellerPayout,
            NewTicketCode = reissuedTicketCode,
            TransferredAt = transferredAt,
            UnlockAt = DateTimeOffset.UtcNow.AddDays(2),
            CreatedAt = transferredAt.AddMinutes(-10),
            UpdatedAt = transferredAt
        };

        await using var db = CreateDbContext();
        db.ShadowUsers.AddRange(staff, buyer, seller);
        db.Organizers.Add(organizer);
        db.Events.Add(concertEvent);
        db.TicketTiers.Add(tier);
        db.ResaleListings.Add(listing);
        db.EscrowTransactions.Add(escrow);
        await db.SaveChangesAsync();

        return new TransactionSeed(escrowId, buyerId, sellerId, staffId, Guid.NewGuid());
    }

    private ServiceProvider BuildServiceProvider(IGateAccessLogClient gateClient)
    {
        var services = new ServiceCollection();
        services.AddDbContext<TicketShieldDbContext>(options => options.UseSqlite(_connection));
        services.AddScoped<ITicketShieldDbContext>(p => p.GetRequiredService<TicketShieldDbContext>());
        services.AddSingleton(gateClient);
        services.AddSingleton<DisputeHarvestWorker>();
        services.AddLogging();
        return services.BuildServiceProvider();
    }

    private sealed class ConfigurableGateAccessLogClient : IGateAccessLogClient
    {
        private readonly Dictionary<string, List<GateScan>> _scansByTicket = new(StringComparer.OrdinalIgnoreCase);

        public void RegisterScans(string ticketCode, params GateScan[] scans)
        {
            if (!_scansByTicket.TryGetValue(ticketCode, out var list))
            {
                list = [];
                _scansByTicket[ticketCode] = list;
            }
            list.AddRange(scans);
        }

        public Task<IReadOnlyList<GateScan>> GetScansAsync(string ticketCode, CancellationToken cancellationToken)
        {
            if (_scansByTicket.TryGetValue(ticketCode, out var list))
            {
                return Task.FromResult<IReadOnlyList<GateScan>>(list.ToList());
            }
            return Task.FromResult<IReadOnlyList<GateScan>>(Array.Empty<GateScan>());
        }
    }

    private sealed class StubCurrentUser : ICurrentUserService
    {
        public StubCurrentUser(Guid? userId, string? role)
        {
            UserId = userId;
            Role = role;
            Email = $"{role?.ToLowerInvariant()}@ticketshield.local";
        }

        public Guid? UserId { get; }
        public string? Email { get; }
        public string? Role { get; }
        public bool IsAuthenticated => UserId.HasValue && UserId != Guid.Empty;
    }

    #endregion
}

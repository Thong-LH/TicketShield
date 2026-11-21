using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Infrastructure.Persistence;
using TicketShield.Infrastructure.Services.Copilot;
using Xunit;

namespace TicketShield.UnitTests.Services.Copilot;

public class TicketCopilotToolCallingTests : IDisposable
{
    private readonly TicketShieldDbContext _dbContext;
    private readonly TicketCopilotService _service;
    private readonly Guid _eventId = Guid.NewGuid();
    private readonly Guid _tierId = Guid.NewGuid();

    public TicketCopilotToolCallingTests()
    {
        var dbOptions = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        _dbContext = new TicketShieldDbContext(dbOptions);
        var sanitizer = new PromptSanitizer();
        var assistantOptions = Options.Create(new AiAssistantOptions
        {
            Provider = "Gemini",
            Gemini = new GeminiOptions { ApiKey = "" }
        });

        _service = new TicketCopilotService(
            _dbContext,
            sanitizer,
            assistantOptions,
            new HttpClient(),
            NullLogger<TicketCopilotService>.Instance);

        SeedTestData();
    }

    private void SeedTestData()
    {
        var ev = new Event
        {
            Id = _eventId,
            Name = "Anh Trai Say Hi Concert 2026",
            Venue = "Vạn Phúc City, TP. Thủ Đức",
            EventStartAt = DateTimeOffset.UtcNow.AddDays(7),
            EventEndAt = DateTimeOffset.UtcNow.AddDays(7).AddHours(4),
            MaxResaleMarkupPercentage = 20m,
            Status = "UPCOMING"
        };

        var tier = new TicketTier
        {
            Id = _tierId,
            EventId = _eventId,
            TierName = "VIP A",
            OriginalPrice = 2000000m
        };

        var listingValid = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = _eventId,
            TierId = _tierId,
            TicketId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            OriginalTicketCode = "ATSH-VIP-001",
            OriginalPrice = 2000000m,
            ResalePrice = 2200000m,
            AppliedMarkupPercentage = 20m,
            ListingStatus = ListingStatus.Verified,
            IsPrivate = false,
            SeatZone = "Khán đài A"
        };

        // Listing của sự kiện sắp diễn ra trong 1 tiếng (vi phạm luật đóng sàn BR-L04)
        var nearEventId = Guid.NewGuid();
        var nearEvent = new Event
        {
            Id = nearEventId,
            Name = "Near Event Closing Soon",
            Venue = "SVĐ Mỹ Đình",
            EventStartAt = DateTimeOffset.UtcNow.AddHours(1),
            EventEndAt = DateTimeOffset.UtcNow.AddHours(4),
            MaxResaleMarkupPercentage = 10m
        };

        var nearTierId = Guid.NewGuid();
        var nearTier = new TicketTier
        {
            Id = nearTierId,
            EventId = nearEventId,
            TierName = "Standard",
            OriginalPrice = 500000m
        };

        var listingExpiredCutoff = new ResaleListing
        {
            Id = Guid.NewGuid(),
            EventId = nearEventId,
            TierId = nearTierId,
            TicketId = Guid.NewGuid(),
            SellerId = Guid.NewGuid(),
            OriginalTicketCode = "NEAR-001",
            OriginalPrice = 500000m,
            ResalePrice = 500000m,
            ListingStatus = ListingStatus.Verified,
            IsPrivate = false
        };

        _dbContext.Events.AddRange(ev, nearEvent);
        _dbContext.TicketTiers.AddRange(tier, nearTier);
        _dbContext.ResaleListings.AddRange(listingValid, listingExpiredCutoff);
        _dbContext.SaveChanges();
    }

    [Fact]
    public async Task CheckPriceCapAsync_WhenWithinCeiling_ReturnsValid()
    {
        // VIP A giá gốc 2.000.000, markup 20% -> trần 2.400.000
        var result = await _service.CheckPriceCapAsync("VIP A", 2300000m);

        Assert.True(result.IsWithinCeiling);
        Assert.Equal(2000000m, result.OriginalPrice);
        Assert.Equal(2400000m, result.PriceCeiling);
        Assert.Contains("hợp lệ", result.Message);
    }

    [Fact]
    public async Task CheckPriceCapAsync_WhenExceedsCeiling_ReturnsViolation()
    {
        // Đề xuất 2.500.000 > trần 2.400.000
        var result = await _service.CheckPriceCapAsync("VIP A", 2500000m);

        Assert.False(result.IsWithinCeiling);
        Assert.Contains("vượt quá trần giá", result.Message);
    }

    [Fact]
    public async Task SearchResaleTicketsAsync_WhenEventWithinCutoff2Hours_ExcludesFromResults()
    {
        // Vé của nearEvent diễn ra trong 1h tới bắt buộc phải bị loại bỏ theo BR-L04
        var tickets = await _service.SearchResaleTicketsAsync(null, null, null, null);

        Assert.NotEmpty(tickets);
        Assert.DoesNotContain(tickets, t => t.EventName == "Near Event Closing Soon");
        Assert.Contains(tickets, t => t.EventName == "Anh Trai Say Hi Concert 2026");
    }

    [Fact]
    public async Task SearchResaleTicketsAsync_FilterByMaxPriceAndTier_ReturnsMatchingTickets()
    {
        var tickets = await _service.SearchResaleTicketsAsync("Anh Trai", "VIP", 2500000m, "Khán đài A");

        Assert.Single(tickets);
        var t = tickets[0];
        Assert.Equal("VIP A", t.TierName);
        Assert.Equal(2200000m, t.ResalePrice);
        Assert.Equal("Khán đài A", t.SeatZone);
    }

    public void Dispose()
    {
        _dbContext.Dispose();
    }
}

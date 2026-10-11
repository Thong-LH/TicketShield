using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.IdentityModel.Tokens;
using TicketShield.API.Middlewares;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Features.Disputes.Commands.ResolveDispute;
using TicketShield.Application.Features.Disputes.Queries.GetDisputeRecommendation;
using TicketShield.Domain.Entities;
using TicketShield.Domain.Enums;
using TicketShield.Domain.Exceptions;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Infrastructure.Services;
using TicketShield.Infrastructure.Persistence;
using IdentityUserRole = TicketShield.Identity.Domain.Enums.UserRole;

namespace TicketShield.UnitTests.Features.Disputes;

public class DisputeStaffAccessTests
{
    private const string Secret = "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026";

    [Fact]
    public async Task Handle_WhenCallerIsNotStaff_DisputeStaysOpen()
    {
        await using var db = CreateDb();
        var disputeId = await SeedOpenDispute(db);

        var readUser = new GetDisputeRecommendationQueryHandler(db, new StubUser(Guid.NewGuid(), "User", true));
        var readMissing = new GetDisputeRecommendationQueryHandler(db, null);
        var resolveUser = new ResolveDisputeCommandHandler(db, new StubUser(Guid.NewGuid(), "User", true));
        var resolveMissing = new ResolveDisputeCommandHandler(db, null);

        Assert.Equal(403, await StatusCodeOf(await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            readUser.Handle(new GetDisputeRecommendationQuery { DisputeId = disputeId }, CancellationToken.None))));
        Assert.Equal(401, await StatusCodeOf(await Assert.ThrowsAsync<UnauthorizedException>(() =>
            readMissing.Handle(new GetDisputeRecommendationQuery { DisputeId = disputeId }, CancellationToken.None))));
        Assert.Equal(403, await StatusCodeOf(await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            resolveUser.Handle(new ResolveDisputeCommand { DisputeId = disputeId, Decision = DisputeDecision.Approve, Reason = "Tự duyệt" }, CancellationToken.None))));
        Assert.Equal(401, await StatusCodeOf(await Assert.ThrowsAsync<UnauthorizedException>(() =>
            resolveMissing.Handle(new ResolveDisputeCommand { DisputeId = disputeId, Decision = DisputeDecision.Approve, Reason = "Tự duyệt" }, CancellationToken.None))));

        Assert.Equal(DisputeStatus.Open, (await db.Disputes.AsNoTracking().SingleAsync()).Status);
    }

    [Fact]
    public void Validate_WhenIdentityIssuesModeratorOrAdmin_IsInRoleMatches()
    {
        var generator = new JwtTokenGenerator(new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = Secret,
                ["JwtSettings:Issuer"] = "TicketShield",
                ["JwtSettings:Audience"] = "TicketShieldApp",
                ["JwtSettings:ExpirationInMinutes"] = "15"
            })
            .Build());

        Assert.True(Validate(generator, IdentityUserRole.Moderator).IsInRole("Moderator"));
        Assert.True(Validate(generator, IdentityUserRole.Admin).IsInRole("Admin"));
    }

    private static ClaimsPrincipal Validate(JwtTokenGenerator generator, IdentityUserRole role)
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = $"{role}@ticketshield.vn",
            FullName = role.ToString(),
            Role = role
        };
        var (token, _) = generator.GenerateAccessToken(user);
        var handler = new JwtSecurityTokenHandler();
        return handler.ValidateToken(token, new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = "TicketShield",
            ValidAudience = "TicketShieldApp",
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(Secret)),
            ClockSkew = TimeSpan.FromSeconds(15)
        }, out _);
    }

    private static TicketShieldDbContext CreateDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task<Guid> SeedOpenDispute(TicketShieldDbContext db)
    {
        var buyerId = Guid.NewGuid();
        var escrow = new EscrowTransaction
        {
            Id = Guid.NewGuid(),
            ListingId = Guid.NewGuid(),
            BuyerId = buyerId,
            SellerId = Guid.NewGuid(),
            Status = EscrowStatus.Disputed
        };
        var dispute = new Dispute
        {
            Id = Guid.NewGuid(),
            EscrowId = escrow.Id,
            BuyerId = buyerId,
            DisputeCode = "DP-STAFF",
            Status = DisputeStatus.Open
        };
        db.EscrowTransactions.Add(escrow);
        db.Disputes.Add(dispute);
        await db.SaveChangesAsync();
        return dispute.Id;
    }

    private static async Task<int> StatusCodeOf(Exception exception)
    {
        var context = new DefaultHttpContext { Response = { Body = new MemoryStream() } };
        var middleware = new GlobalExceptionHandlingMiddleware(
            _ => throw exception,
            NullLogger<GlobalExceptionHandlingMiddleware>.Instance);
        await middleware.InvokeAsync(context);
        return context.Response.StatusCode;
    }

    private sealed class StubUser(Guid? userId, string? role, bool authenticated) : ICurrentUserService
    {
        public Guid? UserId { get; } = userId;
        public string? Email => "staff@test.local";
        public string? Role { get; } = role;
        public bool IsAuthenticated { get; } = authenticated;
    }
}

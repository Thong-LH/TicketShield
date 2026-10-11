using System.Reflection;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TicketShield.Contracts.Events;
using TicketShield.Domain.Entities;
using TicketShield.Identity.API.Controllers;
using TicketShield.Identity.Application.Common.Interfaces;
using TicketShield.Identity.Application.Features.AdminUsers.Commands.SuspendUser;
using TicketShield.Identity.Application.Features.AdminUsers.Queries.GetAdminUser;
using TicketShield.Identity.Application.Features.Auth.Commands.GoogleLogin;
using TicketShield.Identity.Application.Features.Auth.Commands.Login;
using TicketShield.Identity.Application.Features.Auth.Commands.RefreshToken;
using TicketShield.Identity.Domain.Entities;
using TicketShield.Identity.Domain.Enums;
using TicketShield.Identity.Domain.Exceptions;
using TicketShield.Identity.Infrastructure.Persistence;
using TicketShield.Identity.Infrastructure.Services;
using TicketShield.Infrastructure.Messaging.Consumers;
using TicketShield.Infrastructure.Persistence;
using Xunit;
using CoreUserRole = TicketShield.Domain.Enums.UserRole;

namespace TicketShield.UnitTests.Features.AdminUsers;

public class AdminSuspendUserTests
{
    [Fact]
    public async Task Get_WhenUserIsActive_ReturnsScore100_AndUnknownIdIs404()
    {
        await using var db = CreateIdentityDb();
        var user = await AddUser(db, UserRole.User, active: true);
        var handler = new GetAdminUserQueryHandler(db);

        var result = await handler.Handle(new GetAdminUserQuery(user.Id), CancellationToken.None);

        Assert.True(result.Success);
        Assert.Equal(user.Id, result.Data!.Id);
        Assert.Equal(user.Email, result.Data.Email);
        Assert.Equal("Buyer", result.Data.FullName);
        Assert.Equal("0900000001", result.Data.PhoneNumber);
        Assert.Equal("User", result.Data.Role);
        Assert.True(result.Data.IsActive);
        Assert.Equal(100, result.Data.TrustScore);

        var missing = await Assert.ThrowsAsync<NotFoundException>(() =>
            handler.Handle(new GetAdminUserQuery(Guid.NewGuid()), CancellationToken.None));
        Assert.Equal(404, StatusCodeOf(missing));
    }

    [Fact]
    public async Task Suspend_WhenUserIsActive_ClosesAccountRevokesTokenAndPublishes()
    {
        await using var db = CreateIdentityDb();
        var adminId = Guid.NewGuid();
        var user = await AddUser(db, UserRole.User, active: true, trustScore: 100);
        var token = new RefreshToken
        {
            UserId = user.Id,
            Token = "refresh-still-good",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1)
        };
        db.RefreshTokens.Add(token);
        await db.SaveChangesAsync();
        var (handler, publish) = CreateSuspender(db, adminId);

        var first = await handler.Handle(new SuspendUserCommand(user.Id), CancellationToken.None);
        var second = await handler.Handle(new SuspendUserCommand(user.Id), CancellationToken.None);

        var stored = await db.Users.SingleAsync(u => u.Id == user.Id);
        Assert.True(first.Success);
        Assert.True(second.Success);
        Assert.False(stored.IsActive);
        Assert.Equal(0, stored.TrustScore);
        Assert.False(second.Data!.IsActive);
        Assert.Equal(0, second.Data.TrustScore);
        Assert.NotNull((await db.RefreshTokens.SingleAsync()).RevokedAt);
        publish.Verify(p => p.Publish<IUserProfileUpdatedEvent>(
            It.Is<IUserProfileUpdatedEvent>(e => e.UserId == user.Id && e.IsActive == false && e.Role == "User"),
            It.IsAny<CancellationToken>()), Times.Exactly(2));
    }

    [Fact]
    public async Task Suspend_WhenCallerIsSelfOrTargetIsAdmin_Returns403AndDoesNotChange()
    {
        await using var db = CreateIdentityDb();
        var admin = await AddUser(db, UserRole.Admin, active: true, trustScore: 100);
        var otherAdmin = await AddUser(db, UserRole.Admin, active: true, trustScore: 80, email: "other-admin@ticketshield.vn");
        var selfHandler = CreateSuspender(db, admin.Id).Handler;
        var otherHandler = CreateSuspender(db, Guid.NewGuid()).Handler;

        var self = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            selfHandler.Handle(new SuspendUserCommand(admin.Id), CancellationToken.None));
        var staff = await Assert.ThrowsAsync<ForbiddenAccessException>(() =>
            otherHandler.Handle(new SuspendUserCommand(otherAdmin.Id), CancellationToken.None));

        Assert.Equal(403, StatusCodeOf(self));
        Assert.Equal(403, StatusCodeOf(staff));
        Assert.True((await db.Users.SingleAsync(u => u.Id == admin.Id)).IsActive);
        Assert.Equal(100, (await db.Users.SingleAsync(u => u.Id == admin.Id)).TrustScore);
        Assert.True((await db.Users.SingleAsync(u => u.Id == otherAdmin.Id)).IsActive);
        Assert.Equal(80, (await db.Users.SingleAsync(u => u.Id == otherAdmin.Id)).TrustScore);
    }

    [Fact]
    public async Task LoginAndRefresh_WhenSuspended_AreRejected_AndShadowUserTurnsInactive()
    {
        await using var db = CreateIdentityDb();
        var user = await AddUser(db, UserRole.User, active: false, trustScore: 0);
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword("secret");
        user.GoogleId = "google-locked";
        db.RefreshTokens.Add(new RefreshToken
        {
            UserId = user.Id,
            Token = "revoked-refresh",
            ExpiresAt = DateTimeOffset.UtcNow.AddDays(1),
            RevokedAt = DateTimeOffset.UtcNow
        });
        await db.SaveChangesAsync();

        var login = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            new LoginCommandHandler(db, CreateTokenGen()).Handle(new LoginCommand(user.Email, "secret"), CancellationToken.None));
        var refresh = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            new RefreshTokenCommandHandler(db, CreateTokenGen()).Handle(new RefreshTokenCommand("revoked-refresh"), CancellationToken.None));
        var google = new Mock<IGoogleAuthService>();
        google.Setup(g => g.VerifyGoogleTokenAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new GoogleUserInfo { GoogleId = "google-locked", Email = user.Email, Name = "Buyer" });
        var googleLogin = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            new GoogleLoginCommandHandler(db, CreateTokenGen(), google.Object, new Mock<IPublishEndpoint>().Object)
                .Handle(new GoogleLoginCommand("token"), CancellationToken.None));

        await using var core = CreateCoreDb();
        core.ShadowUsers.Add(new ShadowUser
        {
            Id = user.Id,
            Email = user.Email,
            FullName = "Buyer",
            Role = CoreUserRole.User,
            IsActive = true
        });
        await core.SaveChangesAsync();
        var context = new Mock<ConsumeContext<IUserProfileUpdatedEvent>>();
        context.Setup(c => c.Message).Returns(new UserProfileUpdatedEvent(
            user.Id, "Buyer", user.PhoneNumber, "User", false, DateTimeOffset.UtcNow));
        context.Setup(c => c.CancellationToken).Returns(CancellationToken.None);
        await new UserProfileUpdatedConsumer(core, NullLogger<UserProfileUpdatedConsumer>.Instance).Consume(context.Object);

        Assert.Equal(401, StatusCodeOf(login));
        Assert.Equal(401, StatusCodeOf(refresh));
        Assert.Equal(401, StatusCodeOf(googleLogin));
        Assert.False((await core.ShadowUsers.SingleAsync()).IsActive);
    }

    [Fact]
    public void Controller_RequiresAdminRole()
    {
        var controller = typeof(AdminUsersController);
        var authorize = controller.GetCustomAttribute<AuthorizeAttribute>();
        var get = controller.GetMethod(nameof(AdminUsersController.Get))!;
        var suspend = controller.GetMethod(nameof(AdminUsersController.Suspend))!;

        Assert.Equal("Admin", authorize!.Roles);
        Assert.Contains(controller.GetCustomAttributes<RouteAttribute>(), route => route.Template == "api/v1/admin/users");
        Assert.NotNull(get.GetCustomAttribute<HttpGetAttribute>());
        Assert.Equal("{id:guid}/suspension", suspend.GetCustomAttribute<HttpPostAttribute>()!.Template);
    }

    private static int StatusCodeOf(Exception exception) => exception switch
    {
        NotFoundException => 404,
        UnauthorizedException => 401,
        ForbiddenAccessException => 403,
        _ => 500
    };

    private static IdentityDbContext CreateIdentityDb()
    {
        var options = new DbContextOptionsBuilder<IdentityDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new IdentityDbContext(options);
    }

    private static TicketShieldDbContext CreateCoreDb()
    {
        var options = new DbContextOptionsBuilder<TicketShieldDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options;
        return new TicketShieldDbContext(options);
    }

    private static async Task<User> AddUser(
        IdentityDbContext db,
        UserRole role,
        bool active,
        int trustScore = 100,
        string email = "buyer@ticketshield.vn")
    {
        var user = new User
        {
            Id = Guid.NewGuid(),
            Email = email,
            FullName = "Buyer",
            PhoneNumber = "0900000001",
            Role = role,
            IsActive = active,
            TrustScore = trustScore
        };
        db.Users.Add(user);
        await db.SaveChangesAsync();
        return user;
    }

    private static (SuspendUserCommandHandler Handler, Mock<IPublishEndpoint> Publish) CreateSuspender(IdentityDbContext db, Guid callerId)
    {
        var current = new Mock<ICurrentUserService>();
        current.Setup(u => u.UserId).Returns(callerId);
        current.Setup(u => u.IsAuthenticated).Returns(true);
        var publish = new Mock<IPublishEndpoint>();
        return (new SuspendUserCommandHandler(db, current.Object, publish.Object), publish);
    }

    private static JwtTokenGenerator CreateTokenGen()
    {
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["JwtSettings:Secret"] = "TicketShieldSuperSecretSecurityKeyForCapstoneProject2026",
                ["JwtSettings:Issuer"] = "TicketShield",
                ["JwtSettings:Audience"] = "TicketShieldApp",
                ["JwtSettings:ExpirationInMinutes"] = "15"
            })
            .Build();
        return new JwtTokenGenerator(config);
    }
}

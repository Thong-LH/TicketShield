using MediatR;
using Microsoft.EntityFrameworkCore;
using TicketShield.Application.Common.Interfaces;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Features.Auth.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.Application.Features.Auth.Commands.Login;

public class LoginCommandHandler : IRequestHandler<LoginCommand, ApiResponse<AuthResponse>>
{
    private readonly ITicketShieldDbContext _dbContext;
    private readonly IPasswordHasher _passwordHasher;
    private readonly IJwtTokenGenerator _jwtTokenGenerator;

    public LoginCommandHandler(
        ITicketShieldDbContext dbContext,
        IPasswordHasher passwordHasher,
        IJwtTokenGenerator jwtTokenGenerator)
    {
        _dbContext = dbContext;
        _passwordHasher = passwordHasher;
        _jwtTokenGenerator = jwtTokenGenerator;
    }

    public async Task<ApiResponse<AuthResponse>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var normalizedEmail = request.Email.Trim().ToLowerInvariant();

        var user = await _dbContext.Users
            .FirstOrDefaultAsync(u => u.Email.ToLower() == normalizedEmail, cancellationToken);

        if (user == null || string.IsNullOrEmpty(user.PasswordHash))
        {
            throw new DomainException("Địa chỉ email hoặc mật khẩu không chính xác.");
        }

        if (!user.IsActive)
        {
            throw new BusinessRuleViolationException("Tài khoản của bạn hiện đang bị khóa. Vui lòng liên hệ CSKH TicketShield.");
        }

        var isPasswordValid = _passwordHasher.VerifyPassword(request.Password, user.PasswordHash);
        if (!isPasswordValid)
        {
            throw new DomainException("Địa chỉ email hoặc mật khẩu không chính xác.");
        }

        var token = _jwtTokenGenerator.GenerateToken(user.Id, user.Email, user.FullName, user.Role.ToString());

        var response = new AuthResponse
        {
            UserId = user.Id,
            Email = user.Email,
            FullName = user.FullName,
            Role = user.Role.ToString(),
            Token = token
        };

        return ApiResponse<AuthResponse>.SuccessResponse(response, "Đăng nhập thành công.");
    }
}

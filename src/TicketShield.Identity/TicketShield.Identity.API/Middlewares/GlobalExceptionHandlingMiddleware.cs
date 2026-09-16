using System.Net;
using System.Text.Json;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Domain.Exceptions;

namespace TicketShield.Identity.API.Middlewares;

public class GlobalExceptionHandlingMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlingMiddleware> _logger;

    public GlobalExceptionHandlingMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlingMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Đã xảy ra lỗi không xử lý trong Identity API: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = exception switch
        {
            NotFoundException nfEx => (
                (int)HttpStatusCode.NotFound,
                nfEx.Message,
                null as IDictionary<string, string[]>),

            UnauthorizedException uaEx => (
                (int)HttpStatusCode.Unauthorized,
                uaEx.Message,
                null as IDictionary<string, string[]>),

            ForbiddenAccessException faEx => (
                (int)HttpStatusCode.Forbidden,
                faEx.Message,
                null as IDictionary<string, string[]>),

            BadRequestException brEx => (
                (int)HttpStatusCode.BadRequest,
                brEx.Message,
                null as IDictionary<string, string[]>),

            ValidationException vEx => (
                (int)HttpStatusCode.BadRequest,
                "Dữ liệu đầu vào không hợp lệ.",
                vEx.Errors),

            _ => (
                (int)HttpStatusCode.InternalServerError,
                "Đã xảy ra lỗi máy chủ nội bộ. Vui lòng thử lại sau.",
                null as IDictionary<string, string[]>)
        };

        context.Response.StatusCode = statusCode;
        var response = ApiResponse<object>.FailureResponse(message, errors);
        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }
}

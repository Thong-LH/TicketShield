using System.Net;
using System.Text.Json;
using FluentValidation;
using TicketShield.Application.Common.Models;
using TicketShield.Domain.Exceptions;

namespace TicketShield.API.Middlewares;

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
            _logger.LogError(ex, "An unhandled exception occurred: {Message}", ex.Message);
            await HandleExceptionAsync(context, ex);
        }
    }

    private static async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/json";

        var (statusCode, message, errors) = exception switch
        {
            BusinessRuleViolationException brEx => (
                (int)HttpStatusCode.UnprocessableEntity,
                brEx.Message,
                null as List<string>),

            DomainException dEx => (
                (int)HttpStatusCode.BadRequest,
                dEx.Message,
                null as List<string>),

            ValidationException vEx => (
                (int)HttpStatusCode.BadRequest,
                "Validation failed",
                vEx.Errors.Select(e => e.ErrorMessage).ToList()),

            _ => (
                (int)HttpStatusCode.InternalServerError,
                "An unexpected internal error occurred",
                null as List<string>)
        };

        context.Response.StatusCode = statusCode;
        var response = ApiResponse<object>.FailureResponse(message, errors);
        var json = JsonSerializer.Serialize(response);
        await context.Response.WriteAsync(json);
    }
}

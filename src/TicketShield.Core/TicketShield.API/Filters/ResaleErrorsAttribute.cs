using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using TicketShield.Application.Common.Models;
using TicketShield.Application.Resale;

namespace TicketShield.API.Filters;

public sealed class ResaleErrorsAttribute : ExceptionFilterAttribute
{
    public override void OnException(ExceptionContext context)
    {
        if (context.Exception is not ResaleWorkflowException error) return;
        context.Result = new ObjectResult(ApiResponse<object>.FailureResponse(error.Code, [error.Code]))
        {
            StatusCode = error.HttpStatus
        };
        context.ExceptionHandled = true;
    }
}

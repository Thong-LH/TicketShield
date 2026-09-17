using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Commands.CreateUserBankAccount;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;
using TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetUserBankAccounts;

namespace TicketShield.Identity.API.Controllers;

[Authorize]
[Route("api/v1/user-bank-accounts")]
public class UserBankAccountsController : ApiControllerBase
{
    /// <summary>
    /// BE-CORE-3.1.4: Add / Link a new seller beneficiary bank account (UserBankAccount)
    /// </summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<UserBankAccountDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> AddBankAccount([FromBody] CreateUserBankAccountCommand command)
    {
        var result = await Mediator.Send(command);
        return Ok(result);
    }

    /// <summary>
    /// BE-CORE-3.1.4: Get list of seller's linked beneficiary bank accounts
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<List<UserBankAccountDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<object>), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> GetMyBankAccounts()
    {
        var query = new GetUserBankAccountsQuery();
        var result = await Mediator.Send(query);
        return Ok(result);
    }
}

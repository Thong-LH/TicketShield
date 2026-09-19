using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TicketShield.Identity.Application.Common.Models;
using TicketShield.Identity.Application.Features.UserBankAccounts.Commands.CreateUserBankAccount;
using TicketShield.Identity.Application.Features.UserBankAccounts.Dtos;
using TicketShield.Identity.Application.Features.UserBankAccounts.Queries.GetUserBankAccounts;
using TicketShield.Identity.Application.Features.UserBankAccounts.Queries.LookupAccountHolderName;

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

    /// <summary>
    /// BE-CORE-3.1.5: Proxy lookup bank account holder name via BankLookup.net API (avoids browser CORS).
    /// GET /api/v1/user-bank-accounts/lookup-name?bin={bankBin}&amp;accountNumber={accountNumber}
    /// </summary>
    [AllowAnonymous]
    [HttpGet("lookup-name")]
    [ProducesResponseType(typeof(ApiResponse<LookupAccountHolderNameResult>), StatusCodes.Status200OK)]
    public async Task<IActionResult> LookupAccountHolderName([FromQuery] string bin, [FromQuery] string accountNumber)
    {
        var query = new LookupAccountHolderNameQuery(bin, accountNumber);
        var result = await Mediator.Send(query);
        return Ok(result);
    }
}


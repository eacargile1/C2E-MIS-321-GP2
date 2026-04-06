using C2E.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/invoices")]
public sealed class InvoicesController : ControllerBase
{
    /// <summary>Generate invoice (Finance workflow). Replace when E10 lands.</summary>
    [HttpPost("generate")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public IActionResult Generate() => NoContent();
}

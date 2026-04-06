using C2E.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/clients")]
public sealed class ClientsController : ControllerBase
{
    /// <summary>Employee billing rates. Replace when E8/E10 land.</summary>
    [HttpGet("billing-rates")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public ActionResult<object> GetBillingRates() => Ok(new { items = Array.Empty<object>() });

    /// <summary>Create client. Replace when E8 lands.</summary>
    [HttpPost]
    [Authorize(Roles = RbacRoleSets.AdminOnly)]
    public ActionResult<object> Create() =>
        StatusCode(StatusCodes.Status201Created, new { id = Guid.NewGuid(), name = "(stub)" });
}

using C2E.Api.Authorization;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/timesheets")]
public sealed class TimesheetsController : ControllerBase
{
    /// <summary>Org-wide timesheet visibility (FR10). Replace body when E2 lands.</summary>
    [HttpGet("organization")]
    [Authorize(Roles = RbacRoleSets.AdminAndFinance)]
    public ActionResult<IReadOnlyList<object>> GetOrganization() => Ok(Array.Empty<object>());
}

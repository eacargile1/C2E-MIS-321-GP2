using System.Globalization;
using System.Security.Claims;
using C2E.Api.Authorization;
using C2E.Api.Data;
using C2E.Api.Dtos;
using C2E.Api.Models;
using C2E.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Controllers;

[ApiController]
[Route("api/expenses")]
[Authorize]
public sealed class ExpensesController(AppDbContext db) : ControllerBase
{
    private const long MaxInvoiceBytes = 5 * 1024 * 1024;
    private static readonly HashSet<string> InvoiceMimeAllow =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "application/pdf",
            "image/jpeg",
            "image/png",
            "image/webp",
        };

    /// <summary>Full expense register for finance ops (all users, all approval states).</summary>
    [HttpGet("ledger")]
    [Authorize(Roles = RbacRoleSets.AdminFinanceManager)]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListLedger(CancellationToken ct)
    {
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        var rows = await db.ExpenseEntries
            .AsNoTracking()
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListMine(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        var rows = await db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.UserId == userId)
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    /// <summary>
    /// Admin: all org expenses (all statuses). Manager: direct reports only (same detail as <see cref="ListMine"/>).
    /// </summary>
    [HttpGet("team")]
    [Authorize(Roles = RbacRoleSets.AdminAndManager)]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListTeam(CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);

        var q = db.ExpenseEntries.AsNoTracking().AsQueryable();
        if (!User.IsInRole(nameof(AppRole.Admin)))
            q = q.Where(x => db.Users.Any(u => u.Id == x.UserId && u.ManagerUserId == userId));

        var rows = await q
            .OrderByDescending(x => x.ExpenseDate)
            .ThenByDescending(x => x.CreatedAtUtc)
            .ToListAsync(ct);
        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    [HttpPost]
    [Consumes("application/json")]
    public async Task<ActionResult<ExpenseResponse>> CreateJson([FromBody] CreateExpenseRequest body, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return BadRequest(new AuthErrorResponse { Message = FirstModelError() ?? "Invalid request." });
        if (!TryGetUserId(out var userId))
            return Unauthorized();
        if (!TryParseDateOnly(body.ExpenseDate, out var expenseDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid expenseDate. Use YYYY-MM-DD." });

        return await CreateExpenseCoreAsync(
            userId,
            expenseDate,
            body.Client.Trim(),
            body.Project.Trim(),
            body.Category.Trim(),
            body.Description.Trim(),
            body.Amount,
            invoiceBytes: null,
            invoiceFileName: null,
            invoiceContentType: null,
            ct);
    }

    [HttpPost]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(6_000_000)]
    public async Task<ActionResult<ExpenseResponse>> CreateMultipart([FromForm] CreateExpenseFormRequest form, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        if (!TryParseDateOnly(form.ExpenseDate, out var expenseDate))
            return BadRequest(new AuthErrorResponse { Message = "Invalid expenseDate. Use YYYY-MM-DD." });

        var client = form.Client.Trim();
        var project = form.Project.Trim();
        var category = form.Category.Trim();
        var description = form.Description.Trim();
        var err = ValidateExpenseFieldLengths(client, project, category, description, form.Amount);
        if (err is not null) return BadRequest(new AuthErrorResponse { Message = err });

        var (bytes, fn, cty, invErr) = await ReadInvoiceUploadAsync(form.Invoice, ct);
        if (invErr is not null) return BadRequest(new AuthErrorResponse { Message = invErr });

        return await CreateExpenseCoreAsync(
            userId,
            expenseDate,
            client,
            project,
            category,
            description,
            form.Amount,
            bytes,
            fn,
            cty,
            ct);
    }

    [HttpGet("{id:guid}/invoice")]
    public async Task<IActionResult> DownloadInvoice([FromRoute] Guid id, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var row = await db.ExpenseEntries.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound(new AuthErrorResponse { Message = "Expense not found." });
        if (row.InvoiceBytes is null || row.InvoiceBytes.Length == 0)
            return NotFound(new AuthErrorResponse { Message = "No invoice attached to this expense." });

        if (row.UserId != userId)
        {
            if (User.IsInRole(nameof(AppRole.Admin)))
            {
                // ok
            }
            else if (User.IsInRole(nameof(AppRole.Finance)))
            {
                // ok — ledger visibility
            }
            else if (User.IsInRole(nameof(AppRole.Manager)))
            {
                var submitter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
                if (submitter is null) return Forbid();
                var okIc = await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, userId, row, submitter, ct);
                var okFin = await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, userId, row, submitter, ct);
                var okMgr = await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, userId, row, submitter, ct);
                if (!okIc && !okFin && !okMgr) return Forbid();
            }
            else if (User.IsInRole(nameof(AppRole.Partner)))
            {
                var submitter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
                if (submitter is null) return Forbid();
                var okFinance = await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, userId, row, submitter, ct);
                var okPartner = await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, userId, row, submitter, ct);
                var okDm = await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, userId, row, submitter, ct);
                if (!okFinance && !okPartner && !okDm) return Forbid();
            }
            else
                return Forbid();
        }

        var fileName = string.IsNullOrWhiteSpace(row.InvoiceFileName) ? "invoice" : row.InvoiceFileName;
        var contentType = string.IsNullOrWhiteSpace(row.InvoiceContentType)
            ? "application/octet-stream"
            : row.InvoiceContentType;
        return File(row.InvoiceBytes, contentType, fileName);
    }

    [HttpGet("approvals/pending")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public async Task<ActionResult<IReadOnlyList<ExpenseResponse>>> ListPendingApprovals(CancellationToken ct)
    {
        if (!TryGetUserId(out var reviewerId)) return Unauthorized();

        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        var pending = await db.ExpenseEntries
            .AsNoTracking()
            .Where(x => x.Status == ExpenseStatus.Pending)
            .OrderBy(x => x.ExpenseDate)
            .ThenBy(x => x.CreatedAtUtc)
            .ToListAsync(ct);

        var submitterIds = pending.Select(x => x.UserId).Distinct().ToList();
        var submitters = await db.Users.AsNoTracking()
            .Where(u => submitterIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, ct);

        var rows = new List<ExpenseEntry>(pending.Count);
        foreach (var e in pending)
        {
            if (!submitters.TryGetValue(e.UserId, out var sub))
                continue;
            if (User.IsInRole(nameof(AppRole.Admin)))
            {
                rows.Add(e);
                continue;
            }

            var ok = false;
            if (User.IsInRole(nameof(AppRole.Partner)))
            {
                if (await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, reviewerId, e, sub, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, reviewerId, e, sub, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, reviewerId, e, sub, ct))
                    ok = true;
            }

            if (!ok && User.IsInRole(nameof(AppRole.Manager)))
            {
                if (await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, reviewerId, e, sub, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, reviewerId, e, sub, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, reviewerId, e, sub, ct))
                    ok = true;
            }
            if (ok)
                rows.Add(e);
        }

        return Ok(rows.Select(x => Map(x, users)).ToList());
    }

    [HttpPost("{id:guid}/approve")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public Task<IActionResult> Approve([FromRoute] Guid id, CancellationToken ct) =>
        Review(id, ExpenseStatus.Approved, ct);

    [HttpPost("{id:guid}/reject")]
    [Authorize(Roles = RbacRoleSets.AdminManagerPartner)]
    public Task<IActionResult> Reject([FromRoute] Guid id, CancellationToken ct) =>
        Review(id, ExpenseStatus.Rejected, ct);

    private async Task<IActionResult> Review(Guid id, ExpenseStatus nextStatus, CancellationToken ct)
    {
        if (!TryGetUserId(out var userId)) return Unauthorized();
        var row = await db.ExpenseEntries.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (row is null) return NotFound();
        if (row.Status != ExpenseStatus.Pending)
            return BadRequest(new AuthErrorResponse { Message = "Only pending expenses can be reviewed." });

        if (!User.IsInRole(nameof(AppRole.Admin)))
        {
            var submitter = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == row.UserId, ct);
            if (submitter is null) return Forbid();

            var ok = false;
            if (User.IsInRole(nameof(AppRole.Partner)))
            {
                if (await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, userId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, userId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, userId, row, submitter, ct))
                    ok = true;
            }

            if (!ok && User.IsInRole(nameof(AppRole.Manager)))
            {
                if (await ProjectApprovalRouting.ManagerMayApproveIcExpenseAsync(db, userId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.ReviewerMayApproveFinanceExpenseAsync(db, userId, row, submitter, ct))
                    ok = true;
                else if (await ProjectApprovalRouting.PartnerMayApproveManagerExpenseAsync(db, userId, row, submitter, ct))
                    ok = true;
            }
            if (!ok) return Forbid();
        }

        row.Status = nextStatus;
        row.ReviewedByUserId = userId;
        row.ReviewedAtUtc = DateTime.UtcNow;
        row.UpdatedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return NoContent();
    }

    private async Task<ActionResult<ExpenseResponse>> CreateExpenseCoreAsync(
        Guid userId,
        DateOnly expenseDate,
        string client,
        string project,
        string category,
        string description,
        decimal amount,
        byte[]? invoiceBytes,
        string? invoiceFileName,
        string? invoiceContentType,
        CancellationToken ct)
    {
        try
        {
            await ActiveCatalogValidation.EnsureActiveClientAndProjectAsync(db, client, project, ct);
        }
        catch (InvalidOperationException ex)
        {
            return BadRequest(new AuthErrorResponse { Message = ex.Message });
        }

        var catalogEnforced = await db.Clients.AnyAsync(c => c.IsActive, ct);
        var submitterRole = await db.Users.AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Role)
            .FirstAsync(ct);
        if (catalogEnforced && submitterRole == AppRole.IC &&
            !await IcCatalogAccess.MayUseClientProjectAsync(db, userId, client, project, ct))
        {
            return BadRequest(new AuthErrorResponse
            {
                Message =
                    "You are not assigned to that client/project. Ask a Partner or Admin to add you to the client roster, project roster, or project team.",
            });
        }

        var now = DateTime.UtcNow;
        var entity = new ExpenseEntry
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            ExpenseDate = expenseDate,
            Client = client,
            Project = project,
            Category = category,
            Description = description,
            Amount = amount,
            Status = ExpenseStatus.Pending,
            InvoiceBytes = invoiceBytes,
            InvoiceFileName = invoiceFileName,
            InvoiceContentType = invoiceContentType,
            CreatedAtUtc = now,
            UpdatedAtUtc = now,
        };
        db.ExpenseEntries.Add(entity);
        await db.SaveChangesAsync(ct);

        var users = await db.Users.AsNoTracking().ToDictionaryAsync(x => x.Id, x => x.Email, ct);
        return Ok(Map(entity, users));
    }

    private static async Task<(byte[]? Bytes, string? FileName, string? ContentType, string? Error)> ReadInvoiceUploadAsync(
        IFormFile? file,
        CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return (null, null, null, null);
        if (file.Length > MaxInvoiceBytes)
            return (null, null, null, "Invoice file must be 5 MB or smaller.");
        var ctType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType.Trim();
        if (!InvoiceMimeAllow.Contains(ctType))
            return (null, null, null, "Invoice must be PDF, JPEG, PNG, or WebP.");

        await using var ms = new MemoryStream((int)Math.Min(file.Length, int.MaxValue));
        await file.CopyToAsync(ms, ct);
        var name = string.IsNullOrWhiteSpace(file.FileName) ? "invoice" : Path.GetFileName(file.FileName);
        if (name.Length > 260)
            name = name[..260];
        return (ms.ToArray(), name, ctType, null);
    }

    private static string? ValidateExpenseFieldLengths(string client, string project, string category, string description, decimal amount)
    {
        if (string.IsNullOrWhiteSpace(client) || client.Trim().Length > 120)
            return "Client is required (max 120 characters).";
        if (string.IsNullOrWhiteSpace(project) || project.Trim().Length > 120)
            return "Project is required (max 120 characters).";
        if (string.IsNullOrWhiteSpace(category) || category.Trim().Length > 80)
            return "Category is required (max 80 characters).";
        if (string.IsNullOrWhiteSpace(description) || description.Trim().Length > 500)
            return "Description is required (max 500 characters).";
        if (amount < 0.01m || amount > 99_999_999m)
            return "Amount must be between 0.01 and 99999999.";
        return null;
    }

    private static ExpenseResponse Map(ExpenseEntry x, IReadOnlyDictionary<Guid, string> users) =>
        new()
        {
            Id = x.Id,
            UserId = x.UserId,
            UserEmail = users.TryGetValue(x.UserId, out var owner) ? owner : "",
            ExpenseDate = x.ExpenseDate.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture),
            Client = x.Client,
            Project = x.Project,
            Category = x.Category,
            Description = x.Description,
            Amount = x.Amount,
            Status = x.Status.ToString(),
            ReviewedByEmail = x.ReviewedByUserId is { } uid && users.TryGetValue(uid, out var rev) ? rev : null,
            ReviewedAtUtc = x.ReviewedAtUtc,
            HasInvoice = x.InvoiceBytes is { Length: > 0 },
        };

    private bool TryGetUserId(out Guid id)
    {
        var sub = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue(ClaimTypes.Name);
        if (sub is null || !Guid.TryParse(sub, out id))
        {
            id = default;
            return false;
        }
        return true;
    }

    private static bool TryParseDateOnly(string input, out DateOnly date) =>
        DateOnly.TryParseExact(
            input.Trim(),
            "yyyy-MM-dd",
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);

    private string? FirstModelError()
    {
        foreach (var (_, state) in ModelState)
        {
            if (state.Errors.Count == 0) continue;
            var msg = state.Errors[0].ErrorMessage;
            if (!string.IsNullOrWhiteSpace(msg)) return msg;
        }
        return null;
    }
}

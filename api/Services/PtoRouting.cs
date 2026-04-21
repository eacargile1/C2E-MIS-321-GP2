using C2E.Api.Data;
using C2E.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace C2E.Api.Services;

/// <summary>
/// PTO approvers: IC → org manager (primary) + reporting partner when set (secondary); Manager/Finance → reporting partner
/// (primary) + org manager when set (secondary); Partner/Admin → self (auto). Either primary or secondary may approve.
/// </summary>
public static class PtoRouting
{
    public sealed record PtoApproverResolution(
        Guid? PrimaryApproverUserId,
        Guid? SecondaryApproverUserId,
        string? Error,
        bool AutoApprove);

    public static async Task<PtoApproverResolution> ResolvePtoApproversAsync(AppDbContext db, AppUser requester, CancellationToken ct)
    {
        switch (requester.Role)
        {
            case AppRole.Partner:
            case AppRole.Admin:
                return new PtoApproverResolution(requester.Id, null, null, true);

            case AppRole.IC:
                if (requester.ManagerUserId is not { } mid)
                    return new PtoApproverResolution(null, null, "IC accounts must have an org manager assigned before requesting PTO.", false);
                var mgr = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                    u => u.Id == mid && u.IsActive && u.Role == AppRole.Manager,
                    ct);
                if (mgr is null)
                    return new PtoApproverResolution(null, null, "Org manager is missing or inactive.", false);

                Guid? secIc = null;
                if (requester.PartnerUserId is { } pp && pp != mid)
                {
                    var parIc = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                        u => u.Id == pp && u.IsActive && u.Role == AppRole.Partner,
                        ct);
                    if (parIc is not null)
                        secIc = pp;
                }

                return new PtoApproverResolution(mid, secIc, null, false);

            case AppRole.Manager:
            case AppRole.Finance:
                if (requester.PartnerUserId is not { } pid)
                    return new PtoApproverResolution(null, null, "Assign a reporting partner on your profile before requesting PTO.", false);
                var par = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                    u => u.Id == pid && u.IsActive && u.Role == AppRole.Partner,
                    ct);
                if (par is null)
                    return new PtoApproverResolution(null, null, "Reporting partner is missing or inactive.", false);

                Guid? secMgr = null;
                if (requester.ManagerUserId is { } mm && mm != pid)
                {
                    var mgr2 = await db.Users.AsNoTracking().FirstOrDefaultAsync(
                        u => u.Id == mm && u.IsActive && u.Role == AppRole.Manager,
                        ct);
                    if (mgr2 is not null)
                        secMgr = mm;
                }

                return new PtoApproverResolution(pid, secMgr, null, false);

            default:
                return new PtoApproverResolution(null, null, "PTO is not available for this account type.", false);
        }
    }
}

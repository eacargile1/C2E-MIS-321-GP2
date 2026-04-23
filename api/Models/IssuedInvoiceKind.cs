namespace C2E.Api.Models;

public enum IssuedInvoiceKind
{
    /// <summary>All approved project expenses in range on one billable document.</summary>
    ProjectApprovedExpenses = 0,

    /// <summary>Reimbursement-style document for one submitter's approved expenses on the project.</summary>
    UserPayout = 1,
}

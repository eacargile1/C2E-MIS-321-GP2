namespace C2E.Api.Models;

public enum AppRole
{
    IC = 0,
    Admin = 1,
    Manager = 2,
    Finance = 3,
    /// <summary>Delivery lead: same core journey as IC (time, expenses) plus forecasting/staffing UX on the resource tracker.</summary>
    Partner = 4,
}

namespace C2E.Api;

public static class AuthMessages
{
    public const string InvalidCredentials = "Invalid email or password.";

    /// <summary>403 JSON body for failed authorization (authenticated but not permitted).</summary>
    public const string Forbidden = "You do not have permission to perform this action.";
}

namespace C2E.Api;

public static class UserProfileName
{
    /// <summary>Uses the part before '@' when present; otherwise the full email.</summary>
    public static string DefaultFromEmail(string email)
    {
        var i = email.IndexOf('@', StringComparison.Ordinal);
        return i > 0 ? email[..i] : email;
    }
}

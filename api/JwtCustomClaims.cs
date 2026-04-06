namespace C2E.Api;

public static class JwtCustomClaims
{
    /// <summary>String "true" / "false" — matches token introspection without calling /me.</summary>
    public const string Active = "active";
}

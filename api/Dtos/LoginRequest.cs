using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public class LoginRequest
{
    /// <summary>Intentionally not using email-format validation so dev accounts like *.c2e.local always bind.</summary>
    [Required, MaxLength(320)]
    public string Email { get; set; } = "";

    [Required, MinLength(1), MaxLength(200)]
    public string Password { get; set; } = "";
}

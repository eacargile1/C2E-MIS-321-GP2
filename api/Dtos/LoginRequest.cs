using System.ComponentModel.DataAnnotations;

namespace C2E.Api.Dtos;

public class LoginRequest
{
    [Required, EmailAddress, MaxLength(320)]
    public string Email { get; set; } = "";

    [Required, MinLength(1), MaxLength(200)]
    public string Password { get; set; } = "";
}

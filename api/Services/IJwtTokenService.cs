using C2E.Api.Models;

namespace C2E.Api.Services;

public interface IJwtTokenService
{
    string CreateAccessToken(AppUser user);
}

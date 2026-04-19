#if DEBUG
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using VirtualLibrary.Api.Mapping;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

/// <summary>
/// DEV ONLY — never compiled into Release builds (#if DEBUG).
/// Issues a real JWT for a named test persona without any credentials,
/// bypassing the external OAuth flow entirely.
///
/// A second, independent runtime guard checks IsDevelopment() so that
/// a Debug binary accidentally pointed at a non-dev environment still
/// returns 404 for this endpoint.
///
/// Personas: superadmin · admin · member · pending · suspended
/// </summary>
[ApiController]
[Route("api/auth/dev-login")]
public class DevAuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IWebHostEnvironment _env;

    public DevAuthController(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        IWebHostEnvironment env)
    {
        _userManager   = userManager;
        _configuration = configuration;
        _env           = env;
    }

    [HttpPost]
    public async Task<IActionResult> DevLogin([FromBody] DevLoginRequest req)
    {
        // Layer 2: runtime guard (layer 1 is the #if DEBUG compile-time exclusion)
        if (!_env.IsDevelopment())
            return NotFound();

        var email = req.Persona switch
        {
            "superadmin" => "superadmin@dev.local",
            "admin"      => "admin@dev.local",
            "member"     => "member@dev.local",
            "pending"    => "pending@dev.local",
            "suspended"  => "suspended@dev.local",
            _            => null
        };

        if (email is null)
            return BadRequest(new { error = $"Unknown persona '{req.Persona}'. Valid: superadmin, admin, member, pending, suspended." });

        var user = await _userManager.FindByEmailAsync(email);
        if (user is null)
            return NotFound(new { error = $"Persona '{req.Persona}' not seeded yet — restart the API to trigger seeding." });

        var token = GenerateJwt(user);
        return Ok(new AuthResponse(token, user.ToResponse()));
    }

    private string GenerateJwt(AppUser user)
    {
        var key   = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? "DevSecret_ChangeMe_32CharsMin!!!!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email,          user.Email ?? ""),
            new Claim(ClaimTypes.Name,           user.DisplayName ?? ""),
            new Claim(ClaimTypes.Role,           user.Role.ToString()),
            new Claim("status",                  user.Status.ToString())
        };

        var token = new JwtSecurityToken(
            issuer:            _configuration["Jwt:Issuer"]   ?? "VirtualLibrary",
            audience:          _configuration["Jwt:Audience"] ?? "VirtualLibrary",
            claims:            claims,
            expires:           DateTime.UtcNow.AddHours(24),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}
#endif

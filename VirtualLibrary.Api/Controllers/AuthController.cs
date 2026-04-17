using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using VirtualLibrary.Api.Mapping;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Api.Services;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly IConfiguration _configuration;
    private readonly IExternalTokenValidatorFactory _tokenValidator;

    public AuthController(
        UserManager<AppUser> userManager,
        IConfiguration configuration,
        IExternalTokenValidatorFactory tokenValidator)
    {
        _userManager    = userManager;
        _configuration  = configuration;
        _tokenValidator = tokenValidator;
    }

    /// <summary>
    /// Password login (for seeded admin accounts).
    /// </summary>
    [HttpPost("login/password")]
    public async Task<IActionResult> PasswordLogin([FromBody] PasswordLoginRequest request)
    {
        var user = await _userManager.FindByEmailAsync(request.Email);
        if (user == null)
            return Unauthorized(new { error = "Invalid credentials" });

        var validPassword = await _userManager.CheckPasswordAsync(user, request.Password);
        if (!validPassword)
            return Unauthorized(new { error = "Invalid credentials" });

        var token = GenerateJwt(user);
        return Ok(new AuthResponse(token, user.ToResponse()));
    }

    /// <summary>
    /// Exchange an external identity token (Google/Apple) for a local JWT.
    /// The token is fully validated (signature, expiry, audience) using the
    /// provider's published public keys before any claims are trusted.
    /// Creates the user if they don't exist yet (PendingApproval).
    /// </summary>
    [HttpPost("login")]
    public async Task<IActionResult> ExternalLogin([FromBody] ExternalLoginRequest request)
    {
        var identity = await _tokenValidator.ValidateAsync(
            request.Provider, request.IdToken);

        if (identity == null)
            return Unauthorized(new { error = "Identity token validation failed" });

        var email      = identity.Email;
        var externalId = identity.ExternalId;
        var name       = identity.DisplayName;

        if (string.IsNullOrEmpty(email) || string.IsNullOrEmpty(externalId))
            return BadRequest(new { error = "Token missing email or subject" });

        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            user = new AppUser
            {
                UserName = email,
                Email = email,
                DisplayName = name,
                ExternalProvider = request.Provider,
                ExternalId = externalId,
                Status = UserStatus.PendingApproval,
                Role = UserRole.User
            };
            var result = await _userManager.CreateAsync(user);
            if (!result.Succeeded)
                return BadRequest(new { error = result.Errors.Select(e => e.Description) });
        }

        var token = GenerateJwt(user);
        return Ok(new AuthResponse(token, user.ToResponse()));
    }

    /// <summary>
    /// Get the current authenticated user's profile.
    /// </summary>
    [HttpGet("me")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        return Ok(user.ToResponse());
    }

    /// <summary>
    /// Refresh the JWT for the currently authenticated user.
    /// Returns a fresh token with the latest role/status.
    /// </summary>
    [HttpPost("refresh")]
    [Microsoft.AspNetCore.Authorization.Authorize]
    public async Task<IActionResult> Refresh()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (userId == null) return Unauthorized();

        var user = await _userManager.FindByIdAsync(userId);
        if (user == null) return NotFound();

        var token = GenerateJwt(user);
        return Ok(new AuthResponse(token, user.ToResponse()));
    }

    /// <summary>
    /// Simple health check endpoint (no auth required).
    /// </summary>
    [HttpGet("/health")]
    public IActionResult Health() => Ok(new { status = "healthy" });

    private string GenerateJwt(AppUser user)
    {
        var key = new SymmetricSecurityKey(
            Encoding.UTF8.GetBytes(_configuration["Jwt:Key"] ?? "DevSecret_ChangeMe_32CharsMin!!!!"));
        var creds = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var claims = new[]
        {
            new Claim(ClaimTypes.NameIdentifier, user.Id),
            new Claim(ClaimTypes.Email, user.Email ?? ""),
            new Claim(ClaimTypes.Name, user.DisplayName ?? ""),
            new Claim(ClaimTypes.Role, user.Role.ToString()),
            new Claim("status", user.Status.ToString())
        };

        var token = new JwtSecurityToken(
            issuer: _configuration["Jwt:Issuer"] ?? "VirtualLibrary",
            audience: _configuration["Jwt:Audience"] ?? "VirtualLibrary",
            claims: claims,
            expires: DateTime.UtcNow.AddHours(24),
            signingCredentials: creds);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }
}

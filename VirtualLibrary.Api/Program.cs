using System.Text;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Api.Services;
using VirtualLibrary.Shared;

var builder = WebApplication.CreateBuilder(args);

// Database
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection")));

// Identity
builder.Services.AddIdentity<AppUser, IdentityRole>(options =>
{
    options.User.RequireUniqueEmail = true;
    options.Password.RequireDigit = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequireUppercase = false;
    options.Password.RequiredLength = 6;
})
.AddEntityFrameworkStores<AppDbContext>()
.AddDefaultTokenProviders();

// JWT Authentication
var jwtKey = builder.Configuration["Jwt:Key"] ?? "DevSecret_ChangeMe_32CharsMin!!!!";
builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidateAudience = true,
        ValidateLifetime = true,
        ValidateIssuerSigningKey = true,
        ValidIssuer = builder.Configuration["Jwt:Issuer"] ?? "VirtualLibrary",
        ValidAudience = builder.Configuration["Jwt:Audience"] ?? "VirtualLibrary",
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
    };
})
.AddGoogle(options =>
{
    options.ClientId = builder.Configuration["Auth:Google:ClientId"] ?? "";
    options.ClientSecret = builder.Configuration["Auth:Google:ClientSecret"] ?? "";
});

// Only Active users can hit library endpoints.
// AdminUser additionally requires Admin or SuperAdmin role (used by stats, user management, etc.).
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveUser", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("status", UserStatus.Active.ToString()));

    options.AddPolicy("AdminUser", policy =>
        policy.RequireAuthenticatedUser()
              .RequireRole(UserRole.Admin.ToString(), UserRole.SuperAdmin.ToString()));
});

// External OAuth token validator (Google + Apple)
builder.Services.AddSingleton<IExternalTokenValidatorFactory, ExternalTokenValidatorFactory>();
// PKCE authorization code exchange (Google + Apple)
builder.Services.AddScoped<ITokenExchangeService, TokenExchangeService>();

// OpenLibrary client
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>();

// Bulk import
builder.Services.AddScoped<IBulkImportService, BulkImportService>();

builder.Services.AddControllers();
builder.Services.AddOpenApi();

// CORS for WASM frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
    {
        policy.WithOrigins(builder.Configuration["AllowedOrigins"] ?? "http://localhost:8080")
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

var app = builder.Build();

// Auto-migrate and seed in development
if (app.Environment.IsDevelopment())
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    db.Database.Migrate();

    // Seed a SuperAdmin if none exists
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    await SeedSuperAdminAsync(userManager);

#if DEBUG
    // Seed one test persona per role/status combination for use with the dev-login panel.
    await SeedDevPersonasAsync(userManager);
#endif

    app.MapOpenApi();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();

app.Run();

static async Task SeedSuperAdminAsync(UserManager<AppUser> userManager)
{
    const string adminEmail = "admin@virtuallibrary.local";
    var existing = await userManager.FindByEmailAsync(adminEmail);
    if (existing != null) return;

    var admin = new AppUser
    {
        UserName = adminEmail,
        Email = adminEmail,
        DisplayName = "Super Admin",
        Role = UserRole.SuperAdmin,
        Status = UserStatus.Active,
        ExternalProvider = "Seed",
        ExternalId = "seed-superadmin"
    };

    // Create with a default password for local dev — change in production
    await userManager.CreateAsync(admin, "Admin123!");
}

#if DEBUG
/// <summary>
/// Seeds one fixed test account per role/status combination.
/// Idempotent — safe to call on every restart.
/// Only runs in Debug builds; never compiled into Release.
/// </summary>
static async Task SeedDevPersonasAsync(UserManager<AppUser> userManager)
{
    var personas = new[]
    {
        new { Email = "superadmin@dev.local", Name = "Dev SuperAdmin",
              Role = UserRole.SuperAdmin, Status = UserStatus.Active },
        new { Email = "admin@dev.local",      Name = "Dev Admin",
              Role = UserRole.Admin,      Status = UserStatus.Active },
        new { Email = "member@dev.local",     Name = "Dev Member",
              Role = UserRole.User,       Status = UserStatus.Active },
        new { Email = "pending@dev.local",    Name = "Dev Pending",
              Role = UserRole.User,       Status = UserStatus.PendingApproval },
        new { Email = "suspended@dev.local",  Name = "Dev Suspended",
              Role = UserRole.User,       Status = UserStatus.Suspended },
    };

    foreach (var p in personas)
    {
        if (await userManager.FindByEmailAsync(p.Email) is not null) continue;

        var user = new AppUser
        {
            UserName         = p.Email,
            Email            = p.Email,
            DisplayName      = p.Name,
            Role             = p.Role,
            Status           = p.Status,
            ExternalProvider = "Dev",
            ExternalId       = $"dev-{p.Email}",
        };

        // Password is never used — dev-login bypasses credential checks.
        // Identity requires one, so we supply a throwaway.
        await userManager.CreateAsync(user, "DevOnly!0");
    }
}
#endif

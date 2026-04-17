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
builder.Services.AddAuthorization(options =>
{
    options.AddPolicy("ActiveUser", policy =>
        policy.RequireAuthenticatedUser()
              .RequireClaim("status", UserStatus.Active.ToString()));
});

// OpenLibrary client
builder.Services.AddMemoryCache();
builder.Services.AddHttpClient<IOpenLibraryClient, OpenLibraryClient>();

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

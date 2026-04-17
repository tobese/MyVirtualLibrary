using Microsoft.AspNetCore.Identity;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Models;

public class AppUser : IdentityUser
{
    public string? DisplayName { get; set; }
    public string? ExternalProvider { get; set; }
    public string? ExternalId { get; set; }
    public UserRole Role { get; set; } = UserRole.User;
    public UserStatus Status { get; set; } = UserStatus.PendingApproval;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public string? ApprovedBy { get; set; }
    public DateTime? ApprovedAt { get; set; }
}

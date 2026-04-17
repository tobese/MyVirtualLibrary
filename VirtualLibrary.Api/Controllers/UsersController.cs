using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using VirtualLibrary.Api.Data;
using VirtualLibrary.Api.Mapping;
using VirtualLibrary.Api.Models;
using VirtualLibrary.Shared;

namespace VirtualLibrary.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize(Roles = "Admin,SuperAdmin")]
public class UsersController : ControllerBase
{
    private readonly UserManager<AppUser> _userManager;
    private readonly AppDbContext _db;

    public UsersController(UserManager<AppUser> userManager, AppDbContext db)
    {
        _userManager = userManager;
        _db = db;
    }

    [HttpGet]
    public async Task<IActionResult> List([FromQuery] UserStatus? status = null)
    {
        var query = _db.Users.AsQueryable();
        if (status.HasValue)
            query = query.Where(u => u.Status == status.Value);

        var users = await query.OrderByDescending(u => u.CreatedAt).ToListAsync();
        return Ok(users.Select(u => u.ToResponse()));
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetById(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();
        return Ok(user.ToResponse());
    }

    [HttpPost("{id}/approve")]
    public async Task<IActionResult> Approve(string id, [FromBody] ApproveUserRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);

        if (request.Approved)
        {
            user.Status = UserStatus.Active;
            user.ApprovedBy = currentUserId;
            user.ApprovedAt = DateTime.UtcNow;
        }
        else
        {
            user.Status = UserStatus.Rejected;
        }

        await _userManager.UpdateAsync(user);
        return Ok(user.ToResponse());
    }

    [HttpPost("{id}/role")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> ChangeRole(string id, [FromBody] ChangeRoleRequest request)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.Role = request.Role;
        await _userManager.UpdateAsync(user);
        return Ok(user.ToResponse());
    }

    [HttpPost("{id}/suspend")]
    public async Task<IActionResult> Suspend(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.Status = UserStatus.Suspended;
        await _userManager.UpdateAsync(user);
        return Ok(user.ToResponse());
    }

    [HttpPost("{id}/reactivate")]
    public async Task<IActionResult> Reactivate(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        user.Status = UserStatus.Active;
        await _userManager.UpdateAsync(user);
        return Ok(user.ToResponse());
    }

    [HttpDelete("{id}")]
    [Authorize(Roles = "SuperAdmin")]
    public async Task<IActionResult> Delete(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user == null) return NotFound();

        // Prevent self-deletion
        var currentUserId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (user.Id == currentUserId)
            return BadRequest(new { error = "Cannot delete your own account" });

        await _userManager.DeleteAsync(user);
        return NoContent();
    }
}

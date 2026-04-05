using LumiSense.Data;
using LumiSense.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LumiSense.Controllers;

[Authorize(Roles = "Admin")]
public sealed class AdminController : Controller
{
    private static readonly string[] AllowedOrderStatuses =
    [
        "Pending",
        "Processing",
        "Shipped",
        "Delivered",
        "Cancelled"
    ];

    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public AdminController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet("/Admin")]
    public IActionResult Index() => RedirectToAction(nameof(Orders));

    [HttpGet("/Admin/Orders")]
    public async Task<IActionResult> Orders()
    {
        var orders = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .OrderByDescending(o => o.OrderDate)
            .Take(400)
            .ToListAsync();

        ViewData["AllowedOrderStatuses"] = AllowedOrderStatuses;
        return View(orders);
    }

    [HttpPost("/Admin/Orders/{id:int}/Status")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SetOrderStatus(int id, [FromForm] string status)
    {
        status = (status ?? string.Empty).Trim();
        if (!AllowedOrderStatuses.Contains(status, StringComparer.OrdinalIgnoreCase))
        {
            return BadRequest("Invalid status.");
        }

        var order = await _db.Orders.FirstOrDefaultAsync(o => o.Id == id);
        if (order is null) return NotFound();

        // normalize to canonical casing from AllowedOrderStatuses
        var canonical = AllowedOrderStatuses.First(s => string.Equals(s, status, StringComparison.OrdinalIgnoreCase));
        order.Status = canonical;
        await _db.SaveChangesAsync();

        return RedirectToAction(nameof(Orders));
    }

    public sealed class AdminUserRow
    {
        public string Id { get; init; } = string.Empty;
        public string Email { get; init; } = string.Empty;
        public string? PhoneNumber { get; init; }
        public bool IsAdmin { get; init; }
        public DateTimeOffset? LockoutEnd { get; init; }
    }

    [HttpGet("/Admin/Users")]
    public async Task<IActionResult> Users()
    {
        var users = await _userManager.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Take(400)
            .ToListAsync();

        var rows = new List<AdminUserRow>(users.Count);
        foreach (var u in users)
        {
            rows.Add(new AdminUserRow
            {
                Id = u.Id,
                Email = u.Email ?? u.UserName ?? u.Id,
                PhoneNumber = u.PhoneNumber,
                IsAdmin = await _userManager.IsInRoleAsync(u, "Admin"),
                LockoutEnd = u.LockoutEnd
            });
        }

        return View(rows);
    }

    [HttpGet("/Admin/Reviews")]
    public async Task<IActionResult> Reviews()
    {
        var pending = await _db.ProductReviews.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => !r.IsApproved)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(300)
            .ToListAsync();

        var approved = await _db.ProductReviews.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => r.IsApproved)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(120)
            .ToListAsync();

        return View(new AdminReviewsViewModel
        {
            Pending = pending,
            Approved = approved
        });
    }

    public sealed class AdminReviewsViewModel
    {
        public List<ProductReview> Pending { get; init; } = new();
        public List<ProductReview> Approved { get; init; } = new();
    }

    [HttpPost("/Admin/Reviews/{id:int}/Approve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ApproveReview(int id)
    {
        var review = await _db.ProductReviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();

        review.IsApproved = true;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Reviews));
    }

    [HttpPost("/Admin/Reviews/{id:int}/Hide")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> HideReview(int id)
    {
        var review = await _db.ProductReviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();

        review.IsApproved = false;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Reviews));
    }

    [HttpPost("/Admin/Reviews/{id:int}/Delete")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteReview(int id)
    {
        var review = await _db.ProductReviews.FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();

        _db.ProductReviews.Remove(review);
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Reviews));
    }
}


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
        public int UnresolvedReportCount { get; init; }
        public List<string> ReportedByEmails { get; init; } = new();
    }

    private static DateTimeOffset? ParseBanDuration(string? duration)
    {
        duration = (duration ?? string.Empty).Trim().ToLowerInvariant();
        if (duration is "" or "none" or "0" or "unban") return null;
        if (duration is "permanent" or "perm") return DateTimeOffset.UtcNow.AddYears(100);

        // Supported: "6h", "12h", "1d", "2d", "7d"
        if (duration.EndsWith("h") && int.TryParse(duration[..^1], out var hours) && hours > 0)
        {
            return DateTimeOffset.UtcNow.AddHours(hours);
        }
        if (duration.EndsWith("d") && int.TryParse(duration[..^1], out var days) && days > 0)
        {
            return DateTimeOffset.UtcNow.AddDays(days);
        }

        return DateTimeOffset.UtcNow.AddDays(1);
    }

    [HttpGet("/Admin/Users")]
    public async Task<IActionResult> Users()
    {
        var users = await _userManager.Users.AsNoTracking()
            .OrderBy(u => u.Email)
            .Take(400)
            .ToListAsync();

        var userIds = users.Select(u => u.Id).ToList();

        // unresolved reports against reviews authored by these users
        var reportPairs = await (
                from rep in _db.ProductReviewReports.AsNoTracking()
                join rev in _db.ProductReviews.AsNoTracking() on rep.ReviewId equals rev.Id
                where !rep.Resolved && userIds.Contains(rev.UserId)
                select new { AuthorId = rev.UserId, ReporterId = rep.ReporterUserId }
            )
            .ToListAsync();

        var reportCountByAuthor = reportPairs
            .GroupBy(x => x.AuthorId)
            .ToDictionary(g => g.Key, g => g.Count());

        var reporterIds = reportPairs
            .Select(x => x.ReporterId)
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var reporterEmailById = await _db.Users.AsNoTracking()
            .Where(u => reporterIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);

        var reportersByAuthor = reportPairs
            .GroupBy(x => x.AuthorId)
            .ToDictionary(
                g => g.Key,
                g => g.Select(x => x.ReporterId).Distinct().ToList()
            );

        var rows = new List<AdminUserRow>(users.Count);
        foreach (var u in users)
        {
            var reporterEmails = new List<string>();
            if (reportersByAuthor.TryGetValue(u.Id, out var reps))
            {
                reporterEmails = reps
                    .Select(id => reporterEmailById.TryGetValue(id, out var e) ? e : id)
                    .Distinct()
                    .OrderBy(x => x)
                    .ToList();
            }

            rows.Add(new AdminUserRow
            {
                Id = u.Id,
                Email = u.Email ?? u.UserName ?? u.Id,
                PhoneNumber = u.PhoneNumber,
                IsAdmin = await _userManager.IsInRoleAsync(u, "Admin"),
                LockoutEnd = u.LockoutEnd,
                UnresolvedReportCount = reportCountByAuthor.TryGetValue(u.Id, out var c) ? c : 0,
                ReportedByEmails = reporterEmails
            });
        }

        return View(rows);
    }

    [HttpPost("/Admin/Users/{id}/Ban")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BanUser(string id, [FromForm] string duration)
    {
        var me = _userManager.GetUserId(User);
        if (!string.IsNullOrWhiteSpace(me) && string.Equals(me, id, StringComparison.Ordinal))
        {
            return BadRequest("You cannot ban yourself.");
        }

        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        var until = ParseBanDuration(duration);
        user.LockoutEnabled = true;
        await _userManager.UpdateAsync(user);
        await _userManager.SetLockoutEndDateAsync(user, until);

        return RedirectToAction(nameof(Users));
    }

    [HttpPost("/Admin/Users/{id}/Unban")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> UnbanUser(string id)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null) return NotFound();

        await _userManager.SetLockoutEndDateAsync(user, null);
        return RedirectToAction(nameof(Users));
    }

    public sealed class AdminReportRow
    {
        public ProductReviewReport Report { get; init; } = new();
        public ProductReview Review { get; init; } = new();
        public string? ReporterEmail { get; init; }
        public string? AuthorEmail { get; init; }
        public string? ProductName { get; init; }
    }

    [HttpGet("/Admin/Reviews")]
    public async Task<IActionResult> Reviews()
    {
        var unresolvedReports = await _db.ProductReviewReports.AsNoTracking()
            .Include(r => r.Review)
            .Where(r => !r.Resolved)
            .OrderByDescending(r => r.CreatedAtUtc)
            .Take(200)
            .ToListAsync();

        var reviewIds = unresolvedReports
            .Select(r => r.ReviewId)
            .Distinct()
            .ToList();

        var reviewsById = await _db.ProductReviews.AsNoTracking()
            .Include(r => r.Product)
            .Where(r => reviewIds.Contains(r.Id))
            .ToDictionaryAsync(r => r.Id);

        var userIds = unresolvedReports
            .Select(r => r.ReporterUserId)
            .Concat(reviewsById.Values.Select(r => r.UserId))
            .Where(x => !string.IsNullOrWhiteSpace(x))
            .Distinct()
            .ToList();

        var emailsByUserId = await _db.Users.AsNoTracking()
            .Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.Email ?? u.UserName ?? u.Id);

        var reportRows = new List<AdminReportRow>();
        foreach (var rep in unresolvedReports)
        {
            if (!reviewsById.TryGetValue(rep.ReviewId, out var rev)) continue;

            reportRows.Add(new AdminReportRow
            {
                Report = rep,
                Review = rev,
                ReporterEmail = emailsByUserId.TryGetValue(rep.ReporterUserId, out var re) ? re : rep.ReporterUserId,
                AuthorEmail = emailsByUserId.TryGetValue(rev.UserId, out var ae) ? ae : rev.UserId,
                ProductName = rev.Product?.Name
            });
        }

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
            Reports = reportRows,
            Pending = pending,
            Approved = approved
        });
    }

    public sealed class AdminReviewsViewModel
    {
        public List<AdminReportRow> Reports { get; init; } = new();
        public List<ProductReview> Pending { get; init; } = new();
        public List<ProductReview> Approved { get; init; } = new();
    }

    [HttpPost("/Admin/Reports/{id:int}/Resolve")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResolveReport(int id)
    {
        var report = await _db.ProductReviewReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is null) return NotFound();
        report.Resolved = true;
        await _db.SaveChangesAsync();
        return RedirectToAction(nameof(Reviews));
    }

    [HttpPost("/Admin/Reports/{id:int}/BanAuthor")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> BanAuthorFromReport(int id, [FromForm] string duration)
    {
        var rep = await _db.ProductReviewReports.AsNoTracking()
            .Include(r => r.Review)
            .FirstOrDefaultAsync(r => r.Id == id);
        if (rep?.Review is null) return NotFound();

        var authorId = rep.Review.UserId;
        var until = ParseBanDuration(duration);
        var user = await _userManager.FindByIdAsync(authorId);
        if (user is not null)
        {
            user.LockoutEnabled = true;
            await _userManager.UpdateAsync(user);
            await _userManager.SetLockoutEndDateAsync(user, until);
        }

        // resolve the report too
        var report = await _db.ProductReviewReports.FirstOrDefaultAsync(r => r.Id == id);
        if (report is not null)
        {
            report.Resolved = true;
            await _db.SaveChangesAsync();
        }

        return RedirectToAction(nameof(Reviews));
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


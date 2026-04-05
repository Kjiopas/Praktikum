using LumiSense.Data;
using LumiSense.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LumiSense.Controllers;

[Authorize]
public sealed class ReviewsController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public ReviewsController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpPost("/Reviews/{id:int}/Report")]
    public async Task<IActionResult> Report(int id, [FromForm] string? reason = null)
    {
        var review = await _db.ProductReviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();

        var reporterId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(reporterId)) return Forbid();

        reason = (reason ?? string.Empty).Trim();
        if (reason.Length > 250) reason = reason[..250];

        // De-dupe (same reporter + same review) unresolved.
        var exists = await _db.ProductReviewReports.AnyAsync(r =>
            r.ReviewId == id && r.ReporterUserId == reporterId && !r.Resolved);
        if (!exists)
        {
            _db.ProductReviewReports.Add(new ProductReviewReport
            {
                ReviewId = id,
                ReporterUserId = reporterId,
                Reason = string.IsNullOrWhiteSpace(reason) ? null : reason,
                CreatedAtUtc = DateTime.UtcNow,
                Resolved = false
            });
            await _db.SaveChangesAsync();
        }

        return RedirectToAction("Product", "Shop", new { id = review.ProductId });
    }
}


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
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Report(int id, [FromForm] string? reason = null, [FromForm] string? details = null)
    {
        var review = await _db.ProductReviews.AsNoTracking().FirstOrDefaultAsync(r => r.Id == id);
        if (review is null) return NotFound();

        var reporterId = _userManager.GetUserId(User);
        if (string.IsNullOrWhiteSpace(reporterId)) return Forbid();

        reason = (reason ?? string.Empty).Trim();
        details = (details ?? string.Empty).Trim();

        // Keep reason values stable (sent as codes from UI), so admin sees consistent output.
        // Special rule: if user chose "other", admin should see only the typed text.
        var mappedReason = MapReason(reason);
        if (string.Equals(reason, "other", StringComparison.OrdinalIgnoreCase))
        {
            mappedReason = details;
        }
        else if (!string.IsNullOrWhiteSpace(details))
        {
            mappedReason = string.IsNullOrWhiteSpace(mappedReason) ? details : $"{mappedReason} — {details}";
        }

        mappedReason = (mappedReason ?? string.Empty).Trim();
        if (mappedReason.Length > 250) mappedReason = mappedReason[..250];

        // De-dupe (same reporter + same review) unresolved.
        // If it exists, update the reason if the user provided one.
        var existing = await _db.ProductReviewReports
            .FirstOrDefaultAsync(r => r.ReviewId == id && r.ReporterUserId == reporterId && !r.Resolved);

        if (existing is not null)
        {
            if (!string.IsNullOrWhiteSpace(mappedReason))
            {
                existing.Reason = mappedReason;
                existing.CreatedAtUtc = DateTime.UtcNow;
                await _db.SaveChangesAsync();
            }
        }
        else
        {
            _db.ProductReviewReports.Add(new ProductReviewReport
            {
                ReviewId = id,
                ReporterUserId = reporterId,
                Reason = string.IsNullOrWhiteSpace(mappedReason) ? null : mappedReason,
                CreatedAtUtc = DateTime.UtcNow,
                Resolved = false
            });
            await _db.SaveChangesAsync();
        }

        return RedirectToAction("Product", "Shop", new { id = review.ProductId });
    }

    private static string? MapReason(string? reason)
    {
        reason = (reason ?? string.Empty).Trim().ToLowerInvariant();
        return reason switch
        {
            "spam" => "Spam or advertising",
            "abusive" => "Abusive or hateful content",
            "off_topic" => "Off-topic",
            "scam" => "Scam or misleading",
            "other" => "Other",
            "" => null,
            _ => reason // backward compatibility (older localized values)
        };
    }
}


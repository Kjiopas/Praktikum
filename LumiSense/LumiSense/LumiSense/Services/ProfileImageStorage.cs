using System.Security.Cryptography;
using LumiSense.Data;
using LumiSense.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace LumiSense.Services;

public sealed class ProfileImageStorage
{
    private static readonly HashSet<string> AllowedExt = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".png", ".webp"
    };

    private const long MaxBytes = 2 * 1024 * 1024; // 2MB

    private readonly IWebHostEnvironment _env;
    private readonly ApplicationDbContext _db;

    public ProfileImageStorage(IWebHostEnvironment env, ApplicationDbContext db)
    {
        _env = env;
        _db = db;
    }

    public async Task<string?> SaveForUserAsync(string userId, IFormFile? file, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId) || file is null || file.Length <= 0) return null;
        if (file.Length > MaxBytes) throw new InvalidOperationException("Profile photo is too large (max 2MB).");

        var ext = Path.GetExtension(file.FileName);
        if (string.IsNullOrWhiteSpace(ext) || !AllowedExt.Contains(ext))
        {
            throw new InvalidOperationException("Unsupported image type. Use JPG, PNG, or WEBP.");
        }

        var uploadsDir = Path.Combine(_env.WebRootPath, "uploads", "profile");
        Directory.CreateDirectory(uploadsDir);

        // Make filename deterministic but not guessable.
        var safeName = ToHex(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(userId))).Substring(0, 24);
        var fileName = $"{safeName}{ext.ToLowerInvariant()}";
        var absPath = Path.Combine(uploadsDir, fileName);

        await using (var fs = new FileStream(absPath, FileMode.Create, FileAccess.Write, FileShare.None))
        {
            await file.CopyToAsync(fs, ct);
        }

        var relPath = $"/uploads/profile/{fileName}";
        var profile = await _db.UserProfiles.FirstOrDefaultAsync(p => p.UserId == userId, ct);
        if (profile is null)
        {
            profile = new UserProfile { UserId = userId };
            _db.UserProfiles.Add(profile);
        }
        profile.ProfileImagePath = relPath;
        profile.UpdatedAtUtc = DateTime.UtcNow;
        await _db.SaveChangesAsync(ct);

        return relPath;
    }

    public async Task<string?> GetForUserAsync(string userId, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(userId)) return null;
        return await _db.UserProfiles.AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => p.ProfileImagePath)
            .FirstOrDefaultAsync(ct);
    }

    private static string ToHex(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}


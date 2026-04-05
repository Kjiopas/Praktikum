using System.ComponentModel.DataAnnotations;

namespace LumiSense.Models;

public sealed class UserProfile
{
    [Key]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(300)]
    public string? ProfileImagePath { get; set; }

    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}


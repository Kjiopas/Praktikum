using System.ComponentModel.DataAnnotations;

namespace LumiSense.Models;

public sealed class ProductReviewReport
{
    [Key]
    public int Id { get; set; }

    public int ReviewId { get; set; }
    public ProductReview? Review { get; set; }

    [Required]
    [MaxLength(450)]
    public string ReporterUserId { get; set; } = string.Empty;

    [MaxLength(250)]
    public string? Reason { get; set; }

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool Resolved { get; set; } = false;
}


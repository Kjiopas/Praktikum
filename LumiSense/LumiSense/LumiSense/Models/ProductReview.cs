using System.ComponentModel.DataAnnotations;

namespace LumiSense.Models;

public sealed class ProductReview
{
    [Key]
    public int Id { get; set; }

    public int ProductId { get; set; }
    public Product? Product { get; set; }

    [Required]
    [MaxLength(450)]
    public string UserId { get; set; } = string.Empty;

    [MaxLength(120)]
    public string? UserDisplayName { get; set; }

    [Required]
    [MaxLength(250)]
    public string Text { get; set; } = string.Empty;

    [Range(1, 5)]
    public int Rating { get; set; } = 5;

    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;

    public bool IsApproved { get; set; } = false;
}


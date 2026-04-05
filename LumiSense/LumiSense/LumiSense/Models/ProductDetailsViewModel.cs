namespace LumiSense.Models;

public sealed class ProductDetailsViewModel
{
    public Product Product { get; set; } = new();
    public List<ProductReview> Reviews { get; set; } = new();
    public bool IsAdmin { get; set; }
    public string? CurrentUserId { get; set; }
}


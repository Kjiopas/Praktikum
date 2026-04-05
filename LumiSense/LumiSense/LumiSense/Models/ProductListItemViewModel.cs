namespace LumiSense.Models;

public sealed class ProductListItemViewModel
{
    public Product Product { get; set; } = new();
    public int ApprovedReviewCount { get; set; }
    public double AvgRating { get; set; }
}


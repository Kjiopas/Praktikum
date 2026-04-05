namespace LumiSense.Models;

public sealed class DeliveryPoint
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // office | locker
    public string Company { get; set; } = string.Empty; // Econt | Speedy | BoxNow
    public string City { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
    public double Latitude { get; set; }
    public double Longitude { get; set; }
}


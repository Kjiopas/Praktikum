using System.Text.Json;
using LumiSense.Models;

namespace LumiSense.Services;

public sealed class DeliveryPointsService
{
    private readonly IWebHostEnvironment _env;
    private IReadOnlyList<DeliveryPoint>? _cache;
    private readonly object _gate = new();

    public DeliveryPointsService(IWebHostEnvironment env)
    {
        _env = env;
    }

    public IReadOnlyList<DeliveryPoint> GetAll()
    {
        if (_cache is not null) return _cache;

        lock (_gate)
        {
            if (_cache is not null) return _cache;
            var path = Path.Combine(_env.WebRootPath, "data", "delivery-points-bg.json");
            if (!File.Exists(path))
            {
                _cache = Array.Empty<DeliveryPoint>();
                return _cache;
            }

            var json = File.ReadAllText(path);
            var items = JsonSerializer.Deserialize<List<DeliveryPoint>>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            }) ?? new List<DeliveryPoint>();

            _cache = items
                .Where(p => !string.IsNullOrWhiteSpace(p.Name))
                .ToList();

            return _cache;
        }
    }
}


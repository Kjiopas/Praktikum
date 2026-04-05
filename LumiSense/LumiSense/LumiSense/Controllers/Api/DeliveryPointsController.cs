using LumiSense.Services;
using Microsoft.AspNetCore.Mvc;

namespace LumiSense.Controllers.Api;

[ApiController]
[Route("api/delivery-points")]
public sealed class DeliveryPointsController : ControllerBase
{
    private readonly DeliveryPointsService _svc;

    public DeliveryPointsController(DeliveryPointsService svc)
    {
        _svc = svc;
    }

    [HttpGet]
    public IActionResult Get([FromQuery] string? company = null, [FromQuery] string? city = null, [FromQuery] string? type = null)
    {
        var points = _svc.GetAll().AsEnumerable();

        if (!string.IsNullOrWhiteSpace(company))
        {
            points = points.Where(p => string.Equals(p.Company, company, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(city))
        {
            points = points.Where(p => string.Equals(p.City, city, StringComparison.OrdinalIgnoreCase));
        }
        if (!string.IsNullOrWhiteSpace(type))
        {
            points = points.Where(p => string.Equals(p.Type, type, StringComparison.OrdinalIgnoreCase));
        }

        return Ok(points);
    }
}


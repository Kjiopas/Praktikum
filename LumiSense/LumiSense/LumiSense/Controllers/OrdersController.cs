using LumiSense.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LumiSense.Controllers;

public sealed class OrdersController : Controller
{
    private const string OrdersEmailSessionKey = "OrdersEmail";

    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public OrdersController(ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? email = null)
    {
        var isAuth = User.Identity?.IsAuthenticated == true;
        var userId = isAuth ? _userManager.GetUserId(User) : null;

        email = (email ?? HttpContext.Session.GetString(OrdersEmailSessionKey))?.Trim();
        if (!string.IsNullOrWhiteSpace(email))
        {
            HttpContext.Session.SetString(OrdersEmailSessionKey, email);
        }

        IQueryable<Models.Order> query = _db.Orders.AsNoTracking().Include(o => o.Items);

        if (isAuth && !string.IsNullOrWhiteSpace(userId))
        {
            query = query.Where(o => o.UserId == userId);
        }
        else if (!string.IsNullOrWhiteSpace(email))
        {
            query = query.Where(o => o.CustomerEmail == email);
        }
        else
        {
            // No identity and no email lookup: show empty list with lookup form.
            return View(new List<Models.Order>());
        }

        var orders = await query
            .OrderByDescending(o => o.OrderDate)
            .Take(200)
            .ToListAsync();

        return View(orders);
    }

    [HttpPost]
    public IActionResult SetLookupEmail([FromForm] string email)
    {
        email = (email ?? string.Empty).Trim();
        if (!string.IsNullOrWhiteSpace(email))
        {
            HttpContext.Session.SetString(OrdersEmailSessionKey, email);
        }
        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    public IActionResult ClearLookup()
    {
        HttpContext.Session.Remove(OrdersEmailSessionKey);
        return RedirectToAction(nameof(Index));
    }

    [HttpGet("/Orders/Details/{id:int}")]
    public async Task<IActionResult> Details(int id)
    {
        var order = await _db.Orders.AsNoTracking()
            .Include(o => o.Items)
            .FirstOrDefaultAsync(o => o.Id == id);

        if (order is null) return NotFound();

        var isAuth = User.Identity?.IsAuthenticated == true;
        var userId = isAuth ? _userManager.GetUserId(User) : null;
        var lookupEmail = HttpContext.Session.GetString(OrdersEmailSessionKey);

        var canView = (isAuth && !string.IsNullOrWhiteSpace(userId) && order.UserId == userId)
            || (!string.IsNullOrWhiteSpace(lookupEmail) && order.CustomerEmail == lookupEmail);

        if (!canView) return Forbid();

        return View(order);
    }
}


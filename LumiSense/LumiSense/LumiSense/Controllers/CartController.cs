using LumiSense.Data;
using LumiSense.Models;
using LumiSense.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace LumiSense.Controllers;

public sealed class CartController : Controller
{
    private readonly CartSessionService _cart;
    private readonly ApplicationDbContext _db;
    private readonly UserManager<IdentityUser> _userManager;

    public CartController(CartSessionService cart, ApplicationDbContext db, UserManager<IdentityUser> userManager)
    {
        _cart = cart;
        _db = db;
        _userManager = userManager;
    }

    [HttpGet]
    public IActionResult Index()
    {
        var items = _cart.GetItems();
        return View(items);
    }

    [HttpGet]
    public async Task<IActionResult> BuyNow(int productId, int quantity = 1)
    {
        if (productId <= 0) return RedirectToAction(nameof(Index));
        if (quantity <= 0) quantity = 1;

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == productId);
        if (product is not null)
        {
            _cart.AddOrIncrement(product.Id, product.Name, product.Price, quantity);
        }

        return RedirectToAction(nameof(Index));
    }

    public sealed class AddRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; } = 1;
    }

    [HttpPost]
    public async Task<IActionResult> Add([FromBody] AddRequest req)
    {
        if (req is null || req.ProductId <= 0) return BadRequest();

        var product = await _db.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == req.ProductId);
        if (product is null) return NotFound(new { success = false, message = "Product not found" });

        _cart.AddOrIncrement(product.Id, product.Name, product.Price, req.Quantity);
        return Json(new { success = true, count = _cart.GetTotalQuantity() });
    }

    public sealed class UpdateRequest
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
    }

    [HttpPost]
    public IActionResult Update([FromBody] UpdateRequest req)
    {
        if (req is null || req.ProductId <= 0) return BadRequest();
        _cart.UpdateQuantity(req.ProductId, req.Quantity);
        return Json(new { success = true, count = _cart.GetTotalQuantity() });
    }

    [HttpPost]
    public IActionResult Remove([FromBody] UpdateRequest req)
    {
        if (req is null || req.ProductId <= 0) return BadRequest();
        _cart.Remove(req.ProductId);
        return Json(new { success = true, count = _cart.GetTotalQuantity() });
    }

    [HttpPost]
    public IActionResult Clear()
    {
        _cart.Clear();
        return Json(new { success = true, count = 0 });
    }

    [HttpGet]
    public IActionResult Checkout()
    {
        var items = _cart.GetItems();
        if (items.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }
        return View(items);
    }

    public sealed class CheckoutRequest
    {
        public string CustomerName { get; set; } = string.Empty;
        public string CustomerEmail { get; set; } = string.Empty;
        public string? CourierInfo { get; set; }
    }

    [HttpPost]
    public async Task<IActionResult> Checkout([FromForm] CheckoutRequest req)
    {
        var items = _cart.GetItems();
        if (items.Count == 0)
        {
            return RedirectToAction(nameof(Index));
        }

        if (string.IsNullOrWhiteSpace(req.CustomerName) || string.IsNullOrWhiteSpace(req.CustomerEmail))
        {
            ModelState.AddModelError(string.Empty, "Please fill in all fields");
            return View(items);
        }

        var productIds = items.Select(i => i.ProductId).ToList();
        var products = await _db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        foreach (var item in items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                ModelState.AddModelError(string.Empty, $"Product not found (ID {item.ProductId})");
                return View(items);
            }
            if (product.Stock < item.Quantity)
            {
                ModelState.AddModelError(string.Empty, $"Sorry, only {product.Stock} items in stock for {product.Name}");
                return View(items);
            }
        }

        using var tx = await _db.Database.BeginTransactionAsync();

        foreach (var item in items)
        {
            products[item.ProductId].Stock -= item.Quantity;
        }

        var order = new Order
        {
            CustomerName = req.CustomerName,
            CustomerEmail = req.CustomerEmail,
            OrderDate = DateTime.Now,
            TotalAmount = items.Sum(i => i.UnitPrice * i.Quantity),
            CourierInfo = string.IsNullOrWhiteSpace(req.CourierInfo) ? null : req.CourierInfo.Trim(),
            UserId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null
        };

        _db.Orders.Add(order);
        await _db.SaveChangesAsync();

        foreach (var item in items)
        {
            var product = products[item.ProductId];
            _db.OrderItems.Add(new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = item.Quantity,
                UnitPrice = product.Price
            });
        }

        await _db.SaveChangesAsync();
        await tx.CommitAsync();

        _cart.Clear();
        TempData["OrderMessage"] = $"Order placed successfully! Order #{order.Id} - Total: ${order.TotalAmount:F2}";
        return RedirectToAction(nameof(Success), new { id = order.Id });
    }

    [HttpGet]
    public IActionResult Success(int id)
    {
        ViewData["OrderId"] = id;
        ViewData["Message"] = TempData["OrderMessage"]?.ToString();
        return View();
    }
}


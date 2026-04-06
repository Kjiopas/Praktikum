using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using LumiSense.Data;
using LumiSense.Models;
using Microsoft.Extensions.Localization;

namespace LumiSense.Controllers
{
    public class ShopController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;
        private readonly IStringLocalizer<SharedResource> _l;

        public ShopController(ApplicationDbContext context, UserManager<IdentityUser> userManager, IStringLocalizer<SharedResource> l)
        {
            _context = context;
            _userManager = userManager;
            _l = l;
        }

        public async Task<IActionResult> Index(string? sort = null)
        {
            sort = (sort ?? string.Empty).Trim().ToLowerInvariant();

            var baseQuery = _context.Products.AsNoTracking();
            var reviewQuery = _context.ProductReviews.AsNoTracking().Where(r => r.IsApproved);

            var itemsQuery =
                from p in baseQuery
                join r in reviewQuery on p.Id equals r.ProductId into rr
                select new ProductListItemViewModel
                {
                    Product = p,
                    ApprovedReviewCount = rr.Count(),
                    AvgRating = rr.Any() ? rr.Average(x => (double)x.Rating) : 0
                };

            // SQLite provider limitation: it can't ORDER BY decimal expressions reliably.
            // We materialize and sort on the client for SQLite.
            List<ProductListItemViewModel> items;
            if (_context.Database.IsSqlite())
            {
                items = await itemsQuery.ToListAsync();
                items = sort switch
                {
                    "price_asc" => items.OrderBy(x => x.Product.Price).ToList(),
                    "price_desc" => items.OrderByDescending(x => x.Product.Price).ToList(),
                    "rating_desc" => items.OrderByDescending(x => x.AvgRating).ThenByDescending(x => x.ApprovedReviewCount).ToList(),
                    "reviews_desc" => items.OrderByDescending(x => x.ApprovedReviewCount).ThenByDescending(x => x.AvgRating).ToList(),
                    _ => items.OrderByDescending(x => x.Product.IsPopular).ThenBy(x => x.Product.Name).ToList()
                };
            }
            else
            {
                itemsQuery = sort switch
                {
                    "price_asc" => itemsQuery.OrderBy(x => x.Product.Price),
                    "price_desc" => itemsQuery.OrderByDescending(x => x.Product.Price),
                    "rating_desc" => itemsQuery.OrderByDescending(x => x.AvgRating).ThenByDescending(x => x.ApprovedReviewCount),
                    "reviews_desc" => itemsQuery.OrderByDescending(x => x.ApprovedReviewCount).ThenByDescending(x => x.AvgRating),
                    _ => itemsQuery.OrderByDescending(x => x.Product.IsPopular).ThenBy(x => x.Product.Name)
                };
                items = await itemsQuery.ToListAsync();
            }

            ViewData["Sort"] = sort;
            return View(items);
        }

        [HttpGet("/Shop/Product/{id:int}")]
        public async Task<IActionResult> Product(int id)
        {
            var product = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return NotFound();

            var isAdmin = User.Identity?.IsAuthenticated == true && User.IsInRole("Admin");
            var userId = User.Identity?.IsAuthenticated == true ? _userManager.GetUserId(User) : null;

            IQueryable<ProductReview> q = _context.ProductReviews.AsNoTracking().Where(r => r.ProductId == id);
            if (!isAdmin)
            {
                // Non-admins see approved reviews, plus their own pending.
                q = q.Where(r => r.IsApproved || (userId != null && r.UserId == userId));
            }

            var reviews = await q
                .OrderByDescending(r => r.CreatedAtUtc)
                .Take(200)
                .ToListAsync();

            return View("Product", new ProductDetailsViewModel
            {
                Product = product,
                Reviews = reviews,
                IsAdmin = isAdmin,
                CurrentUserId = userId
            });
        }

        [HttpPost("/Shop/Product/{productId:int}/Reviews")]
        [Authorize]
        public async Task<IActionResult> AddReview(int productId, [FromForm] string text, [FromForm] int rating = 5)
        {
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
            {
                return BadRequest(new { success = false, message = _l["Reviews_ErrorTextRequired"].Value });
            }
            if (text.Length > 250)
            {
                text = text[..250];
            }

            if (rating < 1) rating = 1;
            if (rating > 5) rating = 5;

            var productExists = await _context.Products.AsNoTracking().AnyAsync(p => p.Id == productId);
            if (!productExists) return NotFound();

            var user = await _userManager.GetUserAsync(User);
            var userId = _userManager.GetUserId(User);
            if (string.IsNullOrWhiteSpace(userId) || user is null) return Forbid();

            var display = (await _userManager.GetEmailAsync(user)) ?? User.Identity?.Name;
            display = string.IsNullOrWhiteSpace(display) ? null : display.Trim();

            _context.ProductReviews.Add(new ProductReview
            {
                ProductId = productId,
                UserId = userId,
                UserDisplayName = display,
                Text = text,
                Rating = rating,
                CreatedAtUtc = DateTime.UtcNow,
                IsApproved = false
            });
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Product), new { id = productId });
        }

        [HttpPost("/Shop/Product/{id:int}/Stock")]
        [Authorize(Roles = "Admin")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> UpdateStock(int id, [FromForm] int stock)
        {
            if (stock < 0) stock = 0;

            var product = await _context.Products.FirstOrDefaultAsync(p => p.Id == id);
            if (product is null) return NotFound();

            product.Stock = stock;
            await _context.SaveChangesAsync();

            return RedirectToAction(nameof(Product), new { id });
        }

        [HttpPost]
        [Authorize]
        public async Task<IActionResult> PlaceOrder(string customerName, string customerEmail, int productId, int quantity)
        {
            if (productId <= 0)
            {
                return Json(new { success = false, message = "Product not found" });
            }

            if (string.IsNullOrWhiteSpace(customerName))
            {
                return Json(new { success = false, message = "Please enter your name" });
            }

            if (quantity <= 0) quantity = 1;

            var user = await _userManager.GetUserAsync(User);
            var userId = _userManager.GetUserId(User);
            var email = user is null ? null : await _userManager.GetEmailAsync(user);
            email = (email ?? customerEmail ?? string.Empty).Trim();

            if (string.IsNullOrWhiteSpace(userId) || string.IsNullOrWhiteSpace(email))
            {
                return Forbid();
            }

            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return Json(new { success = false, message = "Product not found" });
            }

            if (product.Stock < quantity)
            {
                return Json(new { success = false, message = $"Sorry, only {product.Stock} items in stock" });
            }

            using var tx = await _context.Database.BeginTransactionAsync();
            product.Stock -= quantity;

            var order = new Order
            {
                CustomerName = customerName,
                CustomerEmail = email,
                OrderDate = System.DateTime.Now,
                TotalAmount = product.Price * quantity,
                UserId = userId
            };

            _context.Orders.Add(order);
            await _context.SaveChangesAsync();

            var orderItem = new OrderItem
            {
                OrderId = order.Id,
                ProductId = product.Id,
                ProductName = product.Name,
                Quantity = quantity,
                UnitPrice = product.Price
            };

            _context.OrderItems.Add(orderItem);
            await _context.SaveChangesAsync();

            await tx.CommitAsync();

            return Json(new
            {
                success = true,
                message = $"Order placed successfully! Order #{order.Id} - Total: ${order.TotalAmount:F2}",
                orderId = order.Id,
                total = order.TotalAmount
            });
        }
    }
}
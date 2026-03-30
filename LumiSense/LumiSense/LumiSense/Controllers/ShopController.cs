using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using System.Linq;
using LumiSense.Data;
using LumiSense.Models;

namespace LumiSense.Controllers
{
    public class ShopController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<IdentityUser> _userManager;

        public ShopController(ApplicationDbContext context, UserManager<IdentityUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _context.Products.ToListAsync();
            return View(products);
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
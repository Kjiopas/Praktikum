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

        public ShopController(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<IActionResult> Index()
        {
            var products = await _context.Products.ToListAsync();
            return View(products);
        }

        [HttpPost]
        public async Task<IActionResult> PlaceOrder(string customerName, string customerEmail, int productId, int quantity)
        {
            var product = await _context.Products.FindAsync(productId);
            if (product == null)
            {
                return Json(new { success = false, message = "Product not found" });
            }

            if (product.Stock < quantity)
            {
                return Json(new { success = false, message = $"Sorry, only {product.Stock} items in stock" });
            }

            product.Stock -= quantity;

            var order = new Order
            {
                CustomerName = customerName,
                CustomerEmail = customerEmail,
                OrderDate = System.DateTime.Now,
                TotalAmount = product.Price * quantity
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
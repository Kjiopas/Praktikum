using System.Text.Json;
using LumiSense.Models;

namespace LumiSense.Services;

public sealed class CartSessionService
{
    private const string CartKey = "Cart";
    private readonly IHttpContextAccessor _http;

    public CartSessionService(IHttpContextAccessor http)
    {
        _http = http;
    }

    private ISession Session =>
        _http.HttpContext?.Session ?? throw new InvalidOperationException("Session is not available.");

    public List<CartItem> GetItems()
    {
        var json = Session.GetString(CartKey);
        if (string.IsNullOrWhiteSpace(json))
        {
            return new List<CartItem>();
        }

        return JsonSerializer.Deserialize<List<CartItem>>(json) ?? new List<CartItem>();
    }

    public int GetTotalQuantity() => GetItems().Sum(i => i.Quantity);

    public void SaveItems(List<CartItem> items)
    {
        var json = JsonSerializer.Serialize(items);
        Session.SetString(CartKey, json);
    }

    public void Clear() => Session.Remove(CartKey);

    public void AddOrIncrement(int productId, string name, decimal unitPrice, int quantity)
    {
        if (quantity <= 0) quantity = 1;

        var items = GetItems();
        var existing = items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is null)
        {
            items.Add(new CartItem
            {
                ProductId = productId,
                Name = name,
                UnitPrice = unitPrice,
                Quantity = quantity
            });
        }
        else
        {
            existing.Quantity += quantity;
        }

        SaveItems(items);
    }

    public void UpdateQuantity(int productId, int quantity)
    {
        var items = GetItems();
        var existing = items.FirstOrDefault(i => i.ProductId == productId);
        if (existing is null) return;

        if (quantity <= 0)
        {
            items.Remove(existing);
        }
        else
        {
            existing.Quantity = quantity;
        }

        SaveItems(items);
    }

    public void Remove(int productId)
    {
        var items = GetItems();
        items.RemoveAll(i => i.ProductId == productId);
        SaveItems(items);
    }
}


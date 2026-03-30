using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace LumiSense.Controllers;

public sealed class LocalizationController : Controller
{
    private static readonly HashSet<string> SupportedCultures =
        new(StringComparer.OrdinalIgnoreCase) { "en", "tr", "bg" };

    [HttpGet]
    public IActionResult SetLanguage(string culture, string returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(culture) || !SupportedCultures.Contains(culture))
        {
            culture = "en";
        }

        Response.Cookies.Append(
            CookieRequestCultureProvider.DefaultCookieName,
            CookieRequestCultureProvider.MakeCookieValue(new RequestCulture(culture)),
            new CookieOptions
            {
                Expires = DateTimeOffset.UtcNow.AddYears(1),
                IsEssential = true
            });

        return LocalRedirect(returnUrl);
    }
}


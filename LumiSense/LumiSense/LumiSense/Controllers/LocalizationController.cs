using Microsoft.AspNetCore.Localization;
using Microsoft.AspNetCore.Mvc;

namespace LumiSense.Controllers;

public sealed class LocalizationController : Controller
{
    [HttpGet]
    public IActionResult SetLanguage(string culture, string returnUrl = "/")
    {
        if (string.IsNullOrWhiteSpace(culture))
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


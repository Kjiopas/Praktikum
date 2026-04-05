using System.ComponentModel.DataAnnotations;
using LumiSense.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace LumiSense.Areas.Identity.Pages.Account.Manage;

[Authorize]
public sealed class IndexModel : PageModel
{
    private readonly UserManager<IdentityUser> _userManager;
    private readonly SignInManager<IdentityUser> _signInManager;
    private readonly ProfileImageStorage _profileImages;

    public IndexModel(
        UserManager<IdentityUser> userManager,
        SignInManager<IdentityUser> signInManager,
        ProfileImageStorage profileImages)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _profileImages = profileImages;
    }

    public string Email { get; set; } = string.Empty;
    public string? ProfileImagePath { get; set; }

    [TempData]
    public string? StatusMessage { get; set; }

    [BindProperty]
    public InputModel Input { get; set; } = new();

    public sealed class InputModel
    {
        [Phone]
        [MaxLength(30)]
        public string? PhoneNumber { get; set; }

        public IFormFile? ProfileImage { get; set; }
    }

    public async Task<IActionResult> OnGetAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login", new { area = "Identity" });

        Email = (await _userManager.GetEmailAsync(user)) ?? (user.Email ?? string.Empty);
        Input.PhoneNumber = await _userManager.GetPhoneNumberAsync(user);
        ProfileImagePath = await _profileImages.GetForUserAsync(user.Id);

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null) return RedirectToPage("/Account/Login", new { area = "Identity" });

        Email = (await _userManager.GetEmailAsync(user)) ?? (user.Email ?? string.Empty);

        if (!ModelState.IsValid)
        {
            ProfileImagePath = await _profileImages.GetForUserAsync(user.Id);
            return Page();
        }

        var currentPhone = await _userManager.GetPhoneNumberAsync(user);
        var newPhone = (Input.PhoneNumber ?? string.Empty).Trim();
        if (newPhone.Length > 30) newPhone = newPhone[..30];

        if (!string.Equals(currentPhone ?? string.Empty, newPhone, StringComparison.Ordinal))
        {
            var res = await _userManager.SetPhoneNumberAsync(user, string.IsNullOrWhiteSpace(newPhone) ? null : newPhone);
            if (!res.Succeeded)
            {
                ModelState.AddModelError(string.Empty, "Could not update phone number.");
                ProfileImagePath = await _profileImages.GetForUserAsync(user.Id);
                return Page();
            }
        }

        try
        {
            var updated = await _profileImages.SaveForUserAsync(user.Id, Input.ProfileImage);
            ProfileImagePath = updated ?? await _profileImages.GetForUserAsync(user.Id);
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            ProfileImagePath = await _profileImages.GetForUserAsync(user.Id);
            return Page();
        }

        await _signInManager.RefreshSignInAsync(user);
        StatusMessage = "Saved.";
        return RedirectToPage();
    }
}


using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using MtgForge.Api.Models;

namespace MtgForge.Api.Pages.Account;

public class LogoutModel : PageModel
{
    private readonly SignInManager<ApplicationUser> _signInManager;

    public LogoutModel(SignInManager<ApplicationUser> signInManager)
    {
        _signInManager = signInManager;
    }

    public async Task<IActionResult> OnGetAsync()
    {
        await _signInManager.SignOutAsync();
        return RedirectToPage("/Account/Login");
    }
}

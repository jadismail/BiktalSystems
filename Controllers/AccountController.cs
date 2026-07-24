using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Biktal.Infrastructure.Identity;

namespace Biktal.WebMVC.Controllers;

public sealed class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;

    public AccountController(UserManager<ApplicationUser> userManager)
    {
        _userManager = userManager;
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Login(string? returnUrl = null) =>
        RedirectToLocalOrPos(returnUrl);

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Login(LoginViewModel model) =>
        RedirectToLocalOrPos(model.ReturnUrl);

    [HttpGet]
    [AllowAnonymous]
    public IActionResult Register() =>
        RedirectToAction(nameof(PosController.Index), "Pos");

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public IActionResult Register(RegisterViewModel model) =>
        RedirectToAction(nameof(PosController.Index), "Pos");

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
        {
            return View(new AccountIndexViewModel
            {
                Email = User.Identity?.Name ?? "local@biktal",
                DisplayName = User.Identity?.Name ?? "Local user",
                Roles = User.FindAll(System.Security.Claims.ClaimTypes.Role)
                    .Select(c => c.Value)
                    .Distinct(StringComparer.OrdinalIgnoreCase)
                    .OrderBy(r => r, StringComparer.OrdinalIgnoreCase)
                    .ToList()
            });
        }

        var roles = await _userManager.GetRolesAsync(user);
        return View(new AccountIndexViewModel
        {
            Email = user.Email ?? user.UserName ?? "",
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? (user.UserName ?? user.Email ?? "")
                : user.DisplayName,
            Roles = roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList()
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public IActionResult Logout() =>
        RedirectToAction(nameof(PosController.Index), "Pos");

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private IActionResult RedirectToLocalOrPos(string? returnUrl)
    {
        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
            return Redirect(returnUrl);

        return RedirectToAction(nameof(PosController.Index), "Pos");
    }
}

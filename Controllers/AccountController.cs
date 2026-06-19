using Biktal.Application.Security;
using Biktal.Infrastructure.Identity;
using Biktal.WebMVC.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Biktal.WebMVC.Controllers;

public sealed class AccountController : Controller
{
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly SignInManager<ApplicationUser> _signInManager;
    private readonly IdentitySeedOptions _seedOptions;
    private readonly IWebHostEnvironment _environment;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        UserManager<ApplicationUser> userManager,
        SignInManager<ApplicationUser> signInManager,
        IOptions<IdentitySeedOptions> seedOptions,
        IWebHostEnvironment environment,
        ILogger<AccountController> logger)
    {
        _userManager = userManager;
        _signInManager = signInManager;
        _seedOptions = seedOptions.Value;
        _environment = environment;
        _logger = logger;
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Login(string? returnUrl = null)
    {
        await SetAuthPageHintsAsync();
        return View(new LoginViewModel { ReturnUrl = returnUrl });
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginViewModel model)
    {
        if (!ModelState.IsValid)
        {
            await SetAuthPageHintsAsync();
            return View(model);
        }

        var result = await _signInManager.PasswordSignInAsync(
            model.Email,
            model.Password,
            model.RememberMe,
            lockoutOnFailure: true);

        if (result.Succeeded)
        {
            var user = await _userManager.FindByEmailAsync(model.Email);
            if (user is not null)
            {
                user.LastLoginAtUtc = DateTimeOffset.UtcNow;
                await _userManager.UpdateAsync(user);
            }

            if (!string.IsNullOrEmpty(model.ReturnUrl) && Url.IsLocalUrl(model.ReturnUrl))
                return Redirect(model.ReturnUrl);

            return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
        }

        if (result.IsLockedOut)
        {
            ModelState.AddModelError(string.Empty, "This account is locked out. Try again later.");
            return View(model);
        }

        ModelState.AddModelError(string.Empty, "Invalid email or password.");
        await SetAuthPageHintsAsync();
        return View(model);
    }

    [HttpGet]
    [AllowAnonymous]
    public async Task<IActionResult> Register()
    {
        if (!await CanRegisterAsync())
            return RedirectToAction(nameof(Login));

        ViewData["IsFirstAccount"] = !await _userManager.Users.AnyAsync();
        return View(new RegisterViewModel());
    }

    [HttpPost]
    [AllowAnonymous]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Register(RegisterViewModel model)
    {
        if (!await CanRegisterAsync())
            return RedirectToAction(nameof(Login));

        if (!ModelState.IsValid)
        {
            ViewData["IsFirstAccount"] = !await _userManager.Users.AnyAsync();
            return View(model);
        }

        var isFirstUser = !await _userManager.Users.AnyAsync();

        var user = new ApplicationUser
        {
            UserName = model.Email.Trim(),
            Email = model.Email.Trim(),
            EmailConfirmed = true,
            DisplayName = model.DisplayName.Trim()
        };

        var create = await _userManager.CreateAsync(user, model.Password);
        if (!create.Succeeded)
        {
            foreach (var error in create.Errors)
                ModelState.AddModelError(string.Empty, error.Description);

            return View(model);
        }

        var role = isFirstUser ? ApplicationRoles.Admin : ApplicationRoles.Cashier;
        await _userManager.AddToRoleAsync(user, role);

        await _signInManager.SignInAsync(user, isPersistent: false);
        _logger.LogInformation("User {Email} registered with role {Role}", user.Email, role);

        if (role == ApplicationRoles.Admin)
            TempData["AccountMessage"] = "Your account was created with Administrator access. You can open Admin from the dashboard.";

        return RedirectToAction(nameof(DashboardController.Index), "Dashboard");
    }

    [HttpGet]
    [Authorize]
    public async Task<IActionResult> Index()
    {
        var user = await _userManager.GetUserAsync(User);
        if (user is null)
            return RedirectToAction(nameof(Login));

        var roles = await _userManager.GetRolesAsync(user);
        var vm = new AccountIndexViewModel
        {
            Email = user.Email ?? user.UserName ?? "",
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName) ? (user.UserName ?? user.Email ?? "") : user.DisplayName,
            Roles = roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList()
        };
        return View(vm);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    [Authorize]
    public async Task<IActionResult> Logout()
    {
        await _signInManager.SignOutAsync();
        return RedirectToAction(nameof(HomeController.Index), "Home");
    }

    [HttpGet]
    [AllowAnonymous]
    public IActionResult AccessDenied() => View();

    private async Task<bool> CanRegisterAsync()
    {
        if (_seedOptions.AllowPublicRegistration)
            return true;

        return !await _userManager.Users.AnyAsync();
    }

    private async Task SetAuthPageHintsAsync()
    {
        ViewData["ShowRegisterLink"] = await CanRegisterAsync();

        if (!_environment.IsDevelopment() || !_seedOptions.CreateBootstrapAdmin)
            return;

        var bootstrapEmail = _seedOptions.AdminEmail.Trim();
        if (string.IsNullOrWhiteSpace(bootstrapEmail))
            return;

        var bootstrapUser = await _userManager.FindByEmailAsync(bootstrapEmail);
        if (bootstrapUser is not null)
        {
            ViewData["BootstrapAdminHint"] =
                $"Development administrator: {bootstrapEmail} / password from Seed:AdminPassword in appsettings.Development.json.";
        }
    }
}

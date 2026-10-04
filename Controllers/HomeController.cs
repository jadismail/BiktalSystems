using System.Diagnostics;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biktal.WebMVC.Controllers;

public class HomeController : Controller
{
    private readonly ILogger<HomeController> _logger;

    public HomeController(ILogger<HomeController> logger)
    {
        _logger = logger;
    }

    [AllowAnonymous]
    public IActionResult Index()
    {
        if (User.Identity?.IsAuthenticated != true)
            return View();

        if (User.CanAccessSales())
            return RedirectToAction(nameof(PosController.Index), "Pos");
        if (User.CanAccessFloor())
            return RedirectToAction(nameof(RepairsController.Index), "Repairs");
        if (User.CanAccessFinance())
            return RedirectToAction(nameof(FinanceController.Index), "Finance");

        return RedirectToAction(nameof(AccountController.Index), "Account");
    }

    [AllowAnonymous]
    public IActionResult Privacy() => View();

    [AllowAnonymous]
    [ResponseCache(Duration = 0, Location = ResponseCacheLocation.None, NoStore = true)]
    public IActionResult Error()
    {
        return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
    }
}

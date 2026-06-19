using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biktal.WebMVC.Controllers;

[Authorize]
public sealed class LicenseController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "Subscription & license";
        ViewData["Module"] = "License";
        ViewData["ModuleSubtitle"] = "On-prem activation, weekly validation, and feature flags from Biktal Cloud.";
        return View();
    }
}

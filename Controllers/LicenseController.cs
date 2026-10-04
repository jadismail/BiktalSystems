using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Management)]
public sealed class LicenseController : Controller
{
    public IActionResult Index()
    {
        ViewData["Title"] = "License";
        ViewData["Module"] = "License";
        return View();
    }
}

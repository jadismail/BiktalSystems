using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Biktal.WebMVC.Controllers;

/// <summary>Legacy routes — operational reports moved to Operations; business insights to Insights.</summary>
[Authorize(Roles = AppRoleGroups.Management)]
public sealed class ReportsController : Controller
{
    public IActionResult Index() => RedirectToActionPermanent("Index", "Operations");

    public IActionResult Sales() => RedirectToActionPermanent("Sales", "Operations");

    public IActionResult Inventory() => RedirectToActionPermanent("Inventory", "Operations");

    public IActionResult Repairs() => RedirectToActionPermanent("Repairs", "Operations");

    public IActionResult Financial() => RedirectToActionPermanent("Accounting", "Finance");
}

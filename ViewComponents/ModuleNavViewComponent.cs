using Microsoft.AspNetCore.Mvc;

namespace Biktal.WebMVC.ViewComponents;

public sealed record ModuleNavLink(string Label, string Action, string? Controller = null);

public sealed class ModuleNavViewModel
{
    public required string Controller { get; init; }

    public required string ActiveAction { get; init; }

    public required IReadOnlyList<ModuleNavLink> Links { get; init; }
}

public sealed class ModuleNavViewComponent : ViewComponent
{
    private static readonly Dictionary<string, ModuleNavLink[]> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            ["Inventory"] =
            [
                new("Overview", "Index"),
                new("Products", "Products"),
                new("Import Excel", "Import"),
                new("Categories", "Categories"),
                new("Brands", "Brands"),
                new("Stock", "Stock"),
                new("Movements", "Movements"),
                new("Warehouses", "Warehouses"),
                new("Purchase orders", "PurchaseOrders"),
                new("Suppliers", "Suppliers")
            ],
            ["Repairs"] =
            [
                new("Overview", "Index"),
                new("Tickets", "Tickets"),
                new("New check-in", "New")
            ],
            ["Finance"] =
            [
                new("Overview", "Index"),
                new("Accounting", "Accounting"),
                new("Chart of accounts", "Accounts"),
                new("Journal entries", "Journal"),
                new("New journal", "NewJournal"),
                new("Cashbox", "Cashbox"),
                new("Expenses", "Expenses"),
                new("Aging (AR/AP)", "Aging")
            ],
            ["Crm"] =
            [
                new("Overview", "Index"),
                new("Customers", "Customers"),
                new("New customer", "NewCustomer"),
                new("Installments", "Index", "Installments"),
                new("Loyalty", "Loyalty"),
                new("Campaigns", "Campaigns"),
                new("Support tickets", "Tickets"),
                new("New ticket", "NewTicket")
            ],
            ["Installments"] =
            [
                new("Overview", "Index", "Crm"),
                new("Customers", "Customers", "Crm"),
                new("Installments", "Index", "Installments"),
                new("New plan", "New", "Installments"),
                new("Loyalty", "Loyalty", "Crm"),
                new("Campaigns", "Campaigns", "Crm"),
                new("Support tickets", "Tickets", "Crm")
            ],
            ["Operations"] =
            [
                new("Operations hub", "Index"),
                new("Daily sales", "DailySales"),
                new("Monthly sales", "MonthlySales"),
                new("Sales", "Sales"),
                new("Inventory", "Inventory"),
                new("Low stock", "LowStock"),
                new("Repair revenue", "Repairs"),
                new("Purchase orders", "PurchaseOrders"),
                new("Stock movement", "StockMovement")
            ],
            ["Insights"] =
            [
                new("Business insights", "Index")
            ],
            ["Admin"] =
            [
                new("Overview", "Index"),
                new("Settings", "Settings"),
                new("Branches", "Branches"),
                new("Tax & currency", "Tax"),
                new("Users & roles", "Users"),
                new("Backups", "Backups"),
                new("Reset data", "DataReset")
            ],
            ["Staff"] =
            [
                new("Overview", "Index"),
                new("Team", "Team"),
                new("New member", "NewMember"),
                new("Attendance", "Attendance"),
                new("Payroll", "Payroll"),
                new("Activity log", "Activity")
            ]
        };

    public IViewComponentResult Invoke()
    {
        var controller = ViewContext.RouteData.Values["controller"]?.ToString() ?? "";
        if (!Map.TryGetValue(controller, out var links) || links.Length <= 1)
            return Content(string.Empty);

        var action = ViewContext.RouteData.Values["action"]?.ToString() ?? "Index";
        var vm = new ModuleNavViewModel
        {
            Controller = controller,
            ActiveAction = action,
            Links = links
        };

        return View(vm);
    }
}

using System.Security.Claims;
using Biktal.Application.Security;

namespace Biktal.WebMVC.Security;

public static class PrincipalRoleExtensions
{
    public static bool IsManagement(this ClaimsPrincipal user) =>
        user.IsInRole(ApplicationRoles.Admin) || user.IsInRole(ApplicationRoles.Manager);

    public static bool CanAccessFinance(this ClaimsPrincipal user) =>
        user.IsManagement() || user.IsInRole(ApplicationRoles.Accountant);

    public static bool CanAccessCashbox(this ClaimsPrincipal user) =>
        user.IsManagement()
        || user.IsInRole(ApplicationRoles.Cashier)
        || user.IsInRole(ApplicationRoles.Accountant);

    public static bool CanAccessSales(this ClaimsPrincipal user) =>
        user.IsManagement() || user.IsInRole(ApplicationRoles.Cashier);

    public static bool CanAccessFloor(this ClaimsPrincipal user) =>
        user.IsManagement()
        || user.IsInRole(ApplicationRoles.Cashier)
        || user.IsInRole(ApplicationRoles.Technician);

    /// <summary>Monthly/Yearly insights — Admin (owner) only.</summary>
    public static bool CanAccessInsightsPeriods(this ClaimsPrincipal user) =>
        user.IsInRole(ApplicationRoles.Admin);

    /// <summary>Clear gross profit on Daily insights — Admin/Manager only.</summary>
    public static bool CanViewGrossProfit(this ClaimsPrincipal user) =>
        user.IsInRole(ApplicationRoles.Admin) || user.IsInRole(ApplicationRoles.Manager);
}

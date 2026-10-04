using Biktal.Application.Security;

namespace Biktal.WebMVC.Security;

/// <summary>
/// Comma-separated role lists for <see cref="Microsoft.AspNetCore.Authorization.AuthorizeAttribute.Roles"/>.
/// Cashier/Technician are excluded from management pages.
/// </summary>
public static class AppRoleGroups
{
    /// <summary>Owner + manager (Admin, Manager).</summary>
    public const string Management = ApplicationRoles.AdminModule;

    /// <summary>Finance ledgers and journals.</summary>
    public const string Finance =
        ApplicationRoles.Admin + "," + ApplicationRoles.Manager + "," + ApplicationRoles.Accountant;

    /// <summary>Daily cashbox / Accounts sessions.</summary>
    public const string Cashbox =
        ApplicationRoles.Admin + "," + ApplicationRoles.Manager + "," + ApplicationRoles.Cashier + "," + ApplicationRoles.Accountant;

    /// <summary>POS, CRM, and installment collections.</summary>
    public const string Sales =
        ApplicationRoles.Admin + "," + ApplicationRoles.Manager + "," + ApplicationRoles.Cashier;

    /// <summary>Inventory + repairs floor access.</summary>
    public const string Floor =
        ApplicationRoles.Admin + "," + ApplicationRoles.Manager + "," + ApplicationRoles.Cashier + "," + ApplicationRoles.Technician;

    /// <summary>Owner/admin only (Admin role) for period insights.</summary>
    public const string OwnerAdmin = ApplicationRoles.Admin;
}

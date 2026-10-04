using Biktal.Application.Security;
using Biktal.Domain.Admin;
using Biktal.Infrastructure.Identity;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Models;
using Biktal.WebMVC.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Biktal.WebMVC.Controllers;

[Authorize(Roles = AppRoleGroups.Management)]
public sealed class AdminController : Controller
{
    private readonly ApplicationDbContext _db;
    private readonly UserManager<ApplicationUser> _userManager;
    private readonly IdentitySeedOptions _seedOptions;
    private readonly BusinessDataResetService _dataReset;

    public AdminController(
        ApplicationDbContext db,
        UserManager<ApplicationUser> userManager,
        IOptions<IdentitySeedOptions> seedOptions,
        BusinessDataResetService dataReset)
    {
        _db = db;
        _userManager = userManager;
        _seedOptions = seedOptions.Value;
        _dataReset = dataReset;
    }

    private async Task<TenantSettings> GetOrCreateSettingsAsync(CancellationToken cancellationToken)
    {
        var settings = await _db.TenantSettings.FirstOrDefaultAsync(cancellationToken);
        if (settings is not null)
            return settings;

        settings = new TenantSettings
        {
            Id = AdminDataSeeder.SettingsId,
            StoreDisplayName = "Biktal Main Store",
            DefaultLocale = "en-US",
            ReceiptFooter = null,
            DefaultTaxRate = 0.09m,
            BaseCurrencyCode = "USD",
            PricesTaxInclusive = true,
            LbpPerUsd = 89_500m,
            BackupScheduleNotes = "Nightly pg_dump to secured NAS.",
            UpdatedAtUtc = DateTimeOffset.UtcNow
        };
        _db.TenantSettings.Add(settings);
        await _db.SaveChangesAsync(cancellationToken);
        return settings;
    }

    [HttpGet]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Administration";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Tenant-wide configuration and operational controls.";

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        var branchCount = await _db.AdminBranches.AsNoTracking().CountAsync(cancellationToken);
        var activeBranches = await _db.AdminBranches.AsNoTracking().CountAsync(b => b.IsActive, cancellationToken);
        var userCount = await _userManager.Users.CountAsync(cancellationToken);
        var lastBackup = await _db.AdminBackupRecords.AsNoTracking()
            .Where(b => b.Status == AdminBackupStatus.Success)
            .OrderByDescending(b => b.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        return View(new AdminIndexViewModel
        {
            StoreDisplayName = settings.StoreDisplayName,
            BranchCount = branchCount,
            ActiveBranchCount = activeBranches,
            UserCount = userCount,
            LastSuccessfulBackup = lastBackup
        });
    }

    [HttpGet]
    public async Task<IActionResult> Settings(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "System settings";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Locale, numbering, receipt templates, and integrations.";

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        return View(new AdminSettingsFormModel
        {
            StoreDisplayName = settings.StoreDisplayName,
            DefaultLocale = settings.DefaultLocale,
            ReceiptFooter = settings.ReceiptFooter
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Settings(AdminSettingsFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "System settings";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Locale, numbering, receipt templates, and integrations.";

        if (!ModelState.IsValid)
            return View(model);

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.StoreDisplayName = model.StoreDisplayName.Trim();
        settings.DefaultLocale = model.DefaultLocale.Trim();
        settings.ReceiptFooter = string.IsNullOrWhiteSpace(model.ReceiptFooter) ? null : model.ReceiptFooter.Trim();
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminMessage"] = "Settings saved.";
        return RedirectToAction(nameof(Settings));
    }

    [HttpGet]
    public async Task<IActionResult> Tax(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Tax & currency";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "VAT rules, inclusive/exclusive pricing, and FX.";

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        return View(new AdminTaxFormModel
        {
            DefaultTaxRate = settings.DefaultTaxRate,
            BaseCurrencyCode = settings.BaseCurrencyCode,
            PricesTaxInclusive = settings.PricesTaxInclusive,
            LbpPerUsd = settings.LbpPerUsd
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Tax(AdminTaxFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Tax & currency";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "VAT rules, inclusive/exclusive pricing, and FX.";

        if (!ModelState.IsValid)
            return View(model);

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.DefaultTaxRate = model.DefaultTaxRate;
        settings.BaseCurrencyCode = model.BaseCurrencyCode.Trim().ToUpperInvariant();
        settings.PricesTaxInclusive = model.PricesTaxInclusive;
        settings.LbpPerUsd = model.LbpPerUsd;
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminMessage"] = "Tax and currency settings saved.";
        return RedirectToAction(nameof(Tax));
    }

    [HttpGet]
    public async Task<IActionResult> Branches(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Branches";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Stores, warehouses, and branch-level permissions.";

        var rows = await _db.AdminBranches.AsNoTracking()
            .OrderBy(b => b.Name)
            .Select(b => new AdminBranchListRowViewModel
            {
                Id = b.Id,
                Name = b.Name,
                Code = b.Code,
                TimezoneId = b.TimezoneId,
                IsActive = b.IsActive
            })
            .ToListAsync(cancellationToken);

        return View(new AdminBranchesPageViewModel { Branches = rows });
    }

    [HttpGet]
    public IActionResult NewBranch()
    {
        ViewData["Title"] = "Add branch";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Create a store or warehouse location.";
        return View(new AdminBranchFormModel());
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateBranch(AdminBranchFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Add branch";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Create a store or warehouse location.";

        if (!ModelState.IsValid)
            return View("NewBranch", model);

        var code = model.Code.Trim().ToUpperInvariant();
        if (await _db.AdminBranches.AnyAsync(b => b.Code == code, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Code), "That branch code is already in use.");
            return View("NewBranch", model);
        }

        _db.AdminBranches.Add(new AdminBranch
        {
            Id = Guid.NewGuid(),
            Name = model.Name.Trim(),
            Code = code,
            TimezoneId = model.TimezoneId.Trim(),
            IsActive = model.IsActive,
            CreatedAtUtc = DateTimeOffset.UtcNow
        });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminMessage"] = "Branch created.";
        return RedirectToAction(nameof(Branches));
    }

    [HttpGet]
    public async Task<IActionResult> EditBranch(Guid id, CancellationToken cancellationToken)
    {
        var branch = await _db.AdminBranches.AsNoTracking()
            .FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (branch is null)
            return NotFound();

        ViewData["Title"] = "Edit branch";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = branch.Name;

        return View(new AdminBranchFormModel
        {
            Id = branch.Id,
            Name = branch.Name,
            Code = branch.Code,
            TimezoneId = branch.TimezoneId,
            IsActive = branch.IsActive
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditBranch(AdminBranchFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Edit branch";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = model.Name;

        if (!model.Id.HasValue)
            return NotFound();

        if (!ModelState.IsValid)
            return View(model);

        var branch = await _db.AdminBranches.FirstOrDefaultAsync(b => b.Id == model.Id.Value, cancellationToken);
        if (branch is null)
            return NotFound();

        var code = model.Code.Trim().ToUpperInvariant();
        if (await _db.AdminBranches.AnyAsync(b => b.Code == code && b.Id != branch.Id, cancellationToken))
        {
            ModelState.AddModelError(nameof(model.Code), "That branch code is already in use.");
            return View(model);
        }

        branch.Name = model.Name.Trim();
        branch.Code = code;
        branch.TimezoneId = model.TimezoneId.Trim();
        branch.IsActive = model.IsActive;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminMessage"] = "Branch updated.";
        return RedirectToAction(nameof(Branches));
    }

    [HttpGet]
    public async Task<IActionResult> Users(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Users & roles";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Create accounts, assign roles, reset passwords, and remove users.";

        var currentUserId = _userManager.GetUserId(User);
        var users = await _userManager.Users
            .OrderBy(u => u.Email)
            .ToListAsync(cancellationToken);

        var rows = new List<AdminUserListRowViewModel>(users.Count);
        foreach (var user in users)
        {
            var roles = await _userManager.GetRolesAsync(user);
            rows.Add(new AdminUserListRowViewModel
            {
                Id = user.Id,
                Email = user.Email ?? user.UserName ?? "",
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                    ? (user.UserName ?? user.Email ?? "")
                    : user.DisplayName,
                HireDate = user.HireDate,
                Roles = roles.OrderBy(r => r, StringComparer.OrdinalIgnoreCase).ToList(),
                LastLoginAtUtc = user.LastLoginAtUtc,
                EmailConfirmed = user.EmailConfirmed,
                IsLockedOut = await _userManager.IsLockedOutAsync(user),
                IsCurrentUser = user.Id == currentUserId,
                CanDelete = await CanDeleteUserAsync(user)
            });
        }

        return View(new AdminUsersPageViewModel
        {
            Users = rows,
            CanInviteViaRegister = _seedOptions.AllowPublicRegistration || users.Count == 0
        });
    }

    [HttpGet]
    public async Task<IActionResult> NewUser()
    {
        ViewData["Title"] = "Create user";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Add a staff login with roles.";

        var canAssignAdmin = await CurrentUserCanAssignAdminRoleAsync();
        return View(new AdminCreateUserFormModel
        {
            HireDate = DateOnly.FromDateTime(DateTime.Today),
            AllRoles = GetAssignableRoles(canAssignAdmin)
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> CreateUser(AdminCreateUserFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Create user";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Add a staff login with roles.";

        var canAssignAdmin = await CurrentUserCanAssignAdminRoleAsync();
        model.AllRoles = GetAssignableRoles(canAssignAdmin);

        if (!ModelState.IsValid)
            return View("NewUser", model);

        var email = model.Email.Trim();
        if (await _userManager.FindByEmailAsync(email) is not null)
        {
            ModelState.AddModelError(nameof(model.Email), "A user with that email already exists.");
            return View("NewUser", model);
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            DisplayName = string.IsNullOrWhiteSpace(model.DisplayName) ? null : model.DisplayName.Trim(),
            HireDate = model.HireDate
        };

        var create = await _userManager.CreateAsync(user, model.Password);
        if (!create.Succeeded)
        {
            foreach (var error in create.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            return View("NewUser", model);
        }

        var roles = SanitizeRoles(model.SelectedRoles, canAssignAdmin);
        if (roles.Count == 0)
            roles.Add(ApplicationRoles.Cashier);

        await _userManager.AddToRolesAsync(user, roles);

        TempData["AdminMessage"] = $"User {email} created.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public Task<IActionResult> EditUserRoles(string id, CancellationToken cancellationToken) =>
        EditUser(id, cancellationToken);

    [HttpGet]
    public async Task<IActionResult> EditUser(string id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        ViewData["Title"] = "Manage user";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = user.Email ?? user.UserName ?? "";

        return View(await BuildEditUserFormAsync(user));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> EditUser(AdminEditUserFormModel model, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user is null)
            return NotFound();

        ViewData["Title"] = "Manage user";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = user.Email ?? user.UserName ?? "";

        var canAssignAdmin = await CurrentUserCanAssignAdminRoleAsync();
        model.AllRoles = GetAssignableRoles(canAssignAdmin);
        model.CanAssignAdminRole = canAssignAdmin;
        model.IsCurrentUser = user.Id == _userManager.GetUserId(User);
        model.Email = user.Email ?? user.UserName ?? "";
        model.CanDelete = await CanDeleteUserAsync(user);

        if (!ModelState.IsValid)
        {
            model.IsLockedOut = await _userManager.IsLockedOutAsync(user);
            return View(model);
        }

        var selected = SanitizeRoles(model.SelectedRoles, canAssignAdmin);
        if (selected.Count == 0)
        {
            ModelState.AddModelError(string.Empty, "Select at least one role.");
            model.IsLockedOut = await _userManager.IsLockedOutAsync(user);
            return View(model);
        }

        var roleError = await ValidateRoleChangeAsync(user, selected);
        if (roleError is not null)
        {
            ModelState.AddModelError(string.Empty, roleError);
            model.IsLockedOut = await _userManager.IsLockedOutAsync(user);
            return View(model);
        }

        user.DisplayName = model.DisplayName.Trim();
        user.HireDate = model.HireDate;
        var update = await _userManager.UpdateAsync(user);
        if (!update.Succeeded)
        {
            foreach (var error in update.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            model.IsLockedOut = await _userManager.IsLockedOutAsync(user);
            return View(model);
        }

        var existing = await _userManager.GetRolesAsync(user);
        var toRemove = existing.Except(selected, StringComparer.OrdinalIgnoreCase).ToList();
        var toAdd = selected.Except(existing, StringComparer.OrdinalIgnoreCase).ToList();
        if (toRemove.Count > 0)
            await _userManager.RemoveFromRolesAsync(user, toRemove);
        if (toAdd.Count > 0)
            await _userManager.AddToRolesAsync(user, toAdd);

        if (model.LockAccount)
        {
            await _userManager.SetLockoutEnabledAsync(user, true);
            await _userManager.SetLockoutEndDateAsync(user, DateTimeOffset.UtcNow.AddYears(100));
        }
        else if (await _userManager.IsLockedOutAsync(user))
        {
            await _userManager.SetLockoutEndDateAsync(user, null);
        }

        TempData["AdminMessage"] = "User updated.";
        return RedirectToAction(nameof(Users));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> ResetUserPassword(AdminResetUserPasswordFormModel model, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(model.UserId);
        if (user is null)
            return NotFound();

        if (!ModelState.IsValid)
        {
            ViewData["Title"] = "Manage user";
            ViewData["Module"] = "Admin";
            ViewData["ModuleSubtitle"] = user.Email ?? user.UserName ?? "";
            ViewData["PasswordFormInvalid"] = true;
            return View("EditUser", await BuildEditUserFormAsync(user));
        }

        var token = await _userManager.GeneratePasswordResetTokenAsync(user);
        var reset = await _userManager.ResetPasswordAsync(user, token, model.NewPassword);
        if (!reset.Succeeded)
        {
            foreach (var error in reset.Errors)
                ModelState.AddModelError(string.Empty, error.Description);
            ViewData["Title"] = "Manage user";
            ViewData["Module"] = "Admin";
            ViewData["ModuleSubtitle"] = user.Email ?? user.UserName ?? "";
            ViewData["PasswordFormInvalid"] = true;
            return View("EditUser", await BuildEditUserFormAsync(user));
        }

        if (await _userManager.IsLockedOutAsync(user))
            await _userManager.SetLockoutEndDateAsync(user, null);

        TempData["AdminMessage"] = $"Password reset for {user.Email}.";
        return RedirectToAction(nameof(EditUser), new { id = user.Id });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DeleteUser(string id, CancellationToken cancellationToken)
    {
        var user = await _userManager.FindByIdAsync(id);
        if (user is null)
            return NotFound();

        if (user.Id == _userManager.GetUserId(User))
        {
            TempData["AdminError"] = "You cannot delete your own account.";
            return RedirectToAction(nameof(Users));
        }

        if (!await CanDeleteUserAsync(user))
        {
            TempData["AdminError"] = "Cannot delete the last administrator account.";
            return RedirectToAction(nameof(Users));
        }

        var email = user.Email ?? user.UserName ?? user.Id;
        var delete = await _userManager.DeleteAsync(user);
        if (!delete.Succeeded)
        {
            TempData["AdminError"] = string.Join(" ", delete.Errors.Select(e => e.Description));
            return RedirectToAction(nameof(Users));
        }

        TempData["AdminMessage"] = $"User {email} was deleted.";
        return RedirectToAction(nameof(Users));
    }

    [HttpGet]
    public async Task<IActionResult> Backups(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Backups";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "On-prem backup jobs and restore drills.";

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        var lastSuccess = await _db.AdminBackupRecords.AsNoTracking()
            .Where(b => b.Status == AdminBackupStatus.Success)
            .OrderByDescending(b => b.CompletedAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        var recent = await _db.AdminBackupRecords.AsNoTracking()
            .OrderByDescending(b => b.CompletedAtUtc)
            .Take(12)
            .ToListAsync(cancellationToken);

        return View(new AdminBackupsPageViewModel
        {
            BackupScheduleNotes = settings.BackupScheduleNotes,
            LastSuccessfulBackup = lastSuccess,
            RecentBackups = recent
        });
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> SaveBackupSchedule(AdminBackupScheduleFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Backups));

        var settings = await GetOrCreateSettingsAsync(cancellationToken);
        settings.BackupScheduleNotes = model.BackupScheduleNotes.Trim();
        settings.UpdatedAtUtc = DateTimeOffset.UtcNow;
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminMessage"] = "Backup schedule notes saved.";
        return RedirectToAction(nameof(Backups));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> LogBackup(AdminLogBackupFormModel model, CancellationToken cancellationToken)
    {
        if (!ModelState.IsValid)
            return RedirectToAction(nameof(Backups));

        _db.AdminBackupRecords.Add(new AdminBackupRecord
        {
            Id = Guid.NewGuid(),
            Label = model.Label.Trim(),
            Status = AdminBackupStatus.Success,
            CompletedAtUtc = DateTimeOffset.UtcNow,
            Checksum = $"sha256:{Guid.NewGuid():N}"[..24],
            Notes = string.IsNullOrWhiteSpace(model.Notes) ? null : model.Notes.Trim(),
            SizeBytes = null
        });
        await _db.SaveChangesAsync(cancellationToken);

        TempData["AdminMessage"] = "Backup recorded.";
        return RedirectToAction(nameof(Backups));
    }

    private async Task<AdminEditUserFormModel> BuildEditUserFormAsync(ApplicationUser user)
    {
        var canAssignAdmin = await CurrentUserCanAssignAdminRoleAsync();
        var roles = await _userManager.GetRolesAsync(user);
        var isLocked = await _userManager.IsLockedOutAsync(user);

        return new AdminEditUserFormModel
        {
            UserId = user.Id,
            Email = user.Email ?? user.UserName ?? "",
            DisplayName = string.IsNullOrWhiteSpace(user.DisplayName)
                ? (user.UserName ?? user.Email ?? "")
                : user.DisplayName,
            HireDate = user.HireDate,
            SelectedRoles = roles.ToList(),
            AllRoles = GetAssignableRoles(canAssignAdmin),
            CanAssignAdminRole = canAssignAdmin,
            IsLockedOut = isLocked,
            LockAccount = isLocked,
            IsCurrentUser = user.Id == _userManager.GetUserId(User),
            CanDelete = await CanDeleteUserAsync(user)
        };
    }

    private async Task<bool> CurrentUserCanAssignAdminRoleAsync()
    {
        var current = await _userManager.GetUserAsync(User);
        if (current is null)
            return false;

        var roles = await _userManager.GetRolesAsync(current);
        return roles.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase);
    }

    private static IReadOnlyList<string> GetAssignableRoles(bool includeAdmin) =>
        includeAdmin
            ? ApplicationRoles.All
            : ApplicationRoles.All
                .Where(r => !string.Equals(r, ApplicationRoles.Admin, StringComparison.OrdinalIgnoreCase))
                .ToList();

    private static List<string> SanitizeRoles(IEnumerable<string> selected, bool includeAdmin)
    {
        var allowed = GetAssignableRoles(includeAdmin);
        return selected
            .Where(r => allowed.Contains(r, StringComparer.OrdinalIgnoreCase))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private async Task<string?> ValidateRoleChangeAsync(ApplicationUser user, IReadOnlyList<string> selected)
    {
        var currentUserId = _userManager.GetUserId(User);
        var existing = await _userManager.GetRolesAsync(user);

        if (user.Id == currentUserId
            && existing.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && !selected.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return "You cannot remove the Admin role from your own account.";
        }

        if (existing.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && !selected.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && await CountUsersInRoleAsync(ApplicationRoles.Admin) <= 1)
        {
            return "Cannot remove Admin from the last administrator account.";
        }

        if (!await CurrentUserCanAssignAdminRoleAsync()
            && !existing.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase)
            && selected.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase))
        {
            return "Only administrators can grant the Admin role.";
        }

        return null;
    }

    private async Task<bool> CanDeleteUserAsync(ApplicationUser user)
    {
        if (user.Id == _userManager.GetUserId(User))
            return false;

        var roles = await _userManager.GetRolesAsync(user);
        if (!roles.Contains(ApplicationRoles.Admin, StringComparer.OrdinalIgnoreCase))
            return true;

        return await CountUsersInRoleAsync(ApplicationRoles.Admin) > 1;
    }

    private async Task<int> CountUsersInRoleAsync(string role)
    {
        var users = await _userManager.GetUsersInRoleAsync(role);
        return users.Count;
    }

    [HttpGet]
    public async Task<IActionResult> DataReset(CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Reset data";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Clear business data by area for a fresh start. Users and settings are kept.";
        return View(await BuildDataResetPageAsync(cancellationToken));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> DataReset(AdminDataResetFormModel model, CancellationToken cancellationToken)
    {
        ViewData["Title"] = "Reset data";
        ViewData["Module"] = "Admin";
        ViewData["ModuleSubtitle"] = "Clear business data by area for a fresh start. Users and settings are kept.";

        if (!ModelState.IsValid)
            return View(await BuildDataResetPageAsync(cancellationToken));

        if (!string.Equals(model.Confirmation.Trim(), "RESET", StringComparison.Ordinal))
        {
            ModelState.AddModelError(nameof(AdminDataResetFormModel.Confirmation), "Type RESET exactly to confirm.");
            return View(await BuildDataResetPageAsync(cancellationToken));
        }

        if (!Enum.TryParse<BusinessDataResetArea>(model.Area, ignoreCase: true, out var area))
        {
            TempData["AdminError"] = "Unknown reset area.";
            return RedirectToAction(nameof(DataReset));
        }

        var result = await _dataReset.ResetAsync(area, cancellationToken);
        if (!result.Success)
        {
            TempData["AdminError"] = result.Error ?? "Reset failed.";
            return RedirectToAction(nameof(DataReset));
        }

        TempData["AdminMessage"] = result.Message;
        return RedirectToAction(nameof(DataReset));
    }

    private async Task<AdminDataResetPageViewModel> BuildDataResetPageAsync(CancellationToken cancellationToken)
    {
        var counts = await _dataReset.GetCountsAsync(cancellationToken);

        var areas = new List<AdminDataResetRowViewModel>
        {
            new()
            {
                Key = nameof(BusinessDataResetArea.PosAndCashbox),
                Title = "POS & cashbox",
                Description = "Completed sales, payment splits, and cashbox open/close sessions.",
                CountLabel = $"{counts.PosSales:N0} sale(s), {counts.CashboxSessions:N0} session(s)",
                ConfirmMessage = "Delete all POS sales and cashbox sessions?"
            },
            new()
            {
                Key = nameof(BusinessDataResetArea.Inventory),
                Title = "Inventory & procurement",
                Description = "Products, stock movements, purchase orders, categories, brands, warehouses, and suppliers.",
                CountLabel =
                    $"{counts.CatalogProducts:N0} product(s), {counts.InventoryMovements:N0} movement(s), {counts.PurchaseOrders:N0} PO(s)",
                ConfirmMessage = "Delete all inventory and procurement data?"
            },
            new()
            {
                Key = nameof(BusinessDataResetArea.Finance),
                Title = "Finance ledger",
                Description = "Journal entries, expenses, and cashbox sessions. Chart of accounts is kept.",
                CountLabel = $"{counts.JournalEntries:N0} journal(s), {counts.FinanceExpenses:N0} expense(s)",
                ConfirmMessage = "Delete all journals and expenses? Account codes will remain."
            },
            new()
            {
                Key = nameof(BusinessDataResetArea.Repairs),
                Title = "Repairs",
                Description = "Repair tickets and check-in history.",
                CountLabel = $"{counts.RepairTickets:N0} ticket(s)",
                ConfirmMessage = "Delete all repair tickets?"
            },
            new()
            {
                Key = nameof(BusinessDataResetArea.Crm),
                Title = "CRM",
                Description = "Customers, support tickets, and campaigns.",
                CountLabel =
                    $"{counts.CrmCustomers:N0} customer(s), {counts.CrmSupportTickets:N0} ticket(s), {counts.CrmCampaigns:N0} campaign(s)",
                ConfirmMessage = "Delete all CRM data?"
            },
            new()
            {
                Key = nameof(BusinessDataResetArea.Staff),
                Title = "Staff",
                Description = "Team members, attendance, payroll runs, and activity log.",
                CountLabel =
                    $"{counts.StaffMembers:N0} member(s), {counts.StaffPayrollRuns:N0} payroll run(s)",
                ConfirmMessage = "Delete all staff records?"
            },
            new()
            {
                Key = nameof(BusinessDataResetArea.Everything),
                Title = "Reset everything",
                Description =
                    "All of the above. Keeps login users, store settings, branches, backups, and chart of accounts.",
                CountLabel = "Full business data wipe",
                ConfirmMessage = "Delete ALL business data? This cannot be undone."
            }
        };

        return new AdminDataResetPageViewModel
        {
            Counts = counts,
            Areas = areas
        };
    }
}

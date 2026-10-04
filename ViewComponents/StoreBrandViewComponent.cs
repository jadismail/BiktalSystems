using Biktal.Infrastructure.Persistence;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.ViewComponents;

public sealed class StoreBrandViewComponent : ViewComponent
{
    private const string FallbackName = "Khulasa Retail";

    private readonly ApplicationDbContext _db;

    public StoreBrandViewComponent(ApplicationDbContext db)
    {
        _db = db;
    }

    public async Task<IViewComponentResult> InvokeAsync()
    {
        string name;
        try
        {
            name = await _db.TenantSettings.AsNoTracking()
                .Select(s => s.StoreDisplayName)
                .FirstOrDefaultAsync() ?? FallbackName;
        }
        catch
        {
            name = FallbackName;
        }

        if (string.IsNullOrWhiteSpace(name))
            name = FallbackName;

        return View((object)name.Trim());
    }
}

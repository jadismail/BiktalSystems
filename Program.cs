using Biktal.Infrastructure;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Identity;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddInfrastructure(builder.Configuration);

// Keep passwords simple for store staff accounts.
builder.Services.Configure<IdentityOptions>(options =>
{
    options.Password.RequiredLength = 1;
    options.Password.RequireDigit = false;
    options.Password.RequireLowercase = false;
    options.Password.RequireUppercase = false;
    options.Password.RequireNonAlphanumeric = false;
    options.Password.RequiredUniqueChars = 0;
});

// InstallmentPlanService attaches new payments via plan.Payments.Add(...) with client GUIDs,
// so EF tracks them as Modified and SaveChanges issues a 0-row UPDATE (DbUpdateConcurrencyException).
// NewRowInsertFixInterceptor flips those to Added — it must be wired with AddInterceptors
// (DI registration alone is not enough).
builder.Services.AddSingleton<NewRowInsertFixInterceptor>();
builder.Services.ConfigureDbContext<ApplicationDbContext>((sp, options) =>
{
    options.AddInterceptors(sp.GetRequiredService<NewRowInsertFixInterceptor>());
});

builder.Services.AddScoped<CashboxSessionService>();
builder.Services.AddScoped<InstallmentPlanService>();

builder.Services.AddAuthorization();

builder.Services.AddControllersWithViews();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}
else
{
    app.UseDeveloperExceptionPage();
}

app.UseSerilogRequestLogging();
app.UseHttpsRedirection();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger("Startup");

    try
    {
        var db = services.GetRequiredService<ApplicationDbContext>();
        await db.Database.MigrateAsync();

        async Task RunSeedAsync(string name, Func<Task> seed)
        {
            try
            {
                await seed();
            }
            catch (Exception ex)
            {
                db.ChangeTracker.Clear();
                logger.LogError(ex, "Seed step '{SeedName}' failed; continuing with remaining seeds.", name);
            }
        }

        await RunSeedAsync("CatalogProduct", () => CatalogProductSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("CatalogCategory", () => CatalogCategorySeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("CatalogBrand", () => CatalogBrandSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("CatalogWarehouse", () => CatalogWarehouseSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("CatalogSupplier", () => CatalogSupplierSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("RepairTicket", () => RepairTicketSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("Finance", () => FinanceDataSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("FinanceSalesTax", () => FinanceDataSeeder.EnsureSalesTaxPayableAccountAsync(db));
        await RunSeedAsync("FinanceInventoryAccounts", () => FinanceDataSeeder.EnsureInventoryMovementAccountsAsync(db));
        await RunSeedAsync("FinanceCashboxNames", () => FinanceDataSeeder.EnsureCashboxAccountDisplayNamesAsync(db));
        await RunSeedAsync("Crm", () => SafeCrmDataSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("Staff", () => StaffDataSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("Admin", () => AdminDataSeeder.SeedIfEmptyAsync(db));
        await RunSeedAsync("Identity", () => IdentitySeeder.SeedAsync(services));
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Database migration or seed failed. Check PostgreSQL is running and ConnectionStrings:DefaultConnection is correct.");
        logger.LogWarning("Continuing without a successful migration/seed. Auth and data pages may fail until the database is available.");
    }
}

app.Run();

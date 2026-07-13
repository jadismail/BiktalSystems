using Biktal.Infrastructure;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Identity;
using Biktal.Infrastructure.Persistence;
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
        await CatalogProductSeeder.SeedIfEmptyAsync(db);
        await CatalogCategorySeeder.SeedIfEmptyAsync(db);
        await CatalogBrandSeeder.SeedIfEmptyAsync(db);
        await CatalogWarehouseSeeder.SeedIfEmptyAsync(db);
        await CatalogSupplierSeeder.SeedIfEmptyAsync(db);
        await RepairTicketSeeder.SeedIfEmptyAsync(db);
        await FinanceDataSeeder.SeedIfEmptyAsync(db);
        await FinanceDataSeeder.EnsureSalesTaxPayableAccountAsync(db);
        await FinanceDataSeeder.EnsureInventoryMovementAccountsAsync(db);
        await FinanceDataSeeder.EnsureCashboxAccountDisplayNamesAsync(db);
        await CrmDataSeeder.SeedIfEmptyAsync(db);
        await StaffDataSeeder.SeedIfEmptyAsync(db);
        await AdminDataSeeder.SeedIfEmptyAsync(db);
        await IdentitySeeder.SeedAsync(services);
    }
    catch (Exception ex)
    {
        logger.LogError(ex,
            "Database migration or seed failed. Check PostgreSQL is running and ConnectionStrings:DefaultConnection is correct.");
        logger.LogWarning("Continuing without a successful migration/seed. Auth and data pages may fail until the database is available.");
    }
}

app.Run();

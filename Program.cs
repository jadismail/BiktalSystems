using System.Security.Claims;
using Biktal.Application.Security;
using Biktal.Infrastructure;
using Biktal.Infrastructure.Finance;
using Biktal.Infrastructure.Identity;
using Biktal.Infrastructure.Persistence;
using Biktal.WebMVC.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Serilog;

var builder = WebApplication.CreateBuilder(args);

builder.Host.UseSerilog((ctx, services, cfg) =>
{
    cfg.ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(services)
        .Enrich.FromLogContext();
});

builder.Services.AddInfrastructure(builder.Configuration);

// EF Core auto-applies ISaveChangesInterceptor instances registered in DI to contexts created via
// AddDbContext. This one repairs new child rows that EF mis-tracks as UPDATEs (see class remarks).
builder.Services.AddSingleton<ISaveChangesInterceptor, NewRowInsertFixInterceptor>();

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
// Open access: no login required — every request runs as a local admin principal.
app.Use(async (context, next) =>
{
    var claims = new List<Claim>
    {
        new(ClaimTypes.Name, "Local user"),
        new(ClaimTypes.NameIdentifier, "local-bypass"),
        new(ClaimTypes.Email, "local@biktal")
    };
    foreach (var role in ApplicationRoles.All)
        claims.Add(new Claim(ClaimTypes.Role, role));
    claims.Add(new Claim(ClaimTypes.Role, ApplicationRoles.AdminModule));

    context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, authenticationType: "LocalBypass"));
    await next();
});
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Pos}/{action=Index}/{id?}")
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

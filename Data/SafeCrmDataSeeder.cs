using Biktal.Domain.Crm;
using Biktal.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Biktal.WebMVC.Data;

/// <summary>
/// Safer replacement for <c>CrmDataSeeder</c>: demo support tickets are only inserted when
/// their referenced demo customer exists. The stock seeder fails with FK 23503 when
/// <c>CrmCustomers</c> already has real rows (so demo customers are skipped) but tickets are empty.
/// </summary>
public static class SafeCrmDataSeeder
{
    public static readonly Guid Customer1 = Guid.Parse("66666666-6666-6666-6666-666666666601");
    public static readonly Guid Customer2 = Guid.Parse("66666666-6666-6666-6666-666666666602");
    public static readonly Guid Customer3 = Guid.Parse("66666666-6666-6666-6666-666666666603");

    private static readonly DateTimeOffset SeedTime = new(2026, 2, 1, 12, 0, 0, TimeSpan.Zero);

    private static readonly CrmCustomer[] SeedCustomers =
    [
        new()
        {
            Id = Customer1,
            FullName = "Priya Nair",
            Phone = "+1 555-0201",
            Email = "priya.nair@example.com",
            LoyaltyPoints = 1240,
            MarketingOptIn = true,
            IsActive = true,
            Notes = "Prefers SMS for pickup reminders.",
            CreatedAtUtc = SeedTime
        },
        new()
        {
            Id = Customer2,
            FullName = "Diego Alvarez",
            Phone = "+1 555-0202",
            Email = "diego.a@example.com",
            Company = "Alvarez Auto Glass",
            LoyaltyPoints = 520,
            MarketingOptIn = true,
            IsActive = true,
            Notes = "Bulk accessory buyer — Q2 promo candidate.",
            CreatedAtUtc = SeedTime
        },
        new()
        {
            Id = Customer3,
            FullName = "Mei Lin",
            Phone = "+1 555-0203",
            LoyaltyPoints = 80,
            MarketingOptIn = false,
            IsActive = true,
            Notes = "Walk-in heavy; add email on next visit.",
            CreatedAtUtc = SeedTime
        }
    ];

    private static readonly CrmSupportTicket[] SeedTickets =
    [
        new()
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777701"),
            TicketNumber = "ST-DEMO-0001",
            CustomerId = Customer1,
            Subject = "Billing question on last invoice",
            Description = "Customer asked for itemized breakdown of accessories vs labor.",
            Status = CrmSupportTicketStatus.InProgress,
            Priority = CrmSupportTicketPriority.Normal,
            CreatedAtUtc = SeedTime
        },
        new()
        {
            Id = Guid.Parse("77777777-7777-7777-7777-777777777702"),
            TicketNumber = "ST-DEMO-0002",
            CustomerId = null,
            Subject = "Store Wi-Fi complaint",
            Description = "Anonymous feedback left at counter — slow guest network.",
            Status = CrmSupportTicketStatus.New,
            Priority = CrmSupportTicketPriority.Low,
            CreatedAtUtc = SeedTime
        }
    ];

    private static readonly CrmCampaign[] SeedCampaigns =
    [
        new()
        {
            Id = Guid.Parse("88888888-8888-8888-8888-888888888801"),
            Name = "Spring accessory bundle",
            Channel = "SMS + Email",
            StartDate = new DateOnly(2026, 3, 1),
            EndDate = new DateOnly(2026, 3, 31),
            Description = "15% off cases and chargers for opted-in customers.",
            Status = CrmCampaignStatus.Active,
            CreatedAtUtc = SeedTime
        },
        new()
        {
            Id = Guid.Parse("88888888-8888-8888-8888-888888888802"),
            Name = "Trade-in reminder",
            Channel = "WhatsApp",
            StartDate = new DateOnly(2026, 4, 15),
            Description = "Follow up devices older than 24 months — draft copy in marketing doc.",
            Status = CrmCampaignStatus.Draft,
            CreatedAtUtc = SeedTime
        }
    ];

    public static async Task SeedIfEmptyAsync(ApplicationDbContext db, CancellationToken cancellationToken = default)
    {
        if (!await db.CrmCustomers.AnyAsync(cancellationToken))
        {
            await db.CrmCustomers.AddRangeAsync(SeedCustomers, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }

        if (!await db.CrmSupportTickets.AnyAsync(cancellationToken))
        {
            var demoCustomerExists = await db.CrmCustomers
                .AnyAsync(c => c.Id == Customer1, cancellationToken);

            var tickets = demoCustomerExists
                ? SeedTickets
                : SeedTickets.Where(t => t.CustomerId is null).ToArray();

            if (tickets.Length > 0)
            {
                await db.CrmSupportTickets.AddRangeAsync(tickets, cancellationToken);
                await db.SaveChangesAsync(cancellationToken);
            }
        }

        if (!await db.CrmCampaigns.AnyAsync(cancellationToken))
        {
            await db.CrmCampaigns.AddRangeAsync(SeedCampaigns, cancellationToken);
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}

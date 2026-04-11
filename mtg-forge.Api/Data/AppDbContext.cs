using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using MtgForge.Api.Models;

namespace MtgForge.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<CardPrice> CardPrices => Set<CardPrice>();
    public DbSet<PricingImportRun> PricingImportRuns => Set<PricingImportRun>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<CardPrice>()
            .HasIndex(x => x.NormalizedCardName)
            .IsUnique();

        builder.Entity<CardPrice>()
            .Property(x => x.PriceUsd)
            .HasPrecision(18, 4);
    }
}

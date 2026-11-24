using FruitBilling.Web.Data;
using FruitBilling.Web.Models;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace FruitBilling.Web.Tests;

public class AppDbContextModelTests
{
    private static AppDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlServer("Server=(local);Database=ModelOnly;Trusted_Connection=True;TrustServerCertificate=True")
            .Options;

        return new AppDbContext(options);
    }

    [Fact]
    public void FarmAndSortingTicketsRemainSeparateEntitiesAndRelationships()
    {
        using var db = CreateContext();
        var model = db.Model;

        var farmTicket = model.FindEntityType(typeof(BasculTicket))!;
        var sortingTicket = model.FindEntityType(typeof(SortingBasculTicket))!;

        Assert.Contains(farmTicket.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(FruitExitPermitItem));
        Assert.DoesNotContain(farmTicket.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(SortingExitPermit) ||
                  fk.PrincipalEntityType.ClrType == typeof(SortingExitPermitItem));

        Assert.Contains(sortingTicket.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(SortingExitPermitItem));
        Assert.DoesNotContain(sortingTicket.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(FruitExitPermit) ||
                  fk.PrincipalEntityType.ClrType == typeof(FruitExitPermitItem));
    }

    [Fact]
    public void FarmPermitNumberUniquenessIsScopedToFarm()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(FruitExitPermit))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(FruitExitPermit.FarmId), nameof(FruitExitPermit.PermitNumber) }));
    }

    [Fact]
    public void SortingPermitNumberUniquenessIsScopedToHarvestCycle()
    {
        using var db = CreateContext();
        var entity = db.Model.FindEntityType(typeof(SortingExitPermit))!;

        Assert.Contains(entity.GetIndexes(), index =>
            index.IsUnique &&
            index.Properties.Select(property => property.Name)
                .SequenceEqual(new[] { nameof(SortingExitPermit.HarvestCycleId), nameof(SortingExitPermit.PermitNumber) }));
    }

    [Fact]
    public void BasculTicketNumbersAreNotGloballyUnique()
    {
        using var db = CreateContext();

        foreach (var ticketType in new[] { typeof(BasculTicket), typeof(SortingBasculTicket) })
        {
            var entity = db.Model.FindEntityType(ticketType)!;
            Assert.DoesNotContain(entity.GetIndexes(), index =>
                index.IsUnique &&
                index.Properties.Any(property => property.Name == "TicketNo"));
        }
    }
}

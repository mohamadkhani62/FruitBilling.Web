using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace FruitBilling.Web.Data
{
    /// <summary>
    /// Creates AppDbContext for EF Core tooling without starting the web host
    /// or running application startup tasks (such as initial role/user seeding).
    /// </summary>
    public sealed class AppDbContextFactory : IDesignTimeDbContextFactory<AppDbContext>
    {
        public AppDbContext CreateDbContext(string[] args)
        {
            var connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default");

            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString =
                    "Server=(localdb)\\MSSQLLocalDB;Database=FruitBillingDb;Trusted_Connection=True;MultipleActiveResultSets=true;TrustServerCertificate=True";
            }

            var options = new DbContextOptionsBuilder<AppDbContext>()
                .UseSqlServer(connectionString)
                .Options;

            return new AppDbContext(options);
        }
    }
}

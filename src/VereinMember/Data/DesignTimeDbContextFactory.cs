using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace VereinMember.Data;

/// <summary>
/// Design-time factory for EF Core migrations. Requires the MEMBERSHIP_CONNECTION_STRING
/// environment variable to be set.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<MembershipDbContext>
{
    public MembershipDbContext CreateDbContext(string[] args)
    {
        var connStr = Environment.GetEnvironmentVariable("MEMBERSHIP_CONNECTION_STRING");
        if (string.IsNullOrWhiteSpace(connStr))
        {
            throw new InvalidOperationException(
                "MEMBERSHIP_CONNECTION_STRING environment variable must be set for design-time EF Core commands.");
        }

        var optionsBuilder = new DbContextOptionsBuilder<MembershipDbContext>();
        optionsBuilder.UseSqlServer(connStr);
        return new MembershipDbContext(optionsBuilder.Options);
    }
}
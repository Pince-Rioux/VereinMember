using Microsoft.EntityFrameworkCore;
using VereinMember.Data;
using VereinMember.Models;

namespace VereinMember.E2ETests;

/// <summary>
/// Schema adjustments only for the disposable browser-test database.
/// Production keeps the SQL Server schema and its rowversion behavior.
/// </summary>
public sealed class SqliteMembershipDbContext(DbContextOptions<MembershipDbContext> options)
    : MembershipDbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        foreach (var property in modelBuilder.Model.GetEntityTypes()
                     .SelectMany(entityType => entityType.GetProperties())
                     .Where(property => property.GetColumnType() == "nvarchar(max)"))
        {
            property.SetColumnType("TEXT");
        }

        // SQLite cannot generate SQL Server rowversion values. Optimistic
        // concurrency on that provider is covered by the SQL Server tests.
        modelBuilder.Entity<Member>()
            .Property(member => member.ConcurrencyToken)
            .ValueGeneratedNever()
            .IsConcurrencyToken()
            .HasColumnType("BLOB");
    }
}

using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace VereinMember.Tests.Integration;

public sealed class MembershipMigrationTests
{
    private const string PrivacyMigration = "20261031130948_AddPrivacyConsent";
    private const string GuardianMigration = "20261117183000_AddNonMemberGuardianFields";
    private const string RegistrationMigration = "20261118195432_AddRegistrationConsents";

    [Fact]
    public void GuardianMigrationHasTheHistoricalTargetModel()
    {
        using var context = CreateContext();
        var assembly = context.GetService<IMigrationsAssembly>();
        var migration = assembly.CreateMigration(assembly.Migrations[GuardianMigration],
            context.Database.ProviderName!);
        var member = migration.TargetModel.FindEntityType(typeof(Member).FullName!);
        Assert.NotNull(member);

        foreach (var (name, length) in new[]
        {
            (nameof(Member.GuardianFirstName), 100),
            (nameof(Member.GuardianLastName), 100),
            (nameof(Member.GuardianEmail), 200),
            (nameof(Member.GuardianPhone), 50)
        })
        {
            var property = member.FindProperty(name);
            Assert.NotNull(property);
            Assert.True(property.IsNullable);
            Assert.Equal(length, property.GetMaxLength());
            Assert.Equal($"nvarchar({length})", property.GetColumnType());
        }

        Assert.Equal("nvarchar(max)", member.FindProperty(nameof(Member.PrivacyConsentIp))!.GetColumnType());
        Assert.Null(member.FindProperty(nameof(Member.PrivacyConsentTextVersion)));
        Assert.Null(member.FindProperty(nameof(Member.SepaMandateConsentedAt)));
        Assert.Null(member.FindProperty(nameof(Member.SepaMandateTextVersion)));
    }

    [Fact]
    public void CanGenerateSqlAcrossGuardianMigrationWithoutConnectingToDatabase()
    {
        using var context = CreateContext();
        var migrator = context.GetService<IMigrator>();

        var upgrade = migrator.GenerateScript(PrivacyMigration, RegistrationMigration);
        Assert.Contains("ADD [GuardianEmail] nvarchar(200) NULL", upgrade);
        Assert.Contains("ALTER COLUMN [PrivacyConsentIp] nvarchar(45) NULL", upgrade);

        var rollback = migrator.GenerateScript(RegistrationMigration, PrivacyMigration);
        Assert.Contains("ALTER COLUMN [PrivacyConsentIp] nvarchar(max) NULL", rollback);
        Assert.Contains("DROP COLUMN [GuardianEmail]", rollback);
    }

    private static MembershipDbContext CreateContext() => new(
        new DbContextOptionsBuilder<MembershipDbContext>()
            .UseSqlServer("Server=localhost;Database=SqlGenerationOnly;Integrated Security=true")
            .Options);
}

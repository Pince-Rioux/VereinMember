using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace VereinMember.Tests.Integration;

public sealed class FamilyGroupOptionsSqlTests
{
    [Fact]
    public async Task FamilyGroupOptions_QueryProjectsOnlyIdsAndNames_OnSqlServer()
    {
        var connectionInterceptor = new SuppressConnectionInterceptor();
        var commandInterceptor = new CaptureCommandInterceptor();
        var options = new DbContextOptionsBuilder<MembershipDbContext>()
            .UseSqlServer("Server=localhost;Database=SqlGenerationOnly;Integrated Security=true")
            .AddInterceptors(connectionInterceptor, commandInterceptor)
            .Options;
        using var context = new MembershipDbContext(options);

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(
            () => MembershipFormHelper.LoadFamilyGroupOptionsAsync(context));

        exception.Message.Should().Be("SQL captured without executing it");
        commandInterceptor.Sql.Should().NotBeNullOrEmpty();
        commandInterceptor.Sql.Should().Contain("[FamilyGroup]")
            .And.Contain("[FirstName]")
            .And.Contain("[LastName]")
            .And.Contain("[Status]")
            .And.NotContain("[Email]")
            .And.NotContain("[Phone]")
            .And.NotContain("[BirthDate]")
            .And.NotContain("[Street]")
            .And.NotContain("[City]")
            .And.NotContain("[BankIban]")
            .And.NotContain("[BankBic]")
            .And.NotContain("[Notes]")
            .And.NotContain("[GuardianEmail]")
            .And.NotContain("[PrivacyConsentIp]");
    }

    private sealed class SuppressConnectionInterceptor : DbConnectionInterceptor
    {
        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result,
            CancellationToken cancellationToken = default) =>
            ValueTask.FromResult(InterceptionResult.Suppress());
    }

    private sealed class CaptureCommandInterceptor : DbCommandInterceptor
    {
        public string? Sql { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            Sql = command.CommandText;
            throw new InvalidOperationException("SQL captured without executing it");
        }
    }
}

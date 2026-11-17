using VereinMember.Tests.Fixtures;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using VereinMail;
using VereinMember.Areas.Membership.Pages;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;

namespace VereinMember.Tests.Services;

[Collection("StaticMembershipState")]
public sealed class AuditPurgeAndAccountHardeningTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly List<string> _logMessages = [];

    public AuditPurgeAndAccountHardeningTests()
    {
        var key = TestEncryptionKey.Value;
        MembershipEncryption.Initialize(new VereinMember.Services.Encryption.AesEncryptionService(key));

        var dbName = $"HardeningTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDataProtection();
        services.AddDbContext<MembershipDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddIdentityCore<MembershipUser>(o =>
            {
                o.Password.RequireDigit = true;
                o.Password.RequiredLength = 8;
                o.Password.RequireNonAlphanumeric = false;
                o.Password.RequireUppercase = true;
                o.Password.RequireLowercase = true;
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<MembershipDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(_logMessages));
        services.AddLogging();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IMemberAccountService, DefaultMemberAccountService>();
        services.AddSingleton<IEmailSender>(new Mock<IEmailSender>().Object);

        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        ServiceCollectionExtensions.Enabled = true;
        _provider.Dispose();
    }

    private async Task<Member> SeedMemberAsync(
        MembershipDbContext context,
        string email,
        MemberStatus status = MemberStatus.Active,
        DateTime? leftAt = null,
        string? iban = "DE89370400440532013000",
        string? bic = "COBADEFFXXX")
    {
        var fee = new MembershipFee
        {
            Name = "Adult",
            Amount = 60m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Adult,
            IsDefault = true
        };
        context.MembershipFees.Add(fee);
        await context.SaveChangesAsync();

        var member = new Member
        {
            FirstName = "Max",
            LastName = "Mustermann",
            Email = email,
            Phone = "+491701234567",
            BirthDate = new DateTime(1990, 5, 10),
            Street = "Musterweg 1",
            City = "Neusäß",
            ZipCode = "86356",
            Country = "DE",
            BankIban = iban,
            BankBic = bic,
            PrivacyConsentedAt = DateTime.UtcNow.AddYears(-4),
            PrivacyConsentIp = "203.0.113.10",
            JoinedAt = DateTime.UtcNow.AddYears(-4),
            LeftAt = leftAt,
            Status = status,
            MembershipFeeId = fee.Id,
            Notes = "Internal note",
            CreatedAt = DateTime.UtcNow.AddYears(-4),
            UpdatedAt = DateTime.UtcNow.AddYears(-4),
            CreatedBy = "admin"
        };
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task MemberService_CreateAndErasure_DoNotStoreBankDataOrErasedPiiInAuditLog()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MembershipUser>>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();

        var fee = new MembershipFee
        {
            Name = "Adult",
            Amount = 60m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Adult,
            IsDefault = true
        };
        context.MembershipFees.Add(fee);
        await context.SaveChangesAsync();

        var member = new Member
        {
            FirstName = "Erika",
            LastName = "Muster",
            Email = "erika@example.com",
            BirthDate = new DateTime(1988, 3, 2),
            BankIban = "DE89370400440532013000",
            BankBic = "COBADEFFXXX",
            PrivacyConsentIp = "198.51.100.7",
            PrivacyConsentedAt = DateTime.UtcNow,
            MembershipFeeId = fee.Id
        };

        var created = await memberService.CreateAsync(member, "admin@verein.de");

        var createAudit = await context.AuditLog
            .AsNoTracking()
            .SingleAsync(a => a.EntityId == created.Id && a.Action == "Created");

        createAudit.NewValues.Should().NotBeNull();
        createAudit.NewValues.Should().NotContain("DE89370400440532013000");
        createAudit.NewValues.Should().NotContain("COBADEFFXXX");
        createAudit.NewValues.Should().NotContain("198.51.100.7");
        using (var doc = JsonDocument.Parse(createAudit.NewValues!))
        {
            doc.RootElement.TryGetProperty("BankIban", out _).Should().BeFalse();
            doc.RootElement.TryGetProperty("BankBic", out _).Should().BeFalse();
            doc.RootElement.GetProperty("HasBankIban").GetBoolean().Should().BeTrue();
            doc.RootElement.GetProperty("HasBankBic").GetBoolean().Should().BeTrue();
        }

        // Seed invitation & Identity user for member, then perform DSGVO erasure
        await invitationService.CreateInvitationAsync(created.Id, created.Email);
        (await userManager.CreateAsync(new MembershipUser { Email = created.Email, UserName = created.Email })).Succeeded.Should().BeTrue();

        await memberService.ErasureAsync(created.Id, "dsgvo-admin");

        var erasureAudit = await context.AuditLog
            .AsNoTracking()
            .SingleAsync(a => a.EntityId == created.Id && a.Action == "Erasure");
        erasureAudit.OldValues.Should().BeNull();
        erasureAudit.NewValues.Should().BeNull();

        var erased = await context.Members.AsNoTracking().SingleAsync(m => m.Id == created.Id);
        erased.FirstName.Should().Be("[erased]");
        erased.BankIban.Should().BeNull();
        erased.BankBic.Should().BeNull();
        erased.PrivacyConsentIp.Should().BeNull();
        erased.PrivacyConsentedAt.Should().BeNull();
        (await context.Invitations.CountAsync(i => i.MemberId == created.Id)).Should().Be(0);
        (await userManager.FindByEmailAsync("erika@example.com")).Should().BeNull();
    }

    [Fact]
    public void AddVereinMember_DoesNotRegisterPurgeServiceByDefault()
    {
        var key = TestEncryptionKey.Value;
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClubMembershipDb"] = "Server=localhost;Database=Dummy;",
                ["ClubMembership:Enabled"] = "true",
                ["MembershipEncryption:Key"] = key
            })
            .Build();

        services.AddVereinMember(config);
        services.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PurgeService))
            .Should().BeFalse();
    }

    [Fact]
    public void AddVereinMember_RegistersPurgeServiceOnlyWhenBothModuleAndPurgeAreEnabled()
    {
        var key = TestEncryptionKey.Value;

        var disabledModuleServices = new ServiceCollection();
        var disabledModuleConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClubMembershipDb"] = "Server=localhost;Database=Dummy;",
                ["ClubMembership:Enabled"] = "false",
                ["MembershipPurge:Enabled"] = "true",
                ["MembershipEncryption:Key"] = key
            })
            .Build();
        disabledModuleServices.AddVereinMember(disabledModuleConfig);
        disabledModuleServices.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PurgeService))
            .Should().BeFalse();

        var enabledServices = new ServiceCollection();
        var enabledConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClubMembershipDb"] = "Server=localhost;Database=Dummy;",
                ["ClubMembership:Enabled"] = "true",
                ["MembershipPurge:Enabled"] = "true",
                ["MembershipEncryption:Key"] = key
            })
            .Build();
        enabledServices.AddVereinMember(enabledConfig);
        enabledServices.Any(d => d.ServiceType == typeof(IHostedService) && d.ImplementationType == typeof(PurgeService))
            .Should().BeTrue();
    }

    [Fact]
    public async Task PurgeService_DryRunTrue_DoesNotMutateRecordsOrWriteAudit()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var member = await SeedMemberAsync(context, "expired@example.com", MemberStatus.Former, DateTime.UtcNow.AddYears(-4));

        var purge = new PurgeService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<ILogger<PurgeService>>(),
            Options.Create(new PurgeOptions { Enabled = true, DryRun = true, RetentionYears = 3 }));

        var count = await purge.PurgeExpiredRecordsAsync(CancellationToken.None);
        count.Should().Be(1);

        var unchanged = await context.Members.AsNoTracking().SingleAsync(m => m.Id == member.Id);
        unchanged.FirstName.Should().Be("Max");
        unchanged.BankIban.Should().Be("DE89370400440532013000");
        (await context.AuditLog.CountAsync(a => a.Action == "Purge")).Should().Be(0);
    }

    [Fact]
    public async Task PurgeService_DryRunFalse_ErasesPiiAndLogsWithoutOldOrNewValues()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MembershipUser>>();

        var member = await SeedMemberAsync(context, "purge-me@example.com", MemberStatus.Former, DateTime.UtcNow.AddYears(-4));
        await invitationService.CreateInvitationAsync(member.Id, member.Email);
        (await userManager.CreateAsync(new MembershipUser { Email = member.Email, UserName = member.Email })).Succeeded.Should().BeTrue();

        var purge = new PurgeService(
            _provider.GetRequiredService<IServiceScopeFactory>(),
            _provider.GetRequiredService<ILogger<PurgeService>>(),
            Options.Create(new PurgeOptions { Enabled = true, DryRun = false, RetentionYears = 3 }));

        var count = await purge.PurgeExpiredRecordsAsync(CancellationToken.None);
        count.Should().Be(1);

        var erased = await context.Members.AsNoTracking().SingleAsync(m => m.Id == member.Id);
        erased.FirstName.Should().Be("[erased]");
        erased.Email.Should().Be($"[erased-{member.Id}]@verein.de");
        erased.BankIban.Should().BeNull();
        erased.BankBic.Should().BeNull();
        erased.PrivacyConsentIp.Should().BeNull();
        erased.PrivacyConsentedAt.Should().BeNull();
        (await context.Invitations.CountAsync(i => i.MemberId == member.Id)).Should().Be(0);
        (await userManager.FindByEmailAsync("purge-me@example.com")).Should().BeNull();

        var audit = await context.AuditLog.AsNoTracking().SingleAsync(a => a.EntityId == member.Id && a.Action == "Purge");
        audit.OldValues.Should().BeNull();
        audit.NewValues.Should().BeNull();
    }

    [Fact]
    public async Task SendInvitationEmail_DoesNotLogActivationUrlOrToken()
    {
        using var scope = _provider.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<IMemberAccountService>();

        const string secretToken = "super-secret-token-value-12345";
        var inviteUrl = $"https://club.example.org/Membership/Activate?token={secretToken}";

        await accountService.SendInvitationEmailAsync("member@example.com", "Max Muster", inviteUrl);

        _logMessages.Should().NotBeEmpty();
        _logMessages.Should().NotContain(m => m.Contains(secretToken, StringComparison.Ordinal));
        _logMessages.Should().NotContain(m => m.Contains("Activate?token=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SetPasswordAsync_RejectedNewPassword_PreservesExistingValidPassword()
    {
        using var scope = _provider.CreateScope();
        var accountService = scope.ServiceProvider.GetRequiredService<IMemberAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MembershipUser>>();

        const string email = "keep-pass@example.com";
        (await accountService.SetPasswordAsync(email, "ValidPass1")).Should().BeTrue();

        // Attempt update with a policy-invalid password (missing digit & uppercase)
        (await accountService.SetPasswordAsync(email, "weakpassword")).Should().BeFalse();

        var user = await userManager.FindByEmailAsync(email);
        user.Should().NotBeNull();
        (await userManager.CheckPasswordAsync(user!, "ValidPass1")).Should().BeTrue();

        // Valid replacement succeeds
        (await accountService.SetPasswordAsync(email, "NewValidPass2")).Should().BeTrue();
        (await userManager.CheckPasswordAsync(user!, "NewValidPass2")).Should().BeTrue();
    }

    [Fact]
    public async Task ActivatePage_WeakPasswordRecordsFailureWithoutConsuming_ThenValidActivatesAndBlocksReplay()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var accountService = scope.ServiceProvider.GetRequiredService<IMemberAccountService>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MembershipUser>>();

        var member = await SeedMemberAsync(context, "activate-hardened@example.com");
        var invitation = await invitationService.CreateInvitationAsync(member.Id, member.Email);

        var model = new ActivateModel(invitationService, accountService, userManager)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };

        // Weak password (fails uppercase/digit policy) increments attempts, does not consume
        model.PageContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["password"] = "alllowercase",
            ["confirmPassword"] = "alllowercase"
        });
        await model.OnPostAsync(invitation.Token);

        model.IsCompleted.Should().BeFalse();
        model.IsInvalid.Should().BeFalse();
        var storedAfterFail = await context.Invitations.AsNoTracking().SingleAsync(i => i.Id == invitation.Id);
        storedAfterFail.Attempts.Should().Be(1);
        storedAfterFail.CompletedAt.Should().BeNull();

        // Valid password activates and consumes token
        var model2 = new ActivateModel(invitationService, accountService, userManager)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        model2.PageContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["password"] = "StrongPass1",
            ["confirmPassword"] = "StrongPass1"
        });
        await model2.OnPostAsync(invitation.Token);

        model2.IsCompleted.Should().BeTrue();
        model2.IsInvalid.Should().BeFalse();

        // Replay must fail as invalid
        var replayModel = new ActivateModel(invitationService, accountService, userManager)
        {
            PageContext = new PageContext
            {
                HttpContext = new DefaultHttpContext()
            }
        };
        replayModel.PageContext.HttpContext.Request.Form = new FormCollection(new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
        {
            ["password"] = "AnotherPass2",
            ["confirmPassword"] = "AnotherPass2"
        });
        await replayModel.OnPostAsync(invitation.Token);

        replayModel.IsInvalid.Should().BeTrue();
        replayModel.IsCompleted.Should().BeFalse();
        var user = await userManager.FindByEmailAsync(member.Email);
        (await userManager.CheckPasswordAsync(user!, "StrongPass1")).Should().BeTrue();
    }

    private sealed class CapturingLoggerFactory(List<string> messages) : ILoggerFactory
    {
        public void AddProvider(ILoggerProvider provider) { }
        public ILogger CreateLogger(string categoryName) => new CapturingLogger(messages);
        public void Dispose() { }
    }

    private sealed class CapturingLogger(List<string> messages) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            messages.Add(formatter(state, exception));
        }
    }
}

using System.Net;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VereinMember.Areas.Membership.Pages;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;
using VereinMember.Services.Encryption;
using VereinMember.Services.Validators;
using VereinMember.Tests.Fixtures;

namespace VereinMember.Tests.Services;

[Collection("StaticMembershipState")]
public sealed class RegistrationConsentTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private Exception? _registrationError;

    public RegistrationConsentTests()
    {
        ServiceCollectionExtensions.Enabled = true;
        MembershipEncryption.Initialize(new AesEncryptionService(TestEncryptionKey.Value));
        var services = new ServiceCollection();
        services.AddDbContext<MembershipDbContext>(options =>
            options.UseInMemoryDatabase($"RegistrationConsent_{Guid.NewGuid()}"));
        services.AddLogging();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IValidator<Member>, MemberValidator>();
        services.AddScoped<IInvitationService, InvitationService>();
        _provider = services.BuildServiceProvider();
    }

    [Fact]
    public async Task Registration_RequiresBothDeclarationsAndIban()
    {
        var (model, context) = await CreateModelAsync(privacyConsented: false, sepaConsented: false, iban: null);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<PageResult>();
        model.ModelState[nameof(RegisterModel.PrivacyConsented)]!.Errors.Should().NotBeEmpty();
        model.ModelState[nameof(RegisterModel.SepaMandateConsented)]!.Errors.Should().NotBeEmpty();
        model.ModelState["Member.BankIban"]!.Errors.Should().NotBeEmpty();
        (await context.Members.CountAsync()).Should().Be(0);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task Registration_DoesNotAcceptOnlyOneDeclaration(bool privacy, bool sepa)
    {
        var (model, context) = await CreateModelAsync(privacy, sepa);

        await model.OnPostAsync();

        model.ModelState.IsValid.Should().BeFalse();
        (await context.Members.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task Registration_StoresBothConsentTimestampsAndTextVersions()
    {
        var (model, context) = await CreateModelAsync(privacyConsented: true, sepaConsented: true);

        var result = await model.OnPostAsync();

        result.Should().BeOfType<RedirectToPageResult>(_registrationError?.ToString() ?? string.Join(" | ", model.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage)));
        var member = await context.Members.AsNoTracking().SingleAsync();
        member.PrivacyConsentedAt.Should().NotBeNull();
        member.PrivacyConsentTextVersion.Should().Be(RegisterModel.MembershipDeclarationVersion);
        member.PrivacyConsentIp.Should().Be("192.0.2.10");
        member.SepaMandateConsentedAt.Should().Be(member.PrivacyConsentedAt);
        member.SepaMandateTextVersion.Should().Be(RegisterModel.SepaMandateVersion);
    }

    [Fact]
    public async Task EmptyFamilyTemplateRows_DoNotTriggerAutomaticRequiredErrors()
    {
        var (model, context) = await CreateModelAsync(privacyConsented: true, sepaConsented: true);
        var familyFee = new MembershipFee
        {
            Name = "Familie", Amount = 80m, Interval = FeeInterval.Yearly,
            Category = FeeCategory.Family, IsDefault = false
        };
        context.MembershipFees.Add(familyFee);
        await context.SaveChangesAsync();
        model.Member.MembershipFeeId = familyFee.Id;
        model.FamilyMembers = [new RegisterModel.FamilyMemberItem()];
        model.ModelState.AddModelError("FamilyMembers[INDEX].FirstName", "The FirstName field is required.");
        model.ModelState.AddModelError("FamilyMembers[INDEX].LastName", "The LastName field is required.");
        model.ModelState.AddModelError("FamilyMembers[INDEX].Email", "The Email field is required.");

        await model.OnPostAsync();

        var messages = model.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        messages.Should().ContainSingle(message => message.Contains("mindestens ein weiteres Familienmitglied"));
        messages.Should().NotContain(message => message.Contains("field is required"));
        (await context.Members.CountAsync()).Should().Be(0);
    }

    private async Task<(RegisterModel Model, MembershipDbContext Context)> CreateModelAsync(
        bool privacyConsented,
        bool sepaConsented,
        string? iban = "DE89370400440532013000")
    {
        var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var fee = new MembershipFee
        {
            Name = "Erwachsene",
            Amount = 60m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Adult,
            IsDefault = true
        };
        context.MembershipFees.Add(fee);
        await context.SaveChangesAsync();
        var httpContext = new DefaultHttpContext();
        httpContext.Connection.RemoteIpAddress = IPAddress.Parse("192.0.2.10");
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClubMembership:PublicBaseUrl"] = "https://example.com"
            })
            .Build();
        var model = new RegisterModel(
            scope.ServiceProvider.GetRequiredService<IMemberService>(),
            scope.ServiceProvider.GetRequiredService<IInvitationService>(),
            new Mock<IMemberAccountService>().Object,
            scope.ServiceProvider.GetRequiredService<IValidator<Member>>(),
            context,
            new CallbackLogger(error => _registrationError = error),
            configuration)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new Mock<ITempDataProvider>().Object),
            Url = new Mock<IUrlHelper>().Object,
            PrivacyConsented = privacyConsented,
            SepaMandateConsented = sepaConsented,
            BirthDay = 15,
            BirthMonth = 4,
            BirthYear = 1992,
            Member = new Member
            {
                FirstName = "Anna",
                LastName = "Beispiel",
                Email = "anna.consent@example.com",
                BirthDate = new DateTime(1992, 4, 15),
                BankIban = iban,
                MembershipFeeId = fee.Id
            }
        };
        return (model, context);
    }

    private sealed class CallbackLogger(Action<Exception> onError) : ILogger<RegisterModel>
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel logLevel) => true;
        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (exception is not null) onError(exception);
        }
    }

    public void Dispose() => _provider.Dispose();
}

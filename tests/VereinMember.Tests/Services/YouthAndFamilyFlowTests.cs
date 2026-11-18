using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
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
public sealed class YouthAndFamilyFlowTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public YouthAndFamilyFlowTests()
    {
        ServiceCollectionExtensions.Enabled = true;
        MembershipEncryption.Initialize(new AesEncryptionService(TestEncryptionKey.Value));

        var dbName = $"YouthFamilyTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<MembershipDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddLogging();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IValidator<Member>, MemberValidator>();
        services.AddScoped<IInvitationService, InvitationService>();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static async Task<(MembershipFee YouthFee, MembershipFee FamilyFee)> SeedFeesAsync(MembershipDbContext context)
    {
        var youthFee = new MembershipFee
        {
            Name = "Jugend",
            Amount = 30m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Youth,
            IsDefault = false
        };
        var familyFee = new MembershipFee
        {
            Name = "Familie",
            Amount = 80m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Family,
            IsDefault = false
        };
        context.MembershipFees.AddRange(youthFee, familyFee);
        await context.SaveChangesAsync();
        return (youthFee, familyFee);
    }

    [Fact]
    public async Task RegisterPage_YouthWithNonMemberGuardian_PersistsGuardianContactAndCreatesInvitation()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var (youthFee, _) = await SeedFeesAsync(context);

        var accountMock = new Mock<IMemberAccountService>();
        var urlMock = new Mock<IUrlHelper>();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClubMembership:PublicBaseUrl"] = "https://www.volleyballclub.de"
            })
            .Build();

        var model = new RegisterModel(
            memberService,
            invitationService,
            accountMock.Object,
            validator,
            context,
            scope.ServiceProvider.GetRequiredService<ILogger<RegisterModel>>(),
            config)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            TempData = new TempDataDictionary(new DefaultHttpContext(), new Mock<ITempDataProvider>().Object),
            Url = urlMock.Object,
            BirthDay = 12,
            BirthMonth = 8,
            BirthYear = 2014,
            PrivacyConsented = true,
            SepaMandateConsented = true,
            GuardianFirstName = "Claudia",
            GuardianLastName = "Eltern",
            GuardianEmail = "claudia.eltern@example.com",
            GuardianPhone = "+49171000111",
            Member = new Member
            {
                FirstName = "Tim",
                LastName = "Eltern",
                Email = "tim.eltern@example.com",
                BankIban = "DE89370400440532013000",
                MembershipFeeId = youthFee.Id
            }
        };

        var result = await model.OnPostAsync();
        model.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).Should().BeEmpty();
        result.Should().BeOfType<RedirectToPageResult>();

        var saved = await context.Members.AsNoTracking().SingleAsync(m => m.Email == "tim.eltern@example.com");
        saved.GuardianMemberId.Should().BeNull();
        saved.GuardianFirstName.Should().Be("Claudia");
        saved.GuardianLastName.Should().Be("Eltern");
        saved.GuardianEmail.Should().Be("claudia.eltern@example.com");
        saved.GuardianPhone.Should().Be("+49171000111");
        saved.PrivacyConsentedAt.Should().NotBeNull();
        saved.PrivacyConsentTextVersion.Should().Be(RegisterModel.MembershipDeclarationVersion);
        saved.SepaMandateConsentedAt.Should().Be(saved.PrivacyConsentedAt);
        saved.SepaMandateTextVersion.Should().Be(RegisterModel.SepaMandateVersion);

        accountMock.Verify(a => a.SendInvitationEmailAsync(
            "tim.eltern@example.com", "Tim Eltern", It.IsAny<string>()), Times.Once);

        // Erasure must also remove non-member guardian PII
        await memberService.ErasureAsync(saved.Id, "dsgvo");
        var erased = await context.Members.AsNoTracking().SingleAsync(m => m.Id == saved.Id);
        erased.GuardianFirstName.Should().BeNull();
        erased.GuardianLastName.Should().BeNull();
        erased.GuardianEmail.Should().BeNull();
        erased.GuardianPhone.Should().BeNull();
    }

    [Fact]
    public async Task RegisterPage_YouthWithoutGuardianContact_FailsValidation()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var (youthFee, _) = await SeedFeesAsync(context);

        var model = new RegisterModel(
            memberService,
            invitationService,
            new Mock<IMemberAccountService>().Object,
            validator,
            context)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            BirthDay = 12,
            BirthMonth = 8,
            BirthYear = 2014,
            PrivacyConsented = true,
            SepaMandateConsented = true,
            Member = new Member
            {
                FirstName = "Tim",
                LastName = "OhneEltern",
                Email = "tim.ohne@example.com",
                MembershipFeeId = youthFee.Id
            }
        };

        var result = await model.OnPostAsync();
        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();
        (await context.Members.CountAsync()).Should().Be(0);
    }

    [Fact]
    public async Task CreateFamilyAsync_DuplicateEmails_ThrowsBeforeCreatingOrphanGroup_AndPreservesJoinedAt()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var (_, familyFee) = await SeedFeesAsync(context);

        var customJoin = new DateTime(2025, 1, 15, 0, 0, 0, DateTimeKind.Utc);
        var main = new Member
        {
            FirstName = "Max",
            LastName = "Familie",
            Email = "dup@example.com",
            BirthDate = new DateTime(1985, 1, 1),
            MembershipFeeId = familyFee.Id
        };
        var subDup = new Member
        {
            FirstName = "Erika",
            LastName = "Familie",
            Email = "DUP@example.com",
            BirthDate = new DateTime(1986, 2, 2),
            MembershipFeeId = familyFee.Id
        };

        var act = () => memberService.CreateFamilyAsync(main, [subDup], "admin");
        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("*Duplicate email*");
        (await context.FamilyGroups.CountAsync()).Should().Be(0);

        // Now with distinct emails and explicit JoinedAt on sub-member
        subDup.Email = "erika.familie@example.com";
        subDup.JoinedAt = customJoin;
        var created = await memberService.CreateFamilyAsync(main, [subDup], "admin");
        created.Should().HaveCount(2);
        created[1].JoinedAt.Should().Be(customJoin);
    }

    [Fact]
    public async Task RegisterPage_FamilyInvalidSubMemberBirthDate_DoesNotThrow500_AndDissolveAllowsDeparture()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var (_, familyFee) = await SeedFeesAsync(context);

        // Invalid Feb 31 date in FamilyMemberItem must not throw ArgumentOutOfRangeException
        var model = new RegisterModel(
            memberService,
            invitationService,
            new Mock<IMemberAccountService>().Object,
            validator,
            context)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            BirthDay = 10,
            BirthMonth = 3,
            BirthYear = 1985,
            PrivacyConsented = true,
            SepaMandateConsented = true,
            Member = new Member
            {
                FirstName = "Max",
                LastName = "Muster",
                Email = "max.family@example.com",
                MembershipFeeId = familyFee.Id
            },
            FamilyMembers =
            [
                new RegisterModel.FamilyMemberItem
                {
                    FirstName = "Kind",
                    LastName = "Muster",
                    Email = "kind.family@example.com",
                    BirthDay = 31,
                    BirthMonth = 2,
                    BirthYear = 2015
                }
            ]
        };

        model.FamilyMembers[0].BirthDate.Should().Be(DateTime.MinValue);
        var result = await model.OnPostAsync();
        result.Should().BeOfType<PageResult>();
        model.ModelState.IsValid.Should().BeFalse();

        // Valid 2-person family -> DissolveFamilyGroupAsync allows subsequent SoftDeleteAsync
        var m1 = new Member
        {
            FirstName = "Anna",
            LastName = "Duo",
            Email = "anna.duo@example.com",
            BirthDate = new DateTime(1985, 5, 5),
            MembershipFeeId = familyFee.Id
        };
        var m2 = new Member
        {
            FirstName = "Ben",
            LastName = "Duo",
            Email = "ben.duo@example.com",
            BirthDate = new DateTime(1986, 6, 6),
            MembershipFeeId = familyFee.Id
        };
        var created = await memberService.CreateFamilyAsync(m1, [m2], "admin");
        var groupId = created[0].FamilyGroupId!.Value;

        (await memberService.DissolveFamilyGroupAsync(groupId, "admin@verein.de")).Should().BeTrue();
        (await context.FamilyGroups.CountAsync(g => g.Id == groupId)).Should().Be(0);

        (await memberService.SoftDeleteAsync(created[0].Id, "admin@verein.de")).Should().BeTrue();
        var departing = await context.Members.AsNoTracking().SingleAsync(m => m.Id == created[0].Id);
        departing.Status.Should().Be(MemberStatus.Former);
        departing.FamilyGroupId.Should().BeNull();
    }

    [Fact]
    public async Task EditModel_YouthWithNonMemberGuardian_PreservesAndUpdatesGuardianFields()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var (youthFee, _) = await SeedFeesAsync(context);

        var youth = await memberService.CreateAsync(new Member
        {
            FirstName = "Mia",
            LastName = "Jung",
            Email = "mia.jung@example.com",
            BirthDate = new DateTime(2013, 4, 12),
            MembershipFeeId = youthFee.Id,
            GuardianFirstName = "Laura",
            GuardianLastName = "Jung",
            GuardianEmail = "laura.jung@example.com",
            GuardianPhone = "01711111111"
        }, "admin@verein.de");

        var editModel = new VereinMember.Areas.Membership.Pages.Members.EditModel(
            memberService,
            validator,
            context);
        editModel.PageContext = new PageContext
        {
            HttpContext = new DefaultHttpContext()
        };
        editModel.TempData = new TempDataDictionary(editModel.HttpContext, new TestTempDataProvider());

        var getResult = await editModel.OnGetAsync(youth.Id);
        getResult.Should().BeOfType<PageResult>();
        editModel.Member.GuardianFirstName.Should().Be("Laura");
        editModel.Member.GuardianEmail.Should().Be("laura.jung@example.com");

        editModel.Member.GuardianPhone = " 01722222222 ";
        var postResult = await editModel.OnPostAsync();
        postResult.Should().BeOfType<RedirectToPageResult>();

        var reloaded = await context.Members.AsNoTracking().SingleAsync(m => m.Id == youth.Id);
        reloaded.GuardianFirstName.Should().Be("Laura");
        reloaded.GuardianLastName.Should().Be("Jung");
        reloaded.GuardianEmail.Should().Be("laura.jung@example.com");
        reloaded.GuardianPhone.Should().Be("01722222222");
    }

    private sealed class TestTempDataProvider : ITempDataProvider
    {
        private readonly Dictionary<string, object?> _data = new();
        public IDictionary<string, object?> LoadTempData(HttpContext context) => _data;
        public void SaveTempData(HttpContext context, IDictionary<string, object?> values)
        {
            _data.Clear();
            foreach (var kv in values) _data[kv.Key] = kv.Value;
        }
    }
}

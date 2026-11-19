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
        _provider = TestDataBuilder.CreateServiceProvider("YouthFamilyTest");
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
            Input = new RegisterModel.RegisterInputModel
            {
                FirstName = "Tim",
                LastName = "Eltern",
                Email = "tim.eltern@example.com",
                BirthDay = 12,
                BirthMonth = 8,
                BirthYear = 2014,
                BankIban = "DE89370400440532013000",
                MembershipFeeId = youthFee.Id,
                PrivacyConsented = true,
                SepaMandateConsented = true,
                GuardianFirstName = "Claudia",
                GuardianLastName = "Eltern",
                GuardianEmail = "claudia.eltern@example.com",
                GuardianPhone = "+49171000111"
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
            Input = new RegisterModel.RegisterInputModel
            {
                FirstName = "Tim",
                LastName = "OhneEltern",
                Email = "tim.ohne@example.com",
                BirthDay = 12,
                BirthMonth = 8,
                BirthYear = 2014,
                MembershipFeeId = youthFee.Id,
                PrivacyConsented = true,
                SepaMandateConsented = true
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
            Input = new RegisterModel.RegisterInputModel
            {
                FirstName = "Max",
                LastName = "Muster",
                Email = "max.family@example.com",
                BirthDay = 10,
                BirthMonth = 3,
                BirthYear = 1985,
                MembershipFeeId = familyFee.Id,
                PrivacyConsented = true,
                SepaMandateConsented = true
            },
            FamilyMembers =
            [
                new VereinMember.Areas.Membership.ViewModels.FamilyMemberInput
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

        var familyGroupService = scope.ServiceProvider.GetRequiredService<IFamilyGroupService>();
        (await familyGroupService.DissolveFamilyGroupAsync(groupId, "admin@verein.de")).Should().BeTrue();
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
        editModel.Input.GuardianFirstName.Should().Be("Laura");
        editModel.Input.GuardianEmail.Should().Be("laura.jung@example.com");

        editModel.Input.GuardianPhone = " 01722222222 ";
        var postResult = await editModel.OnPostAsync();
        postResult.Should().BeOfType<RedirectToPageResult>();

        var reloaded = await context.Members.AsNoTracking().SingleAsync(m => m.Id == youth.Id);
        reloaded.GuardianFirstName.Should().Be("Laura");
        reloaded.GuardianLastName.Should().Be("Jung");
        reloaded.GuardianEmail.Should().Be("laura.jung@example.com");
        reloaded.GuardianPhone.Should().Be("01722222222");
    }

    [Fact]
    public async Task EditModel_FamilyGroupOptions_DisambiguatesDuplicateNamesWithIdAndActiveMembers()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var (_, familyFee) = await SeedFeesAsync(context);

        var familyGroupService = scope.ServiceProvider.GetRequiredService<IFamilyGroupService>();
        var group1 = await familyGroupService.CreateFamilyGroupAsync("Familie Müller", "admin@verein.de");
        var group2 = await familyGroupService.CreateFamilyGroupAsync("Familie Müller", "admin@verein.de");
        var unnamedGroup = await familyGroupService.CreateFamilyGroupAsync(null, "admin@verein.de");

        context.Members.AddRange(
            new Member
            {
                FirstName = "Anna",
                LastName = "Müller",
                Email = "anna.mueller@example.com",
                BirthDate = new DateTime(1984, 3, 12),
                MembershipFeeId = familyFee.Id,
                FamilyGroupId = group1.Id,
                Status = MemberStatus.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "admin@verein.de"
            },
            new Member
            {
                FirstName = "Tim",
                LastName = "Müller",
                Email = "tim.mueller@example.com",
                BirthDate = new DateTime(1982, 7, 4),
                MembershipFeeId = familyFee.Id,
                FamilyGroupId = group2.Id,
                Status = MemberStatus.Active,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "admin@verein.de"
            },
            new Member
            {
                FirstName = "Alt",
                LastName = "Müller",
                Email = "alt.mueller@example.com",
                BirthDate = new DateTime(1950, 1, 1),
                MembershipFeeId = familyFee.Id,
                FamilyGroupId = group2.Id,
                Status = MemberStatus.Former,
                JoinedAt = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                CreatedBy = "admin@verein.de"
            });
        await context.SaveChangesAsync();

        var firstMember = await context.Members.AsNoTracking().FirstAsync(m => m.FamilyGroupId == group1.Id);
        var editModel = new VereinMember.Areas.Membership.Pages.Members.EditModel(
            memberService,
            validator,
            context)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() }
        };

        var result = await editModel.OnGetAsync(firstMember.Id);
        result.Should().BeOfType<PageResult>();

        editModel.FamilyGroupOptions.Select(o => o.Text).Should().Equal(
            $"Familie Müller (#{group1.Id}) – Anna Müller",
            $"Familie Müller (#{group2.Id}) – Tim Müller",
            $"Familie #{unnamedGroup.Id}");
    }

    [Fact]
    public void MembershipDisplay_FormatsGermanStatusFeeCategoryAndFamilyLabels()
    {
        MembershipDisplay.GetGermanStatus(MemberStatus.Active).Should().Be("Aktiv");
        MembershipDisplay.GetGermanStatus(MemberStatus.Passive).Should().Be("Passiv");
        MembershipDisplay.GetGermanStatus(MemberStatus.Honorary).Should().Be("Ehrenmitglied");
        MembershipDisplay.GetGermanStatus(MemberStatus.Former).Should().Be("Ausgetreten");

        MembershipDisplay.GetGermanFeeCategory(FeeCategory.Adult).Should().Be("Erwachsene");
        MembershipDisplay.GetGermanFeeCategory(FeeCategory.Youth).Should().Be("Jugend");
        MembershipDisplay.GetGermanFeeCategory(FeeCategory.Family).Should().Be("Familie");

        MembershipDisplay.FormatFamilyGroupLabel(new FamilyGroup { Id = 7, Name = "Familie Becker" })
            .Should().Be("Familie Becker (#7)");
        MembershipDisplay.FormatFamilyGroupLabel(new FamilyGroup { Id = 8, Name = null }, "Eva Becker")
            .Should().Be("Familie #8 – Eva Becker");
    }

    [Fact]
    public async Task UpdateAsync_YouthToAdult_ClearsNonMemberGuardianFields()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var (youthFee, _) = await SeedFeesAsync(context);
        var adultFee = new MembershipFee
        {
            Name = "Erwachsene",
            Amount = 80m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Adult
        };
        context.MembershipFees.Add(adultFee);
        await context.SaveChangesAsync();

        var created = await memberService.CreateAsync(new Member
        {
            FirstName = "Lena",
            LastName = "Jung",
            Email = "lena.adult@example.com",
            BirthDate = new DateTime(2008, 4, 1),
            MembershipFeeId = youthFee.Id,
            GuardianFirstName = "Stefan",
            GuardianLastName = "Jung",
            GuardianEmail = "stefan.jung@example.com",
            GuardianPhone = "+49171111222"
        }, "admin@verein.de");

        created.MembershipFeeId = adultFee.Id;
        var updated = await memberService.UpdateAsync(created, "admin@verein.de");

        updated.GuardianMemberId.Should().BeNull();
        updated.GuardianFirstName.Should().BeNull();
        updated.GuardianLastName.Should().BeNull();
        updated.GuardianEmail.Should().BeNull();
        updated.GuardianPhone.Should().BeNull();
        updated.FamilyGroupId.Should().BeNull();
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

using System.Reflection;
using System.Security.Claims;
using FluentValidation;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VereinMember.Areas.Membership.Pages;
using VereinMember.Areas.Membership.Pages.Members;
using VereinMember.Areas.Membership.ViewModels;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;
using VereinMember.Services.Encryption;
using VereinMember.Services.Validators;
using VereinMember.Tests.Fixtures;

namespace VereinMember.Tests.Services;

[Collection("StaticMembershipState")]
public sealed class PageInputModelBindingTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public PageInputModelBindingTests()
    {
        _provider = TestDataBuilder.CreateServiceProvider("PageInputModels");
    }

    [Fact]
    public void PageModels_NeverBindMemberEntityDirectly_AndInputModelsBlockOverposting()
    {
        var assembly = typeof(RegisterModel).Assembly;
        var boundProperties = assembly.GetTypes()
            .Where(t => typeof(PageModel).IsAssignableFrom(t) && !t.IsAbstract)
            .SelectMany(t => t.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(p => p.GetCustomAttribute<BindPropertyAttribute>() != null)
            .ToList();

        boundProperties.Should().NotContain(p => p.PropertyType == typeof(Member),
            "no PageModel may expose the EF Member entity to [BindProperty]");

        string[] serverOwnedFields =
        [
            nameof(Member.Status),
            nameof(Member.CreatedAt),
            nameof(Member.UpdatedAt),
            nameof(Member.CreatedBy),
            nameof(Member.LeftAt),
            nameof(Member.PrivacyConsentedAt),
            nameof(Member.PrivacyConsentIp),
            nameof(Member.PrivacyConsentTextVersion),
            nameof(Member.SepaMandateConsentedAt),
            nameof(Member.SepaMandateTextVersion)
        ];

        Type[] inputTypes =
        [
            typeof(RegisterModel.RegisterInputModel),
            typeof(CreateModel.CreateMemberInputModel),
            typeof(EditModel.EditMemberInputModel),
            typeof(DeleteModel.DeleteMemberInputModel),
            typeof(ProfileModel.ProfileUpdateInputModel)
        ];

        foreach (var inputType in inputTypes)
        {
            var propNames = inputType.GetProperties().Select(p => p.Name).ToHashSet();
            propNames.Should().NotContain(serverOwnedFields,
                $"{inputType.Name} must not allow overposting server-owned audit/status/consent fields");
        }

        // Public registration must also block admin-only assignments
        var registerProps = typeof(RegisterModel.RegisterInputModel).GetProperties().Select(p => p.Name).ToHashSet();
        registerProps.Should().NotContain([
            nameof(Member.Id),
            nameof(Member.DepartmentId),
            nameof(Member.GuardianMemberId),
            nameof(Member.FamilyGroupId),
            nameof(Member.Notes),
            nameof(Member.JoinedAt),
            nameof(Member.ConcurrencyToken)
        ]);
    }

    [Fact]
    public async Task EditModel_ConcurrencyTokenRoundTrips_AndExcludesSelfFromGuardianOptions()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();

        var adultFee = new MembershipFee
        {
            Name = "Erwachsene",
            Amount = 60m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Adult,
            IsDefault = true
        };
        context.MembershipFees.Add(adultFee);
        await context.SaveChangesAsync();

        var expectedToken = new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 };
        var member = await memberService.CreateAsync(new Member
        {
            FirstName = "Klara",
            LastName = "Konflikt",
            Email = "klara@example.com",
            BirthDate = new DateTime(1988, 6, 20),
            MembershipFeeId = adultFee.Id,
            ConcurrencyToken = expectedToken
        }, "admin@verein.de");

        var otherAdult = await memberService.CreateAsync(new Member
        {
            FirstName = "Peter",
            LastName = "Waechter",
            Email = "peter@example.com",
            BirthDate = new DateTime(1980, 2, 10),
            MembershipFeeId = adultFee.Id
        }, "admin@verein.de");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "editor@verein.de")], "Test"))
        };
        var editModel = new EditModel(memberService, validator, context)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new Mock<ITempDataProvider>().Object)
        };

        (await editModel.OnGetAsync(member.Id)).Should().BeOfType<PageResult>();
        editModel.Input.ConcurrencyToken.Should().Equal(expectedToken);
        editModel.GuardianOptions.Select(o => o.Value).Should().Contain(otherAdult.Id.ToString())
            .And.NotContain(member.Id.ToString());

        editModel.Input.City = "Neusäß";
        var saveResult = await editModel.OnPostAsync();
        saveResult.Should().BeOfType<RedirectToPageResult>();

        var reloaded = await context.Members.AsNoTracking().SingleAsync(m => m.Id == member.Id);
        reloaded.City.Should().Be("Neusäß");
    }

    [Fact]
    public async Task CreateModel_FamilyCreation_UsesSharedPipelineAndSendsInvitationsForAllMembers()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var invitationService = scope.ServiceProvider.GetRequiredService<IInvitationService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var accountMock = new Mock<IMemberAccountService>();

        var familyFee = new MembershipFee
        {
            Name = "Familie",
            Amount = 100m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Family
        };
        context.MembershipFees.Add(familyFee);
        await context.SaveChangesAsync();

        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClubMembership:PublicBaseUrl"] = "https://verein.example.org"
            })
            .Build();

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin@verein.de")], "Test"))
        };

        var createModel = new CreateModel(
            memberService,
            invitationService,
            accountMock.Object,
            validator,
            context,
            config)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new Mock<ITempDataProvider>().Object),
            Url = new Mock<IUrlHelper>().Object,
            Input = new CreateModel.CreateMemberInputModel
            {
                FirstName = "Sabine",
                LastName = "Weber",
                Email = "sabine.weber@example.com",
                BirthDay = 4,
                BirthMonth = 9,
                BirthYear = 1984,
                MembershipFeeId = familyFee.Id,
                Street = "Hauptstr. 10",
                ZipCode = "86356",
                City = "Neusäß"
            },
            FamilyMembers =
            [
                new FamilyMemberInput(), // blank row skipped cleanly
                new FamilyMemberInput
                {
                    FirstName = "Jonas",
                    LastName = "Weber",
                    Email = "jonas.weber@example.com",
                    BirthDay = 14,
                    BirthMonth = 11,
                    BirthYear = 2012,
                    JoinedAt = new DateTime(2026, 1, 1)
                }
            ]
        };

        var result = await createModel.OnPostAsync();
        result.Should().BeOfType<RedirectToPageResult>();

        (await context.Members.CountAsync()).Should().Be(2);
        accountMock.Verify(a => a.SendInvitationEmailAsync(
            "sabine.weber@example.com", "Sabine Weber", It.Is<string>(u => u.StartsWith("https://verein.example.org/Membership/Activate?token="))), Times.Once);
        accountMock.Verify(a => a.SendInvitationEmailAsync(
            "jonas.weber@example.com", "Jonas Weber", It.Is<string>(u => u.StartsWith("https://verein.example.org/Membership/Activate?token="))), Times.Once);
    }

    [Fact]
    public async Task DeleteModel_SoftDeletesByInputId_AndFormatsSuccessMessageWithStoredMemberName()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();

        var fee = new MembershipFee
        {
            Name = "Erwachsene",
            Amount = 60m,
            Interval = FeeInterval.Yearly,
            Category = FeeCategory.Adult
        };
        context.MembershipFees.Add(fee);
        await context.SaveChangesAsync();

        var created = await memberService.CreateAsync(new Member
        {
            FirstName = "Moritz",
            LastName = "Austritt",
            Email = "moritz@example.com",
            BirthDate = new DateTime(1990, 1, 15),
            MembershipFeeId = fee.Id
        }, "admin@verein.de");

        var httpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim(ClaimTypes.Name, "admin@verein.de")], "Test"))
        };
        var deleteModel = new DeleteModel(memberService)
        {
            PageContext = new PageContext { HttpContext = httpContext },
            TempData = new TempDataDictionary(httpContext, new Mock<ITempDataProvider>().Object),
            Input = new DeleteModel.DeleteMemberInputModel { Id = created.Id }
        };

        var result = await deleteModel.OnPostAsync();
        result.Should().BeOfType<RedirectToPageResult>();

        deleteModel.TempData["Success"]?.ToString().Should().Contain("Moritz Austritt");
        var stored = await context.Members.AsNoTracking().SingleAsync(m => m.Id == created.Id);
        stored.Status.Should().Be(MemberStatus.Former);
    }

    public void Dispose() => _provider.Dispose();
}

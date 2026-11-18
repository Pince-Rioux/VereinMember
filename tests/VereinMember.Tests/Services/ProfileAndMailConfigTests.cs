using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VereinMember.Areas.Membership.Pages;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;
using VereinMember.Services.Encryption;
using VereinMember.Tests.Fixtures;

namespace VereinMember.Tests.Services;

public sealed class ProfileAndMailConfigTests : IDisposable
{
    private readonly ServiceProvider _provider;

    public ProfileAndMailConfigTests()
    {
        MembershipEncryption.Initialize(new AesEncryptionService(TestEncryptionKey.Value));

        var dbName = $"ProfileMailTest_{Guid.NewGuid()}";
        var services = new ServiceCollection();
        services.AddDbContext<MembershipDbContext>(o => o.UseInMemoryDatabase(dbName));
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMemberService, MemberService>();
        _provider = services.BuildServiceProvider();
    }

    public void Dispose()
    {
        _provider.Dispose();
    }

    private static ClaimsPrincipal CreateUser(string email) =>
        new(new ClaimsIdentity([new Claim(ClaimTypes.Name, email)], "TestAuth"));

    private static async Task<Member> SeedMemberAsync(MembershipDbContext context, string email = "profile.owner@example.com")
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
            FirstName = "Lena",
            LastName = "Alt",
            Email = email,
            Phone = "+491700001",
            BirthDate = new DateTime(1994, 7, 20),
            Street = "Alte Gasse 1",
            ZipCode = "86356",
            City = "Neusäß",
            Country = "DE",
            BankIban = "DE89370400440532013000",
            BankBic = "COBADEFFXXX",
            JoinedAt = DateTime.UtcNow.AddYears(-1),
            Status = MemberStatus.Active,
            MembershipFeeId = fee.Id,
            Notes = "Alt-Notiz",
            CreatedAt = DateTime.UtcNow.AddYears(-1),
            UpdatedAt = DateTime.UtcNow.AddYears(-1),
            CreatedBy = "admin",
            ConcurrencyToken = [1, 2, 3, 4]
        };
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return member;
    }

    [Fact]
    public async Task ProfilePage_OnGetAndOnPost_UpdatesEditableFieldsWithoutSendingImmutableFields()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var member = await SeedMemberAsync(context);

        var getModel = new ProfileModel(memberService)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = CreateUser(member.Email) } }
        };
        var getResult = await getModel.OnGetAsync();
        getResult.Should().BeOfType<PageResult>();
        getModel.Input.Id.Should().Be(member.Id);
        getModel.Input.FirstName.Should().Be("Lena");

        // Post only Input fields (simulating real browser form where disabled Email/Fee/Status are omitted)
        context.ChangeTracker.Clear();
        var postModel = new ProfileModel(memberService)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = CreateUser(member.Email) } },
            TempData = new Microsoft.AspNetCore.Mvc.ViewFeatures.TempDataDictionary(
                new DefaultHttpContext(),
                new Mock<Microsoft.AspNetCore.Mvc.ViewFeatures.ITempDataProvider>().Object),
            Input = new ProfileModel.ProfileUpdateInputModel
            {
                Id = member.Id,
                ConcurrencyToken = member.ConcurrencyToken,
                FirstName = "Lena-Marie",
                LastName = "Neu",
                Phone = "+491709999",
                Street = "Neue Str. 10",
                ZipCode = "86150",
                City = "Augsburg",
                Country = "DE",
                Notes = "Aktualisiert"
            }
        };

        var postResult = await postModel.OnPostAsync();
        postResult.Should().BeOfType<RedirectToPageResult>();

        var saved = await context.Members.AsNoTracking().SingleAsync(m => m.Id == member.Id);
        saved.FirstName.Should().Be("Lena-Marie");
        saved.LastName.Should().Be("Neu");
        saved.Phone.Should().Be("+491709999");
        saved.Street.Should().Be("Neue Str. 10");
        saved.City.Should().Be("Augsburg");
        saved.Email.Should().Be(member.Email);
        saved.MembershipFeeId.Should().Be(member.MembershipFeeId);
        saved.BankIban.Should().Be("DE89370400440532013000");
        saved.Status.Should().Be(MemberStatus.Active);
    }

    [Fact]
    public async Task ProfilePage_OnPost_ForbidsEditingDifferentMemberId()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var memberA = await SeedMemberAsync(context, "a@example.com");

        var postModel = new ProfileModel(memberService)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = CreateUser(memberA.Email) } },
            Input = new ProfileModel.ProfileUpdateInputModel
            {
                Id = memberA.Id + 999,
                FirstName = "Hack",
                LastName = "Attempt"
            }
        };

        var result = await postModel.OnPostAsync();
        result.Should().BeOfType<ForbidResult>();
    }

    [Fact]
    public async Task ProfilePage_OnPost_InvalidModelState_ReloadsMemberDisplayData()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var member = await SeedMemberAsync(context, "invalid.state@example.com");

        var postModel = new ProfileModel(memberService)
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext { User = CreateUser(member.Email) } },
            Input = new ProfileModel.ProfileUpdateInputModel
            {
                Id = member.Id,
                FirstName = "",
                LastName = "Neu"
            }
        };
        postModel.ModelState.AddModelError("Input.FirstName", "Vorname ist erforderlich.");

        var result = await postModel.OnPostAsync();
        result.Should().BeOfType<PageResult>();
        postModel.Member.Should().NotBeNull();
        postModel.Member!.Email.Should().Be(member.Email);
        postModel.Input.LastName.Should().Be("Neu");
    }

    [Fact]
    public void ActivationUrlBuilder_UsesConfiguredTrustedOrigin_AndRejectsInsecureRemoteHttp()
    {
        var urlHelper = new Mock<IUrlHelper>();
        urlHelper.Setup(u => u.Action(It.IsAny<UrlActionContext>())).Returns("https://fallback.example/Membership/Activate?token=abc");

        var httpsConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClubMembership:PublicBaseUrl"] = "https://www.volleyballclub.de/"
            })
            .Build();

        ActivationUrlBuilder.Build(urlHelper.Object, "tok+1/2", httpsConfig)
            .Should().Be("https://www.volleyballclub.de/Membership/Activate?token=tok%2B1%2F2");

        var insecureConfig = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ClubMembership:PublicBaseUrl"] = "http://evil.example.com"
            })
            .Build();

        var act = () => ActivationUrlBuilder.Build(urlHelper.Object, "tok", insecureConfig);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*PublicBaseUrl*");
    }

    [Fact]
    public void AddVereinMember_SmtpModeWithoutServer_ThrowsInvalidOperationException()
    {
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:ClubMembershipDb"] = "Server=localhost;Database=Dummy;",
                ["MembershipEncryption:Key"] = TestEncryptionKey.Value,
                ["MembershipMail:Mode"] = "Smtp"
            })
            .Build();

        var act = () => services.AddVereinMember(config);
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*MembershipMail:Smtp:Server*");
    }

    [Fact]
    public async Task MembersIndex_PreservesSelectedFilters_UsesGermanLabels_AndFiltersByFeeCategory()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var dept = new Department { Name = "Herren" };
        var adultFee = new MembershipFee { Name = "Mitglied", Amount = 80m, Interval = FeeInterval.Yearly, Category = FeeCategory.Adult };
        var youthFee = new MembershipFee { Name = "Jugend", Amount = 60m, Interval = FeeInterval.Yearly, Category = FeeCategory.Youth };
        context.Departments.Add(dept);
        context.MembershipFees.AddRange(adultFee, youthFee);
        await context.SaveChangesAsync();

        context.Members.AddRange(
            new Member { FirstName = "A", LastName = "Adult", Email = "a@ex.de", BirthDate = new DateTime(1990, 1, 1), MembershipFeeId = adultFee.Id, DepartmentId = dept.Id, Status = MemberStatus.Active },
            new Member { FirstName = "Y", LastName = "Youth", Email = "y@ex.de", BirthDate = new DateTime(2014, 1, 1), MembershipFeeId = youthFee.Id, DepartmentId = dept.Id, Status = MemberStatus.Active });
        await context.SaveChangesAsync();

        var indexModel = new VereinMember.Areas.Membership.Pages.Members.IndexModel(memberService, context)
        {
            Status = "Active",
            DepartmentId = dept.Id,
            FeeCategory = "Youth"
        };

        await indexModel.OnGetAsync();

        indexModel.Members.Should().ContainSingle(m => m.Email == "y@ex.de");
        indexModel.StatusOptions.Single(o => o.Value == "Active").Should().Match<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>(o => o.Selected && o.Text == "Aktiv");
        indexModel.DepartmentOptions.Single(o => o.Value == dept.Id.ToString()).Selected.Should().BeTrue();
        indexModel.FeeCategoryOptions.Single(o => o.Value == "Youth").Should().Match<Microsoft.AspNetCore.Mvc.Rendering.SelectListItem>(o => o.Selected && o.Text == "Jugend");
    }
}

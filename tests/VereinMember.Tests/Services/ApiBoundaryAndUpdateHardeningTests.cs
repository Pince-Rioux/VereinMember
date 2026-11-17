using VereinMember.Tests.Fixtures;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using VereinMail;
using VereinMember.Areas.Membership.Controllers;
using VereinMember.Areas.Membership.Dtos;
using VereinMember.Areas.Membership.Pages;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;
using VereinMember.Services.Encryption;
using VereinMember.Services.Validators;

namespace VereinMember.Tests.Services;

[Collection("StaticMembershipState")]
public sealed class ApiBoundaryAndUpdateHardeningTests : IDisposable
{
    private readonly ServiceProvider _provider;
    private readonly List<string> _logs = [];

    public ApiBoundaryAndUpdateHardeningTests()
    {
        var key = TestEncryptionKey.Value;
        MembershipEncryption.Initialize(new AesEncryptionService(key));

        var dbName = $"Batch3Test_{Guid.NewGuid()}";
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
                o.Lockout.AllowedForNewUsers = true;
                o.Lockout.MaxFailedAccessAttempts = 5;
                o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<MembershipDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        services.AddSingleton<IHttpContextAccessor, HttpContextAccessor>();
        services.AddAuthentication(IdentityConstants.ApplicationScheme)
            .AddCookie(IdentityConstants.ApplicationScheme);
        services.AddSingleton<ILoggerFactory>(new CapturingLoggerFactory(_logs));
        services.AddLogging();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IValidator<Member>, MemberValidator>();
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

    private static ClaimsPrincipal CreatePrincipal(string name, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.Name, name) };
        claims.AddRange(roles.Select(r => new Claim(ClaimTypes.Role, r)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "TestAuth"));
    }

    private static async Task<(MembershipFee Fee, Member Member)> SeedAdultMemberAsync(
        MembershipDbContext context,
        string email = "member@example.com")
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

        var joinedAt = new DateTime(2024, 2, 1, 0, 0, 0, DateTimeKind.Utc);
        var createdAt = new DateTime(2024, 2, 1, 10, 0, 0, DateTimeKind.Utc);
        var consentAt = new DateTime(2024, 2, 1, 10, 5, 0, DateTimeKind.Utc);

        var member = new Member
        {
            FirstName = "Anna",
            LastName = "Beispiel",
            Email = email,
            Phone = "+491701234",
            BirthDate = new DateTime(1992, 4, 15),
            Street = "Hauptstr. 5",
            City = "Neusäß",
            ZipCode = "86356",
            Country = "DE",
            BankIban = "DE89370400440532013000",
            BankBic = "COBADEFFXXX",
            PrivacyConsentedAt = consentAt,
            PrivacyConsentIp = "203.0.113.42",
            JoinedAt = joinedAt,
            Status = MemberStatus.Active,
            MembershipFeeId = fee.Id,
            Notes = "Confidential board note",
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
            CreatedBy = "seed-admin",
            ConcurrencyToken = [1, 2, 3, 4]
        };
        context.Members.Add(member);
        await context.SaveChangesAsync();
        return (fee, member);
    }

    [Fact]
    public async Task MembersController_ViewerRole_ReceivesMaskedIbanAndNoNotes_WhileEditorReceivesNotes()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        var (_, member) = await SeedAdultMemberAsync(context, "dto-scope@example.com");

        var controller = new MembersController(memberService, validator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("trainer@verein.de", "Viewer") }
            }
        };

        var viewerAction = await controller.GetById(member.Id);
        var viewerOk = viewerAction.Should().BeOfType<OkObjectResult>().Subject;
        var viewerJson = JsonSerializer.Serialize(viewerOk.Value);

        viewerJson.Should().NotContain("DE89370400440532013000");
        viewerJson.Should().NotContain("COBADEFFXXX");
        viewerJson.Should().NotContain("203.0.113.42");
        viewerJson.Should().NotContain("Confidential board note");
        var viewerDto = viewerOk.Value.Should().BeOfType<MemberResponseDto>().Subject;
        viewerDto.BankIbanMasked.Should().Be("DE••••••••••••3000");
        viewerDto.HasBankDetails.Should().BeTrue();
        viewerDto.Notes.Should().BeNull();

        controller.ControllerContext.HttpContext.User = CreatePrincipal("editor@verein.de", "Editor");
        var editorAction = await controller.GetById(member.Id);
        var editorDto = editorAction.Should().BeOfType<OkObjectResult>().Subject.Value.Should().BeOfType<MemberResponseDto>().Subject;
        editorDto.Notes.Should().Be("Confidential board note");
        editorDto.BankIbanMasked.Should().Be("DE••••••••••••3000");
    }

    [Fact]
    public async Task MembersController_GuardianCandidatesAndExportPolicy_DoNotLeakExtraPii()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var validator = scope.ServiceProvider.GetRequiredService<IValidator<Member>>();
        await SeedAdultMemberAsync(context, "guardian-dto@example.com");

        var controller = new MembersController(memberService, validator)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext { User = CreatePrincipal("editor@verein.de", "Editor") }
            }
        };

        var result = await controller.GetGuardianCandidates();
        var ok = result.Should().BeOfType<OkObjectResult>().Subject;
        var json = JsonSerializer.Serialize(ok.Value);
        json.Should().Contain("Beispiel, Anna");
        json.Should().NotContain("guardian-dto@example.com");
        json.Should().NotContain("DE89370400440532013000");

        var exportAuth = typeof(MembersController)
            .GetMethod(nameof(MembersController.Export))!
            .GetCustomAttributes(typeof(AuthorizeAttribute), inherit: false)
            .Cast<AuthorizeAttribute>()
            .Single();
        exportAuth.Policy.Should().Be(AuthorizationPolicies.MemberWrite);
    }

    [Fact]
    public async Task RequireMembershipEnabled_BlocksApiAndRazorHandlers_WhenDisabled()
    {
        var filter = new RequireMembershipEnabledAttribute();
        try
        {
            ServiceCollectionExtensions.Enabled = false;

            var actionContext = new ActionExecutingContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()),
                [],
                new Dictionary<string, object?>(),
                new object());
            var nextCalled = false;
            await filter.OnActionExecutionAsync(actionContext, () =>
            {
                nextCalled = true;
                return Task.FromResult(new ActionExecutedContext(actionContext, [], new object()));
            });

            nextCalled.Should().BeFalse();
            actionContext.Result.Should().BeOfType<ObjectResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);

            var pageContext = new PageContext(
                new ActionContext(new DefaultHttpContext(), new RouteData(), new PageActionDescriptor()));
            var pageExecuting = new PageHandlerExecutingContext(
                pageContext,
                [],
                new Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure.HandlerMethodDescriptor(),
                new Dictionary<string, object?>(),
                new object());
            var pageNextCalled = false;
            await filter.OnPageHandlerExecutionAsync(pageExecuting, () =>
            {
                pageNextCalled = true;
                return Task.FromResult(new PageHandlerExecutedContext(pageContext, [], new Microsoft.AspNetCore.Mvc.RazorPages.Infrastructure.HandlerMethodDescriptor(), new object()));
            });

            pageNextCalled.Should().BeFalse();
            pageExecuting.Result.Should().BeOfType<StatusCodeResult>()
                .Which.StatusCode.Should().Be(StatusCodes.Status503ServiceUnavailable);
        }
        finally
        {
            ServiceCollectionExtensions.Enabled = true;
        }
    }

    [Fact]
    public async Task RegisterPage_UnexpectedException_DoesNotLeakExceptionDetailsToPublicModelState()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
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

        var failingService = new Mock<IMemberService>();
        failingService
            .Setup(s => s.CreateAsync(It.IsAny<Member>(), It.IsAny<string>()))
            .ThrowsAsync(new Exception("Outer DB failure", new InvalidOperationException("SQL Server constraint IX_Secret_Column violated")));

        var model = new RegisterModel(
            failingService.Object,
            scope.ServiceProvider.GetRequiredService<IInvitationService>(),
            scope.ServiceProvider.GetRequiredService<IMemberAccountService>(),
            scope.ServiceProvider.GetRequiredService<IValidator<Member>>(),
            context,
            _provider.GetRequiredService<ILogger<RegisterModel>>())
        {
            PageContext = new PageContext { HttpContext = new DefaultHttpContext() },
            BirthDay = 10,
            BirthMonth = 5,
            BirthYear = 1995,
            PrivacyConsented = true,
            Member = new Member
            {
                FirstName = "Tom",
                LastName = "Tester",
                Email = "tom@example.com",
                MembershipFeeId = fee.Id
            }
        };

        await model.OnPostAsync();

        var errors = model.ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).ToList();
        errors.Should().ContainSingle(e => e.Contains("Registrierung fehlgeschlagen"));
        errors.Should().NotContain(e => e.Contains("IX_Secret_Column") || e.Contains("Outer DB failure"));
    }

    [Fact]
    public async Task MemberService_UpdateAsync_IsNonDestructive_AndAuditsActualActorAndChangedFields()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var auditMock = new Mock<IAuditService>();
        var service = new MemberService(context, auditMock.Object);
        var (fee, original) = await SeedAdultMemberAsync(context, "preserve-meta@example.com");

        var originalCreatedAt = original.CreatedAt;
        var originalJoinedAt = original.JoinedAt;
        var originalConsentAt = original.PrivacyConsentedAt;
        var originalConsentIp = original.PrivacyConsentIp;

        context.ChangeTracker.Clear();

        // Caller omits CreatedAt/CreatedBy/JoinedAt/Status/PrivacyConsent metadata
        var update = new Member
        {
            Id = original.Id,
            FirstName = "Anna-Maria",
            LastName = original.LastName,
            Email = original.Email,
            Phone = "+491709999",
            BirthDate = original.BirthDate,
            Street = original.Street,
            City = original.City,
            ZipCode = original.ZipCode,
            Country = original.Country,
            BankIban = original.BankIban,
            BankBic = original.BankBic,
            MembershipFeeId = fee.Id,
            Notes = original.Notes,
            Status = MemberStatus.Former, // must NOT overwrite status in UpdateAsync
            CreatedBy = "tampered-actor",
            ConcurrencyToken = original.ConcurrencyToken
        };

        var updated = await service.UpdateAsync(update, "editor@verein.de");

        updated.CreatedAt.Should().Be(originalCreatedAt);
        updated.CreatedBy.Should().Be("seed-admin");
        updated.JoinedAt.Should().Be(originalJoinedAt);
        updated.Status.Should().Be(MemberStatus.Active);
        updated.PrivacyConsentedAt.Should().Be(originalConsentAt);
        updated.PrivacyConsentIp.Should().Be(originalConsentIp);

        auditMock.Verify(a => a.LogAsync(
            nameof(Member),
            original.Id,
            "Updated",
            It.IsAny<string>(),
            It.IsAny<string>(),
            "FirstName,Phone",
            "editor@verein.de"), Times.Once);
    }

    [Fact]
    public async Task LoginPage_LocksOutAfterRepeatedFailures_AndBlocksSoftDeletedMembers()
    {
        using var scope = _provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var userManager = scope.ServiceProvider.GetRequiredService<UserManager<MembershipUser>>();
        var signInManager = scope.ServiceProvider.GetRequiredService<SignInManager<MembershipUser>>();
        var memberService = scope.ServiceProvider.GetRequiredService<IMemberService>();
        var logger = _provider.GetRequiredService<ILogger<LoginModel>>();

        var (_, member) = await SeedAdultMemberAsync(context, "lockout-user@example.com");
        var user = new MembershipUser { Email = member.Email, UserName = member.Email };
        (await userManager.CreateAsync(user, "ValidPass1")).Succeeded.Should().BeTrue();

        var httpContext = new DefaultHttpContext { RequestServices = _provider };
        signInManager.Context = httpContext;

        // 5 wrong password attempts must trigger lockout
        for (int i = 0; i < 5; i++)
        {
            var loginModel = new LoginModel(signInManager, userManager, memberService, logger)
            {
                PageContext = new PageContext { HttpContext = httpContext }
            };
            loginModel.PageContext.HttpContext.Request.Form = new FormCollection(
                new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
                {
                    ["email"] = member.Email,
                    ["password"] = "WrongPass1"
                });

            await loginModel.OnPostAsync();
        }

        (await userManager.IsLockedOutAsync(user)).Should().BeTrue();

        // Reset lockout and test SoftDelete revocation + login blocking
        await userManager.SetLockoutEndDateAsync(user, null);
        await userManager.ResetAccessFailedCountAsync(user);
        var stampBefore = user.SecurityStamp;

        await memberService.SoftDeleteAsync(member.Id, "admin@verein.de");

        var reloadedUser = await userManager.FindByEmailAsync(member.Email);
        reloadedUser!.SecurityStamp.Should().NotBe(stampBefore);
        (await userManager.IsLockedOutAsync(reloadedUser)).Should().BeTrue();

        var blockedLogin = new LoginModel(signInManager, userManager, memberService, logger)
        {
            PageContext = new PageContext { HttpContext = httpContext }
        };
        blockedLogin.PageContext.HttpContext.Request.Form = new FormCollection(
            new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>
            {
                ["email"] = member.Email,
                ["password"] = "ValidPass1"
            });

        var result = await blockedLogin.OnPostAsync();
        result.Should().BeOfType<PageResult>();
        blockedLogin.ModelState.IsValid.Should().BeFalse();
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

using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Routing;
using Moq;
using VereinMember.Areas.Membership.Pages;
using VereinMember.Models;

namespace VereinMember.Tests.Services;

public class MembershipLandingTests
{
    private const string MemberId = "member-user-id";

    [Fact]
    public async Task AnonymousUser_CanLoginAndRegister()
    {
        var model = await CreateLandingPageAsync(null);

        Assert.False(model.IsMemberSignedIn);
        Assert.False(model.CanAdministerMembers);
    }

    [Fact]
    public async Task CmsOnlyIdentity_IsNotTreatedAsMemberOrAdmin()
    {
        // The host CMS and membership module can share a cookie scheme. Even
        // administrator-like claims in that identity must not grant access here.
        var principal = CreatePrincipal("cms-user-id", "Admin");
        var model = await CreateLandingPageAsync(principal);

        Assert.False(model.IsMemberSignedIn);
        Assert.False(model.CanAdministerMembers);
    }

    [Fact]
    public async Task Member_CanOpenProfileAndLogout()
    {
        var model = await CreateLandingPageAsync(CreatePrincipal(MemberId));

        Assert.True(model.IsMemberSignedIn);
        Assert.False(model.CanAdministerMembers);
    }

    [Fact]
    public async Task MembershipAdmin_CanOpenAdministration()
    {
        var model = await CreateLandingPageAsync(CreatePrincipal(MemberId), isAdmin: true);

        Assert.True(model.IsMemberSignedIn);
        Assert.True(model.CanAdministerMembers);
    }

    private static async Task<IndexModel> CreateLandingPageAsync(
        ClaimsPrincipal? principal,
        bool isAdmin = false)
    {
        var userManager = new Mock<UserManager<MembershipUser>>(
            Mock.Of<IUserStore<MembershipUser>>(), null!, null!, null!, null!,
            null!, null!, null!, null!);
        var membershipUser = new MembershipUser { Id = MemberId };
        userManager.Setup(m => m.GetUserAsync(It.IsAny<ClaimsPrincipal>()))
            .ReturnsAsync((ClaimsPrincipal user) => user?.Identity?.Name == "member" ? membershipUser : null);
        userManager.Setup(m => m.IsInRoleAsync(membershipUser, "Admin"))
            .ReturnsAsync(isAdmin);

        var httpContext = new DefaultHttpContext
        {
            User = principal ?? new ClaimsPrincipal(new ClaimsIdentity())
        };
        var model = new IndexModel(userManager.Object)
        {
            PageContext = new PageContext(new ActionContext(
                httpContext, new RouteData(), new PageActionDescriptor()))
        };
        await model.OnGetAsync();
        return model;
    }

    private static ClaimsPrincipal CreatePrincipal(string id, params string[] roles)
    {
        var claims = new List<Claim> { new(ClaimTypes.NameIdentifier, id) };
        if (id == MemberId)
        {
            claims.Add(new Claim(ClaimTypes.Name, "member"));
        }
        claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));
        return new ClaimsPrincipal(new ClaimsIdentity(claims, "Test"));
    }
}

using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VereinMember.Extensions;
using VereinMember.Models;

namespace VereinMember.Areas.Membership.Pages;

/// <summary>
/// The public landing page for the member area. All membership-specific
/// navigation and visibility decisions belong to this module.
/// </summary>
[RequireMembershipEnabled]
public class IndexModel(UserManager<MembershipUser> userManager) : PageModel
{
    public bool IsMemberSignedIn { get; private set; }

    public bool CanAdministerMembers { get; private set; }

    public async Task OnGetAsync()
    {
        // Membership and a host CMS may use the same cookie authentication
        // scheme. Authenticating that cookie alone is not proof of membership.
        var user = await userManager.GetUserAsync(User);
        if (user is null)
        {
            return;
        }

        IsMemberSignedIn = true;
        CanAdministerMembers = await userManager.IsInRoleAsync(user, "Admin");
    }
}

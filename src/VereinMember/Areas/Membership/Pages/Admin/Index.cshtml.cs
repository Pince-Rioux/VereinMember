using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VereinMember.Extensions;

namespace VereinMember.Areas.Membership.Pages.Admin;

[Authorize(Policy = AuthorizationPolicies.AdminOnly)]
[RequireMembershipEnabled]
public class IndexModel : PageModel
{
}

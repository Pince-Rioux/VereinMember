using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VereinMember.Areas.Membership.Pages.Members;

[Authorize(Policy = AuthorizationPolicies.MemberDelete)]
[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class DeleteModel(IMemberService memberService) : PageModel
{
    [BindProperty]
    public Member? Member { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Member = await memberService.GetByIdAsync(id);
        if (Member == null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (Member == null)
        {
            return NotFound();
        }

        var performedBy = User.Identity?.Name ?? "system";
        var result = await memberService.SoftDeleteAsync(Member.Id, performedBy);

        if (!result)
        {
            return NotFound();
        }

        TempData["Success"] = $"Mitglied „{Member.FirstName} {Member.LastName}“ wurde deaktiviert (Status: Ausgetreten).";
        return RedirectToPage("Index");
    }
}
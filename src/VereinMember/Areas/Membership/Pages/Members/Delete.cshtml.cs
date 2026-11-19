using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Pages.Members;

[Authorize(Policy = AuthorizationPolicies.MemberDelete)]
[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class DeleteModel(IMemberService memberService) : PageModel
{
    public Member? Member { get; private set; }

    [BindProperty]
    public DeleteMemberInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Member = await memberService.GetByIdAsync(id);
        if (Member == null)
        {
            return NotFound();
        }

        Input = new DeleteMemberInputModel { Id = Member.Id };
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(int? id = null)
    {
        var targetId = Input.Id > 0 ? Input.Id : (id ?? 0);
        if (targetId <= 0)
        {
            return NotFound();
        }

        var member = await memberService.GetByIdAsync(targetId);
        if (member == null)
        {
            return NotFound();
        }

        var performedBy = User.Identity?.Name ?? "system";
        var result = await memberService.SoftDeleteAsync(member.Id, performedBy);
        if (!result)
        {
            return NotFound();
        }

        TempData["Success"] = $"Mitglied „{member.FirstName} {member.LastName}“ wurde deaktiviert (Status: Ausgetreten).";
        return RedirectToPage("Index");
    }

    public sealed class DeleteMemberInputModel
    {
        public int Id { get; set; }
    }
}

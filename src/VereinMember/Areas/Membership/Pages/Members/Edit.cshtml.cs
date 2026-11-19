using VereinMember.Data;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace VereinMember.Areas.Membership.Pages.Members;

[Authorize(Policy = AuthorizationPolicies.MemberWrite)]
[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class EditModel(
    IMemberService memberService,
    IValidator<Member> validator,
    MembershipDbContext context) : PageModel
{
    [BindProperty]
    public Member Member { get; set; } = new();

    [BindProperty]
    public int? BirthDay { get; set; }

    [BindProperty]
    public int? BirthMonth { get; set; }

    [BindProperty]
    public int? BirthYear { get; set; }

    public List<SelectListItem> FeeOptions { get; set; } = [];
    public List<SelectListItem> DepartmentOptions { get; set; } = [];
    public List<SelectListItem> GuardianOptions { get; set; } = [];
    public List<SelectListItem> FamilyGroupOptions { get; set; } = [];

    public async Task<IActionResult> OnGetAsync(int id)
    {
        var member = await memberService.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound();
        }

        Member = member;
        if (member.BirthDate.Year > 1)
        {
            BirthDay = member.BirthDate.Day;
            BirthMonth = member.BirthDate.Month;
            BirthYear = member.BirthDate.Year;
        }
        await LoadOptionsAsync();
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        // Compose BirthDate from cross-browser selects
        if (BirthYear.HasValue && BirthMonth.HasValue && BirthDay.HasValue)
        {
            try { Member.BirthDate = new DateTime(BirthYear.Value, BirthMonth.Value, BirthDay.Value); }
            catch (ArgumentOutOfRangeException) { ModelState.AddModelError("Member.BirthDate", "Ungültiges Geburtsdatum."); }
        }

        Member.GuardianFirstName = string.IsNullOrWhiteSpace(Member.GuardianFirstName) ? null : Member.GuardianFirstName.Trim();
        Member.GuardianLastName = string.IsNullOrWhiteSpace(Member.GuardianLastName) ? null : Member.GuardianLastName.Trim();
        Member.GuardianEmail = string.IsNullOrWhiteSpace(Member.GuardianEmail) ? null : Member.GuardianEmail.Trim();
        Member.GuardianPhone = string.IsNullOrWhiteSpace(Member.GuardianPhone) ? null : Member.GuardianPhone.Trim();

        var validationResult = await validator.ValidateAsync(Member);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError($"Member.{error.PropertyName}", error.ErrorMessage);
            }

            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            var performedBy = User.Identity?.Name ?? "system";
            var updated = await memberService.UpdateAsync(Member, performedBy);
            TempData["Success"] = $"Mitglied „{updated.FirstName} {updated.LastName}“ erfolgreich aktualisiert.";
            return RedirectToPage("Index");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadOptionsAsync();
            return Page();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task LoadOptionsAsync()
    {
        FeeOptions = await context.MembershipFees
            .Select(f => new SelectListItem
            {
                Value = f.Id.ToString(),
                Text = f.Name + " (" + f.Amount.ToString("C") + ")",
                Group = new SelectListGroup { Name = f.Category.ToString() }
            })
            .ToListAsync();

        DepartmentOptions = await context.Departments
            .Select(d => new SelectListItem { Value = d.Id.ToString(), Text = d.Name })
            .ToListAsync();

        GuardianOptions = await context.Members
            .Where(m => m.MembershipFee!.Category == FeeCategory.Adult && m.Status == MemberStatus.Active)
            .Select(m => new SelectListItem { Value = m.Id.ToString(), Text = m.LastName + ", " + m.FirstName })
            .ToListAsync();

        var familyGroups = await context.FamilyGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .OrderBy(g => g.Id)
            .ToListAsync();

        FamilyGroupOptions = familyGroups
            .Select(g =>
            {
                var activeNames = g.Members
                    .Where(m => m.Status != MemberStatus.Former)
                    .OrderBy(m => m.LastName)
                    .ThenBy(m => m.FirstName)
                    .Select(m => $"{m.FirstName} {m.LastName}".Trim())
                    .Where(n => !string.IsNullOrWhiteSpace(n))
                    .Take(3)
                    .ToList();
                var hint = activeNames.Count > 0 ? string.Join(", ", activeNames) : null;
                return new SelectListItem
                {
                    Value = g.Id.ToString(),
                    Text = MembershipDisplay.FormatFamilyGroupLabel(g, hint)
                };
            })
            .ToList();
    }
}
using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using VereinMember.Data;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

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
    public EditMemberInputModel Input { get; set; } = new();

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

        Input = EditMemberInputModel.FromMember(member);
        await LoadOptionsAsync(id);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var member = Input.ToMember();

        if (!BirthDateHelper.TryComposeBirthDate(member, Input.BirthDay, Input.BirthMonth, Input.BirthYear, ModelState.AddModelError, "Input.BirthDate"))
        {
            await LoadOptionsAsync(Input.Id);
            return Page();
        }

        var validationResult = await validator.ValidateAsync(member);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
            }

            await LoadOptionsAsync(Input.Id);
            return Page();
        }

        try
        {
            var performedBy = User.Identity?.Name ?? "system";
            var updated = await memberService.UpdateAsync(member, performedBy);
            TempData["Success"] = $"Mitglied „{updated.FirstName} {updated.LastName}“ erfolgreich aktualisiert.";
            return RedirectToPage("Index");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadOptionsAsync(Input.Id);
            return Page();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task LoadOptionsAsync(int excludeMemberId)
    {
        FeeOptions = await MembershipFormHelper.LoadFeeOptionsAsync(context);
        DepartmentOptions = await MembershipFormHelper.LoadDepartmentOptionsAsync(context);
        GuardianOptions = await MembershipFormHelper.LoadGuardianOptionsAsync(context, excludeMemberId);
        FamilyGroupOptions = await MembershipFormHelper.LoadFamilyGroupOptionsAsync(context);
    }

    public sealed class EditMemberInputModel
    {
        public int Id { get; set; }
        public byte[] ConcurrencyToken { get; set; } = [];

        [Display(Name = "Vorname")]
        public string? FirstName { get; set; }

        [Display(Name = "Nachname")]
        public string? LastName { get; set; }

        [Display(Name = "E-Mail")]
        public string? Email { get; set; }

        [Display(Name = "Telefon")]
        public string? Phone { get; set; }

        public int? BirthDay { get; set; }
        public int? BirthMonth { get; set; }
        public int? BirthYear { get; set; }

        [Display(Name = "Geburtsdatum")]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Beitragsgruppe")]
        public int MembershipFeeId { get; set; }

        [Display(Name = "Abteilung")]
        public int? DepartmentId { get; set; }

        [Display(Name = "Erziehungsberechtigter")]
        public int? GuardianMemberId { get; set; }

        public string? GuardianFirstName { get; set; }
        public string? GuardianLastName { get; set; }
        public string? GuardianEmail { get; set; }
        public string? GuardianPhone { get; set; }

        [Display(Name = "Familie")]
        public int? FamilyGroupId { get; set; }

        [Display(Name = "Straße")]
        public string? Street { get; set; }

        [Display(Name = "PLZ")]
        public string? ZipCode { get; set; }

        [Display(Name = "Ort")]
        public string? City { get; set; }

        [Display(Name = "Land")]
        public string? Country { get; set; }

        [Display(Name = "Notizen")]
        public string? Notes { get; set; }

        [Display(Name = "IBAN")]
        public string? BankIban { get; set; }

        [Display(Name = "BIC")]
        public string? BankBic { get; set; }

        public static EditMemberInputModel FromMember(Member member) => new()
        {
            Id = member.Id,
            ConcurrencyToken = member.ConcurrencyToken,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Email = member.Email,
            Phone = member.Phone,
            BirthDay = member.BirthDate.Year > 1 ? member.BirthDate.Day : null,
            BirthMonth = member.BirthDate.Year > 1 ? member.BirthDate.Month : null,
            BirthYear = member.BirthDate.Year > 1 ? member.BirthDate.Year : null,
            MembershipFeeId = member.MembershipFeeId,
            DepartmentId = member.DepartmentId,
            GuardianMemberId = member.GuardianMemberId,
            GuardianFirstName = member.GuardianFirstName,
            GuardianLastName = member.GuardianLastName,
            GuardianEmail = member.GuardianEmail,
            GuardianPhone = member.GuardianPhone,
            FamilyGroupId = member.FamilyGroupId,
            Street = member.Street,
            ZipCode = member.ZipCode,
            City = member.City,
            Country = member.Country,
            Notes = member.Notes,
            BankIban = member.BankIban,
            BankBic = member.BankBic
        };

        public Member ToMember() => new()
        {
            Id = Id,
            ConcurrencyToken = ConcurrencyToken,
            FirstName = FirstName?.Trim() ?? string.Empty,
            LastName = LastName?.Trim() ?? string.Empty,
            Email = Email?.Trim() ?? string.Empty,
            Phone = MembershipFormHelper.TrimToNull(Phone),
            MembershipFeeId = MembershipFeeId,
            DepartmentId = DepartmentId,
            GuardianMemberId = GuardianMemberId,
            GuardianFirstName = MembershipFormHelper.TrimToNull(GuardianFirstName),
            GuardianLastName = MembershipFormHelper.TrimToNull(GuardianLastName),
            GuardianEmail = MembershipFormHelper.TrimToNull(GuardianEmail),
            GuardianPhone = MembershipFormHelper.TrimToNull(GuardianPhone),
            FamilyGroupId = FamilyGroupId,
            Street = MembershipFormHelper.TrimToNull(Street),
            ZipCode = MembershipFormHelper.TrimToNull(ZipCode),
            City = MembershipFormHelper.TrimToNull(City),
            Country = MembershipFormHelper.TrimToNull(Country),
            Notes = MembershipFormHelper.TrimToNull(Notes),
            BankIban = MembershipFormHelper.TrimToNull(BankIban),
            BankBic = MembershipFormHelper.TrimToNull(BankBic)
        };
    }
}

using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VereinMember.Areas.Membership.ViewModels;
using VereinMember.Data;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Pages.Members;

[Authorize(Policy = AuthorizationPolicies.MemberWrite)]
[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class CreateModel(
    IMemberService memberService,
    IInvitationService invitationService,
    IMemberAccountService accountService,
    IValidator<Member> validator,
    MembershipDbContext context,
    IConfiguration? configuration = null) : PageModel
{
    [BindProperty]
    public CreateMemberInputModel Input { get; set; } = new();

    /// <summary>
    /// Additional family members (only used when FeeCategory = Family).
    /// </summary>
    [BindProperty]
    public List<FamilyMemberInput> FamilyMembers { get; set; } = [];

    public List<SelectListItem> FeeOptions { get; set; } = [];
    public List<SelectListItem> DepartmentOptions { get; set; } = [];
    public List<SelectListItem> GuardianOptions { get; set; } = [];

    public async Task OnGetAsync()
    {
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var createdBy = User.Identity?.Name ?? "system";

        var selectedFee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Input.MembershipFeeId);

        var member = Input.ToMember();

        if (!BirthDateHelper.TryComposeBirthDate(member, Input.BirthDay, Input.BirthMonth, Input.BirthYear, ModelState.AddModelError, "Input.BirthDate"))
        {
            return await ReloadPageAsync();
        }

        if (selectedFee?.Category == FeeCategory.Family)
        {
            return await HandleFamilyCreateAsync(member, createdBy);
        }

        var validationResult = await validator.ValidateAsync(member);
        if (!validationResult.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
            }

            return await ReloadPageAsync();
        }

        try
        {
            var created = await memberService.CreateAsync(member, createdBy);
            await MembershipFormHelper.SendInvitationAsync(invitationService, accountService, Url, configuration, created);

            TempData["Success"] = $"Mitglied „{created.FirstName} {created.LastName}“ angelegt. Einladungs-E-Mail an {created.Email} gesendet.";
            return RedirectToPage("Index");
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadPageAsync();
        }
    }

    private async Task<IActionResult> HandleFamilyCreateAsync(Member mainMember, string createdBy)
    {
        var hasErrors = false;
        mainMember.GuardianMemberId = null;

        var mainResult = await validator.ValidateAsync(mainMember);
        if (!mainResult.IsValid)
        {
            foreach (var error in mainResult.Errors)
            {
                if (error.PropertyName == nameof(Member.FamilyGroupId))
                {
                    continue;
                }

                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
                hasErrors = true;
            }
        }

        var (subMembers, subHasErrors) = await MembershipFormHelper.ValidateAndMapFamilySubMembersAsync(
            FamilyMembers,
            mainMember,
            context,
            validator,
            ModelState.AddModelError);

        if (hasErrors || subHasErrors)
        {
            return await ReloadPageAsync();
        }

        if (subMembers.Count < 1)
        {
            ModelState.AddModelError(string.Empty, "Eine Familie erfordert mindestens 2 Mitglieder. Bitte fügen Sie mindestens ein weiteres Familienmitglied hinzu.");
            return await ReloadPageAsync();
        }

        try
        {
            var createdMembers = await memberService.CreateFamilyAsync(mainMember, subMembers, createdBy);
            await MembershipFormHelper.SendInvitationsAsync(invitationService, accountService, Url, configuration, createdMembers);

            TempData["Success"] = $"{createdMembers.Count} Familienmitglieder angelegt. Einladungs-E-Mails gesendet.";
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            return await ReloadPageAsync();
        }
    }

    private async Task LoadOptionsAsync()
    {
        FeeOptions = await MembershipFormHelper.LoadFeeOptionsAsync(context);
        DepartmentOptions = await MembershipFormHelper.LoadDepartmentOptionsAsync(context);
        GuardianOptions = await MembershipFormHelper.LoadGuardianOptionsAsync(context);
    }

    private async Task<IActionResult> ReloadPageAsync()
    {
        await LoadOptionsAsync();
        return Page();
    }

    public sealed class CreateMemberInputModel
    {
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

        public Member ToMember() => new()
        {
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

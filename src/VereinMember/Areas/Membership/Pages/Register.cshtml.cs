using System.ComponentModel.DataAnnotations;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using VereinMember.Areas.Membership.ViewModels;
using VereinMember.Data;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Pages;

[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class RegisterModel(
    IMemberService memberService,
    IInvitationService invitationService,
    IMemberAccountService accountService,
    IValidator<Member> validator,
    MembershipDbContext context,
    ILogger<RegisterModel>? logger = null,
    IConfiguration? configuration = null) : PageModel
{
    [BindProperty]
    public RegisterInputModel Input { get; set; } = new();

    /// <summary>
    /// Additional family members (used when FeeCategory = Family).
    /// </summary>
    [BindProperty]
    public List<FamilyMemberInput> FamilyMembers { get; set; } = [];

    public const string MembershipDeclarationVersion = "2026-11-18";
    public const string SepaMandateVersion = "2026-11-18";

    public List<SelectListItem> FeeOptions { get; set; } = [];

    public async Task OnGetAsync()
    {
        if (!VereinMember.Extensions.ServiceCollectionExtensions.Enabled)
        {
            return;
        }

        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!VereinMember.Extensions.ServiceCollectionExtensions.Enabled)
        {
            ModelState.AddModelError(string.Empty, "Mitgliederverwaltung deaktiviert.");
            await LoadOptionsAsync();
            return Page();
        }

        var selectedFee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Input.MembershipFeeId);

        if (selectedFee == null)
        {
            ModelState.AddModelError("Input.MembershipFeeId", "Bitte wählen Sie eine Beitragsgruppe.");
            await LoadOptionsAsync();
            return Page();
        }

        var member = Input.ToMember();

        // Compose BirthDate from the day/month/year selects before any validation path runs.
        if (!BirthDateHelper.TryComposeBirthDate(member, Input.BirthDay, Input.BirthMonth, Input.BirthYear, ModelState.AddModelError, "Input.BirthDate"))
        {
            await LoadOptionsAsync();
            return Page();
        }

        ValidateConsents();

        if (selectedFee.Category == FeeCategory.Family)
        {
            return await HandleFamilyRegistrationAsync(member);
        }

        if (await context.Members.AsNoTracking().AnyAsync(m => m.Email == member.Email))
        {
            ModelState.AddModelError("Input.Email", "Diese E-Mail-Adresse wird bereits verwendet.");
            await LoadOptionsAsync();
            return Page();
        }

        if (selectedFee.Category == FeeCategory.Youth)
        {
            if (string.IsNullOrWhiteSpace(member.GuardianFirstName))
                ModelState.AddModelError("Input.GuardianFirstName", "Vorname des Erziehungsberechtigten ist erforderlich.");
            if (string.IsNullOrWhiteSpace(member.GuardianLastName))
                ModelState.AddModelError("Input.GuardianLastName", "Nachname des Erziehungsberechtigten ist erforderlich.");
            if (string.IsNullOrWhiteSpace(member.GuardianEmail))
                ModelState.AddModelError("Input.GuardianEmail", "E-Mail des Erziehungsberechtigten ist erforderlich.");
        }
        else
        {
            member.GuardianFirstName = null;
            member.GuardianLastName = null;
            member.GuardianEmail = null;
            member.GuardianPhone = null;
        }

        var validationResult = await validator.ValidateAsync(member);
        if (!validationResult.IsValid || !ModelState.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
            }

            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            member.Status = MemberStatus.Active;
            member.JoinedAt = DateTime.UtcNow;
            RecordConsents(member);

            var created = await memberService.CreateAsync(member, member.Email);
            await MembershipFormHelper.SendInvitationAsync(invitationService, accountService, Url, configuration, created);

            TempData["Success"] = $"Registrierung erfolgreich! Bitte prüfen Sie Ihr E-Mail-Postfach ({created.Email}).";
            return RedirectToPage("RegisterConfirmation", new { email = created.Email });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Public registration failed for {Email}", member.Email);
            ModelState.AddModelError(string.Empty, "Registrierung fehlgeschlagen. Bitte prüfen Sie Ihre Eingaben oder versuchen Sie es später erneut.");
            await LoadOptionsAsync();
            return Page();
        }
    }

    private async Task<IActionResult> HandleFamilyRegistrationAsync(Member mainMember)
    {
        mainMember.GuardianMemberId = null;
        mainMember.GuardianFirstName = null;
        mainMember.GuardianLastName = null;
        mainMember.GuardianEmail = null;
        mainMember.GuardianPhone = null;

        var mainResult = await validator.ValidateAsync(mainMember);
        foreach (var error in mainResult.Errors)
        {
            if (error.PropertyName != nameof(Member.FamilyGroupId) && error.PropertyName != nameof(Member.GuardianMemberId))
            {
                ModelState.AddModelError($"Input.{error.PropertyName}", error.ErrorMessage);
            }
        }

        if (await context.Members.AsNoTracking().AnyAsync(m => m.Email == mainMember.Email))
        {
            ModelState.AddModelError("Input.Email", "Diese E-Mail-Adresse wird bereits verwendet.");
            await LoadOptionsAsync();
            return Page();
        }

        var (subMembers, _) = await MembershipFormHelper.ValidateAndMapFamilySubMembersAsync(
            FamilyMembers,
            mainMember,
            context,
            validator,
            ModelState.AddModelError);

        if (!ModelState.IsValid || subMembers.Count < 1)
        {
            if (subMembers.Count < 1)
            {
                ModelState.AddModelError(string.Empty, "Bitte fügen Sie mindestens ein weiteres Familienmitglied hinzu.");
            }

            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            RecordConsents(mainMember);

            var createdMembers = await memberService.CreateFamilyAsync(mainMember, subMembers, mainMember.Email);
            await MembershipFormHelper.SendInvitationsAsync(invitationService, accountService, Url, configuration, createdMembers);

            TempData["Success"] = $"{createdMembers.Count} Familienmitglieder wurden registriert.";
            return RedirectToPage("RegisterConfirmation", new { email = mainMember.Email });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Public family registration failed for {Email}", mainMember.Email);
            ModelState.AddModelError(string.Empty, "Registrierung fehlgeschlagen. Bitte prüfen Sie Ihre Eingaben oder versuchen Sie es später erneut.");
            await LoadOptionsAsync();
            return Page();
        }
    }

    private void ValidateConsents()
    {
        if (!Input.PrivacyConsented)
        {
            ModelState.AddModelError("Input.PrivacyConsented",
                "Bitte bestätigen Sie die Einwilligung und die Mitgliedschaftsbedingungen.");
        }

        if (string.IsNullOrWhiteSpace(Input.BankIban))
        {
            ModelState.AddModelError("Input.BankIban", "Bitte geben Sie Ihre IBAN für das SEPA-Lastschriftmandat ein.");
        }

        if (!Input.SepaMandateConsented)
        {
            ModelState.AddModelError("Input.SepaMandateConsented",
                "Bitte bestätigen Sie das SEPA-Lastschriftmandat.");
        }
    }

    private void RecordConsents(Member member)
    {
        var consentedAt = DateTime.UtcNow;
        member.PrivacyConsentedAt = consentedAt;
        member.PrivacyConsentTextVersion = MembershipDeclarationVersion;
        member.PrivacyConsentIp = HttpContext.Connection.RemoteIpAddress?.ToString();
        member.SepaMandateConsentedAt = consentedAt;
        member.SepaMandateTextVersion = SepaMandateVersion;
    }

    private async Task LoadOptionsAsync()
    {
        FeeOptions = await MembershipFormHelper.LoadFeeOptionsAsync(context);
    }

    public sealed class RegisterInputModel
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

        /// <summary>Validation anchor for composed birth date errors.</summary>
        [Display(Name = "Geburtsdatum")]
        public DateTime? BirthDate { get; set; }

        [Display(Name = "Beitragsgruppe")]
        public int MembershipFeeId { get; set; }

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

        [Display(Name = "IBAN")]
        public string? BankIban { get; set; }

        [Display(Name = "BIC")]
        public string? BankBic { get; set; }

        public bool PrivacyConsented { get; set; }
        public bool SepaMandateConsented { get; set; }

        public Member ToMember() => new()
        {
            FirstName = FirstName?.Trim() ?? string.Empty,
            LastName = LastName?.Trim() ?? string.Empty,
            Email = Email?.Trim() ?? string.Empty,
            Phone = MembershipFormHelper.TrimToNull(Phone),
            MembershipFeeId = MembershipFeeId,
            GuardianFirstName = MembershipFormHelper.TrimToNull(GuardianFirstName),
            GuardianLastName = MembershipFormHelper.TrimToNull(GuardianLastName),
            GuardianEmail = MembershipFormHelper.TrimToNull(GuardianEmail),
            GuardianPhone = MembershipFormHelper.TrimToNull(GuardianPhone),
            Street = MembershipFormHelper.TrimToNull(Street),
            ZipCode = MembershipFormHelper.TrimToNull(ZipCode),
            City = MembershipFormHelper.TrimToNull(City),
            Country = MembershipFormHelper.TrimToNull(Country),
            BankIban = MembershipFormHelper.TrimToNull(BankIban),
            BankBic = MembershipFormHelper.TrimToNull(BankBic)
        };
    }
}

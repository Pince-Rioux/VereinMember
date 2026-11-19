using VereinMember.Data;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

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
    public Member Member { get; set; } = new();

    /// <summary>
    /// Cross-browser date input: day/month/year selects.
    /// Combined into Member.BirthDate before validation.
    /// </summary>
    [BindProperty]
    public int? BirthDay { get; set; }

    [BindProperty]
    public int? BirthMonth { get; set; }

    [BindProperty]
    public int? BirthYear { get; set; }

    /// <summary>
    /// Guardian data for Youth members (not persisted as Member — saved as admin note for now).
    /// In a full implementation, this would create a guardian Member record or link to existing.
    /// </summary>
    [BindProperty]
    public string? GuardianFirstName { get; set; }

    [BindProperty]
    public string? GuardianLastName { get; set; }

    [BindProperty]
    public string? GuardianEmail { get; set; }

    [BindProperty]
    public string? GuardianPhone { get; set; }

    /// <summary>
    /// Additional family members (used when FeeCategory = Family).
    /// </summary>
    [BindProperty]
    public List<FamilyMemberItem> FamilyMembers { get; set; } = [];

    [BindProperty]
    public bool PrivacyConsented { get; set; }

    [BindProperty]
    public bool SepaMandateConsented { get; set; }

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
        ClearAutomaticFamilyRowValidation();

        var selectedFee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Member.MembershipFeeId);

        if (!VereinMember.Extensions.ServiceCollectionExtensions.Enabled)
        {
            ModelState.AddModelError(string.Empty, "Mitgliederverwaltung deaktiviert.");
            await LoadOptionsAsync();
            return Page();
        }

        if (selectedFee == null)
        {
            ModelState.AddModelError("Member.MembershipFeeId", "Bitte wählen Sie eine Beitragsgruppe.");
            await LoadOptionsAsync();
            return Page();
        }

        // Compose BirthDate from the day/month/year selects before any validation path runs.
        // This must happen for family registrations too, otherwise the main member's
        // BirthDate stays at its default and the validator reports "Birth date is required".
        if (!TryComposeBirthDate(Member, BirthDay, BirthMonth, BirthYear))
        {
            await LoadOptionsAsync();
            return Page();
        }

        ValidateConsents();

        // For self-service registration, Family members use CreateFamilyAsync
        if (selectedFee.Category == FeeCategory.Family)
        {
            return await HandleFamilyRegistrationAsync();
        }

        // Validate email uniqueness
        var existingEmail = await context.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Email == Member.Email);
        if (existingEmail != null)
        {
            ModelState.AddModelError("Member.Email", "Diese E-Mail-Adresse wird bereits verwendet.");
            await LoadOptionsAsync();
            return Page();
        }

        // For Youth registrations, capture and validate non-member guardian contact details
        if (selectedFee.Category == FeeCategory.Youth && !Member.GuardianMemberId.HasValue)
        {
            Member.GuardianFirstName = string.IsNullOrWhiteSpace(GuardianFirstName) ? null : GuardianFirstName.Trim();
            Member.GuardianLastName = string.IsNullOrWhiteSpace(GuardianLastName) ? null : GuardianLastName.Trim();
            Member.GuardianEmail = string.IsNullOrWhiteSpace(GuardianEmail) ? null : GuardianEmail.Trim();
            Member.GuardianPhone = string.IsNullOrWhiteSpace(GuardianPhone) ? null : GuardianPhone.Trim();

            if (string.IsNullOrWhiteSpace(Member.GuardianFirstName))
                ModelState.AddModelError(nameof(GuardianFirstName), "Vorname des Erziehungsberechtigten ist erforderlich.");
            if (string.IsNullOrWhiteSpace(Member.GuardianLastName))
                ModelState.AddModelError(nameof(GuardianLastName), "Nachname des Erziehungsberechtigten ist erforderlich.");
            if (string.IsNullOrWhiteSpace(Member.GuardianEmail))
                ModelState.AddModelError(nameof(GuardianEmail), "E-Mail des Erziehungsberechtigten ist erforderlich.");
        }

        // Validate member
        var validationResult = await validator.ValidateAsync(Member);
        if (!validationResult.IsValid || !ModelState.IsValid)
        {
            foreach (var error in validationResult.Errors)
            {
                if (error.PropertyName == nameof(Member.GuardianEmail))
                    ModelState.AddModelError(nameof(GuardianEmail), error.ErrorMessage);
                else
                    ModelState.AddModelError($"Member.{error.PropertyName}", error.ErrorMessage);
            }
            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            // Create the member
            Member.Status = MemberStatus.Active;
            Member.JoinedAt = DateTime.UtcNow;
            RecordConsents(Member);

            var created = await memberService.CreateAsync(Member, Member.Email);

            // Send invitation email with magic link
            var invitation = await invitationService.CreateInvitationAsync(created.Id, created.Email);
            string inviteUrl = ActivationUrlBuilder.Build(Url, invitation.Token, configuration);
            await accountService.SendInvitationEmailAsync(
                created.Email,
                $"{created.FirstName} {created.LastName}",
                inviteUrl);

            TempData["Success"] = $"Registrierung erfolgreich! Bitte prüfen Sie Ihr E-Mail-Postfach ({created.Email}).";
            return RedirectToPage("RegisterConfirmation", new { email = created.Email });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Public registration failed for {Email}", Member.Email);
            ModelState.AddModelError(string.Empty, "Registrierung fehlgeschlagen. Bitte prüfen Sie Ihre Eingaben oder versuchen Sie es später erneut.");
            await LoadOptionsAsync();
            return Page();
        }
    }

    private async Task<IActionResult> HandleFamilyRegistrationAsync()
    {
        // Validate main member
        Member.GuardianMemberId = null;
        var mainResult = await validator.ValidateAsync(Member);
        foreach (var error in mainResult.Errors)
        {
            if (error.PropertyName != "FamilyGroupId" && error.PropertyName != "GuardianMemberId")
                ModelState.AddModelError($"Member.{error.PropertyName}", error.ErrorMessage);
        }

        // Validate email uniqueness
        var existingEmail = await context.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Email == Member.Email);
        if (existingEmail != null)
        {
            ModelState.AddModelError("Member.Email", "Diese E-Mail-Adresse wird bereits verwendet.");
            await LoadOptionsAsync();
            return Page();
        }

        // Convert sub-members
        var subMembers = new List<Member>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Member.Email))
            seenEmails.Add(Member.Email.Trim());

        for (int i = 0; i < FamilyMembers.Count; i++)
        {
            var fm = FamilyMembers[i];
            if (string.IsNullOrWhiteSpace(fm.FirstName) && string.IsNullOrWhiteSpace(fm.LastName))
                continue;

            var subEmail = fm.Email?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(subEmail))
            {
                if (!seenEmails.Add(subEmail))
                {
                    ModelState.AddModelError($"FamilyMembers[{i}].Email", "Diese E-Mail-Adresse ist in der Familie doppelt.");
                }
                else if (await context.Members.AsNoTracking().AnyAsync(m => m.Email == subEmail))
                {
                    ModelState.AddModelError($"FamilyMembers[{i}].Email", "Diese E-Mail-Adresse wird bereits verwendet.");
                }
            }

            if (fm.BirthDay.HasValue && fm.BirthMonth.HasValue && fm.BirthYear.HasValue && fm.BirthDate == DateTime.MinValue)
            {
                ModelState.AddModelError($"FamilyMembers[{i}].BirthDate", "Ungültiges Geburtsdatum.");
            }

            var subMember = new Member
            {
                FirstName = fm.FirstName?.Trim() ?? string.Empty,
                LastName = fm.LastName?.Trim() ?? string.Empty,
                Email = subEmail,
                BirthDate = fm.BirthDate,
                JoinedAt = fm.JoinedAt == default ? DateTime.UtcNow : fm.JoinedAt,
                MembershipFeeId = Member.MembershipFeeId,
                Street = Member.Street,
                City = Member.City,
                ZipCode = Member.ZipCode,
                Country = Member.Country,
            };

            var subResult = await validator.ValidateAsync(subMember);
            if (!subResult.IsValid)
            {
                foreach (var error in subResult.Errors)
                    ModelState.AddModelError($"FamilyMembers[{i}].{error.PropertyName}", error.ErrorMessage);
            }
            subMembers.Add(subMember);
        }

        if (!ModelState.IsValid || subMembers.Count < 1)
        {
            if (subMembers.Count < 1)
                ModelState.AddModelError(string.Empty, "Bitte fügen Sie mindestens ein weiteres Familienmitglied hinzu.");
            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            // The account holder registers the family and gives both declarations.
            RecordConsents(Member);

            // Create all family members
            var createdMembers = await memberService.CreateFamilyAsync(Member, subMembers, Member.Email);

            // Create invitations for all family members
            foreach (var cm in createdMembers)
            {
                var invitation = await invitationService.CreateInvitationAsync(cm.Id, cm.Email);
                string inviteUrl = ActivationUrlBuilder.Build(Url, invitation.Token, configuration);

                await accountService.SendInvitationEmailAsync(
                    cm.Email,
                    $"{cm.FirstName} {cm.LastName}",
                    inviteUrl);
            }

            TempData["Success"] = $"{createdMembers.Count} Familienmitglieder wurden registriert.";
            return RedirectToPage("RegisterConfirmation", new { email = Member.Email });
        }
        catch (Exception ex)
        {
            logger?.LogError(ex, "Public family registration failed for {Email}", Member.Email);
            ModelState.AddModelError(string.Empty, "Registrierung fehlgeschlagen. Bitte prüfen Sie Ihre Eingaben oder versuchen Sie es später erneut.");
            await LoadOptionsAsync();
            return Page();
        }
    }

    private bool TryComposeBirthDate(Member member, int? day, int? month, int? year)
    {
        return BirthDateHelper.TryComposeBirthDate(member, day, month, year, ModelState.AddModelError);
    }

    private void ClearAutomaticFamilyRowValidation()
    {
        // MVC also binds the deliberately non-numeric INDEX template. Its blank
        // fields must not generate implicit required errors. Filled-in rows are
        // validated explicitly in HandleFamilyRegistrationAsync below.
        foreach (var key in ModelState.Keys
                     .Where(key => key.StartsWith("FamilyMembers", StringComparison.Ordinal))
                     .ToList())
        {
            ModelState.Remove(key);
        }
    }

    private void ValidateConsents()
    {
        if (!PrivacyConsented)
        {
            ModelState.AddModelError(nameof(PrivacyConsented),
                "Bitte bestätigen Sie die Einwilligung und die Mitgliedschaftsbedingungen.");
        }
        if (string.IsNullOrWhiteSpace(Member.BankIban))
        {
            ModelState.AddModelError("Member.BankIban", "Bitte geben Sie Ihre IBAN für das SEPA-Lastschriftmandat ein.");
        }
        if (!SepaMandateConsented)
        {
            ModelState.AddModelError(nameof(SepaMandateConsented),
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
        FeeOptions = await context.MembershipFees
            .Select(f => new SelectListItem
            {
                Value = f.Id.ToString(),
                Text = f.Name + " (" + f.Amount.ToString("C") + ")",
                Group = new SelectListGroup { Name = f.Category.ToString() }
            })
            .ToListAsync();
    }

    public class FamilyMemberItem
    {
        // Empty template rows are intentionally allowed here; filled-in rows
        // are validated explicitly in HandleFamilyRegistrationAsync.
        public string? FirstName { get; set; }
        public string? LastName { get; set; }
        public string? Email { get; set; }
        public int? BirthDay { get; set; }
        public int? BirthMonth { get; set; }
        public int? BirthYear { get; set; }
        [System.Text.Json.Serialization.JsonIgnore]
        public DateTime BirthDate
        {
            get
            {
                if (BirthDay is >= 1 and <= 31 && BirthMonth is >= 1 and <= 12 && BirthYear is >= 1900 and <= 2100)
                {
                    if (BirthDay.Value <= DateTime.DaysInMonth(BirthYear.Value, BirthMonth.Value))
                        return new DateTime(BirthYear.Value, BirthMonth.Value, BirthDay.Value);
                }
                return DateTime.MinValue;
            }
        }
        public DateTime JoinedAt { get; set; } = DateTime.Today;
    }
}
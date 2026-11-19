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
using Microsoft.Extensions.Configuration;

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
    public Member Member { get; set; } = new();

    [BindProperty]
    public int? BirthDay { get; set; }

    [BindProperty]
    public int? BirthMonth { get; set; }

    [BindProperty]
    public int? BirthYear { get; set; }

    /// <summary>
    /// Additional family members (only used when FeeCategory = Family).
    /// </summary>
    [BindProperty]
    public List<FamilyMemberItem> FamilyMembers { get; set; } = [];

    public List<SelectListItem> FeeOptions { get; set; } = [];
    public List<SelectListItem> DepartmentOptions { get; set; } = [];
    public List<SelectListItem> GuardianOptions { get; set; } = [];
    public bool IsFamilyFee { get; set; }

    public async Task OnGetAsync()
    {
        await LoadOptionsAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var createdBy = User.Identity?.Name ?? "system";

        // Check if Family fee category
        var selectedFee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == Member.MembershipFeeId);

        // Compose BirthDate from cross-browser selects
        if (!TryComposeBirthDate(Member, BirthDay, BirthMonth, BirthYear)) return await ReloadPageAsync();

        if (selectedFee?.Category == FeeCategory.Family)
        {
            return await HandleFamilyCreateAsync(createdBy);
        }

        // Standard (non-family) create
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

        var created = await memberService.CreateAsync(Member, createdBy);

        // Create invitation + send Magic-Link email
        var invitation = await invitationService.CreateInvitationAsync(created.Id, created.Email);
        var inviteUrl = ActivationUrlBuilder.Build(Url, invitation.Token, configuration);
        await accountService.SendInvitationEmailAsync(
            created.Email,
            $"{created.FirstName} {created.LastName}",
            inviteUrl);

        TempData["Success"] = $"Mitglied „{created.FirstName} {created.LastName}“ angelegt. Einladungs-E-Mail an {created.Email} gesendet.";
        return RedirectToPage("Index");
    }

    private async Task<IActionResult> HandleFamilyCreateAsync(string createdBy)
    {
        // Collect all members: main + family sub-members (if any)
        var allFamilyMembers = new List<Member>();
        bool hasErrors = false;

        // Validate main member (skip FamilyGroupId check — it's set programmatically)
        Member.GuardianMemberId = null; // Family members don't have guardians
        var mainResult = await validator.ValidateAsync(Member, options =>
            options.IncludeRuleSets(new string[0])); // lightweight: just run all rules

        if (!mainResult.IsValid)
        {
            foreach (var error in mainResult.Errors)
            {
                // Skip FamilyGroupId error — it will be set by the service
                if (error.PropertyName == "FamilyGroupId") continue;
                ModelState.AddModelError($"Member.{error.PropertyName}", error.ErrorMessage);
                hasErrors = true;
            }
        }

        // Convert FamilyMemberItem → Member for each sub-member
        var subMembers = new List<Member>();
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(Member.Email))
            seenEmails.Add(Member.Email.Trim());

        for (int i = 0; i < FamilyMembers.Count; i++)
        {
            var fm = FamilyMembers[i];
            if (string.IsNullOrWhiteSpace(fm.FirstName) && string.IsNullOrWhiteSpace(fm.LastName) && string.IsNullOrWhiteSpace(fm.Email))
                continue; // skip empty rows

            var subEmail = fm.Email?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(subEmail))
            {
                if (!seenEmails.Add(subEmail))
                {
                    ModelState.AddModelError($"FamilyMembers[{i}].Email", "Doppelte E-Mail-Adresse innerhalb der Familie.");
                    hasErrors = true;
                }
                else if (await context.Members.AsNoTracking().AnyAsync(m => m.Email == subEmail))
                {
                    ModelState.AddModelError($"FamilyMembers[{i}].Email", "Diese E-Mail-Adresse ist bereits registriert.");
                    hasErrors = true;
                }
            }

            if (fm.BirthDay.HasValue && fm.BirthMonth.HasValue && fm.BirthYear.HasValue && fm.BirthDate == DateTime.MinValue)
            {
                ModelState.AddModelError($"FamilyMembers[{i}].BirthDate", "Ungültiges Geburtsdatum.");
                hasErrors = true;
            }

            var subMember = new Member
            {
                FirstName = fm.FirstName?.Trim() ?? "",
                LastName = fm.LastName?.Trim() ?? "",
                Email = subEmail,
                BirthDate = fm.BirthDate,
                JoinedAt = fm.JoinedAt == default ? DateTime.UtcNow : fm.JoinedAt,
                MembershipFeeId = Member.MembershipFeeId, // same fee category
                Street = Member.Street,
                City = Member.City,
                ZipCode = Member.ZipCode,
                Country = Member.Country,
            };

            // Validate sub-member
            var subResult = await validator.ValidateAsync(subMember, options =>
                options.IncludeRuleSets(new string[0]));

            if (!subResult.IsValid)
            {
                foreach (var error in subResult.Errors)
                {
                    ModelState.AddModelError($"FamilyMembers[{i}].{error.PropertyName}", error.ErrorMessage);
                    hasErrors = true;
                }
            }

            subMembers.Add(subMember);
        }

        if (hasErrors)
        {
            await LoadOptionsAsync();
            return Page();
        }

        // Minimum 2 total members (main + at least 1 sub)
        if (subMembers.Count < 1)
        {
            ModelState.AddModelError(string.Empty, "Eine Familie erfordert mindestens 2 Mitglieder. Bitte fügen Sie mindestens ein weiteres Familienmitglied hinzu.");
            await LoadOptionsAsync();
            return Page();
        }

        try
        {
            var createdMembers = await memberService.CreateFamilyAsync(Member, subMembers, createdBy);

            // Create invitations + send Magic-Link emails for all family members
            foreach (var cm in createdMembers)
            {
                var invitation = await invitationService.CreateInvitationAsync(cm.Id, cm.Email);
                var inviteUrl = ActivationUrlBuilder.Build(Url, invitation.Token, configuration);
                await accountService.SendInvitationEmailAsync(
                    cm.Email,
                    $"{cm.FirstName} {cm.LastName}",
                    inviteUrl);
            }

            TempData["Success"] = $"{createdMembers.Count} Familienmitglieder angelegt. Einladungs-E-Mails gesendet.";
            return RedirectToPage("Index");
        }
        catch (Exception ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            await LoadOptionsAsync();
            return Page();
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
    }

    private bool TryComposeBirthDate(Member member, int? day, int? month, int? year)
    {
        return BirthDateHelper.TryComposeBirthDate(member, day, month, year, ModelState.AddModelError);
    }

    private async Task<IActionResult> ReloadPageAsync()
    {
        await LoadOptionsAsync();
        return Page();
    }

    public class FamilyMemberItem
    {
        public string FirstName { get; set; } = string.Empty;
        public string LastName { get; set; } = string.Empty;
        public string Email { get; set; } = string.Empty;
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
        public string Relationship { get; set; } = string.Empty;
        public DateTime JoinedAt { get; set; } = DateTime.Today;
    }
}
using System.ComponentModel.DataAnnotations;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VereinMember.Areas.Membership.Pages;

[Authorize]
[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class ProfileModel(IMemberService memberService, IFamilyGroupService familyGroupService) : PageModel
{
    public Member? Member { get; set; }

    public IReadOnlyList<FamilyMemberSummary> FamilyMembers { get; private set; } = [];

    public string? FamilyDisplayName => Member?.FamilyGroup is { } group
        ? (string.IsNullOrWhiteSpace(group.Name) ? $"Familie #{group.Id}" : group.Name)
        : null;

    public string GuardianDisplayName => Member?.GuardianMember is { } guardian
        ? $"{guardian.FirstName} {guardian.LastName}".Trim()
        : $"{Member?.GuardianFirstName} {Member?.GuardianLastName}".Trim();

    public string? GuardianEmail => Member?.GuardianMember is { } guardian
        ? guardian.Email
        : Member?.GuardianEmail;

    public string? GuardianPhone => Member?.GuardianMember is { } guardian
        ? guardian.Phone
        : Member?.GuardianPhone;

    [BindProperty]
    public ProfileUpdateInputModel Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
        {
            return Challenge();
        }

        var member = await memberService.GetByEmailAsync(email);
        if (member == null)
        {
            return NotFound();
        }

        Member = member;
        Input = ProfileUpdateInputModel.FromMember(member);
        await LoadFamilyMembersAsync(member);
        return Page();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var email = User.Identity?.Name;
        if (string.IsNullOrEmpty(email))
        {
            return Challenge();
        }

        var currentMember = await memberService.GetByEmailAsync(email);
        if (currentMember == null)
        {
            return NotFound();
        }

        if (currentMember.Id != Input.Id)
        {
            return Forbid();
        }

        if (!ModelState.IsValid)
        {
            Member = currentMember;
            await LoadFamilyMembersAsync(currentMember);
            return Page();
        }

        try
        {
            var updatePayload = new Member
            {
                Id = currentMember.Id,
                FirstName = Input.FirstName.Trim(),
                LastName = Input.LastName.Trim(),
                Phone = string.IsNullOrWhiteSpace(Input.Phone) ? null : Input.Phone.Trim(),
                Street = string.IsNullOrWhiteSpace(Input.Street) ? null : Input.Street.Trim(),
                ZipCode = string.IsNullOrWhiteSpace(Input.ZipCode) ? null : Input.ZipCode.Trim(),
                City = string.IsNullOrWhiteSpace(Input.City) ? null : Input.City.Trim(),
                Country = string.IsNullOrWhiteSpace(Input.Country) ? null : Input.Country.Trim(),
                Notes = string.IsNullOrWhiteSpace(Input.Notes) ? null : Input.Notes.Trim(),
                ConcurrencyToken = Input.ConcurrencyToken,

                // Immutable / admin-controlled fields preserved from stored member
                Email = currentMember.Email,
                BirthDate = currentMember.BirthDate,
                MembershipFeeId = currentMember.MembershipFeeId,
                DepartmentId = currentMember.DepartmentId,
                GuardianMemberId = currentMember.GuardianMemberId,
                FamilyGroupId = currentMember.FamilyGroupId,
                BankIban = currentMember.BankIban,
                BankBic = currentMember.BankBic,
                Status = currentMember.Status
            };

            await memberService.UpdateOwnProfileAsync(currentMember.Id, updatePayload, email);
            TempData["Success"] = "Profil erfolgreich aktualisiert.";
            return RedirectToPage();
        }
        catch (InvalidOperationException ex)
        {
            ModelState.AddModelError(string.Empty, ex.Message);
            Member = currentMember;
            await LoadFamilyMembersAsync(currentMember);
            return Page();
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
    }

    private async Task LoadFamilyMembersAsync(Member member)
    {
        FamilyMembers = member.FamilyGroupId is int familyGroupId
            ? await familyGroupService.GetFamilyMembersAsync(familyGroupId)
            : [];
    }

    public sealed class ProfileUpdateInputModel
    {
        public int Id { get; set; }
        public byte[] ConcurrencyToken { get; set; } = [];

        [Required(ErrorMessage = "Vorname ist erforderlich.")]
        [MaxLength(100)]
        public string FirstName { get; set; } = string.Empty;

        [Required(ErrorMessage = "Nachname ist erforderlich.")]
        [MaxLength(100)]
        public string LastName { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? Phone { get; set; }

        [MaxLength(200)]
        public string? Street { get; set; }

        [MaxLength(20)]
        public string? ZipCode { get; set; }

        [MaxLength(100)]
        public string? City { get; set; }

        [MaxLength(100)]
        public string? Country { get; set; }

        [MaxLength(2000)]
        public string? Notes { get; set; }

        public static ProfileUpdateInputModel FromMember(Member member) => new()
        {
            Id = member.Id,
            ConcurrencyToken = member.ConcurrencyToken,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Phone = member.Phone,
            Street = member.Street,
            ZipCode = member.ZipCode,
            City = member.City,
            Country = member.Country,
            Notes = member.Notes
        };
    }
}

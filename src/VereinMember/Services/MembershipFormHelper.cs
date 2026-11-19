using FluentValidation;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using VereinMember.Areas.Membership.ViewModels;
using VereinMember.Data;
using VereinMember.Models;

namespace VereinMember.Services;

/// <summary>
/// Shared Razor Pages form helpers for dropdown loading, family-batch validation/mapping,
/// and magic-link invitation dispatch. Keeps PageModels thin without introducing
/// an abstract base PageModel hierarchy.
/// </summary>
public static class MembershipFormHelper
{
    public static Task<List<SelectListItem>> LoadFeeOptionsAsync(MembershipDbContext context)
    {
        return context.MembershipFees
            .AsNoTracking()
            .Select(f => new SelectListItem
            {
                Value = f.Id.ToString(),
                Text = f.Name + " (" + f.Amount.ToString("C") + ")",
                Group = new SelectListGroup { Name = f.Category.ToString() }
            })
            .ToListAsync();
    }

    public static Task<List<SelectListItem>> LoadDepartmentOptionsAsync(MembershipDbContext context)
    {
        return context.Departments
            .AsNoTracking()
            .Select(d => new SelectListItem { Value = d.Id.ToString(), Text = d.Name })
            .ToListAsync();
    }

    public static Task<List<SelectListItem>> LoadGuardianOptionsAsync(
        MembershipDbContext context,
        int? excludeMemberId = null)
    {
        var query = context.Members
            .AsNoTracking()
            .Where(m => m.MembershipFee!.Category == FeeCategory.Adult && m.Status == MemberStatus.Active);

        if (excludeMemberId.HasValue)
        {
            query = query.Where(m => m.Id != excludeMemberId.Value);
        }

        return query
            .Select(m => new SelectListItem { Value = m.Id.ToString(), Text = m.LastName + ", " + m.FirstName })
            .ToListAsync();
    }

    public static async Task<List<SelectListItem>> LoadFamilyGroupOptionsAsync(MembershipDbContext context)
    {
        var familyGroups = await context.FamilyGroups
            .AsNoTracking()
            .Include(g => g.Members)
            .OrderBy(g => g.Id)
            .ToListAsync();

        return familyGroups
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

    public static async Task SendInvitationAsync(
        IInvitationService invitationService,
        IMemberAccountService accountService,
        IUrlHelper urlHelper,
        IConfiguration? configuration,
        Member member)
    {
        var invitation = await invitationService.CreateInvitationAsync(member.Id, member.Email);
        var inviteUrl = ActivationUrlBuilder.Build(urlHelper, invitation.Token, configuration);
        await accountService.SendInvitationEmailAsync(
            member.Email,
            $"{member.FirstName} {member.LastName}",
            inviteUrl);
    }

    public static async Task SendInvitationsAsync(
        IInvitationService invitationService,
        IMemberAccountService accountService,
        IUrlHelper urlHelper,
        IConfiguration? configuration,
        IEnumerable<Member> members)
    {
        foreach (var member in members)
        {
            await SendInvitationAsync(invitationService, accountService, urlHelper, configuration, member);
        }
    }

    public static async Task<(List<Member> SubMembers, bool HasErrors)> ValidateAndMapFamilySubMembersAsync(
        IReadOnlyList<FamilyMemberInput> inputs,
        Member mainMember,
        MembershipDbContext context,
        IValidator<Member> validator,
        Action<string, string> addModelError)
    {
        var subMembers = new List<Member>();
        var hasErrors = false;
        var seenEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(mainMember.Email))
        {
            seenEmails.Add(mainMember.Email.Trim());
        }

        for (int i = 0; i < inputs.Count; i++)
        {
            var fm = inputs[i];
            if (fm.IsEmpty)
            {
                continue;
            }

            var subEmail = fm.Email?.Trim() ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(subEmail))
            {
                if (!seenEmails.Add(subEmail))
                {
                    addModelError($"FamilyMembers[{i}].Email", "Diese E-Mail-Adresse ist in der Familie doppelt.");
                    hasErrors = true;
                }
                else if (await context.Members.AsNoTracking().AnyAsync(m => m.Email == subEmail))
                {
                    addModelError($"FamilyMembers[{i}].Email", "Diese E-Mail-Adresse wird bereits verwendet.");
                    hasErrors = true;
                }
            }

            if (fm.BirthDay.HasValue && fm.BirthMonth.HasValue && fm.BirthYear.HasValue && fm.BirthDate == DateTime.MinValue)
            {
                addModelError($"FamilyMembers[{i}].BirthDate", "Ungültiges Geburtsdatum.");
                hasErrors = true;
            }

            var joinedAt = fm.JoinedAt is { Year: > 1 } explicitJoin ? explicitJoin : DateTime.UtcNow;
            var subMember = new Member
            {
                FirstName = fm.FirstName?.Trim() ?? string.Empty,
                LastName = fm.LastName?.Trim() ?? string.Empty,
                Email = subEmail,
                BirthDate = fm.BirthDate,
                JoinedAt = joinedAt,
                MembershipFeeId = mainMember.MembershipFeeId,
                Street = mainMember.Street,
                City = mainMember.City,
                ZipCode = mainMember.ZipCode,
                Country = mainMember.Country
            };

            var subResult = await validator.ValidateAsync(subMember);
            if (!subResult.IsValid)
            {
                foreach (var error in subResult.Errors)
                {
                    if (error.PropertyName == nameof(Member.FamilyGroupId))
                    {
                        continue;
                    }

                    addModelError($"FamilyMembers[{i}].{error.PropertyName}", error.ErrorMessage);
                    hasErrors = true;
                }
            }

            subMembers.Add(subMember);
        }

        return (subMembers, hasErrors);
    }

    public static string? TrimToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

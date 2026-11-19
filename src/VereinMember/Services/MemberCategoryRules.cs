using Microsoft.EntityFrameworkCore;
using VereinMember.Data;
using VereinMember.Models;

namespace VereinMember.Services;

internal static class MemberCategoryRules
{
    public static bool HasNonMemberGuardian(Member member) =>
        !string.IsNullOrWhiteSpace(member.GuardianFirstName)
        && !string.IsNullOrWhiteSpace(member.GuardianLastName)
        && !string.IsNullOrWhiteSpace(member.GuardianEmail);

    public static void ClearNonMemberGuardian(Member member)
    {
        member.GuardianFirstName = null;
        member.GuardianLastName = null;
        member.GuardianEmail = null;
        member.GuardianPhone = null;
    }

    /// <summary>
    /// Validates fee category invariants for Youth, Family, and Adult members
    /// and clears stale cross-category references so Create and Update share one rule set.
    /// </summary>
    public static async Task ValidateAndNormalizeAsync(
        MembershipDbContext context,
        Member member,
        int? existingMemberId = null)
    {
        var fee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == member.MembershipFeeId)
            ?? throw new InvalidOperationException("Selected membership fee not found.");

        switch (fee.Category)
        {
            case FeeCategory.Youth:
                if (member.GuardianMemberId != null)
                {
                    if (existingMemberId.HasValue && member.GuardianMemberId == existingMemberId.Value)
                        throw new InvalidOperationException("A member cannot be their own guardian.");

                    var guardianFee = await context.MembershipFees
                        .AsNoTracking()
                        .FirstOrDefaultAsync(f => f.Id == context.Members
                            .Where(m => m.Id == member.GuardianMemberId)
                            .Select(m => m.MembershipFeeId)
                            .FirstOrDefault());
                    if (guardianFee == null || guardianFee.Category != FeeCategory.Adult)
                        throw new InvalidOperationException("Guardian must be an Adult member.");
                }
                else if (!HasNonMemberGuardian(member))
                {
                    throw new InvalidOperationException("Youth member must have a guardian.");
                }
                break;

            case FeeCategory.Family:
                if (member.FamilyGroupId == null)
                    throw new InvalidOperationException("Family member must belong to a family group.");
                member.GuardianMemberId = null;
                ClearNonMemberGuardian(member);
                break;

            case FeeCategory.Adult:
                member.GuardianMemberId = null;
                ClearNonMemberGuardian(member);
                member.FamilyGroupId = null;
                break;
        }
    }
}

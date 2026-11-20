using Microsoft.EntityFrameworkCore;
using VereinMember.Data;
using VereinMember.Models;

namespace VereinMember.Services;

internal static class MemberErasureHelper
{
    public static async Task EraseMemberPersonalDataAsync(
        MembershipDbContext context,
        IAuditService auditService,
        Member member,
        string auditAction,
        string performedBy,
        bool preserveLeftAt,
        CancellationToken ct = default)
    {
        var originalEmail = member.Email;

        // Remove invitations and Identity account before overwriting email
        var invitations = await context.Invitations
            .Where(i => i.MemberId == member.Id)
            .ToListAsync(ct);
        if (invitations.Count > 0)
            context.Invitations.RemoveRange(invitations);

        if (!string.IsNullOrWhiteSpace(originalEmail))
        {
            var identityUsers = await context.Users
                .Where(u => u.Email == originalEmail)
                .ToListAsync(ct);
            if (identityUsers.Count > 0)
                context.Users.RemoveRange(identityUsers);
        }

        // Erase personal data without copying PII into AuditLog
        var now = DateTime.UtcNow;
        member.FirstName = "[erased]";
        member.LastName = "[erased]";
        member.Email = $"[erased-{member.Id}]@verein.de";
        member.Phone = null;
        member.BirthDate = DateTime.MinValue;
        member.Street = null;
        member.City = null;
        member.ZipCode = null;
        member.Country = null;
        member.BankIban = null;
        member.BankBic = null;
        member.PrivacyConsentedAt = null;
        member.PrivacyConsentTextVersion = null;
        member.PrivacyConsentIp = null;
        member.SepaMandateConsentedAt = null;
        member.SepaMandateTextVersion = null;
        member.Notes = null;
        member.GuardianMemberId = null;
        MemberCategoryRules.ClearNonMemberGuardian(member);
        member.FamilyGroupId = null;
        member.DepartmentId = null;
        member.Status = MemberStatus.Former;
        if (!preserveLeftAt)
            member.LeftAt = now;
        member.UpdatedAt = now;

        // Redact historical audit snapshots for this member so erased PII is not retained in AuditLog
        var historicalAuditEntries = await context.AuditLog
            .Where(a => a.EntityName == nameof(Member) && a.EntityId == member.Id)
            .ToListAsync(ct);
        foreach (var entry in historicalAuditEntries)
        {
            entry.OldValues = null;
            entry.NewValues = null;
        }

        await context.SaveChangesAsync(ct);

        await auditService.LogAsync(
            nameof(Member), member.Id, auditAction,
            null, null,
            nameof(Member), performedBy);
    }
}

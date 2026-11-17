using System.Text.Json;
using System.Text.Json.Serialization;
using VereinMember.Data;
using VereinMember.Models;
using Microsoft.EntityFrameworkCore;

namespace VereinMember.Services;

public class MemberService(MembershipDbContext context, IAuditService auditService) : IMemberService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
        ReferenceHandler = ReferenceHandler.IgnoreCycles
    };

    public async Task<Member?> GetByIdAsync(int id)
    {
        return await context.Members
            .Include(m => m.Department)
            .Include(m => m.MembershipFee)
            .Include(m => m.GuardianMember)
            .Include(m => m.FamilyGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id);
    }

    public async Task<Member?> GetByEmailAsync(string email)
    {
        return await context.Members
            .Include(m => m.Department)
            .Include(m => m.MembershipFee)
            .Include(m => m.GuardianMember)
            .Include(m => m.FamilyGroup)
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Email == email);
    }

    public async Task<List<Member>> GetAllAsync()
    {
        return await context.Members
            .Include(m => m.Department)
            .Include(m => m.MembershipFee)
            .AsNoTracking()
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .ToListAsync();
    }

    public async Task<(List<Member> Items, int TotalCount)> SearchAsync(
        string? searchTerm, MemberStatus? status, int? departmentId,
        FeeCategory? feeCategory, int page, int pageSize)
    {
        IQueryable<Member> query = context.Members
            .Include(m => m.Department)
            .Include(m => m.MembershipFee)
            .AsNoTracking()
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(searchTerm))
        {
            var term = searchTerm.Trim().ToLower();
            query = query.Where(m =>
                m.FirstName.ToLower().Contains(term) ||
                m.LastName.ToLower().Contains(term) ||
                m.Email.ToLower().Contains(term));
        }

        if (status.HasValue)
        {
            query = query.Where(m => m.Status == status.Value);
        }

        if (departmentId.HasValue)
        {
            query = query.Where(m => m.DepartmentId == departmentId.Value);
        }

        if (feeCategory.HasValue)
        {
            query = query.Where(m => m.MembershipFee != null && m.MembershipFee.Category == feeCategory.Value);
        }

        var totalCount = await query.CountAsync();

        var items = await query
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        return (items, totalCount);
    }

    public async Task<Member> CreateAsync(Member member, string createdBy)
    {
        // Load fee to validate category constraints
        var fee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == member.MembershipFeeId)
            ?? throw new InvalidOperationException("Selected membership fee not found.");

        // Validate based on fee category
        switch (fee.Category)
        {
            case FeeCategory.Youth:
                if (member.GuardianMemberId == null)
                    throw new InvalidOperationException("Youth member must have a guardian.");
                var guardianFee = await context.MembershipFees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == context.Members
                        .Where(m => m.Id == member.GuardianMemberId)
                        .Select(m => m.MembershipFeeId)
                        .FirstOrDefault());
                if (guardianFee == null || guardianFee.Category != FeeCategory.Adult)
                    throw new InvalidOperationException("Guardian must be an Adult member.");
                break;

            case FeeCategory.Family:
                if (member.FamilyGroupId == null)
                    throw new InvalidOperationException("Family member must belong to a family group.");
                break;

            case FeeCategory.Adult:
                member.GuardianMemberId = null;
                member.FamilyGroupId = null;
                break;
        }

        member.CreatedAt = DateTime.UtcNow;
        member.UpdatedAt = DateTime.UtcNow;
        member.CreatedBy = createdBy;
        member.JoinedAt = DateTime.UtcNow;

        context.Members.Add(member);
        await context.SaveChangesAsync();

        await auditService.LogAsync(
            nameof(Member), member.Id, "Created",
            null, member.ToAuditJson(),
            nameof(Member), createdBy);

        return member;
    }

    public async Task<List<Member>> CreateFamilyAsync(Member mainMember, List<Member> familyMembers, string createdBy)
    {
        var fee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == mainMember.MembershipFeeId)
            ?? throw new InvalidOperationException("Selected membership fee not found.");

        if (fee.Category != FeeCategory.Family)
            throw new InvalidOperationException("CreateFamilyAsync requires FeeCategory.Family.");

        var allMembers = new List<Member> { mainMember };
        allMembers.AddRange(familyMembers);

        if (allMembers.Count < 2)
            throw new InvalidOperationException("A family group requires at least 2 members.");

        var now = DateTime.UtcNow;

        // Create family group
        var group = new FamilyGroup
        {
            Name = $"Familie {mainMember.LastName}",
            CreatedAt = now
        };
        context.FamilyGroups.Add(group);
        await context.SaveChangesAsync();

        // Create all members with the same FamilyGroupId
        foreach (var member in allMembers)
        {
            member.FamilyGroupId = group.Id;
            member.CreatedAt = now;
            member.UpdatedAt = now;
            member.CreatedBy = createdBy;
            member.JoinedAt = now;
            member.Status = MemberStatus.Active;
            member.GuardianMemberId = null; // Family category cannot be Youth

            context.Members.Add(member);
        }

        await context.SaveChangesAsync();

        // Audit each member creation
        foreach (var member in allMembers)
        {
            await auditService.LogAsync(
                nameof(Member), member.Id, "Created",
                null, member.ToAuditJson(),
                nameof(Member), createdBy);
        }

        return allMembers;
    }

    public async Task<Member> UpdateAsync(Member member, string performedBy = "system")
    {
        var existing = await context.Members
            .FirstOrDefaultAsync(m => m.Id == member.Id);

        if (existing == null)
        {
            throw new KeyNotFoundException($"Member with Id {member.Id} not found.");
        }

        // Load fee to validate category constraints
        var fee = await context.MembershipFees
            .AsNoTracking()
            .FirstOrDefaultAsync(f => f.Id == member.MembershipFeeId)
            ?? throw new InvalidOperationException("Selected membership fee not found.");

        var guardianMemberId = member.GuardianMemberId;
        var familyGroupId = member.FamilyGroupId;

        // Validate based on fee category
        switch (fee.Category)
        {
            case FeeCategory.Youth:
                if (guardianMemberId == null)
                    throw new InvalidOperationException("Youth member must have a guardian.");
                if (guardianMemberId == existing.Id)
                    throw new InvalidOperationException("A member cannot be their own guardian.");
                var guardianFee = await context.MembershipFees
                    .AsNoTracking()
                    .FirstOrDefaultAsync(f => f.Id == context.Members
                        .Where(m => m.Id == guardianMemberId)
                        .Select(m => m.MembershipFeeId)
                        .FirstOrDefault());
                if (guardianFee == null || guardianFee.Category != FeeCategory.Adult)
                    throw new InvalidOperationException("Guardian must be an Adult member.");
                break;

            case FeeCategory.Family:
                if (familyGroupId == null)
                    throw new InvalidOperationException("Family member must belong to a family group.");
                break;

            case FeeCategory.Adult:
                guardianMemberId = null;
                familyGroupId = null;
                break;
        }

        var oldValues = existing.ToAuditJson();

        // Apply optimistic concurrency token supplied by the caller
        if (member.ConcurrencyToken is { Length: > 0 })
        {
            context.Entry(existing).Property(m => m.ConcurrencyToken).OriginalValue = member.ConcurrencyToken;
        }

        var changedFields = new List<string>();
        void TrackChange<T>(string fieldName, T currentValue, T newValue, Action apply)
        {
            if (!EqualityComparer<T>.Default.Equals(currentValue, newValue))
                changedFields.Add(fieldName);
            apply();
        }

        TrackChange(nameof(Member.FirstName), existing.FirstName, member.FirstName, () => existing.FirstName = member.FirstName);
        TrackChange(nameof(Member.LastName), existing.LastName, member.LastName, () => existing.LastName = member.LastName);
        TrackChange(nameof(Member.Email), existing.Email, member.Email, () => existing.Email = member.Email);
        TrackChange(nameof(Member.Phone), existing.Phone, member.Phone, () => existing.Phone = member.Phone);
        TrackChange(nameof(Member.BirthDate), existing.BirthDate, member.BirthDate, () => existing.BirthDate = member.BirthDate);
        TrackChange(nameof(Member.Street), existing.Street, member.Street, () => existing.Street = member.Street);
        TrackChange(nameof(Member.City), existing.City, member.City, () => existing.City = member.City);
        TrackChange(nameof(Member.ZipCode), existing.ZipCode, member.ZipCode, () => existing.ZipCode = member.ZipCode);
        TrackChange(nameof(Member.Country), existing.Country, member.Country, () => existing.Country = member.Country);
        TrackChange(nameof(Member.BankIban), existing.BankIban, member.BankIban, () => existing.BankIban = member.BankIban);
        TrackChange(nameof(Member.BankBic), existing.BankBic, member.BankBic, () => existing.BankBic = member.BankBic);
        TrackChange(nameof(Member.DepartmentId), existing.DepartmentId, member.DepartmentId, () => existing.DepartmentId = member.DepartmentId);
        TrackChange(nameof(Member.MembershipFeeId), existing.MembershipFeeId, member.MembershipFeeId, () => existing.MembershipFeeId = member.MembershipFeeId);
        TrackChange(nameof(Member.GuardianMemberId), existing.GuardianMemberId, guardianMemberId, () => existing.GuardianMemberId = guardianMemberId);
        TrackChange(nameof(Member.FamilyGroupId), existing.FamilyGroupId, familyGroupId, () => existing.FamilyGroupId = familyGroupId);
        TrackChange(nameof(Member.Notes), existing.Notes, member.Notes, () => existing.Notes = member.Notes);

        existing.UpdatedAt = DateTime.UtcNow;

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("The record was modified by another user. Please reload and try again.");
        }

        await auditService.LogAsync(
            nameof(Member), existing.Id, "Updated",
            oldValues, existing.ToAuditJson(),
            string.Join(",", changedFields),
            string.IsNullOrWhiteSpace(performedBy) ? "system" : performedBy);

        return existing;
    }

    public async Task<Member> UpdateOwnProfileAsync(int id, Member updated, string performedBy)
    {
        var existing = await context.Members
            .AsNoTracking()
            .FirstOrDefaultAsync(m => m.Id == id)
            ?? throw new KeyNotFoundException($"Member with Id {id} not found.");

        var oldValues = existing.ToAuditJson();

        // Only allow editing these fields
        existing.FirstName = updated.FirstName;
        existing.LastName = updated.LastName;
        existing.Phone = updated.Phone;
        existing.Street = updated.Street;
        existing.City = updated.City;
        existing.ZipCode = updated.ZipCode;
        existing.Country = updated.Country;
        existing.Notes = updated.Notes;
        existing.UpdatedAt = DateTime.UtcNow;

        // Reject any attempts to change restricted fields
        if (existing.Email != updated.Email)
            throw new InvalidOperationException("Email cannot be changed through profile editing.");
        if (existing.BirthDate != updated.BirthDate)
            throw new InvalidOperationException("Birth date cannot be changed through profile editing.");
        if (existing.MembershipFeeId != updated.MembershipFeeId)
            throw new InvalidOperationException("Membership fee cannot be changed through profile editing.");
        if (existing.DepartmentId != updated.DepartmentId)
            throw new InvalidOperationException("Department cannot be changed through profile editing.");
        if (existing.GuardianMemberId != updated.GuardianMemberId)
            throw new InvalidOperationException("Guardian cannot be changed through profile editing.");
        if (existing.FamilyGroupId != updated.FamilyGroupId)
            throw new InvalidOperationException("Family group cannot be changed through profile editing.");
        if (existing.BankIban != updated.BankIban || existing.BankBic != updated.BankBic)
            throw new InvalidOperationException("Bank details cannot be changed through profile editing.");
        if (existing.Status != updated.Status)
            throw new InvalidOperationException("Status cannot be changed through profile editing.");

        if (updated.ConcurrencyToken is { Length: > 0 })
        {
            context.Entry(existing).Property(m => m.ConcurrencyToken).OriginalValue = updated.ConcurrencyToken;
        }

        context.Members.Update(existing);

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new InvalidOperationException("The record was modified by another user. Please reload and try again.");
        }

        await auditService.LogAsync(
            nameof(Member), existing.Id, "ProfileUpdated",
            oldValues, existing.ToAuditJson(),
            "FirstName,LastName,Phone,Street,City,ZipCode,Country,Notes",
            performedBy);

        return existing;
    }

    public async Task<bool> SoftDeleteAsync(int id, string performedBy)
    {
        // If member is in a family group, check group doesn't drop below 2
        var member = await context.Members.FindAsync(id);
        if (member == null) return false;

        if (member.FamilyGroupId.HasValue)
        {
            var familyCount = await context.Members
                .CountAsync(m => m.FamilyGroupId == member.FamilyGroupId
                                 && m.Status != MemberStatus.Former
                                 && m.Id != id);
            if (familyCount < 2)
                throw new InvalidOperationException("Cannot remove member: family group would have fewer than 2 active members.");
        }

        // Check for dependent Youth members
        var dependents = await context.Members
            .AnyAsync(m => m.GuardianMemberId == id && m.Status != MemberStatus.Former);
        if (dependents)
            throw new InvalidOperationException("Member cannot be deactivated because they are guardian for active Youth members. Re-assign first.");

        var oldValues = member.ToAuditJson();
        member.LeftAt = DateTime.UtcNow;
        member.Status = MemberStatus.Former;
        member.UpdatedAt = DateTime.UtcNow;

        // Revoke active sessions and block future logins for deactivated member
        if (!string.IsNullOrWhiteSpace(member.Email))
        {
            var identityUsers = await context.Users
                .Where(u => u.Email == member.Email)
                .ToListAsync();
            foreach (var user in identityUsers)
            {
                user.SecurityStamp = Guid.NewGuid().ToString();
                user.LockoutEnabled = true;
                user.LockoutEnd = DateTimeOffset.MaxValue;
            }
        }

        await context.SaveChangesAsync();

        await auditService.LogAsync(
            nameof(Member), member.Id, "SoftDeleted",
            oldValues, member.ToAuditJson(),
            nameof(Member.LeftAt) + "," + nameof(Member.Status),
            performedBy);

        return true;
    }

    public async Task<List<Member>> GetGuardianCandidatesAsync()
    {
        return await context.Members
            .Include(m => m.MembershipFee)
            .Where(m => m.MembershipFee != null
                        && m.MembershipFee.Category == FeeCategory.Adult
                        && m.Status == MemberStatus.Active)
            .AsNoTracking()
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .ToListAsync();
    }

    public async Task<List<Member>> GetDependentsAsync(int guardianId)
    {
        return await context.Members
            .Include(m => m.MembershipFee)
            .Where(m => m.GuardianMemberId == guardianId)
            .AsNoTracking()
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .ToListAsync();
    }

    public async Task ErasureAsync(int id, string performedBy)
    {
        // Check dependent Youth members
        var dependents = await context.Members
            .AnyAsync(m => m.GuardianMemberId == id && m.Status != MemberStatus.Former);
        if (dependents)
            throw new InvalidOperationException("Cannot erase: member is guardian of active Youth members.");

        var member = await context.Members.FindAsync(id)
            ?? throw new KeyNotFoundException($"Member {id} not found.");

        var originalEmail = member.Email;

        // Remove invitations and Identity account before overwriting email
        var invitations = await context.Invitations
            .Where(i => i.MemberId == member.Id)
            .ToListAsync();
        if (invitations.Count > 0)
            context.Invitations.RemoveRange(invitations);

        if (!string.IsNullOrWhiteSpace(originalEmail))
        {
            var identityUsers = await context.Users
                .Where(u => u.Email == originalEmail)
                .ToListAsync();
            if (identityUsers.Count > 0)
                context.Users.RemoveRange(identityUsers);
        }

        // Null out personal data (right to erasure keeps anonymous audit reference without PII)
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
        member.PrivacyConsentIp = null;
        member.Notes = null;
        member.GuardianMemberId = null;
        member.FamilyGroupId = null;
        member.DepartmentId = null;
        member.Status = MemberStatus.Former;
        member.LeftAt = DateTime.UtcNow;
        member.UpdatedAt = DateTime.UtcNow;

        await context.SaveChangesAsync();

        await auditService.LogAsync(
            nameof(Member), member.Id, "Erasure",
            null, null,
            nameof(Member), performedBy);
    }

    public async Task<List<FamilyGroup>> GetAllFamilyGroupsAsync()
    {
        return await context.FamilyGroups
            .Include(g => g.Members)
            .AsNoTracking()
            .OrderBy(g => g.Name)
            .ToListAsync();
    }

    public async Task<FamilyGroup?> GetFamilyGroupByIdAsync(int id)
    {
        return await context.FamilyGroups
            .Include(g => g.Members)
            .AsNoTracking()
            .FirstOrDefaultAsync(g => g.Id == id);
    }

    public async Task<FamilyGroup> CreateFamilyGroupAsync(string? name, string createdBy)
    {
        var group = new FamilyGroup
        {
            Name = name,
            CreatedAt = DateTime.UtcNow
        };

        context.FamilyGroups.Add(group);
        await context.SaveChangesAsync();

        return group;
    }

    public async Task<FamilyGroup> AddMemberToFamilyGroupAsync(int memberId, int familyGroupId)
    {
        var member = await context.Members
            .Include(m => m.MembershipFee)
            .FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new KeyNotFoundException($"Member with Id {memberId} not found.");

        var group = await context.FamilyGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == familyGroupId)
            ?? throw new KeyNotFoundException($"Family group with Id {familyGroupId} not found.");

        if (member.MembershipFee?.Category != FeeCategory.Family)
            throw new InvalidOperationException("Only Family-category members can be added to a family group.");

        member.FamilyGroupId = familyGroupId;
        await context.SaveChangesAsync();

        return group;
    }

    public async Task<FamilyGroup> RemoveMemberFromFamilyGroupAsync(int memberId)
    {
        var member = await context.Members
            .Include(m => m.FamilyGroup)
            .FirstOrDefaultAsync(m => m.Id == memberId)
            ?? throw new KeyNotFoundException($"Member with Id {memberId} not found.");

        var group = member.FamilyGroup
            ?? throw new InvalidOperationException("Member is not assigned to any family group.");

        // Check min 2 members
        var activeCount = await context.Members
            .CountAsync(m => m.FamilyGroupId == group.Id && m.Id != memberId);
        if (activeCount < 2)
            throw new InvalidOperationException("Family group must have at least 2 members. Remove the entire group instead.");

        member.FamilyGroupId = null;
        await context.SaveChangesAsync();

        return group;
    }

    public async Task<bool> DeleteFamilyGroupAsync(int id)
    {
        var group = await context.FamilyGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return false;

        if (group.Members.Any(m => m.Status == MemberStatus.Active))
            throw new InvalidOperationException("Cannot delete a family group that still has active members.");

        context.FamilyGroups.Remove(group);
        await context.SaveChangesAsync();
        return true;
    }
}

internal static class MemberExtensions
{
    public static string ToAuditJson(this Member member)
    {
        var snapshot = new
        {
            member.Id,
            member.FirstName,
            member.LastName,
            member.Email,
            member.Phone,
            member.BirthDate,
            member.Street,
            member.City,
            member.ZipCode,
            member.Country,
            HasBankIban = !string.IsNullOrEmpty(member.BankIban),
            HasBankBic = !string.IsNullOrEmpty(member.BankBic),
            member.PrivacyConsentedAt,
            member.JoinedAt,
            member.LeftAt,
            member.Status,
            member.DepartmentId,
            member.MembershipFeeId,
            member.Notes,
            member.CreatedAt,
            member.UpdatedAt,
            member.CreatedBy,
            member.GuardianMemberId,
            member.FamilyGroupId
        };

        return System.Text.Json.JsonSerializer.Serialize(snapshot, new System.Text.Json.JsonSerializerOptions
        {
            WriteIndented = false
        });
    }
}
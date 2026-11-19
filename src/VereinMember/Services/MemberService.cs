using VereinMember.Data;
using VereinMember.Models;
using Microsoft.EntityFrameworkCore;

namespace VereinMember.Services;

public class MemberService(MembershipDbContext context, IAuditService auditService) : IMemberService
{
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
        await MemberCategoryRules.ValidateAndNormalizeAsync(context, member);

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

        // Validate birth dates and email uniqueness across the batch and existing database
        var normalizedEmails = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var member in allMembers)
        {
            if (member.BirthDate == DateTime.MinValue || member.BirthDate > DateTime.UtcNow)
                throw new InvalidOperationException($"Invalid birth date for {member.FirstName} {member.LastName}.");

            var trimmedEmail = member.Email?.Trim() ?? string.Empty;
            if (string.IsNullOrWhiteSpace(trimmedEmail))
                throw new InvalidOperationException("Every family member must have an email address.");

            if (!normalizedEmails.Add(trimmedEmail))
                throw new InvalidOperationException($"Duplicate email address in family registration: {trimmedEmail}");
        }

        var existingEmail = await context.Members
            .AsNoTracking()
            .Where(m => normalizedEmails.Contains(m.Email))
            .Select(m => m.Email)
            .FirstOrDefaultAsync();
        if (existingEmail != null)
            throw new InvalidOperationException($"Email address is already registered: {existingEmail}");

        var now = DateTime.UtcNow;

        await using var transaction = context.Database.IsRelational()
            ? await context.Database.BeginTransactionAsync()
            : null;

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
            member.Email = member.Email.Trim();
            member.FamilyGroupId = group.Id;
            member.CreatedAt = now;
            member.UpdatedAt = now;
            member.CreatedBy = createdBy;
            if (member.JoinedAt == default)
                member.JoinedAt = now;
            member.Status = MemberStatus.Active;
            member.GuardianMemberId = null; // Family category cannot be Youth
            MemberCategoryRules.ClearNonMemberGuardian(member);

            context.Members.Add(member);
        }

        await context.SaveChangesAsync();

        if (transaction != null)
            await transaction.CommitAsync();

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

        await MemberCategoryRules.ValidateAndNormalizeAsync(context, member, existing.Id);

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
        TrackChange(nameof(Member.GuardianMemberId), existing.GuardianMemberId, member.GuardianMemberId, () => existing.GuardianMemberId = member.GuardianMemberId);
        TrackChange(nameof(Member.GuardianFirstName), existing.GuardianFirstName, member.GuardianFirstName, () => existing.GuardianFirstName = member.GuardianFirstName);
        TrackChange(nameof(Member.GuardianLastName), existing.GuardianLastName, member.GuardianLastName, () => existing.GuardianLastName = member.GuardianLastName);
        TrackChange(nameof(Member.GuardianEmail), existing.GuardianEmail, member.GuardianEmail, () => existing.GuardianEmail = member.GuardianEmail);
        TrackChange(nameof(Member.GuardianPhone), existing.GuardianPhone, member.GuardianPhone, () => existing.GuardianPhone = member.GuardianPhone);
        TrackChange(nameof(Member.FamilyGroupId), existing.FamilyGroupId, member.FamilyGroupId, () => existing.FamilyGroupId = member.FamilyGroupId);
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

        await MemberErasureHelper.EraseMemberPersonalDataAsync(
            context,
            auditService,
            member,
            auditAction: "Erasure",
            performedBy: performedBy,
            preserveLeftAt: false);
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
            member.PrivacyConsentTextVersion,
            member.SepaMandateConsentedAt,
            member.SepaMandateTextVersion,
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
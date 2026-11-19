using Microsoft.EntityFrameworkCore;
using VereinMember.Data;
using VereinMember.Models;

namespace VereinMember.Services;

public class FamilyGroupService(MembershipDbContext context, IAuditService auditService) : IFamilyGroupService
{
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

    public async Task<List<FamilyMemberSummary>> GetFamilyMembersAsync(int familyGroupId)
    {
        return await context.Members
            .AsNoTracking()
            .Where(m => m.FamilyGroupId == familyGroupId && m.Status != MemberStatus.Former)
            .OrderBy(m => m.LastName)
            .ThenBy(m => m.FirstName)
            .Select(m => new FamilyMemberSummary(m.Id, m.FirstName, m.LastName))
            .ToListAsync();
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

    public async Task<bool> DissolveFamilyGroupAsync(int id, string performedBy)
    {
        var group = await context.FamilyGroups
            .Include(g => g.Members)
            .FirstOrDefaultAsync(g => g.Id == id);

        if (group == null) return false;

        var memberIds = group.Members.Select(m => m.Id).ToList();
        foreach (var member in group.Members)
        {
            member.FamilyGroupId = null;
            member.UpdatedAt = DateTime.UtcNow;
        }

        context.FamilyGroups.Remove(group);
        await context.SaveChangesAsync();

        await auditService.LogAsync(
            nameof(FamilyGroup),
            id,
            "FamilyGroupDissolved",
            $"Members:{string.Join(",", memberIds)}",
            null,
            nameof(Member.FamilyGroupId),
            string.IsNullOrWhiteSpace(performedBy) ? "system" : performedBy);

        return true;
    }
}

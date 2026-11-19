using VereinMember.Models;

namespace VereinMember.Services;

public interface IFamilyGroupService
{
    Task<List<FamilyGroup>> GetAllFamilyGroupsAsync();
    Task<FamilyGroup?> GetFamilyGroupByIdAsync(int id);

    /// <summary>
    /// Returns the members of a family group for display on a member's own profile.
    /// Excludes former members and orders by name.
    /// </summary>
    Task<List<FamilyMemberSummary>> GetFamilyMembersAsync(int familyGroupId);

    Task<FamilyGroup> CreateFamilyGroupAsync(string? name, string createdBy);
    Task<FamilyGroup> AddMemberToFamilyGroupAsync(int memberId, int familyGroupId);
    Task<FamilyGroup> RemoveMemberFromFamilyGroupAsync(int memberId);
    Task<bool> DeleteFamilyGroupAsync(int id);
    Task<bool> DissolveFamilyGroupAsync(int id, string performedBy);
}

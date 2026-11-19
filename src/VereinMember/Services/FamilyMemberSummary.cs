namespace VereinMember.Services;

/// <summary>Only the information needed to display a family member on a profile.</summary>
public sealed record FamilyMemberSummary(int Id, string FirstName, string LastName);

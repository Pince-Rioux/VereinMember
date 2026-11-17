using System.ComponentModel.DataAnnotations;
using VereinMember.Models;

namespace VereinMember.Areas.Membership.Dtos;

public sealed record CreateMemberRequestDto
{
    [Required, MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; init; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; init; }

    [Required]
    public DateTime BirthDate { get; init; }

    [MaxLength(200)]
    public string? Street { get; init; }

    [MaxLength(100)]
    public string? City { get; init; }

    [MaxLength(20)]
    public string? ZipCode { get; init; }

    [MaxLength(100)]
    public string? Country { get; init; }

    [MaxLength(34)]
    public string? BankIban { get; init; }

    [MaxLength(11)]
    public string? BankBic { get; init; }

    public int? DepartmentId { get; init; }

    [Range(1, int.MaxValue)]
    public int MembershipFeeId { get; init; }

    public int? GuardianMemberId { get; init; }
    public int? FamilyGroupId { get; init; }
    public string? Notes { get; init; }
}

public sealed record UpdateMemberRequestDto
{
    [Required, MaxLength(100)]
    public string FirstName { get; init; } = string.Empty;

    [Required, MaxLength(100)]
    public string LastName { get; init; } = string.Empty;

    [Required, EmailAddress, MaxLength(200)]
    public string Email { get; init; } = string.Empty;

    [MaxLength(50)]
    public string? Phone { get; init; }

    [Required]
    public DateTime BirthDate { get; init; }

    [MaxLength(200)]
    public string? Street { get; init; }

    [MaxLength(100)]
    public string? City { get; init; }

    [MaxLength(20)]
    public string? ZipCode { get; init; }

    [MaxLength(100)]
    public string? Country { get; init; }

    [MaxLength(34)]
    public string? BankIban { get; init; }

    [MaxLength(11)]
    public string? BankBic { get; init; }

    public int? DepartmentId { get; init; }

    [Range(1, int.MaxValue)]
    public int MembershipFeeId { get; init; }

    public int? GuardianMemberId { get; init; }
    public int? FamilyGroupId { get; init; }
    public string? Notes { get; init; }
    public byte[] ConcurrencyToken { get; init; } = [];
}

public sealed record CreateFamilyGroupRequestDto
{
    [MaxLength(200)]
    public string? Name { get; init; }
}

public sealed record MemberResponseDto
{
    public int Id { get; init; }
    public string FirstName { get; init; } = string.Empty;
    public string LastName { get; init; } = string.Empty;
    public string Email { get; init; } = string.Empty;
    public string? Phone { get; init; }
    public DateTime BirthDate { get; init; }
    public string? Street { get; init; }
    public string? City { get; init; }
    public string? ZipCode { get; init; }
    public string? Country { get; init; }
    public bool HasBankDetails { get; init; }
    public string? BankIbanMasked { get; init; }
    public DateTime JoinedAt { get; init; }
    public DateTime? LeftAt { get; init; }
    public string Status { get; init; } = string.Empty;
    public int? DepartmentId { get; init; }
    public string? DepartmentName { get; init; }
    public int MembershipFeeId { get; init; }
    public string? MembershipFeeName { get; init; }
    public string? FeeCategory { get; init; }
    public int? GuardianMemberId { get; init; }
    public string? GuardianName { get; init; }
    public int? FamilyGroupId { get; init; }
    public string? FamilyGroupName { get; init; }
    public string? Notes { get; init; }
    public byte[] ConcurrencyToken { get; init; } = [];

    public static MemberResponseDto FromMember(Member member, bool includeInternalNotes)
    {
        return new MemberResponseDto
        {
            Id = member.Id,
            FirstName = member.FirstName,
            LastName = member.LastName,
            Email = member.Email,
            Phone = member.Phone,
            BirthDate = member.BirthDate,
            Street = member.Street,
            City = member.City,
            ZipCode = member.ZipCode,
            Country = member.Country,
            HasBankDetails = !string.IsNullOrEmpty(member.BankIban) || !string.IsNullOrEmpty(member.BankBic),
            BankIbanMasked = MaskIban(member.BankIban),
            JoinedAt = member.JoinedAt,
            LeftAt = member.LeftAt,
            Status = member.Status.ToString(),
            DepartmentId = member.DepartmentId,
            DepartmentName = member.Department?.Name,
            MembershipFeeId = member.MembershipFeeId,
            MembershipFeeName = member.MembershipFee?.Name,
            FeeCategory = member.MembershipFee?.Category.ToString(),
            GuardianMemberId = member.GuardianMemberId,
            GuardianName = member.GuardianMember != null
                ? $"{member.GuardianMember.FirstName} {member.GuardianMember.LastName}"
                : null,
            FamilyGroupId = member.FamilyGroupId,
            FamilyGroupName = member.FamilyGroup?.Name,
            Notes = includeInternalNotes ? member.Notes : null,
            ConcurrencyToken = member.ConcurrencyToken
        };
    }

    public static string? MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
            return null;

        var trimmed = iban.Replace(" ", "").Trim();
        if (trimmed.Length <= 4)
            return "••••";

        var prefix = trimmed.Length >= 2 ? trimmed[..2] : string.Empty;
        return $"{prefix}••••••••••••{trimmed[^4..]}";
    }
}

public sealed record GuardianCandidateDto(int Id, string FullName);

public sealed record DependentDto(int Id, string FirstName, string LastName, DateTime BirthDate, string Status);

public sealed record FamilyGroupResponseDto(
    int Id,
    string? Name,
    DateTime CreatedAt,
    IReadOnlyList<MemberResponseDto> Members)
{
    public static FamilyGroupResponseDto FromFamilyGroup(FamilyGroup group, bool includeInternalNotes) =>
        new(
            group.Id,
            group.Name,
            group.CreatedAt,
            group.Members.Select(m => MemberResponseDto.FromMember(m, includeInternalNotes)).ToList());
}

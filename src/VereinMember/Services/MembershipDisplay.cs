using VereinMember.Models;

namespace VereinMember.Services;

/// <summary>
/// Shared German display formatting helpers for Membership UI views and dropdowns.
/// </summary>
public static class MembershipDisplay
{
    public static string GetGermanStatus(MemberStatus status) => status switch
    {
        MemberStatus.Active => "Aktiv",
        MemberStatus.Passive => "Passiv",
        MemberStatus.Honorary => "Ehrenmitglied",
        MemberStatus.Former => "Ausgetreten",
        _ => status.ToString()
    };

    public static string GetGermanFeeCategory(FeeCategory category) => category switch
    {
        FeeCategory.Adult => "Erwachsene",
        FeeCategory.Youth => "Jugend",
        FeeCategory.Family => "Familie",
        _ => category.ToString()
    };

    public static string FormatFamilyGroupLabel(FamilyGroup group, string? memberHint = null)
    {
        var baseName = string.IsNullOrWhiteSpace(group.Name)
            ? $"Familie #{group.Id}"
            : $"{group.Name} (#{group.Id})";

        return string.IsNullOrWhiteSpace(memberHint)
            ? baseName
            : $"{baseName} – {memberHint}";
    }

    public static string? MaskIban(string? iban)
    {
        if (string.IsNullOrWhiteSpace(iban))
            return null;

        var compact = new string(iban.Where(c => !char.IsWhiteSpace(c)).ToArray());
        if (compact.Length <= 6)
            return new string('•', compact.Length);

        return $"{compact[..2]}{new string('•', compact.Length - 6)}{compact[^4..]}";
    }
}

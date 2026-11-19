using System.Text.Json.Serialization;

namespace VereinMember.Areas.Membership.ViewModels;

/// <summary>
/// Shared input model for inline family-member rows on public registration
/// and admin member creation. All fields are nullable so empty/template rows
/// never trigger implicit non-nullable required model-binding errors.
/// </summary>
public sealed class FamilyMemberInput
{
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string? Email { get; set; }
    public int? BirthDay { get; set; }
    public int? BirthMonth { get; set; }
    public int? BirthYear { get; set; }
    public DateTime? JoinedAt { get; set; } = DateTime.Today;

    [JsonIgnore]
    public bool IsEmpty =>
        string.IsNullOrWhiteSpace(FirstName)
        && string.IsNullOrWhiteSpace(LastName)
        && string.IsNullOrWhiteSpace(Email);

    [JsonIgnore]
    public DateTime BirthDate
    {
        get
        {
            if (BirthDay is >= 1 and <= 31 && BirthMonth is >= 1 and <= 12 && BirthYear is >= 1900 and <= 2100)
            {
                if (BirthDay.Value <= DateTime.DaysInMonth(BirthYear.Value, BirthMonth.Value))
                {
                    return new DateTime(BirthYear.Value, BirthMonth.Value, BirthDay.Value);
                }
            }

            return DateTime.MinValue;
        }
    }
}

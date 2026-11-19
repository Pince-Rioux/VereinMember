using Microsoft.AspNetCore.Mvc.Rendering;

namespace VereinMember.Areas.Membership.ViewModels;

public sealed record DateSelectsViewModel(
    string Prefix,
    int? Day,
    int? Month,
    int? Year,
    string SelectCssClass = "form-select",
    string? IdPrefix = null);

public sealed record AddressFieldsViewModel(
    string? Street,
    string? ZipCode,
    string? City,
    string? Country,
    string FieldPrefix = "Member.",
    bool ShowHeading = false);

public sealed record GuardianFieldsViewModel(
    bool ShowMemberSelector,
    int? GuardianMemberId,
    IEnumerable<SelectListItem>? GuardianOptions,
    string? GuardianFirstName,
    string? GuardianLastName,
    string? GuardianEmail,
    string? GuardianPhone,
    string FieldPrefix = "Member.",
    bool InitiallyHidden = true,
    bool KeepVisibleIfPopulated = false,
    string HeadingText = "Erziehungsberechtigter",
    string HeadingClass = "border-bottom pb-2 mt-2",
    string? IntroText = null);

public sealed record FamilyMembersEditorViewModel(
    bool ShowJoinedAt,
    string IntroText,
    string InitialRowsJson,
    bool ShowHrBefore = false);

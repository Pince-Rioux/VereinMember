namespace VereinMember.Models;

using System;

/// <summary>
/// Shared helper for composing a <see cref="Member.BirthDate"/> from three separate
/// day/month/year select fields. Called identically by Register and Create models.
/// Accepts an error-reporting callback instead of a concrete ModelState type
/// so the helper has no dependency on Razor Pages infrastructure.
/// </summary>
public static class BirthDateHelper
{
    /// <summary>
    /// Tries to compose <c>member.BirthDate</c> from the three select values.
    /// If any value is missing, or if the date is invalid (e.g. 31. February),
    /// calls <paramref name="addModelError"/> and returns <c>false</c>.
    /// </summary>
    public static bool TryComposeBirthDate(
        Member member,
        int? birthDay,
        int? birthMonth,
        int? birthYear,
        Action<string, string>? addModelError)
    {
        if (birthYear.HasValue && birthMonth.HasValue && birthDay.HasValue)
        {
            try
            {
                member.BirthDate = new DateTime(birthYear.Value, birthMonth.Value, birthDay.Value);
                return true;
            }
            catch (ArgumentOutOfRangeException)
            {
                if (addModelError != null)
                    addModelError("Member.BirthDate", "Bitte geben Sie ein gültiges Datum ein.");
                return false;
            }
        }

        if (addModelError != null)
            addModelError("Member.BirthDate", "Bitte wählen Sie Ihr Geburtsdatum aus (Tag, Monat, Jahr).");
        return false;
    }
}
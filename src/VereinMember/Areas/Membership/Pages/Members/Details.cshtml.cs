using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Pages.Members;

[Authorize(Policy = AuthorizationPolicies.MemberView)]
[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class DetailsModel(IMemberService memberService) : PageModel
{
    private static readonly JsonSerializerOptions ExportJsonOptions = new()
    {
        WriteIndented = true
    };

    public Member? Member { get; set; }

    public async Task<IActionResult> OnGetAsync(int id)
    {
        Member = await memberService.GetByIdAsync(id);
        if (Member == null)
        {
            return NotFound();
        }

        return Page();
    }

    public async Task<IActionResult> OnPostExportAsync(int id)
    {
        if (!User.IsInRole("Admin") && !User.IsInRole("Editor"))
        {
            return Forbid();
        }

        var member = await memberService.GetByIdAsync(id);
        if (member == null)
        {
            return NotFound();
        }

        var export = new
        {
            exportDate = DateTime.UtcNow,
            requestedById = id,
            personalData = new
            {
                name = $"{member.FirstName} {member.LastName}",
                email = member.Email,
                phone = member.Phone,
                birthDate = member.BirthDate,
                address = new
                {
                    street = member.Street,
                    city = member.City,
                    zipCode = member.ZipCode,
                    country = member.Country
                },
                bankInfo = MembershipDisplay.MaskIban(member.BankIban),
                membership = new
                {
                    status = member.Status.ToString(),
                    joinedAt = member.JoinedAt,
                    department = member.Department?.Name,
                    fee = member.MembershipFee != null
                        ? new
                        {
                            name = member.MembershipFee.Name,
                            amount = member.MembershipFee.Amount,
                            interval = member.MembershipFee.Interval.ToString()
                        }
                        : null,
                    guardian = member.GuardianMember != null
                        ? $"{member.GuardianMember.FirstName} {member.GuardianMember.LastName}"
                        : (!string.IsNullOrWhiteSpace(member.GuardianFirstName) || !string.IsNullOrWhiteSpace(member.GuardianLastName)
                            ? $"{member.GuardianFirstName} {member.GuardianLastName}".Trim()
                            : null),
                    familyGroup = member.FamilyGroup?.Name
                },
                notes = member.Notes,
                createdAt = member.CreatedAt,
                updatedAt = member.UpdatedAt
            },
            dataRetentionPolicy = "Member data retained per club policy. See DSGVO_SECURITY.md"
        };

        var json = JsonSerializer.Serialize(export, ExportJsonOptions);
        return File(Encoding.UTF8.GetBytes(json), "application/json", $"mitglied-{id}-export.json");
    }

    public async Task<IActionResult> OnPostErasureAsync(int id)
    {
        if (!User.IsInRole("Admin"))
        {
            return Forbid();
        }

        try
        {
            await memberService.ErasureAsync(id, User.Identity?.Name ?? "system");
            TempData["Success"] = $"Personenbezogene Daten für Mitglied #{id} wurden gemäß DSGVO gelöscht.";
            return RedirectToPage("Index");
        }
        catch (KeyNotFoundException)
        {
            return NotFound();
        }
        catch (InvalidOperationException ex)
        {
            TempData["Error"] = ex.Message;
            return RedirectToPage(new { id });
        }
    }
}

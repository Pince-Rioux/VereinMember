using VereinMember.Extensions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;

namespace VereinMember.Areas.Membership.Pages;

[RequireMembershipEnabled]
public class RegisterConfirmationModel : PageModel
{
    public string Email { get; set; } = string.Empty;

    public void OnGet(string email)
    {
        Email = email;
    }
}
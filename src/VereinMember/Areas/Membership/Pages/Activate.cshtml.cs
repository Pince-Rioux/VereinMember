using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Pages;

[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class ActivateModel(
    IInvitationService invitationService,
    IMemberAccountService accountService,
    UserManager<MembershipUser> userManager) : PageModel
{
    [BindProperty]
    public ActivateInputModel Input { get; set; } = new();

    public string Token => Input.Token ?? string.Empty;
    public string MemberFirstName { get; private set; } = string.Empty;
    public string MemberLastName { get; private set; } = string.Empty;
    public string MemberEmail { get; private set; } = string.Empty;
    public bool IsInvalid { get; private set; }
    public bool IsCompleted { get; private set; }

    public async Task<IActionResult> OnGetAsync(string token)
    {
        Input.Token = token;

        var invitation = await invitationService.ValidateTokenAsync(token);
        if (invitation?.Member == null)
        {
            IsInvalid = true;
            return Page();
        }

        MemberFirstName = invitation.Member.FirstName;
        MemberLastName = invitation.Member.LastName;
        MemberEmail = invitation.Member.Email;
        return Page();
    }

    public async Task<IActionResult> OnPostAsync(string? token = null)
    {
        var effectiveToken = !string.IsNullOrWhiteSpace(Input.Token)
            ? Input.Token
            : (token ?? string.Empty);
        Input.Token = effectiveToken;

        var invitation = await invitationService.ValidateTokenAsync(effectiveToken);
        if (invitation?.Member == null)
        {
            IsInvalid = true;
            return Page();
        }

        MemberFirstName = invitation.Member.FirstName;
        MemberLastName = invitation.Member.LastName;
        MemberEmail = invitation.Member.Email;

        var password = Input.Password ?? string.Empty;
        var confirmPassword = Input.ConfirmPassword ?? string.Empty;

        if (string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Bitte geben Sie ein Passwort ein.");
            return Page();
        }

        if (password.Length < 8)
        {
            ModelState.AddModelError(string.Empty, "Das Passwort muss mindestens 8 Zeichen lang sein.");
            await invitationService.RecordFailedAttemptAsync(invitation.Id);
            return Page();
        }

        if (password != confirmPassword)
        {
            ModelState.AddModelError(string.Empty, "Die Passwörter stimmen nicht überein.");
            await invitationService.RecordFailedAttemptAsync(invitation.Id);
            return Page();
        }

        // Pre-validate against Identity's password policy before consuming the invitation
        var existingUser = await userManager.FindByEmailAsync(invitation.Member.Email);
        var candidateUser = existingUser ?? new MembershipUser
        {
            Email = invitation.Member.Email,
            UserName = invitation.Member.Email
        };

        foreach (var validator in userManager.PasswordValidators)
        {
            var validation = await validator.ValidateAsync(userManager, candidateUser, password);
            if (!validation.Succeeded)
            {
                foreach (var err in validation.Errors)
                {
                    ModelState.AddModelError(string.Empty, err.Description);
                }

                await invitationService.RecordFailedAttemptAsync(invitation.Id);
                return Page();
            }
        }

        // Atomically consume the invitation before changing credentials so concurrent replays cannot race
        var consumed = await invitationService.TryCompleteInvitationAsync(invitation.Id);
        if (!consumed)
        {
            IsInvalid = true;
            return Page();
        }

        var success = await accountService.SetPasswordAsync(invitation.Member.Email, password);
        if (!success)
        {
            ModelState.AddModelError(string.Empty, "Passwort konnte nicht gesetzt werden. Bitte versuchen Sie es später erneut.");
            return Page();
        }

        IsCompleted = true;
        return Page();
    }

    public sealed class ActivateInputModel
    {
        public string? Token { get; set; }

        [Display(Name = "Passwort")]
        public string? Password { get; set; }

        [Display(Name = "Passwort wiederholen")]
        public string? ConfirmPassword { get; set; }
    }
}

using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;

namespace VereinMember.Areas.Membership.Pages;

[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class LoginModel(
    SignInManager<MembershipUser> signInManager,
    UserManager<MembershipUser> userManager,
    IMemberService memberService,
    ILogger<LoginModel> logger) : PageModel
{
    [BindProperty]
    public LoginInputModel Input { get; set; } = new();

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var effectiveReturnUrl = !string.IsNullOrWhiteSpace(returnUrl) ? returnUrl : ReturnUrl;
        var email = Input.Email?.Trim() ?? string.Empty;
        var password = Input.Password ?? string.Empty;

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Bitte geben Sie E-Mail und Passwort ein.");
            return Page();
        }

        // Sign out any existing session first
        await signInManager.SignOutAsync();

        var user = await userManager.FindByEmailAsync(email);
        if (user == null)
        {
            logger.LogWarning("Member login failed for {Email}: user not found", email);
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "E-Mail und/oder Passwort sind falsch.");
            return Page();
        }

        // Block deactivated or erased members even if an Identity user record exists
        var linkedMember = await memberService.GetByEmailAsync(email);
        if (linkedMember != null && (linkedMember.Status == MemberStatus.Former || linkedMember.FirstName == "[erased]"))
        {
            logger.LogWarning("Member login blocked for deactivated/erased member {Email}", email);
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "E-Mail und/oder Passwort sind falsch.");
            return Page();
        }

        var result = await signInManager.PasswordSignInAsync(user, password, true, lockoutOnFailure: true);
        if (!result.Succeeded)
        {
            logger.LogWarning("Member login failed for {Email}: {Reason}",
                email, result.IsLockedOut ? "locked out" : "wrong password");
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "E-Mail und/oder Passwort sind falsch.");
            return Page();
        }

        logger.LogInformation("Member login successful for {Email}", email);

        if (!string.IsNullOrEmpty(effectiveReturnUrl) && Url.IsLocalUrl(effectiveReturnUrl))
        {
            return LocalRedirect(effectiveReturnUrl);
        }

        // Role-aware default landing page:
        // Admin -> administration, everyone else -> their own profile.
        if (await userManager.IsInRoleAsync(user, "Admin"))
        {
            return RedirectToPage("/Admin/Index", new { area = "Membership" });
        }

        return Redirect("~/Membership/Profile");
    }

    public sealed class LoginInputModel
    {
        [Display(Name = "E-Mail")]
        public string? Email { get; set; }

        [Display(Name = "Passwort")]
        public string? Password { get; set; }
    }
}

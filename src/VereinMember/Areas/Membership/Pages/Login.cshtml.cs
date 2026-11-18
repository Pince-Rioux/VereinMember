using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Microsoft.Extensions.Logging;

namespace VereinMember.Areas.Membership.Pages;

[RequireMembershipEnabled]
[ValidateAntiForgeryToken]
public class LoginModel : PageModel
{
    private readonly SignInManager<MembershipUser> _signInManager;
    private readonly UserManager<MembershipUser> _userManager;
    private readonly IMemberService _memberService;
    private readonly ILogger<LoginModel> _logger;

    public LoginModel(
        SignInManager<MembershipUser> signInManager,
        UserManager<MembershipUser> userManager,
        IMemberService memberService,
        ILogger<LoginModel> logger)
    {
        _signInManager = signInManager;
        _userManager = userManager;
        _memberService = memberService;
        _logger = logger;
    }

    public void OnGet(string? returnUrl = null)
    {
        ReturnUrl = returnUrl;
    }

    [BindProperty]
    public string? ReturnUrl { get; set; }

    public async Task<IActionResult> OnPostAsync(string? returnUrl = null)
    {
        var email = Request.Form["email"].ToString();
        var password = Request.Form["password"].ToString();

        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            ModelState.AddModelError(string.Empty, "Bitte geben Sie E-Mail und Passwort ein.");
            return Page();
        }

        // Sign out any existing session first
        await _signInManager.SignOutAsync();

        // Find the user by email
        var user = await _userManager.FindByEmailAsync(email);
        if (user == null)
        {
            _logger.LogWarning("Member login failed for {Email}: user not found", email);
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "E-Mail und/oder Passwort sind falsch.");
            return Page();
        }

        // Block deactivated or erased members even if an Identity user record exists
        var linkedMember = await _memberService.GetByEmailAsync(email);
        if (linkedMember != null && (linkedMember.Status == MemberStatus.Former || linkedMember.FirstName == "[erased]"))
        {
            _logger.LogWarning("Member login blocked for deactivated/erased member {Email}", email);
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "E-Mail und/oder Passwort sind falsch.");
            return Page();
        }

        var result = await _signInManager.PasswordSignInAsync(user, password, true, lockoutOnFailure: true);

        if (!result.Succeeded)
        {
            _logger.LogWarning("Member login failed for {Email}: {Reason}",
                email, result.IsLockedOut ? "locked out" : "wrong password");
            ModelState.Clear();
            ModelState.AddModelError(string.Empty, "E-Mail und/oder Passwort sind falsch.");
            return Page();
        }

        _logger.LogInformation("Member login successful for {Email}", email);

        if (!string.IsNullOrEmpty(returnUrl) && Url.IsLocalUrl(returnUrl))
        {
            return LocalRedirect(returnUrl);
        }

        // Role-aware default landing page:
        // Admin → administration, everyone else → their own profile.
        if (await _userManager.IsInRoleAsync(user, "Admin"))
        {
            return RedirectToPage("/Admin/Index", new { area = "Membership" });
        }

        return Redirect("~/Membership/Profile");
    }
}

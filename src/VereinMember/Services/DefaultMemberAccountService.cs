using VereinMember.Models;
using VereinMail;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging;

namespace VereinMember.Services;

/// <summary>
/// Default implementation of IMemberAccountService that uses the RCL's own Identity infrastructure.
/// No dependency on the host application's Identity or Piranha.
/// </summary>
public class DefaultMemberAccountService : IMemberAccountService
{
    private readonly UserManager<MembershipUser> _userManager;
    private readonly IEmailSender _emailSender;
    private readonly ILogger<DefaultMemberAccountService> _logger;

    public DefaultMemberAccountService(
        UserManager<MembershipUser> userManager,
        IEmailSender emailSender,
        ILogger<DefaultMemberAccountService> logger)
    {
        _userManager = userManager;
        _emailSender = emailSender;
        _logger = logger;
    }

    public async Task<bool> SetPasswordAsync(string email, string password)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return false;

        var existingUser = await _userManager.FindByEmailAsync(email);
        var candidateUser = existingUser ?? new MembershipUser
        {
            Email = email,
            UserName = email
        };

        // Validate password against all configured Identity password validators
        // BEFORE creating a user or mutating an existing password.
        foreach (var validator in _userManager.PasswordValidators)
        {
            var validationResult = await validator.ValidateAsync(_userManager, candidateUser, password);
            if (!validationResult.Succeeded)
            {
                _logger.LogWarning(
                    "SetPasswordAsync: password rejected for {Email}: {Errors}",
                    email, string.Join(", ", validationResult.Errors.Select(e => e.Description)));
                return false;
            }
        }

        if (existingUser == null)
        {
            var createResult = await _userManager.CreateAsync(candidateUser);
            if (!createResult.Succeeded)
            {
                _logger.LogError("SetPasswordAsync: failed to create Identity user for {Email}: {Errors}",
                    email, string.Join(", ", createResult.Errors.Select(e => e.Description)));
                return false;
            }
            _logger.LogInformation("SetPasswordAsync: created Identity user for {Email}", email);
        }

        IdentityResult passwordResult;
        if (await _userManager.HasPasswordAsync(candidateUser))
        {
            var resetToken = await _userManager.GeneratePasswordResetTokenAsync(candidateUser);
            passwordResult = await _userManager.ResetPasswordAsync(candidateUser, resetToken, password);
        }
        else
        {
            passwordResult = await _userManager.AddPasswordAsync(candidateUser, password);
        }

        if (!passwordResult.Succeeded)
        {
            _logger.LogError("SetPasswordAsync: failed to set password for {Email}: {Errors}",
                email, string.Join(", ", passwordResult.Errors.Select(e => e.Description)));
            return false;
        }

        _logger.LogInformation("SetPasswordAsync: password set for {Email}", email);
        return true;
    }

    public async Task SendInvitationEmailAsync(string email, string memberName, string inviteUrl)
    {
        var subject = $"Willkommen beim VC Neusäß – Registrierung abschließen";
        var body = $"Hallo {memberName},\n\n" +
                   $"Sie wurden beim Volleyballclub Neusäß e. V. registriert.\n" +
                   $"Bitte klicken Sie auf den folgenden Link, um Ihr Passwort zu setzen und Ihren Account zu aktivieren:\n\n" +
                   $"{inviteUrl}\n\n" +
                   $"Der Link ist 7 Tage gültig.\n\n" +
                   $"Sportliche Grüße,\nIhr VC Neusäß";

        await _emailSender.SendAsync(email, subject, body);

        _logger.LogInformation(
            "Invitation email sent to {Email} (activation link omitted from logs)",
            email);
    }
}

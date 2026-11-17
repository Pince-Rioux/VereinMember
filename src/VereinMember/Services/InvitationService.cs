using System.Security.Cryptography;
using System.Text;
using VereinMember.Data;
using VereinMember.Models;
using Microsoft.EntityFrameworkCore;

namespace VereinMember.Services;

public class InvitationService(MembershipDbContext context) : IInvitationService
{
    private const int TokenByteLength = 32; // 256 bits
    private const int InvitationExpiryDays = 7;
    private const int MaxAttempts = 5;

    public async Task<Invitation> CreateInvitationAsync(int memberId, string email)
    {
        // Cancel any pending invitations for this member
        await CancelPendingInvitationsAsync(memberId);

        var rawToken = GenerateToken();
        var tokenHash = HashToken(rawToken);

        var invitation = new Invitation
        {
            MemberId = memberId,
            Token = tokenHash,
            Email = email,
            CreatedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(InvitationExpiryDays),
            CompletedAt = null,
            Attempts = 0
        };

        context.Invitations.Add(invitation);
        await context.SaveChangesAsync();

        // Detach the tracked entity so replacing Token with the one-time raw token
        // for the caller does not overwrite the stored hash in the DbContext.
        context.Entry(invitation).State = EntityState.Detached;

        return new Invitation
        {
            Id = invitation.Id,
            MemberId = invitation.MemberId,
            Token = rawToken,
            Email = invitation.Email,
            CreatedAt = invitation.CreatedAt,
            ExpiresAt = invitation.ExpiresAt,
            CompletedAt = invitation.CompletedAt,
            Attempts = invitation.Attempts
        };
    }

    public async Task<Invitation?> ValidateTokenAsync(string token)
    {
        if (string.IsNullOrWhiteSpace(token))
            return null;

        var tokenHash = HashToken(token);

        var invitation = await context.Invitations
            .Include(i => i.Member)
            .FirstOrDefaultAsync(i => i.Token == tokenHash);

        if (invitation == null)
            return null;

        if (invitation.CompletedAt.HasValue)
            return null;

        if (invitation.ExpiresAt < DateTime.UtcNow)
            return null;

        if (invitation.Attempts >= MaxAttempts)
            return null;

        return invitation;
    }

    public async Task CompleteInvitationAsync(Guid invitationId)
    {
        await TryCompleteInvitationAsync(invitationId);
    }

    public async Task<bool> TryCompleteInvitationAsync(Guid invitationId)
    {
        var now = DateTime.UtcNow;

        if (context.Database.IsRelational())
        {
            var affected = await context.Invitations
                .Where(i => i.Id == invitationId
                            && i.CompletedAt == null
                            && i.ExpiresAt >= now
                            && i.Attempts < MaxAttempts)
                .ExecuteUpdateAsync(setters => setters.SetProperty(i => i.CompletedAt, now));

            return affected == 1;
        }

        var invitation = await context.Invitations.FindAsync(invitationId);
        if (invitation == null
            || invitation.CompletedAt.HasValue
            || invitation.ExpiresAt < now
            || invitation.Attempts >= MaxAttempts)
        {
            return false;
        }

        invitation.CompletedAt = now;
        await context.SaveChangesAsync();
        return true;
    }

    public async Task<List<Invitation>> GetPendingInvitationsAsync(string email)
    {
        return await context.Invitations
            .Where(i => i.Email == email
                        && i.CompletedAt == null
                        && i.ExpiresAt >= DateTime.UtcNow)
            .ToListAsync();
    }

    public async Task CancelPendingInvitationsAsync(int memberId)
    {
        var pending = await context.Invitations
            .Where(i => i.MemberId == memberId && i.CompletedAt == null)
            .ToListAsync();

        foreach (var inv in pending)
        {
            inv.ExpiresAt = DateTime.UtcNow.AddTicks(-1); // expire immediately
        }

        if (pending.Count > 0)
            await context.SaveChangesAsync();
    }

    /// <summary>
    /// Increments the attempt counter for an invitation token.
    /// Called when the user submits an invalid password or the token validation fails.
    /// </summary>
    public async Task RecordFailedAttemptAsync(Guid invitationId)
    {
        var invitation = await context.Invitations.FindAsync(invitationId);
        if (invitation != null)
        {
            invitation.Attempts++;
            await context.SaveChangesAsync();
        }
    }

    internal static string HashToken(string rawToken)
    {
        var bytes = Encoding.UTF8.GetBytes(rawToken);
        var hash = SHA256.HashData(bytes);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    private static string GenerateToken()
    {
        var bytes = new byte[TokenByteLength];
        RandomNumberGenerator.Fill(bytes);
        // URL-safe base64 for .NET 8 compatibility
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }
}

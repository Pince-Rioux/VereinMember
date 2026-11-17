using VereinMember.Data;
using VereinMember.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace VereinMember.Services;

/// <summary>
/// Background service that periodically purges personal data of former members
/// whose retention period has expired. Opt-in via MembershipPurge:Enabled=true
/// and non-destructive by default (MembershipPurge:DryRun=true).
/// </summary>
public class PurgeService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PurgeService> _logger;
    private readonly PurgeOptions _options;

    public PurgeService(
        IServiceScopeFactory scopeFactory,
        ILogger<PurgeService> logger,
        IOptions<PurgeOptions>? options = null)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _options = options?.Value ?? new PurgeOptions();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!_options.Enabled)
        {
            _logger.LogInformation("PurgeService is disabled (MembershipPurge:Enabled=false).");
            return;
        }

        _logger.LogInformation(
            "PurgeService started. Retention period: {RetentionYears} years. DryRun: {DryRun}.",
            _options.RetentionYears, _options.DryRun);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PurgeExpiredRecordsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during purge cycle");
            }

            // Run every 24 hours
            await Task.Delay(TimeSpan.FromHours(24), stoppingToken);
        }
    }

    public async Task<int> PurgeExpiredRecordsAsync(CancellationToken ct)
    {
        if (!_options.Enabled)
            return 0;

        if (_options.RetentionYears < 1)
            throw new InvalidOperationException("MembershipPurge:RetentionYears must be at least 1.");

        using var scope = _scopeFactory.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
        var auditService = scope.ServiceProvider.GetRequiredService<IAuditService>();

        var cutoff = DateTime.UtcNow.AddYears(-_options.RetentionYears);

        var expiredMembers = await context.Members
            .Where(m => m.Status == MemberStatus.Former
                        && m.LeftAt != null
                        && m.LeftAt < cutoff
                        && m.FirstName != "[erased]")  // skip already erased
            .ToListAsync(ct);

        if (expiredMembers.Count == 0)
        {
            _logger.LogInformation("Purge: no expired records found.");
            return 0;
        }

        if (_options.DryRun)
        {
            _logger.LogInformation(
                "Purge dry-run: {Count} expired member records eligible for erasure (no changes written).",
                expiredMembers.Count);
            return expiredMembers.Count;
        }

        foreach (var member in expiredMembers)
        {
            var originalEmail = member.Email;

            // Remove invitations and Identity account before overwriting email
            var invitations = await context.Invitations
                .Where(i => i.MemberId == member.Id)
                .ToListAsync(ct);
            if (invitations.Count > 0)
                context.Invitations.RemoveRange(invitations);

            if (!string.IsNullOrWhiteSpace(originalEmail))
            {
                var identityUsers = await context.Users
                    .Where(u => u.Email == originalEmail)
                    .ToListAsync(ct);
                if (identityUsers.Count > 0)
                    context.Users.RemoveRange(identityUsers);
            }

            // Erase personal data without copying PII into AuditLog
            member.FirstName = "[erased]";
            member.LastName = "[erased]";
            member.Email = $"[erased-{member.Id}]@verein.de";
            member.Phone = null;
            member.BirthDate = DateTime.MinValue;
            member.Street = null;
            member.City = null;
            member.ZipCode = null;
            member.Country = null;
            member.BankIban = null;
            member.BankBic = null;
            member.PrivacyConsentedAt = null;
            member.PrivacyConsentIp = null;
            member.Notes = null;
            member.GuardianMemberId = null;
            member.FamilyGroupId = null;
            member.DepartmentId = null;
            member.UpdatedAt = DateTime.UtcNow;

            await auditService.LogAsync(
                nameof(Member), member.Id, "Purge",
                null, null,
                nameof(Member), "system");
        }

        await context.SaveChangesAsync(ct);

        _logger.LogInformation("Purge: erased {Count} expired member records.", expiredMembers.Count);
        return expiredMembers.Count;
    }
}

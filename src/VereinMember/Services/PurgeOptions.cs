namespace VereinMember.Services;

/// <summary>
/// Configuration options for <see cref="PurgeService"/> (bound to "MembershipPurge").
/// Disabled and dry-run by default so destructive retention purges are strictly opt-in.
/// </summary>
public sealed class PurgeOptions
{
    public bool Enabled { get; set; } = false;
    public bool DryRun { get; set; } = true;
    public int RetentionYears { get; set; } = 3;
}

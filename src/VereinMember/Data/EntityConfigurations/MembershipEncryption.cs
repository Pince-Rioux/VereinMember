namespace VereinMember.Data.EntityConfigurations;

/// <summary>
/// Static accessor for the encryption service, needed by EF Core value converters.
/// Must be initialized at app startup (before any DbContext instance is created).
/// A persistent 256-bit key must be configured via MembershipEncryption:Key or MEMBERSHIP_ENCRYPTION_KEY.
/// </summary>
public static class MembershipEncryption
{
    private static Services.Encryption.IEncryptionService? _current;

    public static Services.Encryption.IEncryptionService Current =>
        _current ?? throw new InvalidOperationException(
            "Encryption service not initialized. Call MembershipEncryption.Initialize at app startup.");

    public static void Initialize(Services.Encryption.IEncryptionService service)
    {
        _current = service;
    }
}
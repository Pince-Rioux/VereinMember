namespace VereinMember.Tests.Fixtures;

/// <summary>
/// MembershipEncryption is process-wide static state, and xUnit runs test classes in parallel.
/// All tests that initialize it must use this one fixed key so they cannot invalidate each other's data.
/// </summary>
public static class TestEncryptionKey
{
    public const string Value = "dmVyZWlubWVtYmVyLXRlc3Qta2V5LTAxMjM0NTY3ODk=";
}

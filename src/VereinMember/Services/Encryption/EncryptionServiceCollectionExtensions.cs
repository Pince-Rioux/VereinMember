using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace VereinMember.Services.Encryption;

public static class EncryptionServiceCollectionExtensions
{
    public static IServiceCollection AddMembershipEncryption(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Key from config (appsettings) or environment variable — fail closed if missing
        var key = configuration["MembershipEncryption:Key"];
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "MembershipEncryption:Key configuration or MEMBERSHIP_ENCRYPTION_KEY environment variable must be set to a valid base64-encoded 256-bit (32-byte) key.");
        }

        var encryptionService = new AesEncryptionService(key);
        services.AddSingleton<IEncryptionService>(encryptionService);
        Data.EntityConfigurations.MembershipEncryption.Initialize(encryptionService);
        return services;
    }
}
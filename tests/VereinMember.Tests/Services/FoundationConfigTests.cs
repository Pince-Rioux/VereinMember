using VereinMember.Tests.Fixtures;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;
using VereinMember.Services.Encryption;

namespace VereinMember.Tests.Services;

[Collection("StaticMembershipState")]
public class FoundationConfigTests
{
    private static string CreateValidKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    [Fact]
    public void DesignTimeDbContextFactory_WithoutConnectionString_ThrowsInvalidOperationException()
    {
        var previous = Environment.GetEnvironmentVariable("MEMBERSHIP_CONNECTION_STRING");
        try
        {
            Environment.SetEnvironmentVariable("MEMBERSHIP_CONNECTION_STRING", null);
            var factory = new DesignTimeDbContextFactory();

            var act = () => factory.CreateDbContext([]);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*MEMBERSHIP_CONNECTION_STRING*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEMBERSHIP_CONNECTION_STRING", previous);
        }
    }

    [Fact]
    public void AesEncryptionService_ValidKey_RoundTripsAcrossInstances()
    {
        var key = CreateValidKey();
        var writer = new AesEncryptionService(key);
        var reader = new AesEncryptionService(key);

        var cipher = writer.Encrypt("DE89370400440532013000");
        cipher.Should().NotBe("DE89370400440532013000");
        reader.Decrypt(cipher).Should().Be("DE89370400440532013000");
    }

    [Fact]
    public void AesEncryptionService_WrongKeyOrTamperedCiphertext_ThrowsCryptographicException()
    {
        var writer = new AesEncryptionService(CreateValidKey());
        var other = new AesEncryptionService(CreateValidKey());

        var cipher = writer.Encrypt("DE89370400440532013000");

        var wrongKeyAct = () => other.Decrypt(cipher);
        wrongKeyAct.Should().Throw<CryptographicException>();

        var bytes = Convert.FromBase64String(cipher);
        bytes[^1] ^= 0xFF;
        var tampered = Convert.ToBase64String(bytes);

        var tamperedAct = () => writer.Decrypt(tampered);
        tamperedAct.Should().Throw<CryptographicException>();

        var shortAct = () => writer.Decrypt(Convert.ToBase64String(new byte[10]));
        shortAct.Should().Throw<CryptographicException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-valid-base64!!!")]
    public void AesEncryptionService_InvalidBase64_ThrowsArgumentException(string badKey)
    {
        var act = () => new AesEncryptionService(badKey);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void AesEncryptionService_WrongKeyLength_ThrowsArgumentException()
    {
        var shortKey = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16));
        var act = () => new AesEncryptionService(shortKey);
        act.Should().Throw<ArgumentException>()
            .WithMessage("*32 bytes*");
    }

    [Fact]
    public void AddMembershipEncryption_WithoutKey_ThrowsInvalidOperationException()
    {
        var previous = Environment.GetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY", null);
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder().Build();

            var act = () => services.AddMembershipEncryption(config);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*MembershipEncryption:Key*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY", previous);
        }
    }

    [Fact]
    public void AddVereinMember_WithoutEncryptionKey_ThrowsInvalidOperationException()
    {
        var previous = Environment.GetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY");
        try
        {
            Environment.SetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY", null);
            var services = new ServiceCollection();
            var config = new ConfigurationBuilder()
                .AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:ClubMembershipDb"] = "Server=localhost;Database=Dummy;"
                })
                .Build();

            var act = () => services.AddVereinMember(config);
            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*MembershipEncryption:Key*");
        }
        finally
        {
            Environment.SetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY", previous);
        }
    }

    [Fact]
    public void AddMembershipEncryption_WithValidKey_RegistersAndInitializesEncryption()
    {
        var key = TestEncryptionKey.Value;
        var services = new ServiceCollection();
        var config = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["MembershipEncryption:Key"] = key
            })
            .Build();

        services.AddMembershipEncryption(config);
        using var provider = services.BuildServiceProvider();
        var resolved = provider.GetRequiredService<IEncryptionService>();

        var encrypted = resolved.Encrypt("DE1234");
        MembershipEncryption.Current.Decrypt(encrypted).Should().Be("DE1234");
    }
}

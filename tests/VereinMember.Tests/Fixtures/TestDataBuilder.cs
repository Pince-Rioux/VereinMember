using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VereinMember.Data.EntityConfigurations;
using VereinMember.Extensions;
using VereinMember.Models;
using VereinMember.Services;
using VereinMember.Services.Encryption;
using VereinMember.Services.Validators;

namespace VereinMember.Tests.Fixtures;

public static class TestDataBuilder
{
    public static ServiceProvider CreateServiceProvider(
        string dbPrefix = "TestDb",
        Action<IServiceCollection>? configure = null)
    {
        ServiceCollectionExtensions.Enabled = true;
        MembershipEncryption.Initialize(new AesEncryptionService(TestEncryptionKey.Value));

        var services = new ServiceCollection();
        services.AddDbContext<MembershipDbContext>(o =>
            o.UseInMemoryDatabase($"{dbPrefix}_{Guid.NewGuid()}"));
        services.AddLogging();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IFamilyGroupService, FamilyGroupService>();
        services.AddScoped<IValidator<Member>, MemberValidator>();
        services.AddScoped<IInvitationService, InvitationService>();

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }

    public static MembershipFee AdultFee(
        int id = 0,
        string name = "Adult Fee",
        decimal amount = 50m,
        FeeInterval interval = FeeInterval.Yearly,
        bool isDefault = true) => new()
    {
        Id = id,
        Name = name,
        Amount = amount,
        Interval = interval,
        Category = FeeCategory.Adult,
        IsDefault = isDefault
    };

    public static MembershipFee YouthFee(
        int id = 0,
        string name = "Youth Fee",
        decimal amount = 25m,
        FeeInterval interval = FeeInterval.Yearly) => new()
    {
        Id = id,
        Name = name,
        Amount = amount,
        Interval = interval,
        Category = FeeCategory.Youth,
        IsDefault = false
    };

    public static MembershipFee FamilyFee(
        int id = 0,
        string name = "Family Fee",
        decimal amount = 80m,
        FeeInterval interval = FeeInterval.Yearly) => new()
    {
        Id = id,
        Name = name,
        Amount = amount,
        Interval = interval,
        Category = FeeCategory.Family,
        IsDefault = false
    };

    public static Department Department(
        int id = 0,
        string name = "Soccer",
        string? description = "Soccer Department") => new()
    {
        Id = id,
        Name = name,
        Description = description
    };

    public static Member Member(
        int id = 0,
        string email = "john.doe@example.com",
        MembershipFee? fee = null,
        Department? department = null,
        MemberStatus status = MemberStatus.Active,
        string firstName = "John",
        string lastName = "Doe",
        DateTime? birthDate = null) => new()
    {
        Id = id,
        FirstName = firstName,
        LastName = lastName,
        Email = email,
        BirthDate = birthDate ?? new DateTime(1990, 1, 1),
        Status = status,
        MembershipFeeId = fee?.Id ?? 0,
        MembershipFee = fee,
        DepartmentId = department?.Id,
        Department = department,
        JoinedAt = DateTime.UtcNow.AddDays(-30),
        CreatedAt = DateTime.UtcNow.AddDays(-30),
        UpdatedAt = DateTime.UtcNow.AddDays(-30),
        CreatedBy = "test-user"
    };
}

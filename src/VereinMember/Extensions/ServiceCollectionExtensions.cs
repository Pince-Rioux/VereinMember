using VereinMember.Data;
using VereinMember.Models;
using VereinMember.Services;
using VereinMail;
using Microsoft.AspNetCore.Identity;
using VereinMember.Services.Validators;
using VereinMember.Services.Encryption;
using VereinMember.Data.EntityConfigurations;
using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;

namespace VereinMember.Extensions;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Set by ClubSite based on ClubMembership.Enabled in AppSettings.
    /// Checked by Membership _ViewStart to block pages when disabled.
    /// </summary>
    public static bool Enabled = true;
    public static IServiceCollection AddVereinMember(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        // Validate required configuration first before mutating static module state
        var enabled = configuration.GetValue<bool>("ClubMembership:Enabled");
        var connStr = configuration.GetConnectionString("ClubMembershipDb")
                      ?? configuration.GetConnectionString("VolleyballClub")
                      ?? throw new InvalidOperationException("No valid connection string found. Configure 'ClubMembershipDb' or 'VolleyballClub'.");

        // Add AES-256-GCM encryption for sensitive personal data (fail closed if key is missing)
        var key = configuration["MembershipEncryption:Key"];
        if (string.IsNullOrWhiteSpace(key))
            key = Environment.GetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY");

        if (string.IsNullOrWhiteSpace(key))
        {
            throw new InvalidOperationException(
                "MembershipEncryption:Key configuration or MEMBERSHIP_ENCRYPTION_KEY environment variable must be set to a valid base64-encoded 256-bit (32-byte) key.");
        }

        var encryptionService = new AesEncryptionService(key);

        var mailMode = configuration.GetValue<string>("MembershipMail:Mode")?.Trim().ToLowerInvariant();
        if (mailMode == "smtp" && string.IsNullOrWhiteSpace(configuration["MembershipMail:Smtp:Server"]))
        {
            throw new InvalidOperationException(
                "MembershipMail:Smtp:Server must be configured when MembershipMail:Mode is 'Smtp'.");
        }

        Enabled = enabled;

        // Register the Membership DbContext (now includes Identity tables in Membership schema)
        services.AddDbContext<MembershipDbContext>(options =>
            options.UseSqlServer(connStr));

        // Register Identity for the Membership module (independent from Piranha Identity)
        services.AddIdentityCore<MembershipUser>(options =>
            {
                // Password requirements for member accounts
                options.Password.RequireDigit = true;
                options.Password.RequiredLength = 8;
                options.Password.RequireNonAlphanumeric = false;
                options.Password.RequireUppercase = true;
                options.Password.RequireLowercase = true;

                // Lockout settings
                options.Lockout.AllowedForNewUsers = true;
                options.Lockout.MaxFailedAccessAttempts = 5;
                options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
            })
            .AddRoles<IdentityRole>()
            .AddEntityFrameworkStores<MembershipDbContext>()
            .AddSignInManager()
            .AddDefaultTokenProviders();

        // Register Identity services
        services.AddScoped<IMemberService, MemberService>();
        services.AddScoped<IAuditService, AuditService>();
        services.AddScoped<IInvitationService, InvitationService>();
        services.AddScoped<IMemberAccountService, DefaultMemberAccountService>();
        // Mail transport for the module:
        //   File -> writes .eml files (development)
        //   Smtp -> sends through a mail server (production)
        //   anything else -> logs only (safe default when unconfigured)
        services.Configure<MailOptions>(configuration.GetSection("MembershipMail"));
        switch (mailMode)
        {
            case "file":
                services.TryAddScoped<IEmailSender, FileEmailSender>();
                break;
            case "smtp":
                services.TryAddScoped<IEmailSender, SmtpEmailSender>();
                break;
            default:
                services.TryAddScoped<IEmailSender, LogOnlyEmailSender>();
                break;
        }
        services.AddScoped<IValidator<Member>, MemberValidator>();

        services.AddSingleton<IEncryptionService>(encryptionService);

        // Initialize static encryption reference for EF Core value converters
        MembershipEncryption.Initialize(encryptionService);

        // Background purge service is strictly opt-in (disabled unless both module and MembershipPurge:Enabled are true)
        services.Configure<PurgeOptions>(configuration.GetSection("MembershipPurge"));
        if (Enabled && configuration.GetValue<bool>("MembershipPurge:Enabled"))
        {
            services.AddHostedService<PurgeService>();
        }

        services.AddMembershipAuthorization();

        return services;
    }
}
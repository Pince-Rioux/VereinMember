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
        // Use ClubMembershipDb connection string, fall back to shared VolleyballClub
        Enabled = configuration.GetValue<bool>("ClubMembership:Enabled");
        var connStr = configuration.GetConnectionString("ClubMembershipDb")
                      ?? configuration.GetConnectionString("VolleyballClub")
                      ?? throw new InvalidOperationException("No valid connection string found. Configure 'ClubMembershipDb' or 'VolleyballClub'.");

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
        switch (configuration.GetValue<string>("MembershipMail:Mode")?.Trim().ToLowerInvariant())
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

        // Add AES-256-GCM encryption for sensitive personal data
        var key = configuration["MembershipEncryption:Key"]
                  ?? Environment.GetEnvironmentVariable("MEMBERSHIP_ENCRYPTION_KEY")
                  ?? Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

        var encryptionService = new AesEncryptionService(key);
        services.AddSingleton<IEncryptionService>(encryptionService);

        // Initialize static encryption reference for EF Core value converters
        MembershipEncryption.Initialize(encryptionService);

        // Background service that purges expired personal data every 24 hours
        services.AddHostedService<PurgeService>();

        services.AddMembershipAuthorization();

        return services;
    }
}
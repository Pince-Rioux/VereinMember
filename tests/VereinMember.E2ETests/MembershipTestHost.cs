using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Playwright;
using VereinMember.Data;
using VereinMember.Extensions;
using VereinMember.Models;

namespace VereinMember.E2ETests;

/// <summary>
/// Minimal host for the real membership Razor Pages. No ClubSite or Piranha dependency.
/// One open SQLite connection keeps its private in-memory database alive for this fixture.
/// </summary>
public sealed class MembershipTestHost : IAsyncLifetime
{
    private readonly string _tempDirectory = Path.Combine(
        Path.GetTempPath(), "VereinMember.E2ETests", Guid.NewGuid().ToString("N"));
    private readonly SqliteConnection _connection = new("Data Source=:memory:");
    private WebApplication? _app;
    private IPlaywright? _playwright;
    private IBrowser? _browser;

    public string BaseUrl { get; private set; } = string.Empty;
    public string MailDirectory => Path.Combine(_tempDirectory, "mail");
    public int AdultFeeId { get; private set; }

    public async Task InitializeAsync()
    {
        Directory.CreateDirectory(Path.Combine(_tempDirectory, "wwwroot"));
        await _connection.OpenAsync();

        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(MembershipTestHost).Assembly.GetName().Name,
            EnvironmentName = Environments.Development,
            ContentRootPath = _tempDirectory
        });
        builder.Logging.SetMinimumLevel(LogLevel.Warning);
        builder.WebHost.UseUrls("http://127.0.0.1:0").UseStaticWebAssets();
        builder.Configuration.Sources.Clear();
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ClubMembership:Enabled"] = "true",
            ["ConnectionStrings:ClubMembershipDb"] = "Not used: the test host overrides the provider",
            ["MembershipEncryption:Key"] = Convert.ToBase64String(Enumerable.Range(0, 32).Select(i => (byte)i).ToArray()),
            ["MembershipMail:Mode"] = "File",
            ["MembershipMail:File:Path"] = MailDirectory,
            ["MembershipMail:DefaultFrom:Email"] = "tests@example.test",
            ["MembershipMail:DefaultFrom:Name"] = "VereinMember-Tests"
        });

        builder.Services.AddRazorPages().AddApplicationPart(typeof(MembershipDbContext).Assembly);
        builder.Services.AddAuthentication(IdentityConstants.ApplicationScheme).AddIdentityCookies();
        builder.Services.ConfigureApplicationCookie(options =>
        {
            options.LoginPath = "/Membership/Login";
            options.AccessDeniedPath = "/Membership/AccessDenied";
        });
        builder.Services.AddDataProtection().UseEphemeralDataProtectionProvider();
        builder.Services.AddVereinMember(builder.Configuration);
        // AddVereinMember registers the production SQL Server provider. Replace its
        // options rather than registering a second EF provider in the same host.
        builder.Services.RemoveAll<DbContextOptions<MembershipDbContext>>();
        builder.Services.RemoveAll<DbContextOptions>();
        builder.Services.RemoveAll<MembershipDbContext>();
        builder.Services.RemoveAll<IDbContextOptionsConfiguration<MembershipDbContext>>();
        builder.Services.AddDbContext<MembershipDbContext>(options => options.UseSqlite(_connection));
        builder.Services.AddScoped<MembershipDbContext>(services =>
            new SqliteMembershipDbContext(services.GetRequiredService<DbContextOptions<MembershipDbContext>>()));

        _app = builder.Build();
        _app.UseStaticFiles();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapRazorPages();
        await _app.StartAsync();

        var addresses = _app.Services.GetRequiredService<IServer>().Features.Get<IServerAddressesFeature>()
            ?? throw new InvalidOperationException("The test host has no server addresses.");
        BaseUrl = addresses.Addresses.Single();

        await using (var scope = _app.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<MembershipDbContext>();
            await db.Database.EnsureCreatedAsync();
            var fee = new MembershipFee
            {
                Name = "Erwachsene",
                Amount = 48,
                Category = FeeCategory.Adult,
                IsDefault = true
            };
            db.MembershipFees.Add(fee);
            await db.SaveChangesAsync();
            AdultFeeId = fee.Id;
        }

        _playwright = await Playwright.CreateAsync();
        _browser = await _playwright.Chromium.LaunchAsync(new BrowserTypeLaunchOptions { Headless = true });
    }

    public async Task<IPage> NewPageAsync()
    {
        if (_browser is null)
            throw new InvalidOperationException("The test host has not been initialized.");

        var page = await _browser.NewPageAsync(new BrowserNewPageOptions { BaseURL = BaseUrl });
        page.SetDefaultTimeout(5_000);
        return page;
    }

    public async Task<string> WaitForInvitationLinkAsync(string recipient)
    {
        var timeout = DateTime.UtcNow.AddSeconds(10);
        while (DateTime.UtcNow < timeout)
        {
            if (Directory.Exists(MailDirectory))
            {
                foreach (var file in Directory.EnumerateFiles(MailDirectory, "*.eml"))
                {
                    var message = await MimeKit.MimeMessage.LoadAsync(file);
                    if (!message.To.Mailboxes.Any(mailbox =>
                            mailbox.Address.Equals(recipient, StringComparison.OrdinalIgnoreCase)))
                        continue;

                    var body = message.TextBody ?? string.Empty;
                    var start = body.IndexOf(BaseUrl + "/Membership/Activate?token=", StringComparison.Ordinal);
                    if (start < 0)
                        continue;

                    var end = body.IndexOfAny(['\r', '\n', ' ', '\t'], start);
                    return body[start..(end < 0 ? body.Length : end)];
                }
            }

            await Task.Delay(100);
        }

        throw new TimeoutException($"No invitation email for {recipient} arrived in the isolated mail directory.");
    }

    public async Task DisposeAsync()
    {
        if (_browser is not null)
            await _browser.DisposeAsync();
        _playwright?.Dispose();
        if (_app is not null)
            await _app.DisposeAsync();
        await _connection.DisposeAsync();

        try
        {
            Directory.Delete(_tempDirectory, recursive: true);
        }
        catch (IOException)
        {
            // A temporary test directory must never hide the test result.
        }
    }
}

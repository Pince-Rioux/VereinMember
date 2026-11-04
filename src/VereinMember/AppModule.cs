namespace VereinMember;

using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VereinMember.Extensions;

/// <summary>
/// Entry point for the VereinMember plugin/module.
/// Called by ClubSite's <c>AppModuleLoader</c> via reflection at startup.
/// </summary>
public static class AppModule
{
    /// <summary>
    /// Registers VereinMember services, databases, and dependencies
    /// with the host application's DI container.
    /// </summary>
    /// <param name="services">The host application's service collection.</param>
    /// <param name="configuration">The host application's configuration.</param>
    public static void ConfigureServices(IServiceCollection services, IConfiguration configuration)
    {
        ServiceCollectionExtensions.AddVereinMember(services, configuration);
    }
}
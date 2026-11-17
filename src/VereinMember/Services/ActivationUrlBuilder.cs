using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;

namespace VereinMember.Services;

/// <summary>
/// Builds activation links bound to a configured trusted origin (<c>ClubMembership:PublicBaseUrl</c>)
/// when available, preventing Host-header spoofing in invitation emails.
/// </summary>
public static class ActivationUrlBuilder
{
    public static string Build(IUrlHelper urlHelper, string token, IConfiguration? configuration)
    {
        var configuredBaseUrl = configuration?["ClubMembership:PublicBaseUrl"];
        if (!string.IsNullOrWhiteSpace(configuredBaseUrl))
        {
            if (!Uri.TryCreate(configuredBaseUrl, UriKind.Absolute, out var uri) ||
                (uri.Scheme != Uri.UriSchemeHttps &&
                 !(uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback)))
            {
                throw new InvalidOperationException(
                    "ClubMembership:PublicBaseUrl must be an absolute https:// URL (or http://localhost for local development).");
            }

            var baseUri = configuredBaseUrl.TrimEnd('/');
            return $"{baseUri}/Membership/Activate?token={Uri.EscapeDataString(token)}";
        }

        return urlHelper.PageLink("Activate", null, new { token })
               ?? $"/Membership/Activate?token={Uri.EscapeDataString(token)}";
    }
}

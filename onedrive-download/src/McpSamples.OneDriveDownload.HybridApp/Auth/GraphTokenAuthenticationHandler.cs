using System.Security.Claims;
using System.Text.Encodings.Web;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace McpSamples.OneDriveDownload.HybridApp.Auth;

/// <summary>
/// Authentication handler that extracts the bearer token forwarded by Azure API
/// Management (APIM). APIM has already validated the JWT against Entra ID, so this
/// handler simply extracts the token and creates an authenticated principal.
/// The token is then used downstream for the On-Behalf-Of (OBO) flow to obtain
/// a Microsoft Graph access token.
/// </summary>
public class GraphTokenAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    /// <summary>
    /// The key used to store the access token in <see cref="AuthenticationProperties"/>.
    /// </summary>
    public const string AccessTokenKey = "access_token";

    /// <inheritdoc />
    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        var authorization = Request.Headers.Authorization.ToString();
        if (string.IsNullOrEmpty(authorization) ||
            !authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var token = authorization["Bearer ".Length..].Trim();
        if (string.IsNullOrEmpty(token))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "mcp-user") };
        var identity = new ClaimsIdentity(claims, Scheme.Name);
        var principal = new ClaimsPrincipal(identity);

        var properties = new AuthenticationProperties();
        properties.StoreTokens(
        [
            new AuthenticationToken { Name = AccessTokenKey, Value = token }
        ]);

        var ticket = new AuthenticationTicket(principal, properties, Scheme.Name);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }
}

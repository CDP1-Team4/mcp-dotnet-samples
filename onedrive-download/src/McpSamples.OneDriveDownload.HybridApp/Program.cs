using System.Reflection;

using Azure.Core;
using Azure.Identity;
using Azure.Storage.Files.Shares;

using McpSamples.OneDriveDownload.HybridApp.Auth;
using McpSamples.OneDriveDownload.HybridApp.Configurations;
using McpSamples.OneDriveDownload.HybridApp.Services;
using McpSamples.Shared.Configurations;
using McpSamples.Shared.Extensions;

using Microsoft.AspNetCore.Authentication;
using Microsoft.Graph;

using Constants = McpSamples.OneDriveDownload.HybridApp.Constants;
using WebApplication = Microsoft.AspNetCore.Builder.WebApplication;

var envs = Environment.GetEnvironmentVariables();
var useStreamableHttp = AppSettings.UseStreamableHttp(envs, args);

IHostApplicationBuilder builder = useStreamableHttp
                                ? WebApplication.CreateBuilder(args)
                                : Host.CreateApplicationBuilder(args);

if (useStreamableHttp == true)
{
    var port = Environment.GetEnvironmentVariable(Constants.AzureFunctionsCustomHandlerPortEnvironmentKey) ?? $"{Constants.DefaultAppPort}";
    (builder as WebApplicationBuilder)!.WebHost.UseUrls(string.Format(Constants.DefaultAppUrl, port));

    Console.WriteLine($"Listening on port {port}");
}

// ---------------------------------------------------------------
// Common service registrations
// ---------------------------------------------------------------
builder.Services.AddHttpContextAccessor();
builder.Services.AddAppSettings<OneDriveDownloadAppSettings>(builder.Configuration, args);
builder.Services.AddScoped<IOneDriveDownloadService, OneDriveDownloadService>();

var settings = AppSettings.Parse<OneDriveDownloadAppSettings>(builder.Configuration, args);

if (settings.UseAzureStorage == true)
{
    builder.Services.AddSingleton<ShareClient>(sp =>
    {
        var settings = sp.GetRequiredService<OneDriveDownloadAppSettings>();
        var connectionString = settings.AzureStorage.ConnectionString;
        var shareName = settings.AzureStorage.FileShareName;

        var client = new ShareClient(connectionString, shareName);

        return client;
    });
}

// ---------------------------------------------------------------
// HTTP mode: APIM + On-Behalf-Of (OBO) flow
//   - Azure API Management (APIM) sits in front of this backend.
//     APIM validates the JWT, serves Protected Resource Metadata
//     (RFC 9728), and returns 401 + WWW-Authenticate on challenge.
//   - The MCP client authenticates with Entra ID and obtains a
//     user_impersonation token scoped to this app's client ID.
//   - APIM validates the token and forwards the request (with the
//     original Authorization header) to this backend.
//   - This backend extracts the token and exchanges it via OBO
//     for a Microsoft Graph access token with Files.Read.All scope.
// ---------------------------------------------------------------
if (useStreamableHttp)
{
    // Authentication:
    //   GraphTokenAuthenticationHandler extracts the bearer token
    //   forwarded by APIM. No JWT validation here — APIM already did it.
    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = GraphTokenDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = GraphTokenDefaults.AuthenticationScheme;
    })
    .AddScheme<AuthenticationSchemeOptions, GraphTokenAuthenticationHandler>(GraphTokenDefaults.AuthenticationScheme, _ => { });

    builder.Services.AddAuthorization();

    // GraphServiceClient – scoped, created per-request.
    // Exchanges the user_impersonation token (validated by APIM) for a
    // Microsoft Graph token via the On-Behalf-Of (OBO) flow.
    // The client assertion is obtained from the managed identity via FIC
    // (Federated Identity Credentials) — no client secrets needed.
    builder.Services.AddScoped<GraphServiceClient>(sp =>
    {
        var settings = sp.GetRequiredService<OneDriveDownloadAppSettings>();
        var entraId = settings.EntraId;

        var accessor = sp.GetRequiredService<IHttpContextAccessor>();
        var authHeader = accessor.HttpContext?.Request.Headers.Authorization.FirstOrDefault();
        var userToken = authHeader?.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase) == true
                      ? authHeader["Bearer ".Length..].Trim()
                      : null;

        if (string.IsNullOrEmpty(userToken))
        {
            throw new InvalidOperationException("No bearer token found. APIM must forward the Authorization header.");
        }

        // Use managed identity to obtain a client assertion via FIC.
        // The managed identity requests a token whose audience is
        // api://AzureADTokenExchange — the standard FIC audience.
        // Entra ID matches this against the FIC on the app registration
        // and issues an assertion for the OBO flow.
        var miCredential = new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entraId.UserAssignedClientId));

        async Task<string> getAssertion(CancellationToken ct)
        {
            var tokenRequest = new TokenRequestContext([ Constants.ClientAssertionScope ]);
            var token = await miCredential.GetTokenAsync(tokenRequest, ct);

            return token.Token;
        }

        TokenCredential credential = new OnBehalfOfCredential(entraId.TenantId, entraId.ClientId, getAssertion, userToken);

        string[] scopes = [ Constants.GraphFilesReadWriteAllScope ];

        return new GraphServiceClient(credential, scopes);
    });

    // Build HTTP app manually (cannot use shared BuildApp because we need
    // UseAuthentication/UseAuthorization before MapMcp)
    builder.Services.AddMcpServer()
                    .WithHttpTransport(o => o.Stateless = true)
                    .WithPromptsFromAssembly(Assembly.GetEntryAssembly())
                    .WithResourcesFromAssembly(Assembly.GetEntryAssembly())
                    .WithToolsFromAssembly(Assembly.GetEntryAssembly());

    var webApp = (builder as WebApplicationBuilder)!.Build();

    if (webApp.Environment.IsDevelopment() == false)
    {
        webApp.UseHttpsRedirection();
    }

    webApp.UseAuthentication();
    webApp.UseAuthorization();
    webApp.UseStaticFiles();
    webApp.MapMcp("/mcp");

    await webApp.RunAsync();
}
// ---------------------------------------------------------------
// STDIO mode: Interactive browser credential (local development)
//   - The user authenticates in a browser popup on their machine.
//   - Token is cached locally for subsequent runs.
// ---------------------------------------------------------------
else
{
    builder.Services.AddSingleton<GraphServiceClient>(sp =>
    {
        var settings = sp.GetRequiredService<OneDriveDownloadAppSettings>();
        var entraId = settings.EntraId;

        var options = new InteractiveBrowserCredentialOptions()
        {
            TenantId = entraId.TenantId,
            ClientId = entraId.ClientId,
            TokenCachePersistenceOptions = new TokenCachePersistenceOptions() { Name = "onedrive-download" }
        };
        TokenCredential credential = new InteractiveBrowserCredential(options);

        string[] scopes = ["Files.ReadWrite.All"];
        var client = new GraphServiceClient(credential, scopes);

        return client;
    });

    IHost app = builder.BuildApp(useStreamableHttp: false);

    await app.RunAsync();
}

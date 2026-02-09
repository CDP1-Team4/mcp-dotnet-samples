using Azure.Core;
using Azure.Identity;
using Azure.Storage.Files.Shares;

using McpSamples.OneDriveDownload.HybridApp.Configurations;
using McpSamples.OneDriveDownload.HybridApp.Services;
using McpSamples.Shared.Configurations;
using McpSamples.Shared.Extensions;

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

builder.Services.AddHttpContextAccessor();
builder.Services.AddAppSettings<OneDriveDownloadAppSettings>(builder.Configuration, args);
builder.Services.AddScoped<IOneDriveDownloadService, OneDriveDownloadService>();
builder.Services.AddSingleton<GraphServiceClient>(sp =>
{
    var settings = sp.GetRequiredService<OneDriveDownloadAppSettings>();
    var entraId = settings.EntraId;

    var options = new InteractiveBrowserCredentialOptions()
    {
        TenantId = entraId.TenantId,
        ClientId = entraId.ClientId,
    };
    TokenCredential credential = entraId.UseManagedIdentity
                               ? new ManagedIdentityCredential(ManagedIdentityId.FromUserAssignedClientId(entraId.UserAssignedClientId))
                               : new InteractiveBrowserCredential(options);

    string[] scopes = [ "Files.Read.All" ];
    var client = new GraphServiceClient(credential, scopes);

    return client;
});

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

IHost app = builder.BuildApp(useStreamableHttp);

if (useStreamableHttp == true)
{
    (app as WebApplication)!.UseStaticFiles();
}

await app.RunAsync();

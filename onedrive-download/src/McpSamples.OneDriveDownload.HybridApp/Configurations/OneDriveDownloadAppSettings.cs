using McpSamples.Shared.Configurations;

using Microsoft.OpenApi.Models;

namespace McpSamples.OneDriveDownload.HybridApp.Configurations;

/// <summary>
/// This represents the application settings for the onedrive-download app.
/// </summary>
public class OneDriveDownloadAppSettings : AppSettings
{
    /// <inheritdoc />
    public override OpenApiInfo OpenApi { get; set; } = new()
    {
        Title = "MCP OneDrive Download",
        Version = "1.0.0",
        Description = "A simple MCP server for downloading files from OneDrive."
    };

    /// <summary>
    /// Gets or sets the value indicating whether to use Azure Storage to save downloaded files.
    /// </summary>
    public bool UseAzureStorage { get; set; } = false;

    /// <summary>
    /// Gets or sets the <see cref="AzureStorageSettings"/> instance.
    /// </summary>
    public AzureStorageSettings AzureStorage { get; set; } = new AzureStorageSettings();

    /// <summary>
    /// Gets or sets the <see cref="EntraIdSettings"/> instance.
    /// </summary>
    public EntraIdSettings EntraId { get; set; } = new EntraIdSettings(Environment.GetEnvironmentVariable(Constants.AzureClientIdEnvironmentKey));

    /// <inheritdoc />
    protected override T ParseMore<T>(IConfiguration config, string[] args)
    {
        var settings = base.ParseMore<T>(config, args);

        for (var i = 0; i < args.Length; i++)
        {
            var arg = args[i];
            switch (arg)
            {
                case "--use-azure-storage":
                case "-a":
                    (settings as OneDriveDownloadAppSettings)!.UseAzureStorage = true;
                    break;

                case "--tenant-id":
                case "-t":
                    (settings as OneDriveDownloadAppSettings)!.EntraId.TenantId = args[++i];
                    break;

                case "--client-id":
                case "-c":
                    (settings as OneDriveDownloadAppSettings)!.EntraId.ClientId = args[++i];
                    break;

                case "--client-secret":
                case "-s":
                    (settings as OneDriveDownloadAppSettings)!.EntraId.ClientSecret = args[++i];
                    break;

                default:
                    break;
            }
        }

        return settings;
    }
}

/// <summary>
/// This represents the Azure Storage settings.
/// </summary>
public class AzureStorageSettings
{
    /// <summary>
    /// Gets or sets the Azure Storage connection string.
    /// </summary>
    public string? ConnectionString { get; set; }

    /// <summary>
    /// Gets the Azure File Share name.
    /// </summary>
    public string? FileShareName { get; } = Constants.DefaultDownloadFolder;
}

/// <summary>
/// This represents the Entra ID settings.
/// </summary>
/// <param name="userAssignedClientId">The user-assigned client ID from AZURE_CLIENT_ID environment variable.</param>
public class EntraIdSettings(string? userAssignedClientId = default)
{
    /// <summary>
    /// Gets or sets the tenant ID.
    /// </summary>
    public string? TenantId { get; set; }

    /// <summary>
    /// Gets or sets the client ID for Personal OneDrive OAuth.
    /// </summary>
    public string? ClientId { get; set; }

    /// <summary>
    /// Gets or sets the client secret.
    /// </summary>
    public string? ClientSecret { get; set; }

    /// <summary>
    /// Gets the value indicating whether to use the managed identity or not.
    /// </summary>
    public bool UseManagedIdentity { get; } = string.IsNullOrWhiteSpace(userAssignedClientId) == false;

    /// <summary>
    /// Gets the user-assigned client ID.
    /// </summary>
    public string? UserAssignedClientId { get; } = userAssignedClientId;
}
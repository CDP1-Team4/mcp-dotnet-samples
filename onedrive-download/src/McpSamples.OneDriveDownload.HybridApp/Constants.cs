namespace McpSamples.OneDriveDownload.HybridApp;

/// <summary>
/// This represents the entity containing all the magic numbers and strings.
/// </summary>
public class Constants
{
    /// <summary>
    /// The default scope for Microsoft Graph API.
    /// </summary>
    public const string DefaultScope = "https://graph.microsoft.com/.default";

    /// <summary>
    /// The environment variable key for UseAzureStorage setting.
    /// </summary>
    public const string UseAzureStorageKey = "UseAzureStorage";

    /// <summary>
    /// The environment variable key for Azure Client ID.
    /// </summary>
    public const string AzureClientIdEnvironmentKey = "AZURE_CLIENT_ID";

    /// <summary>
    /// The environment variable key for Azure Functions Custom Handler Port.
    /// </summary>
    public const string AzureFunctionsCustomHandlerPortEnvironmentKey = "FUNCTIONS_CUSTOMHANDLER_PORT";

    /// <summary>
    /// The default port for the custom handler.
    /// </summary>
    public const int DefaultAppPort = 5870;

    /// <summary>
    /// The default URL for the application.
    /// </summary>
    public const string DefaultAppUrl = "http://0.0.0.0:{0}";

    /// <summary>
    /// The default web root folder name.
    /// </summary>
    public const string DefaultWebRootFolder = "wwwroot";

    /// <summary>
    /// The default folder to save downloaded files.
    /// </summary>
    public const string DefaultDownloadFolder = "downloads";

    /// <summary>
    /// The Microsoft Graph scope for reading/writing files, used in the On-Behalf-Of (OBO) flow.
    /// The Shares API requires Files.ReadWrite (minimum) for personal Microsoft accounts.
    /// See: https://learn.microsoft.com/graph/api/shares-get
    /// </summary>
    public const string GraphFilesReadWriteAllScope = "https://graph.microsoft.com/Files.ReadWrite.All";

    /// <summary>
    /// The client assertion scope for Federated Identity Credentials (FIC).
    /// The managed identity requests a token whose audience matches the FIC
    /// audience configured on the app registration (api://AzureADTokenExchange).
    /// </summary>
    public const string ClientAssertionScope = "api://AzureADTokenExchange/.default";
}

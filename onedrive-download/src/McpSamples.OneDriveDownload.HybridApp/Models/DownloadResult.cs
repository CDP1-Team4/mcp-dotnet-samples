namespace McpSamples.OneDriveDownload.HybridApp.Models;

/// <summary>
/// This represents the download result entity from OneDrive to Azure Storage.
/// </summary>
public class DownloadResult
{
    /// <summary>
    /// Gets or sets the file name.
    /// </summary>
    public string? FileName { get; set; }

    /// <summary>
    /// Gets or sets the download URL.
    /// </summary>
    public string? DownloadUrl { get; set; }

    /// <summary>
    /// Gets or sets the SAS URL of the file where it is saved on Azure Storage.
    /// </summary>
    public string? SasUrl { get; set; }

    /// <summary>
    /// Gets or sets the error message if any error occurs.
    /// </summary>
    public string? ErrorMessage { get; set; }
}
